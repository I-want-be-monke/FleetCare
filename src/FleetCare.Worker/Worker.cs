
using Confluent.Kafka;
using FleetCare.Worker.Services;

namespace FleetCare.Worker;

public sealed class Worker : BackgroundService
{
    private const string TelemetryTopic =
        "telemetry.recorded.v1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;
    private readonly ConsumerConfig _consumerConfig;

    public Worker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<Worker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        string bootstrapServers =
            configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException(
                "Не настроен Kafka:BootstrapServers.");

        string groupId =
            configuration["Kafka:GroupId"]
            ?? throw new InvalidOperationException(
                "Не настроен Kafka:GroupId.");

        if (string.IsNullOrWhiteSpace(bootstrapServers) ||
            string.IsNullOrWhiteSpace(groupId))
        {
            throw new InvalidOperationException(
                "Параметры Kafka не должны быть пустыми.");
        }

        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,

            AutoOffsetReset = AutoOffsetReset.Earliest,

            // Отключаем автоматический commit.
            EnableAutoCommit = false,

            // Не сохраняем offset автоматически при Consume.
            EnableAutoOffsetStore = false,

            AllowAutoCreateTopics = false
        };
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        // Не блокируем запуск Generic Host
        // синхронным ожиданием первого Kafka-сообщения.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var consumer =
                    new ConsumerBuilder<string, string>(
                        _consumerConfig).Build();

                try
                {
                    consumer.Subscribe(TelemetryTopic);

                    _logger.LogInformation(
                        "Worker запущен. Topic={Topic}, GroupId={GroupId}",
                        TelemetryTopic,
                        _consumerConfig.GroupId);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        var result =
                            consumer.Consume(stoppingToken);

                        if (result is null ||
                            result.IsPartitionEOF)
                        {
                            continue;
                        }

                        // Новый scope и новый DbContext
                        // для каждого сообщения.
                        using var scope =
                            _scopeFactory.CreateScope();

                        var processor =
                            scope.ServiceProvider
                                .GetRequiredService<
                                    TelemetryEventProcessor>();

                        // Сначала PostgreSQL.
                        var processingResult =
                            await processor.ProcessAsync(
                                result.Message.Key,
                                result.Message.Value,
                                stoppingToken);

                        // Только после успешного завершения
                        // обработки подтверждаем Kafka offset.
                        consumer.Commit(result);

                        _logger.LogInformation(
                            "Kafka offset подтверждён. " +
                            "EventKey={Key}, Offset={Offset}, " +
                            "Outcome={Outcome}, CreatedCases={Count}",
                            result.Message.Key,
                            result.TopicPartitionOffset,
                            processingResult.Outcome,
                            processingResult.CreatedCases);
                    }
                }
                finally
                {
                    try
                    {
                        consumer.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Ошибка закрытия Kafka Consumer.");
                    }
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка Kafka Worker. " +
                    "Пересоздание Consumer через 3 секунды.");
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(3),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation(
            "FleetCare Worker остановлен.");
    }
}
