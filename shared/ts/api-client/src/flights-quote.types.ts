import type { BookableFlightOffer, FlightPassengerCount } from './flights-search.types';

export interface FlightQuoteRequest {
  explanationMode?: 'organized' | 'deterministic';
  providerOfferRef: string;
  provider: string;
  aggregateId: string | null;
  passengerCount: FlightPassengerCount;
  selections?: import('./flights-ancillaries.types').FlightAncillarySelection[];
}

export interface FlightFareConditions {
  changeAllowed: boolean;
  refundAllowed: boolean;
  fareBasisCode: string | null;
  cabinClassMarketing: string | null;
  checkedBaggageQuantity: number;
  carryOnBaggageQuantity: number;
}

export interface FlightQuoteBinding {
  revision: string;
  passengerCount: FlightPassengerCount;
  firstDepartureLocalDate: string;
  slots: { bookingPassengerId: string; kind: 'adult' }[];
}

export interface FlightQuoteResponse {
  fareSummary?: import('./flights-fare-summary.types').FlightFareSummary | null;
  fareSummaryNotice?: string;
  purchase?: import('./flights-ancillaries.types').FlightPurchase | null;
  binding: FlightQuoteBinding;
  aggregateId: string;
  offer: BookableFlightOffer;
  fareConditions: FlightFareConditions;
  priceChanged: boolean;
  oldAmount: number | null;
  oldCurrency: string | null;
  newAmount: number | null;
  newCurrency: string | null;
}
