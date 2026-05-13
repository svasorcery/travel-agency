using Travel.Shared.Abstractions;

namespace Travel.Shared.Domain;

public abstract class AggregateRoot<TId>
    where TId : notnull
{
    private readonly List<IDomainEvent> _events = new();

    public TId Id { get; protected set; } = default!;

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    protected void Raise(IDomainEvent @event) => _events.Add(@event);

    public void ClearEvents() => _events.Clear();
}
