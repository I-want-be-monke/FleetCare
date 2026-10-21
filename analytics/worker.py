"""Consume committed FleetCare readings, detect trends, publish risk events."""

import json
import logging
import math
import os
import time
from datetime import datetime, timezone
from uuid import UUID

import psycopg
from confluent_kafka import Consumer, Producer
from dotenv import load_dotenv
from pathlib import Path

load_dotenv(Path(__file__).resolve().parents[1] / ".env", override=False)

from analytics.analyzer import RISK_KIND, Sample, analyze, make_risk_id

TELEMETRY_TOPIC = "telemetry.recorded.v1"
RISK_TOPIC = "risk.detected.v1"
GROUP_ID = "fleetcare-python-analytics-v1"
BOOTSTRAP = os.getenv("FLEETCARE_KAFKA_BOOTSTRAP", "localhost:9092")

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger(__name__)


def parse_message(key: bytes | None, value: bytes) -> dict:
    try:
        data = json.loads(value)
        event_id = UUID(data["eventId"])
        vehicle_id = UUID(data["vehicleId"])
        occurred_at = datetime.fromisoformat(data["occurredAt"].replace("Z", "+00:00"))
        odometer = data["odometerKm"]
        temperature = data["engineTempC"]
        if (
            event_id.int == 0 or vehicle_id.int == 0
            or key is None or key.decode().lower() != str(vehicle_id)
            or occurred_at.tzinfo is None
            or type(odometer) is not int or odometer < 0
            or type(temperature) not in (int, float) or not math.isfinite(temperature)
        ):
            raise ValueError("invalid telemetry fields")
        return {
            "event_id": event_id,
            "vehicle_id": vehicle_id,
            "occurred_at": occurred_at.astimezone(timezone.utc),
            "odometer": odometer,
            "temperature": float(temperature),
            "fault_code": data.get("faultCode"),
        }
    except (UnicodeError, KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        raise ValueError("invalid telemetry Kafka message") from exc


def load_samples(connection: psycopg.Connection, event: dict) -> list[Sample] | None:
    with connection.cursor() as cursor:
        cursor.execute(
            'SELECT "VehicleId", "OccurredAt", "OdometerKm", "EngineTempC", "FaultCode" '
            'FROM "TelemetryReadings" WHERE "EventId" = %s',
            (event["event_id"],),
        )
        source = cursor.fetchone()
        if source is None:
            return None  # Earlier Kafka smoke messages have no persisted reading.
        vehicle_id, occurred_at, odometer, temperature, fault_code = source
        if (
            vehicle_id != event["vehicle_id"]
            or occurred_at != event["occurred_at"]
            or odometer != event["odometer"]
            or temperature != event["temperature"]
            or fault_code != event["fault_code"]
        ):
            raise ValueError("Kafka event differs from PostgreSQL reading")

        cursor.execute(
            'SELECT "EventId", "EngineTempC" FROM "TelemetryReadings" '
            'WHERE "VehicleId" = %s AND ("OccurredAt", "EventId") <= (%s, %s) '
            'ORDER BY "OccurredAt" DESC, "EventId" DESC LIMIT 9',
            (vehicle_id, occurred_at, event["event_id"]),
        )
        rows = cursor.fetchall()
    rows.reverse()
    return [Sample(event_id, temp_c) for event_id, temp_c in rows]


def publish_risk(producer: Producer, event: dict, result) -> None:
    risk_id = make_risk_id(event["vehicle_id"], event["event_id"])
    payload = {
        "riskId": str(risk_id),
        "sourceEventId": str(event["event_id"]),
        "vehicleId": str(event["vehicle_id"]),
        "kind": RISK_KIND,
        "score": result.score,
        "explanation": result.explanation,
        "detectedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    delivery_errors = []

    def delivered(error, _message):
        if error is not None:
            delivery_errors.append(error)

    producer.produce(
        RISK_TOPIC,
        key=str(event["vehicle_id"]),
        value=json.dumps(payload, ensure_ascii=False),
        on_delivery=delivered,
    )
    remaining = producer.flush(15)
    if remaining or delivery_errors:
        raise RuntimeError(f"risk event delivery failed: {delivery_errors or remaining}")
    logger.info("Published riskId=%s sourceEventId=%s", risk_id, event["event_id"])


def process_message(connection: psycopg.Connection, producer: Producer, key, value) -> str:
    event = parse_message(key, value)
    samples = load_samples(connection, event)
    if samples is None:
        logger.warning("No persisted reading for eventId=%s; skipping", event["event_id"])
        return "unknown-reading"
    if not samples or samples[-1].event_id != event["event_id"]:
        raise ValueError("source reading is not last in its historical window")

    result = analyze(samples)
    if result.status == "risk":
        publish_risk(producer, event, result)
    else:
        logger.info("eventId=%s analysis=%s", event["event_id"], result.status)
    return result.status


def main() -> None:
    producer = Producer({"bootstrap.servers": BOOTSTRAP, "enable.idempotence": True})
    logger.info("Python Analytics starting. Group=%s", GROUP_ID)
    while True:
        consumer = Consumer({
            "bootstrap.servers": BOOTSTRAP,
            "group.id": GROUP_ID,
            "auto.offset.reset": "earliest",
            "enable.auto.commit": False,
            "enable.auto.offset.store": False,
            "allow.auto.create.topics": False,
        })
        try:
            with psycopg.connect() as connection:
                connection.autocommit = True
                consumer.subscribe([TELEMETRY_TOPIC])
                while True:
                    message = consumer.poll(1.0)
                    if message is None:
                        continue
                    if message.error():
                        raise RuntimeError(message.error())
                    outcome = process_message(connection, producer, message.key(), message.value())
                    consumer.commit(message=message, asynchronous=False)
                    logger.info("Committed %s[%s]@%s outcome=%s", message.topic(),
                                message.partition(), message.offset(), outcome)
        except KeyboardInterrupt:
            logger.info("Python Analytics stopped")
            break
        except Exception:
            logger.exception("Analytics failed; restarting consumer from committed offset")
            time.sleep(3)
        finally:
            consumer.close()


if __name__ == "__main__":
    main()
