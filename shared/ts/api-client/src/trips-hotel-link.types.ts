export interface TripHotelLinkAvailability {
  enabled: boolean;
  canAttach: boolean;
}

export interface TripHotelLink {
  hasLink: boolean;
  revision: string;
}

export type TripHotelLinkOperation = { op: 'add'; reservationId: string } | { op: 'remove' };

export interface ConfirmedTripHotelLink {
  revision: string;
  state: 'confirmed';
  reservationId: string;
  propertyName: string;
  checkIn: string;
  checkOut: string;
}

export type TripHotelLinkStatus =
  | ConfirmedTripHotelLink
  | { revision: string; state: 'empty' | 'cancelled' | 'reviewRequired' | 'unavailable' };
