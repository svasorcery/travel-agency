namespace Travel.Shared.Abstractions;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
