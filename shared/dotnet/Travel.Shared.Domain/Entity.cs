namespace Travel.Shared.Domain;

public abstract class Entity<TId>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (GetType() != other.GetType())
            return false;
        // transient entities (Id == default) are never equal — they're not yet "the same thing"
        if (
            EqualityComparer<TId>.Default.Equals(Id, default!)
            || EqualityComparer<TId>.Default.Equals(other.Id, default!)
        )
            return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    public static bool operator ==(Entity<TId>? a, Entity<TId>? b) => Equals(a, b);

    public static bool operator !=(Entity<TId>? a, Entity<TId>? b) => !Equals(a, b);
}
