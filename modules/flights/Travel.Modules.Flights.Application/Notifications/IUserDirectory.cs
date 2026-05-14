namespace Travel.Modules.Flights.Application.Notifications;

public sealed record UserProfile(
    Guid Id,
    string Email,
    string GivenName,
    string FamilyName,
    string? Locale
);

public interface IUserDirectory
{
    Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct);
}
