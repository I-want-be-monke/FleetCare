namespace FleetCare.Worker.Contracts;

public sealed record RiskDetectedV1(
    Guid RiskId,
    Guid SourceEventId,
    Guid VehicleId,
    string Kind,
    double Score,
    string Explanation,
    DateTimeOffset DetectedAt);
