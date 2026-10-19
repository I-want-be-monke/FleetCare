namespace FleetCare.Domain.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; } = Guid.NewGuid();

    public string EventType { get; }

    public string PayloadJson { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public OutboxMessage(
        string eventType,
        string payloadJson,
        DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException(
                "Тип события обязателен.",
                nameof(eventType));

        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException(
                "Содержимое события обязательно.",
                nameof(payloadJson));

        EventType = eventType;
        PayloadJson = payloadJson;
        CreatedAt = createdAt.ToUniversalTime();
    }

    public void MarkPublished(DateTimeOffset publishedAt)
    {
        if (PublishedAt is not null)
            throw new InvalidOperationException(
                "Сообщение уже опубликовано.");

        if (publishedAt < CreatedAt)
            throw new ArgumentException(
                "Дата публикации не может быть раньше даты создания.",
                nameof(publishedAt));

        PublishedAt = publishedAt.ToUniversalTime();
    }
}