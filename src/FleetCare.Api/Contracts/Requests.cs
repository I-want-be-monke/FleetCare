using FleetCare.Domain.Enums;

namespace FleetCare.Api.Contracts;

public sealed record CreateVehicleRequest(
    string Name,
    int CurrentOdometerKm,
    int LastServiceOdometerKm,
    int ServiceIntervalKm
);

public sealed record CreateTelemetryRequest(
    Guid EventId,
    Guid VehicleId,
    DateTimeOffset OccurredAt,
    int OdometerKm,
    double EngineTempC,
    string? FaultCode
);

public sealed record UpdateCaseStatusRequest(
    CaseStatus Status
);