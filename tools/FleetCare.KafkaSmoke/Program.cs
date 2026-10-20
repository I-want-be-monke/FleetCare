
using System.Text.Json;
using Confluent.Kafka;

// Kafka работает в Docker и доступна с Windows.
const string bootstrapServers = "localhost:9092";

const string telemetryTopic = "telemetry.recorded.v1";
const string riskTopic = "risk.detected.v1";

var jsonOptions = new JsonSerializerOptions(
    JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

// Идентификаторы тестовых событий.
var vehicleId = Guid.NewGuid();
var eventId = Guid.NewGuid();

// 1. Создаём тестовую телеметрию.
var telemetry = new TelemetryRecordedV1(
    EventId: eventId,
    VehicleId: vehicleId,
    OccurredAt: DateTimeOffset.UtcNow,
    OdometerKm: 25000,
    EngineTempC: 96.5,
    FaultCode: "P0420"
);

Console.WriteLine("=== Проверка телеметрии ===");

string receivedTelemetryJson = await RoundTripAsync(
    telemetryTopic,
    vehicleId.ToString("D"),
    JsonSerializer.Serialize(telemetry, jsonOptions),
    "eventId",
    eventId,
    new[]
    {
        "eventId",
        "vehicleId",
        "occurredAt",
        "odometerKm",
        "engineTempC",
        "faultCode"
    });

var receivedTelemetry =
    JsonSerializer.Deserialize<TelemetryRecordedV1>(
        receivedTelemetryJson,
        jsonOptions);

if (receivedTelemetry != telemetry)
{
    throw new Exception(
        "Полученная телеметрия отличается от отправленной.");
}

Console.WriteLine("PASS: все поля телеметрии совпадают.");

// 2. Создаём тестовый риск.
// sourceEventId ссылается на телеметрию выше.
var risk = new RiskDetectedV1(
    RiskId: Guid.NewGuid(),
    SourceEventId: eventId,
    VehicleId: vehicleId,
    Kind: "EngineTemperatureTrend",
    Score: 0.93,
    Explanation: "Обнаружен устойчивый рост температуры.",
    DetectedAt: DateTimeOffset.UtcNow
);

Console.WriteLine();
Console.WriteLine("=== Проверка риска ===");

string receivedRiskJson = await RoundTripAsync(
    riskTopic,
    vehicleId.ToString("D"),
    JsonSerializer.Serialize(risk, jsonOptions),
    "riskId",
    risk.RiskId,
    new[]
    {
        "riskId",
        "sourceEventId",
        "vehicleId",
        "kind",
        "score",
        "explanation",
        "detectedAt"
    });

var receivedRisk =
    JsonSerializer.Deserialize<RiskDetectedV1>(
        receivedRiskJson,
        jsonOptions);

if (receivedRisk != risk)
{
    throw new Exception(
        "Полученный риск отличается от отправленного.");
}

if (receivedRisk.SourceEventId != receivedTelemetry.EventId)
{
    throw new Exception(
        "Риск не связан с исходной телеметрией.");
}

Console.WriteLine("PASS: все поля риска совпадают.");
Console.WriteLine();
Console.WriteLine("SUCCESS: шаг 5 — Kafka работает.");

// Отправляем событие в Kafka и читаем его обратно.
static async Task<string> RoundTripAsync(
    string topic,
    string key,
    string payload,
    string idField,
    Guid expectedId,
    string[] requiredFields)
{
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true
    };

    using var producer =
        new ProducerBuilder<string, string>(
            producerConfig).Build();

    var delivery = await producer.ProduceAsync(
        topic,
        new Message<string, string>
        {
            Key = key,
            Value = payload
        });

    Console.WriteLine(
        $"Отправлено: {delivery.TopicPartitionOffset}");

    var consumerConfig = new ConsumerConfig
    {
        BootstrapServers = bootstrapServers,

        // Отдельная учебная группа для каждой проверки.
        GroupId = "fleetcare-step5-" +
                  Guid.NewGuid().ToString("N"),

        AutoOffsetReset = AutoOffsetReset.Earliest,

        EnableAutoCommit = false
    };

    using var consumer =
        new ConsumerBuilder<string, string>(
            consumerConfig).Build();

    consumer.Subscribe(topic);

    var deadline = DateTime.UtcNow.AddSeconds(30);

    while (DateTime.UtcNow < deadline)
    {
        var result = consumer.Consume(
            TimeSpan.FromSeconds(2));

        if (result is null)
            continue;

        using var document =
            JsonDocument.Parse(result.Message.Value);

        var root = document.RootElement;

        if (!root.TryGetProperty(idField, out var id))
            continue;

        // Пропускаем старые тестовые сообщения.
        if (id.GetGuid() != expectedId)
            continue;

        if (result.Message.Key != key)
        {
            throw new Exception(
                "Неверный Kafka message key.");
        }

        foreach (var field in requiredFields)
        {
            if (!root.TryGetProperty(field, out _))
            {
                throw new Exception(
                    $"В сообщении отсутствует поле: {field}");
            }
        }

        Console.WriteLine(
            $"Получено: {result.TopicPartitionOffset}");

        Console.WriteLine("Ключ: " + result.Message.Key);

        Console.WriteLine("JSON:");
        Console.WriteLine(result.Message.Value);

        consumer.Close();

        return result.Message.Value;
    }

    consumer.Close();

    throw new TimeoutException(
        $"Не удалось прочитать тестовое сообщение из {topic}.");
}

// Контракт первого события.
public sealed record TelemetryRecordedV1(
    Guid EventId,
    Guid VehicleId,
    DateTimeOffset OccurredAt,
    int OdometerKm,
    double EngineTempC,
    string? FaultCode
);

// Контракт второго события.
public sealed record RiskDetectedV1(
    Guid RiskId,
    Guid SourceEventId,
    Guid VehicleId,
    string Kind,
    double Score,
    string Explanation,
    DateTimeOffset DetectedAt
);
