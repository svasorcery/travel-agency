namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class IdempotencyKeyEntity
{
    public string Key { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Route { get; set; } = default!;
    public string BodyHash { get; set; } = default!;
    public string? ResponseHash { get; set; }
    public int ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
