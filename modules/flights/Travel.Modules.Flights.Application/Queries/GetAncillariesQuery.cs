using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Queries;

public sealed record GetAncillariesQuery(
    Guid AggregateId,
    Guid UserId,
    Guid QuoteRevision,
    bool IncludeSeats
);

public sealed record AncillaryCatalogResult(
    Guid AggregateId,
    QuoteBinding Binding,
    AncillaryCatalogFacts Catalog
);
