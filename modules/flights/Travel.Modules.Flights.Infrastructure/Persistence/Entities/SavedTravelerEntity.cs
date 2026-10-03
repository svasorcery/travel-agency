namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class SavedTravelerEntity
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid Revision { get; set; }
    public string ProtectedDetailsJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public override string ToString() => nameof(SavedTravelerEntity);
}
