
# FleetCare — Kafka Event Contracts

## Kafka topics

### telemetry.recorded.v1

Событие о принятом показании телеметрии.

Поля:
- eventId: UUID
- vehicleId: UUID
- occurredAt: UTC timestamp
- odometerKm: integer >= 0
- engineTempC: finite number
- faultCode: string | null

Пример: telemetry.recorded.v1.json

### risk.detected.v1

Событие о риске, обнаруженном аналитикой.

Поля:
- riskId: UUID
- sourceEventId: UUID
- vehicleId: UUID
- kind: string
- score: number from 0 to 1
- explanation: string
- detectedAt: UTC timestamp

Пример: risk.detected.v1.json

## Kafka message key

Для обоих топиков ключ сообщения — vehicleId.

Формат ключа: UUID в строковом представлении.

## Time format

Все временные метки передаются в UTC.
Формат: ISO 8601, например 2026-10-20T09:30:00Z.

## Ordering

События одной машины сохраняют порядок
в пределах одной Kafka-партиции.

Общий порядок событий всех машин
не гарантируется.

## Versioning

Суффикс v1 обозначает первую версию контракта.

При несовместимом изменении структуры события
нужно создать новую версию контракта.

## Idempotency

Повторная отправка исходного события
использует тот же eventId.

riskId в рабочей Python-аналитике должен
определяться детерминированно на основе
vehicleId, kind и sourceEventId.


## Current implementation


- .NET Worker читает telemetry.recorded.v1.
- Consumer Group: fleetcare-maintenance-worker-v1.
- EnableAutoCommit=false и EnableAutoOffsetStore=false.
- Worker использует MaintenanceService для проверки правил ТО.
- Изменения Vehicle, MaintenanceCases и ProcessedMessage
  сохраняются в одной PostgreSQL-транзакции.
- Kafka offset подтверждается только после коммита PostgreSQL.
- Повторное событие с тем же eventId безопасно
  благодаря ProcessedMessage.
- Worker восстанавливает обработку после перезапуска.
- risk.detected.v1 будет подключён после Python-аналитики.

Python-аналитика и обработка risk.detected.v1 запланированы на шаг 8.
