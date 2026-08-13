using Wolverine.Attributes;

namespace Travel.IntegrationContracts.AI.NlSearch;

public static class NlSearchMessageIdentity
{
    public const string Requested = "travel.ai.nl-search.requested";
    public const string Parsed = "travel.ai.nl-search.parsed";
    public const int Version = 1;
}

[MessageIdentity(NlSearchMessageIdentity.Requested, Version = NlSearchMessageIdentity.Version)]
public sealed record NlSearchRequested(string Query, Guid CorrelationId, string Locale = "ru");

[MessageIdentity(NlSearchMessageIdentity.Parsed, Version = NlSearchMessageIdentity.Version)]
public sealed record NlSearchParsed(
    Guid CorrelationId,
    string Origin,
    string Destination,
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    int PassengerCount,
    string CabinClass,
    string Currency,
    int InputTokens = 0,
    int OutputTokens = 0,
    decimal CostUsd = 0m,
    string ModelId = ""
);
