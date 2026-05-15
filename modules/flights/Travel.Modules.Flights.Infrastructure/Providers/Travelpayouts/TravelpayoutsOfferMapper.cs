using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

public static class TravelpayoutsOfferMapper
{
    public static ErrorOr<DeeplinkOffer> Map(
        PriceEntryDto entry,
        SearchCriteria criteria,
        TravelpayoutsDeeplinkBuilder deeplink,
        TimeProvider time
    )
    {
        // Money — currency from criteria is authoritative
        var moneyResult = Money.Create(entry.Price, criteria.Currency);
        if (moneyResult.IsError)
            return moneyResult.FirstError;

        // Origin / destination
        var originResult = IataCode.Create(entry.Origin);
        if (originResult.IsError)
            return originResult.FirstError;

        var destinationResult = IataCode.Create(entry.Destination);
        if (destinationResult.IsError)
            return destinationResult.FirstError;

        // Carrier / flight number: required fields — skip entries that lack them
        if (string.IsNullOrWhiteSpace(entry.Airline))
            return Error.Validation(
                "TravelpayoutsOffer.MissingCarrier",
                "Travelpayouts offer is missing carrier code; skipping."
            );
        if (string.IsNullOrWhiteSpace(entry.FlightNumber))
            return Error.Validation(
                "TravelpayoutsOffer.MissingFlightNumber",
                "Travelpayouts offer is missing flight number; skipping."
            );
        var carrierCode = entry.Airline;
        var flightNumber = entry.FlightNumber;

        // Duration: fall back to +1h when zero or negative so Segment.Create invariant holds
        var durationMinutes = entry.Duration > 0 ? entry.Duration : 60;
        var arriveAt = entry.DepartureAt.AddMinutes(durationMinutes);

        var segmentResult = Segment.Create(
            originResult.Value,
            destinationResult.Value,
            entry.DepartureAt,
            arriveAt,
            carrierCode,
            flightNumber,
            CabinClass.Economy
        );
        if (segmentResult.IsError)
            return segmentResult.FirstError;

        var sliceResult = Slice.Create([segmentResult.Value]);
        if (sliceResult.IsError)
            return sliceResult.FirstError;

        var itineraryResult = Itinerary.Create([sliceResult.Value]);
        if (itineraryResult.IsError)
            return itineraryResult.FirstError;

        return new DeeplinkOffer(
            Id: OfferId.New(),
            Itinerary: itineraryResult.Value,
            TotalAmount: moneyResult.Value,
            Provider: ProviderId.Travelpayouts,
            FetchedAt: time.GetUtcNow(),
            DeeplinkUrl: deeplink.Build(criteria),
            PartnerName: "Aviasales"
        );
    }
}
