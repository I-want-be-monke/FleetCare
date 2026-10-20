namespace FleetCare.Worker.Contracts;

public sealed record TelemetryRecordedV1(
    Guid EventId,
    Guid VehicleId,
    DateTimeOffset OccurredAt,
    int OdometerKm,
    double EngineTempC,
    string? FaultCode
);