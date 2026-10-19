
using FleetCare.Domain.Entities;
using FleetCare.Domain.Enums;

namespace FleetCare.Domain.Services;

public sealed class MaintenanceService
{
    public IReadOnlyList<MaintenanceCase> Evaluate(
        Vehicle vehicle,
        TelemetryReading reading,
        IEnumerable<MaintenanceCase> existingCases,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(existingCases);

        if (vehicle.Id != reading.VehicleId)
        {
            throw new ArgumentException(
                "Показание относится к другому автомобилю.",
                nameof(reading));
        }

        vehicle.UpdateOdometer(reading.OdometerKm);

        var previousCases = existingCases.ToList();
        var newCases = new List<MaintenanceCase>();

        if (vehicle.IsServiceDue)
        {
            TryAddCase(MaintenanceReason.ScheduledService, null);
        }

        if (reading.FaultCode is not null)
        {
            TryAddCase(MaintenanceReason.FaultCode, reading.FaultCode);
        }

        return newCases;

        void TryAddCase(MaintenanceReason reason, string? faultCode)
        {
            var candidate = new MaintenanceCase(
                vehicle.Id,
                reading.EventId,
                reason,
                now,
                faultCode);

            bool isDuplicate = previousCases
                .Concat(newCases)
                .Any(existing =>
                    existing.VehicleId == candidate.VehicleId &&
                    existing.HasSameReasonAs(candidate) &&
                    (existing.IsOpen ||
                     existing.SourceEventId == candidate.SourceEventId));

            if (!isDuplicate)
            {
                newCases.Add(candidate);
            }
        }
    }

    public void CompleteCase(
        Vehicle vehicle,
        MaintenanceCase maintenanceCase,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(maintenanceCase);

        if (vehicle.Id != maintenanceCase.VehicleId)
        {
            throw new ArgumentException(
                "Заявка относится к другому автомобилю.",
                nameof(maintenanceCase));
        }

        maintenanceCase.Complete(completedAt);

        if (maintenanceCase.Reason == MaintenanceReason.ScheduledService)
        {
            vehicle.CompleteScheduledService();
        }
    }
}
