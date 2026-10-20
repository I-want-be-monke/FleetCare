
using System.Text.Json;
using Confluent.Kafka;
using FleetCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetCare.Api.Background;

public sealed class OutboxPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        string bootstrapServers =
            _configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException(
                "Не задан Kafka:BootstrapServers.");

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,

            // Ждём подтверждение от всех доступных ISR.
            Acks = Acks.All,

            // Защита от части дублей при внутренних
            // повторных попытках самого producer.
            EnableIdempotence = true,

            // Не блокируем обработку надолго,
            // если Kafka выключена.
            MessageTimeoutMs = 10000
        };

        using var producer =
            new ProducerBuilder<string, string>(
                producerConfig).Build();

        _logger.LogInformation(
            "OutboxPublisher запущен. Kafka: {Servers}",
            bootstrapServers);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    var db = scope.ServiceProvider
                        .GetRequiredService<FleetCareDbContext>();

                    // Берём только неопубликованные сообщения.
                    var messages = await db.OutboxMessages
                        .Where(x => x.PublishedAt == null)
                        .OrderBy(x => x.CreatedAt)
                        .ThenBy(x => x.Id)
                        .Take(25)
                        .ToListAsync(stoppingToken);

                    foreach (var message in messages)
                    {
                        // Из JSON берём vehicleId.
                        // Это ключ Kafka из контракта шага 5.
                        using var document =
                            JsonDocument.Parse(message.PayloadJson);

                        string vehicleId = document.RootElement
                            .GetProperty("vehicleId")
                            .GetGuid()
                            .ToString("D");

                        var kafkaMessage =
                            new Message<string, string>
                            {
                                Key = vehicleId,
                                Value = message.PayloadJson
                            };

                        // Пока Kafka не подтвердила отправку,
                        // PublishedAt остаётся null.
                        var delivery = await producer.ProduceAsync(
                            message.EventType,
                            kafkaMessage,
                            stoppingToken);

                        // Kafka подтвердила доставку.
                        message.MarkPublished(
                            DateTimeOffset.UtcNow);

                        await db.SaveChangesAsync(stoppingToken);

                        _logger.LogInformation(
                            "Outbox {OutboxId} опубликован: " +
                            "topic={Topic}, vehicleId={VehicleId}, " +
                            "offset={Offset}",
                            message.Id,
                            message.EventType,
                            vehicleId,
                            delivery.TopicPartitionOffset);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ProduceException<string, string> ex)
                {
                    // Kafka недоступна или не подтвердила доставку.
                    // Сообщение остаётся в outbox.
                    _logger.LogWarning(
                        ex,
                        "Ошибка публикации в Kafka. " +
                        "Будет повторная попытка.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Ошибка OutboxPublisher. " +
                        "Будет повторная попытка.");
                }

                // Периодический опрос базы.
                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка приложения.
        }

        _logger.LogInformation("OutboxPublisher остановлен.");
    }
}
