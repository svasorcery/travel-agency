using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record FareSummaryConditionDto(
    string Permission,
    MoneyStringDto? Penalty,
    string PermissionReason,
    string PenaltyReason
)
{
    public static FareSummaryConditionDto? From(FareCondition? condition) =>
        condition is null
            ? null
            : new(
                condition.Permission.ToString(),
                condition.Penalty is { } penalty ? MoneyStringDto.From(penalty) : null,
                condition.PermissionReason,
                condition.PenaltyReason
            );
}

public sealed record FareSummaryLegDto(
    int Leg,
    string? FareBrandName,
    FareSummaryConditionDto ChangeBeforeDeparture
);

public sealed record FareSummaryPassengerDto(
    int Leg,
    int Segment,
    int Adult,
    string? FareBasisCode,
    string? CabinClassMarketing,
    int? CheckedBaggageQuantity,
    int? CarryOnBaggageQuantity
);

public sealed record FareSummaryResponse(
    int SchemaVersion,
    Guid AggregateId,
    Guid? OfferId,
    Guid? QuoteRevision,
    string? Fingerprint,
    string State,
    string? Reason,
    string Provenance,
    DateTimeOffset? SourceObservedAt,
    DateTimeOffset? OfferExpiresAt,
    DateTimeOffset? FirstDepartureAt,
    DateTimeOffset ServerObservedAt,
    int PassengerCount,
    int[] SegmentCounts,
    bool AiAvailable,
    string AiStatus,
    string Presentation,
    FareSummaryConditionDto? ChangeBeforeDeparture,
    FareSummaryConditionDto? RefundBeforeDeparture,
    FareSummarySection[] Sections,
    FareSummaryLegDto[] Legs,
    FareSummaryPassengerDto[] Passengers,
    string[] MissingInformation,
    string[] Notices
)
{
    public static FareSummaryResponse? From(FareSummaryResult? result) =>
        result is null
            ? null
            : new(
                result.SchemaVersion,
                result.AggregateId,
                result.OfferId,
                result.QuoteRevision,
                result.Fingerprint,
                result.State,
                result.Reason,
                result.Provenance,
                result.SourceObservedAt,
                result.OfferExpiresAt,
                result.FirstDepartureAt,
                result.ServerObservedAt,
                result.PassengerCount,
                result.SegmentCounts,
                result.AiAvailable,
                result.AiStatus,
                result.Presentation,
                FareSummaryConditionDto.From(result.ChangeBeforeDeparture),
                FareSummaryConditionDto.From(result.RefundBeforeDeparture),
                result.Sections,
                result
                    .Legs.Select(x => new FareSummaryLegDto(
                        x.Leg,
                        x.FareBrandName,
                        FareSummaryConditionDto.From(x.ChangeBeforeDeparture)!
                    ))
                    .ToArray(),
                result
                    .Passengers.Select(x => new FareSummaryPassengerDto(
                        x.Leg,
                        x.Segment,
                        x.Adult,
                        x.FareBasisCode,
                        x.CabinClassMarketing,
                        x.CheckedBaggageQuantity,
                        x.CarryOnBaggageQuantity
                    ))
                    .ToArray(),
                result.MissingInformation,
                result.Notices
            );
}
