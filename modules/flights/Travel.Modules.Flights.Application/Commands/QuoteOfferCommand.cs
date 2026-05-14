using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Application.Commands;

public sealed record QuoteOfferCommand(string ProviderOfferRef, ProviderId Provider);

public sealed record QuotedOfferResult(Guid AggregateId, BookableOffer Offer);
