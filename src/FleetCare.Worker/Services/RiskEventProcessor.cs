using System.Text.Json;
using FleetCare.Domain.Entities;
using FleetCare.Infrastructure.Persistence;
using FleetCare.Worker.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Worker.Services;

public sealed class RiskEventProcessor(FleetCareDbContext db)
{
    public const string HandlerName = "analytics.risk.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> ProcessAsync(string? kafkaKey, string? payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidDataException("Пустое событие риска.");

        RiskDetectedV1 message;
        try
        {
            message = JsonSerializer.Deserialize<RiskDetectedV1>(payload, JsonOptions)
                ?? throw new InvalidDataException("Пустое событие риска.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Некорректный JSON события риска.", ex);
        }

        if (message.RiskId == Guid.Empty || message.SourceEventId == Guid.Empty ||
            message.VehicleId == Guid.Empty || message.DetectedAt == default ||
            message.Kind != "EngineTemperatureTrend" ||
            !double.IsFinite(message.Score) || message.Score is < 0 or > 1 ||
            string.IsNullOrWhiteSpace(message.Explanation) || message.Explanation.Length > 1000 ||
            !string.Equals(kafkaKey, message.VehicleId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Некорректные поля события риска или Kafka key.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        bool processed = await db.ProcessedMessages.AsNoTracking().AnyAsync(x =>
            x.MessageId == message.RiskId && x.HandlerName == HandlerName, ct);
        if (processed)
        {
            await transaction.CommitAsync(ct);
            return "Duplicate";
        }

        // Блокировка строки машины сериализует повторную обработку риска
        // несколькими экземплярами Worker.
        var vehicles = await db.Vehicles.FromSqlInterpolated(
            $"SELECT * FROM \"Vehicles\" WHERE \"Id\" = {message.VehicleId} FOR UPDATE")
            .ToListAsync(ct);
        if (vehicles.Count == 0)
        {
            // Сохранённые на шаге 5 учебные Kafka-события не связаны с БД.
            db.ProcessedMessages.Add(new ProcessedMessage(message.RiskId,
                HandlerName, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return "UnknownVehicle";
        }

        // Повторно проверяем после блокировки для нескольких экземпляров Worker.
        processed = await db.ProcessedMessages.AsNoTracking().AnyAsync(x =>
            x.MessageId == message.RiskId && x.HandlerName == HandlerName, ct);
        if (processed)
        {
            await transaction.CommitAsync(ct);
            return "Duplicate";
        }

        var source = await db.TelemetryReadings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.EventId == message.SourceEventId, ct);
        if (source is null)
        {
            db.ProcessedMessages.Add(new ProcessedMessage(message.RiskId,
                HandlerName, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return "UnknownReading";
        }
        if (source.VehicleId != message.VehicleId || message.DetectedAt < source.OccurredAt)
            throw new InvalidDataException("Источник риска не совпадает с сохранённой телеметрией.");

        bool exists = await db.RiskAlerts.AsNoTracking()
            .AnyAsync(x => x.RiskId == message.RiskId, ct);
        if (!exists)
            db.RiskAlerts.Add(new RiskAlert(message.RiskId, message.SourceEventId,
                message.VehicleId, message.Kind, message.Score,
                message.Explanation, message.DetectedAt));

        db.ProcessedMessages.Add(new ProcessedMessage(message.RiskId,
            HandlerName, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return exists ? "Duplicate" : "Created";
    }
}
