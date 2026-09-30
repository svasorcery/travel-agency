export interface FlightPassengerInfo {
  givenName: string;
  familyName: string;
  dateOfBirth: string;
  gender: 'male' | 'female' | 'unspecified';
  email: string;
  phone: string;
}

export interface HoldFlightOrderRequest {
  aggregateId: string;
  passengers: [FlightPassengerInfo];
}

export interface HeldFlightOrderResponse {
  aggregateId: string;
  providerOrderId: string;
  heldUntil: string;
}

export interface ConfirmFlightOrderRequest {
  aggregateId: string;
}

export interface ConfirmedFlightOrderResponse {
  aggregateId: string;
  status: 'Confirmed';
  paymentRef: string | null;
}

import type { FlightItinerary } from './flights-search.types';

export type FlightOrderStatus = 'Held' | 'Confirmed' | 'Ticketed' | 'Cancelled' | 'Refunded';

export interface FlightOrderResponse {
  aggregateId: string;
  status: FlightOrderStatus;
  totalAmount: number;
  currency: string;
  itinerary: FlightItinerary;
  ticketNumbers: string[];
  bookedAt: string;
  ticketedAt: string | null;
  cancelledAt: string | null;
  refundedAt: string | null;
}
