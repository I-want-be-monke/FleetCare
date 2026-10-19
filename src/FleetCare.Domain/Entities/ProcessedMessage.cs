namespace FleetCare.Domain.Entities;

public sealed class ProcessedMessage
{
    public Guid MessageId { get; private set; }

    public string HandlerName { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }

    public ProcessedMessage(
        Guid messageId,
        string handlerName,
        DateTimeOffset processedAt)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException(
                "MessageId обязателен.",
                nameof(messageId));

        if (string.IsNullOrWhiteSpace(handlerName))
            throw new ArgumentException(
                "Имя обработчика обязательно.",
                nameof(handlerName));

        MessageId = messageId;
        HandlerName = handlerName;
        ProcessedAt = processedAt.ToUniversalTime();
    }
}