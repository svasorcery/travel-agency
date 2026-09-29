import type { BookableFlightOffer } from './flights-search.types';

export interface FlightQuoteRequest {
  providerOfferRef: string;
  provider: string;
  aggregateId: string | null;
}

export interface FlightFareConditions {
  changeAllowed: boolean;
  refundAllowed: boolean;
  fareBasisCode: string | null;
  cabinClassMarketing: string | null;
  checkedBaggageQuantity: number;
  carryOnBaggageQuantity: number;
}

export interface FlightQuoteResponse {
  aggregateId: string;
  offer: BookableFlightOffer;
  fareConditions: FlightFareConditions;
  priceChanged: boolean;
  oldAmount: number | null;
  oldCurrency: string | null;
  newAmount: number | null;
  newCurrency: string | null;
}
