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
