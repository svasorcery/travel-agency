using System.Text.Json.Serialization;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavedTravelerDetailsDto(
    string Title,
    string GivenName,
    string FamilyName,
    DateOnly DateOfBirth,
    string Gender,
    string Email,
    string Phone
)
{
    public static SavedTravelerDetailsDto From(SavedTravelerDetails details) =>
        new(
            details.Title.Code,
            details.Passenger.GivenName,
            details.Passenger.FamilyName,
            details.Passenger.DateOfBirth,
            details.Passenger.Gender.Code,
            details.Passenger.Email,
            details.Passenger.Phone.Value
        );

    public override string ToString() => nameof(SavedTravelerDetailsDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavedTravelerViewDto(Guid Id, Guid Revision, SavedTravelerDetailsDto Details)
{
    public static SavedTravelerViewDto From(SavedTravelerView view) =>
        new(view.Id.Value, view.Revision, SavedTravelerDetailsDto.From(view.Details));

    public override string ToString() => nameof(SavedTravelerViewDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavedTravelerPageDto(SavedTravelerViewDto[] Items, int Offset, bool HasMore)
{
    public static SavedTravelerPageDto From(SavedTravelerPage page) =>
        new(page.Items.Select(SavedTravelerViewDto.From).ToArray(), page.Offset, page.HasMore);

    public override string ToString() => nameof(SavedTravelerPageDto);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SavedTravelerReceiptDto(Guid Id, Guid Revision);
