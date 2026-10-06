export interface FlightPassengerInfo {
  bookingPassengerId: string;
  title: 'mr' | 'ms' | 'mrs' | 'miss' | 'dr';
  givenName: string;
  familyName: string;
  dateOfBirth: string;
  gender: 'male' | 'female';
  email: string;
  phone: string;
}

export interface HoldFlightOrderRequest {
  aggregateId: string;
  quoteRevision: string;
  passengers: FlightPassengerInfo[];
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
  status: 'Confirmed' | 'Ticketed' | 'Cancelled' | 'Refunded';
  paymentRef: string | null;
}

import type { FlightItinerary } from './flights-search.types';

export type FlightOrderStatus = 'Held' | 'Confirmed' | 'Ticketed' | 'Cancelled' | 'Refunded';

export interface FlightOrderResponse {
  passengerCount: import('./flights-search.types').FlightPassengerCount;
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

export type CancelledFlightOrderResponse = FlightOrderResponse & { status: 'Cancelled' | 'Refunded' };

export interface FlightOrderListResponse {
  items: FlightOrderResponse[];
  limit: number;
  offset: number;
}
