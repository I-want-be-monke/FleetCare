using FleetCare.Domain.Enums;

namespace FleetCare.Domain.Entities;

public sealed class RiskAlert
{
    public Guid RiskId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid VehicleId { get; private set; }
    public string Kind { get; private set; }
    public double Score { get; private set; }
    public string Explanation { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public RiskAlertStatus Status { get; private set; } = RiskAlertStatus.New;
    public DateTimeOffset? ReviewedAt { get; private set; }

    public RiskAlert(Guid riskId, Guid sourceEventId, Guid vehicleId,
        string kind, double score, string explanation, DateTimeOffset detectedAt)
    {
        if (riskId == Guid.Empty || sourceEventId == Guid.Empty || vehicleId == Guid.Empty)
            throw new ArgumentException("Идентификаторы риска, события и автомобиля обязательны.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 100)
            throw new ArgumentException("Некорректный тип риска.", nameof(kind));
        if (!double.IsFinite(score) || score is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(score));
        if (string.IsNullOrWhiteSpace(explanation) || explanation.Length > 1000)
            throw new ArgumentException("Некорректное объяснение риска.", nameof(explanation));
        if (detectedAt == default)
            throw new ArgumentException("Время обнаружения обязательно.", nameof(detectedAt));

        RiskId = riskId;
        SourceEventId = sourceEventId;
        VehicleId = vehicleId;
        Kind = kind.Trim();
        Score = score;
        Explanation = explanation.Trim();
        DetectedAt = detectedAt.ToUniversalTime();
    }

    public void Review(DateTimeOffset reviewedAt)
    {
        if (Status != RiskAlertStatus.New)
            throw new InvalidOperationException("Риск уже проверен.");
        if (reviewedAt < DetectedAt)
            throw new ArgumentException("Дата проверки раньше обнаружения.", nameof(reviewedAt));
        Status = RiskAlertStatus.Reviewed;
        ReviewedAt = reviewedAt.ToUniversalTime();
    }
}
