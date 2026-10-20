using System.Text.Json;
using FleetCare.Domain.Entities;
using FleetCare.Infrastructure.Persistence;

namespace FleetCare.Infrastructure.Services;

public sealed class TelemetryIngestionService
{
    public const string TelemetryTopic =
        "telemetry.recorded.v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly FleetCareDbContext _db;

    public TelemetryIngestionService(FleetCareDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(
        TelemetryReading reading,
        CancellationToken ct = default)
    {
        // Формируем событие по существующему JSON-контракту.
        // Web defaults дают имена eventId, vehicleId и т.д.
        string payloadJson =
            JsonSerializer.Serialize(reading, JsonOptions);

        var outboxMessage = new OutboxMessage(
            TelemetryTopic,
            payloadJson,
            DateTimeOffset.UtcNow);

        // Обе записи сохраняются в одной транзакции.
        await using var transaction =
            await _db.Database.BeginTransactionAsync(ct);

        _db.TelemetryReadings.Add(reading);
        _db.OutboxMessages.Add(outboxMessage);

        await _db.SaveChangesAsync(ct);

        // Если сохранение упало, транзакция откатится.
        await transaction.CommitAsync(ct);
    }
}