export type FlightPassengerCount = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;
export type FlightJourneyKind = 'one-way' | 'round-trip' | 'multi-leg';

export interface FlightSearchV2Request {
  legs: { origin: string; destination: string; departureDate: string }[];
  passengerCount: FlightPassengerCount;
  cabinClass: 'economy';
}

export interface FlightSearchRequest {
  origin: string;
  destination: string;
  departureDate: string;
  returnDate: string | null;
  passengerCount: FlightPassengerCount;
  cabinClass: 'economy';
}

export interface FlightSegment {
  origin: string;
  destination: string;
  departAt: string;
  arriveAt: string;
  carrierCode: string;
  flightNumber: string;
  cabinClass: string;
}

export interface FlightSlice {
  origin: string;
  destination: string;
  segments: FlightSegment[];
  duration: string;
}

export interface FlightItinerary {
  journeyKind?: FlightJourneyKind;
  slices: FlightSlice[];
  totalDuration: string;
  isRoundTrip: boolean;
}

interface FlightOfferBase {
  id: string;
  provider: string;
  totalAmount: number;
  currency: string;
  itinerary: FlightItinerary;
  fetchedAt: string;
}

export interface BookableFlightOffer extends FlightOfferBase {
  passengerCount: FlightPassengerCount;
  holdEligible: boolean;
  holdIneligibilityReason: null | 'hold-not-supported' | 'identity-documents-required' | 'capability-unknown';
  expiresAt: string;
  providerOfferRef: string;
  deeplinkUrl: null;
  partnerName: null;
}

export interface PartnerFlightOffer extends FlightOfferBase {
  passengerCount: null;
  holdEligible: null;
  holdIneligibilityReason: null;
  expiresAt: null;
  providerOfferRef: null;
  deeplinkUrl: string;
  partnerName: string;
}

export type FlightOffer = BookableFlightOffer | PartnerFlightOffer;

export interface FlightPartialFailure {
  provider: string;
  errorCode: string;
  elapsedMs: number;
}

export interface FlightSearchResponse {
  offers: FlightOffer[];
  partialFailures: FlightPartialFailure[];
  skippedProviders: { provider: string; reasonCode: 'passenger-count-unsupported' | 'journey-unsupported' }[];
  ranking?: FlightRanking | null;
}

export interface FlightRankingEntry {
  offerId: string;
  currency: string;
  rank: number;
  sourceAmount: number;
  sourceCurrency: string;
  priceState: 'native' | 'converted' | 'fx-unavailable';
  durationSeconds: number | null;
  transfers: number | null;
  limitations: ('partial-itinerary' | 'fx-unavailable')[];
}

export interface FlightRanking {
  policy: 'price-first-v1';
  requestedCurrency: string;
  entries: FlightRankingEntry[];
}
