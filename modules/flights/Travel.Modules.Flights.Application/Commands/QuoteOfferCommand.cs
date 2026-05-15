using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Commands;

/// <summary>
/// Quote an offer. When <paramref name="AggregateId"/> is supplied and refers to
/// an existing stream in OfferQuoted state, the handler appends an OfferReQuoted
/// event to that stream instead of starting a new one. Otherwise a new booking
/// stream is created via OfferQuoted.
/// </summary>
public sealed record QuoteOfferCommand(
    string ProviderOfferRef,
    ProviderId Provider,
    Guid? AggregateId = null
);

public sealed record QuotedOfferResult(Guid AggregateId, BookableOffer Offer);
