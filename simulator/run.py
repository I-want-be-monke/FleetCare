"""Reproducible FleetCare scenarios sent through the public API (never direct SQL)."""

import argparse
import json
import os
import random
import time
from datetime import datetime, timedelta, timezone
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from uuid import NAMESPACE_URL, uuid5

API_BASE = os.getenv("FLEETCARE_API_BASE", "http://localhost:15015").rstrip("/")
BASE_TIME = datetime(2026, 1, 1, 12, tzinfo=timezone.utc)


def request_json(method, path, payload=None, retries=1):
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    for attempt in range(retries):
        req = Request(
            API_BASE + path,
            data=body,
            method=method,
            headers={"Content-Type": "application/json", "Accept": "application/json"},
        )
        try:
            with urlopen(req, timeout=10) as response:
                data = response.read()
                return json.loads(data) if data else None
        except HTTPError as exc:
            # A client error must not be retried: the event may have been rejected.
            if exc.code < 500 or attempt + 1 == retries:
                raise
        except (URLError, TimeoutError):
            if attempt + 1 == retries:
                raise
        time.sleep(1)
    raise RuntimeError("Unreachable API retry state")


def vehicle_name(seed, kind):
    return f"FC-DEMO-{seed}-{kind}"


def find_or_create_vehicle(name, odometer, interval):
    existing = [v for v in request_json("GET", "/vehicles", retries=40) if v["name"] == name]
    if existing:
        if len(existing) != 1:
            raise RuntimeError(f"Duplicate demo vehicle name: {name}")
        vehicle = existing[0]
        if (vehicle["lastServiceOdometerKm"] != odometer or
                vehicle["serviceIntervalKm"] != interval):
            raise RuntimeError(f"{name} was modified; reset the demo before replay.")
        print(f"Existing vehicle: {name}", flush=True)
        return vehicle["id"]

    vehicle = request_json("POST", "/vehicles", {
        "name": name,
        "currentOdometerKm": odometer,
        "lastServiceOdometerKm": odometer,
        "serviceIntervalKm": interval,
    })
    print(f"Created vehicle: {name}", flush=True)
    return vehicle["id"]


def event_id(seed, kind, vehicle_id, index):
    # Include vehicle UUID to avoid conflicts with historical Kafka messages after reset.
    return str(uuid5(NAMESPACE_URL, f"fleetcare:step9:v1:{seed}:{kind}:{vehicle_id}:{index}"))


def send_reading(seed, kind, vehicle_id, index, odometer, temperature, fault=None):
    payload = {
        "eventId": event_id(seed, kind, vehicle_id, index),
        "vehicleId": vehicle_id,
        "occurredAt": (BASE_TIME + timedelta(days=seed, minutes=index))
            .isoformat().replace("+00:00", "Z"),
        "odometerKm": odometer,
        "engineTempC": temperature,
        "faultCode": fault,
    }
    try:
        request_json("POST", "/telemetry", payload)
        print(f"{kind}[{index}]: recorded at {odometer} km, {temperature} C", flush=True)
    except HTTPError as exc:
        if exc.code != 409:
            raise
        # 409 is expected on an exact replay, not a generic success condition.
        readings = request_json("GET", f"/vehicles/{vehicle_id}/telemetry")
        matching = [r for r in readings if r["eventId"] == payload["eventId"]]
        if (len(matching) != 1
                or matching[0]["vehicleId"] != vehicle_id
                or matching[0]["odometerKm"] != odometer
                or float(matching[0]["engineTempC"]) != float(temperature)
                or matching[0]["faultCode"] != fault):
            raise RuntimeError(f"Event collision or mismatched replay: {payload['eventId']}")
        print(f"{kind}[{index}]: already recorded", flush=True)


def run_scenario(seed):
    if seed < 0 or seed > 10000:
        raise ValueError("seed must be between 0 and 10000")

    service = find_or_create_vehicle(vehicle_name(seed, "SERVICE"), 5000, 1000)
    send_reading(seed, "SERVICE", service, 0, 5600, 80)
    send_reading(seed, "SERVICE", service, 1, 6000, 81)

    trend = find_or_create_vehicle(vehicle_name(seed, "TREND"), 10000, 20000)
    rng = random.Random(f"{seed}:trend")
    for index, temperature in enumerate((80, 81, 80, 79, 80, 90, 92, 94, 96)):
        km = 10000 + index * 10 + rng.randint(0, 3)
        send_reading(seed, "TREND", trend, index, km, temperature)

    fault = find_or_create_vehicle(vehicle_name(seed, "FAULT"), 7000, 20000)
    send_reading(seed, "FAULT", fault, 0, 7010, 83, "P0116")

    return {service, trend, fault}


def verify(seed, vehicle_ids, timeout=120):
    """Confirm eventual .NET case processing and Python risk creation."""
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        vehicles = request_json("GET", "/vehicles")
        readings = sum(
            len(request_json("GET", f"/vehicles/{vehicle_id}/telemetry"))
            for vehicle_id in vehicle_ids
        )
        cases = [c for c in request_json("GET", "/cases")
                 if c["vehicleId"] in vehicle_ids]
        risks = [r for r in request_json("GET", "/risks")
                 if r["vehicleId"] in vehicle_ids]
        matching_vehicles = [v for v in vehicles if v["id"] in vehicle_ids]
        last = (len(matching_vehicles), readings, len(cases), len(risks))
        if last == (3, 12, 2, 1):
            if sorted(c["reason"] for c in cases) != ["FaultCode", "ScheduledService"]:
                raise AssertionError(f"Unexpected maintenance reasons: {cases}")
            if risks[0]["kind"] != "EngineTemperatureTrend" or risks[0]["score"] != 0.5:
                raise AssertionError(f"Unexpected temperature risk: {risks}")
            print("PASS: 3 vehicles, 12 readings, 2 cases, 1 temperature risk (score 0.5)")
            return
        if len(cases) > 2 or len(risks) > 1:
            raise AssertionError(f"Unexpected duplicates: {last}")
        time.sleep(2)
    raise TimeoutError(f"Workers did not converge to expected counts: {last}")


def main():
    parser = argparse.ArgumentParser(description="FleetCare step-9 deterministic demo")
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--verify", action="store_true",
                        help="Wait for both Workers and assert expected results")
    args = parser.parse_args()
    print(f"FleetCare demo: seed={args.seed}, API={API_BASE}", flush=True)
    ids = run_scenario(args.seed)
    print("Submitted 12 readings. Background processing is asynchronous.", flush=True)
    if args.verify:
        verify(args.seed, ids)


if __name__ == "__main__":
    main()
