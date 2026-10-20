
using System.Text.Json;
using FleetCare.Domain.Entities;
using FleetCare.Domain.Services;
using FleetCare.Infrastructure.Persistence;
using FleetCare.Worker.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FleetCare.Worker.Services;

public sealed class TelemetryEventProcessor
{
    public const string HandlerName = "maintenance.telemetry.v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly FleetCareDbContext _db;
    private readonly MaintenanceService _maintenance;
    private readonly ILogger<TelemetryEventProcessor> _logger;

    public TelemetryEventProcessor(
        FleetCareDbContext db,
        MaintenanceService maintenance,
        ILogger<TelemetryEventProcessor> logger)
    {
        _db = db;
        _maintenance = maintenance;
        _logger = logger;
    }

    public async Task<TelemetryProcessingResult> ProcessAsync(
        string? kafkaKey,
        string? payload,
        CancellationToken ct)
    {
        // 1. Проверяем JSON из Kafka.
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidDataException(
                "Получено пустое Kafka-сообщение.");
        }

        var message = JsonSerializer.Deserialize<TelemetryRecordedV1>(
            payload, JsonOptions)
            ?? throw new InvalidDataException(
                "Не удалось прочитать событие телеметрии.");

        if (message.EventId == Guid.Empty ||
            message.VehicleId == Guid.Empty ||
            message.OccurredAt == default ||
            message.OdometerKm < 0 ||
            !double.IsFinite(message.EngineTempC) ||
            message.FaultCode?.Trim().Length > 50)
        {
            throw new InvalidDataException(
                "Некорректные поля события телеметрии.");
        }

        if (!string.Equals(
                kafkaKey,
                message.VehicleId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Kafka key не совпадает с vehicleId.");
        }

        // 2. Начинаем PostgreSQL-транзакцию.
        await using var transaction =
            await _db.Database.BeginTransactionAsync(ct);

        // 3. Проверяем, не обработано ли событие раньше.
        bool alreadyProcessed =
            await _db.ProcessedMessages
                .AsNoTracking()
                .AnyAsync(
                    x => x.MessageId == message.EventId &&
                         x.HandlerName == HandlerName,
                    ct);

        if (alreadyProcessed)
        {
            await transaction.CommitAsync(ct);

            return new TelemetryProcessingResult(
                TelemetryProcessingOutcome.Duplicate, 0);
        }

        // 4. Находим уже сохранённую через API телеметрию.
        var reading = await _db.TelemetryReadings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.EventId == message.EventId,
                ct);

        // KafkaSmoke из шага 5 мог записать события
        // без соответствующих строк в PostgreSQL.
        // Такие учебные сообщения игнорируем,
        // но фиксируем факт их рассмотрения.
        if (reading is null)
        {
            _logger.LogWarning(
                "Нет TelemetryReading для EventId={EventId}. " +
                "Событие пропущено.",
                message.EventId);

            _db.ProcessedMessages.Add(
                new ProcessedMessage(
                    message.EventId,
                    HandlerName,
                    DateTimeOffset.UtcNow));

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new TelemetryProcessingResult(
                TelemetryProcessingOutcome.UnknownReading, 0);
        }

        if (reading.VehicleId != message.VehicleId ||
            reading.OdometerKm != message.OdometerKm ||
            reading.EngineTempC != message.EngineTempC ||
            !string.Equals(
                reading.FaultCode,
                message.FaultCode?.Trim().ToUpperInvariant(),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Kafka-событие не совпадает с данными PostgreSQL.");
        }

        // 5. Блокируем строку автомобиля в PostgreSQL.
        // Это защищает последовательную обработку машины
        // при возможной конкуренции экземпляров Worker.
        var vehicles = await _db.Vehicles
            .FromSqlInterpolated(
                $"SELECT * FROM \"Vehicles\" WHERE \"Id\" = {message.VehicleId} FOR UPDATE")
            .ToListAsync(ct);

        var vehicle = vehicles.SingleOrDefault()
            ?? throw new InvalidOperationException(
                "Автомобиль из события не найден.");

        // 6. Повторно проверяем идемпотентность после
        // получения блокировки строки автомобиля.
        alreadyProcessed = await _db.ProcessedMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.MessageId == message.EventId &&
                     x.HandlerName == HandlerName,
                ct);

        if (alreadyProcessed)
        {
            await transaction.CommitAsync(ct);

            return new TelemetryProcessingResult(
                TelemetryProcessingOutcome.Duplicate, 0);
        }

        // 7. Загружаем существующие заявки автомобиля.
        var existingCases = await _db.MaintenanceCases
            .AsNoTracking()
            .Where(x => x.VehicleId == vehicle.Id)
            .ToListAsync(ct);

        // Пробег автомобиля не должен уменьшаться.
        // Для старого события сохраняем текущий максимум,
        // но не теряем его код неисправности.
        TelemetryReading effectiveReading = reading;

        if (reading.OdometerKm < vehicle.CurrentOdometerKm)
        {
            _logger.LogWarning(
                "Устаревший пробег. EventId={EventId}, " +
                "ReadingKm={ReadingKm}, CurrentKm={CurrentKm}",
                reading.EventId,
                reading.OdometerKm,
                vehicle.CurrentOdometerKm);

            effectiveReading = new TelemetryReading(
                reading.EventId,
                reading.VehicleId,
                reading.OccurredAt,
                vehicle.CurrentOdometerKm,
                reading.EngineTempC,
                reading.FaultCode);
        }

        // 8. Применяем существующие бизнес-правила.
        var newCases = _maintenance.Evaluate(
            vehicle,
            effectiveReading,
            existingCases,
            DateTimeOffset.UtcNow);

        // 9. Регистрируем новые заявки.
        _db.MaintenanceCases.AddRange(newCases);

        // 10. Регистрируем успешную обработку события.
        _db.ProcessedMessages.Add(
            new ProcessedMessage(
                message.EventId,
                HandlerName,
                DateTimeOffset.UtcNow));

        // 11. Одним SaveChanges сохраняем:
        // - изменение пробега Vehicle;
        // - новые MaintenanceCases;
        // - ProcessedMessage.
        await _db.SaveChangesAsync(ct);

        // 12. Подтверждаем транзакцию PostgreSQL.
        await transaction.CommitAsync(ct);

        return new TelemetryProcessingResult(
            TelemetryProcessingOutcome.Processed,
            newCases.Count);
    }
}

public enum TelemetryProcessingOutcome
{
    Processed,
    Duplicate,
    UnknownReading
}

public sealed record TelemetryProcessingResult(
    TelemetryProcessingOutcome Outcome,
    int CreatedCases);
