namespace FleetCare.Domain.Entities;

public sealed class TelemetryReading
{
    public Guid EventId { get; }
    public Guid VehicleId { get; }
    public DateTimeOffset OccurredAt { get; }
    public int OdometerKm { get; }
    public double EngineTempC { get; }
    public string? FaultCode { get; }

    public TelemetryReading(
        Guid eventId,
        Guid vehicleId,
        DateTimeOffset occurredAt,
        int odometerKm,
        double engineTempC,
        string? faultCode = null)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId обязателен.", nameof(eventId));

        if (vehicleId == Guid.Empty)
            throw new ArgumentException("VehicleId обязателен.", nameof(vehicleId));

        if (occurredAt == default)
            throw new ArgumentException("Время измерения обязательно.", nameof(occurredAt));

        if (odometerKm < 0)
            throw new ArgumentOutOfRangeException(nameof(odometerKm));

        if (!double.IsFinite(engineTempC))
            throw new ArgumentException(
                "Температура должна быть конечным числом.",
                nameof(engineTempC));

        EventId = eventId;
        VehicleId = vehicleId;
        OccurredAt = occurredAt.ToUniversalTime();
        OdometerKm = odometerKm;
        EngineTempC = engineTempC;

        FaultCode = string.IsNullOrWhiteSpace(faultCode)
            ? null
            : faultCode.Trim().ToUpperInvariant();
    }
}