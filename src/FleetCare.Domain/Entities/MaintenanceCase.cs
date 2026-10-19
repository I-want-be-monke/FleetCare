
using FleetCare.Domain.Enums;

namespace FleetCare.Domain.Entities;

public sealed class MaintenanceCase
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid VehicleId { get; private set; }

    public Guid SourceEventId { get; private set; }

    public MaintenanceReason Reason { get; private set; }

    public string? FaultCode { get; private set; }

    public CaseStatus Status { get; private set; } = CaseStatus.New;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsOpen => Status != CaseStatus.Completed;

    public MaintenanceCase(
        Guid vehicleId,
        Guid sourceEventId,
        MaintenanceReason reason,
        DateTimeOffset createdAt,
        string? faultCode = null)
    {
        if (vehicleId == Guid.Empty)
            throw new ArgumentException("VehicleId обязателен.", nameof(vehicleId));

        if (sourceEventId == Guid.Empty)
            throw new ArgumentException(
                "SourceEventId обязателен.",
                nameof(sourceEventId));

        if (createdAt == default)
            throw new ArgumentException(
                "Дата создания обязательна.",
                nameof(createdAt));

        if (reason == MaintenanceReason.FaultCode &&
            string.IsNullOrWhiteSpace(faultCode))
        {
            throw new ArgumentException(
                "Для заявки по неисправности нужен код ошибки.",
                nameof(faultCode));
        }

        if (reason != MaintenanceReason.FaultCode &&
            !string.IsNullOrWhiteSpace(faultCode))
        {
            throw new ArgumentException(
                "Код ошибки допустим только для заявки по неисправности.",
                nameof(faultCode));
        }

        VehicleId = vehicleId;
        SourceEventId = sourceEventId;
        Reason = reason;
        CreatedAt = createdAt.ToUniversalTime();

        FaultCode = string.IsNullOrWhiteSpace(faultCode)
            ? null
            : faultCode.Trim().ToUpperInvariant();
    }

    public void Start()
    {
        if (Status != CaseStatus.New)
            throw new InvalidOperationException(
                "В работу можно взять только новую заявку.");

        Status = CaseStatus.InProgress;
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (Status == CaseStatus.Completed)
            throw new InvalidOperationException(
                "Заявка уже завершена.");

        if (completedAt < CreatedAt)
            throw new ArgumentException(
                "Дата завершения не может быть раньше даты создания.",
                nameof(completedAt));

        Status = CaseStatus.Completed;
        CompletedAt = completedAt.ToUniversalTime();
    }

    public bool HasSameReasonAs(MaintenanceCase other)
    {
        if (Reason != other.Reason)
            return false;

        if (Reason == MaintenanceReason.FaultCode)
        {
            return string.Equals(
                FaultCode,
                other.FaultCode,
                StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}
