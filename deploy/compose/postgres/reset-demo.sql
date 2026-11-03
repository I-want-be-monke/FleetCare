-- Explicit demo-only reset. Stop API/workers and wait for processing first.
BEGIN;
CREATE TEMP TABLE demo_vehicles ON COMMIT DROP AS
  SELECT "Id" FROM public."Vehicles"
  WHERE "Name" ~ '^FC-DEMO-[0-9]+-(SERVICE|TREND|FAULT)$';
CREATE TEMP TABLE demo_events ON COMMIT DROP AS
  SELECT "EventId" FROM public."TelemetryReadings"
  WHERE "VehicleId" IN (SELECT "Id" FROM demo_vehicles);
CREATE TEMP TABLE demo_risks ON COMMIT DROP AS
  SELECT "RiskId" FROM public."RiskAlerts"
  WHERE "VehicleId" IN (SELECT "Id" FROM demo_vehicles);
DELETE FROM public."ProcessedMessages"
 WHERE "MessageId" IN (SELECT "EventId" FROM demo_events)
    OR "MessageId" IN (SELECT "RiskId" FROM demo_risks);
DELETE FROM public."RiskAlerts"
 WHERE "VehicleId" IN (SELECT "Id" FROM demo_vehicles);
DELETE FROM public."MaintenanceCases"
 WHERE "VehicleId" IN (SELECT "Id" FROM demo_vehicles);
DELETE FROM public."OutboxMessages"
 WHERE "EventType" = 'telemetry.recorded.v1'
   AND ("PayloadJson"::jsonb ->> 'vehicleId') IN
       (SELECT "Id"::text FROM demo_vehicles);
DELETE FROM public."TelemetryReadings"
 WHERE "VehicleId" IN (SELECT "Id" FROM demo_vehicles);
DELETE FROM public."Vehicles"
 WHERE "Id" IN (SELECT "Id" FROM demo_vehicles);
COMMIT;
