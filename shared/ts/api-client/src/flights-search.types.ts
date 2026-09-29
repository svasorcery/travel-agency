export interface FlightSearchRequest {
  origin: string;
  destination: string;
  departureDate: string;
  returnDate: string | null;
  passengerCount: 1;
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
  expiresAt: string;
  providerOfferRef: string;
  deeplinkUrl: null;
  partnerName: null;
}

export interface PartnerFlightOffer extends FlightOfferBase {
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
}
