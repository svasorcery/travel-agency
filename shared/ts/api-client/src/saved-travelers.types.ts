import type { FlightPassengerInfo } from './flights-booking.types';
export type SavedTravelerDetails = Omit<FlightPassengerInfo, 'bookingPassengerId'>;
export interface SavedTravelerReceipt {
  id: string;
  revision: string;
}
export interface SavedTraveler extends SavedTravelerReceipt {
  details: SavedTravelerDetails;
}
export interface SavedTravelerPage {
  items: SavedTraveler[];
  offset: number;
  hasMore: boolean;
}
export type SavedTravelerField = keyof SavedTravelerDetails;
export interface SavedTravelerFieldError {
  field: SavedTravelerField;
  code: string;
}
