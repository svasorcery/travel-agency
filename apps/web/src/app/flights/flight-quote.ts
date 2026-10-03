import type { BookableFlightOffer, FlightItinerary, FlightQuoteResponse } from '@travel/api-client';
import type { BookableOfferView } from './flight-results';

export interface QuoteIntent {
  source: BookableFlightOffer | null;
  previousQuote?: BookableFlightOffer;
  provider: string;
  providerOfferRef: string;
  aggregateId: string | null;
}

export type QuoteState =
  | { kind: 'idle' }
  | { kind: 'loading'; intent: QuoteIntent }
  | {
      kind: 'ready';
      intent: QuoteIntent;
      quote: FlightQuoteResponse;
      view: BookableOfferView;
      searchView: BookableOfferView | null;
      routeChanged: boolean;
      changed: boolean;
      requiresAcceptance: boolean;
      accepted: boolean;
    }
  | { kind: 'error'; intent: QuoteIntent; message: string };

function routeFacts(itinerary: FlightItinerary): string {
  return JSON.stringify({
    totalDuration: itinerary.totalDuration,
    isRoundTrip: itinerary.isRoundTrip,
    slices: itinerary.slices.map((slice) => ({
      origin: slice.origin,
      destination: slice.destination,
      duration: slice.duration,
      segments: slice.segments.map((segment) => ({
        origin: segment.origin,
        destination: segment.destination,
        departAt: segment.departAt,
        arriveAt: segment.arriveAt,
        carrierCode: segment.carrierCode,
        flightNumber: segment.flightNumber,
        cabinClass: segment.cabinClass,
      })),
    })),
  });
}

export function itineraryDiffersFromSearch(searched: BookableFlightOffer, quoted: BookableFlightOffer): boolean {
  return routeFacts(searched.itinerary) !== routeFacts(quoted.itinerary);
}

export function quoteDiffersFromSearch(searched: BookableFlightOffer, quoted: BookableFlightOffer): boolean {
  return (
    searched.totalAmount !== quoted.totalAmount ||
    searched.currency !== quoted.currency ||
    itineraryDiffersFromSearch(searched, quoted)
  );
}
