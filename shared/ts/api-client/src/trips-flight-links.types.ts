export type FlightLinkOperation = 'add' | 'remove';
export type LinkedBookingStatus = 'Held' | 'Confirmed' | 'Ticketed' | 'Cancelled' | 'Refunded';
export interface TripFlightLinks {
  tripId: string;
  linksRevision: string;
  links: { orderId: string; linkedAt: string }[];
}
export interface FlightLinkLeg {
  origin: string;
  destination: string;
  departAt: string;
  arriveAt: string;
}
export interface FlightLinkCandidate {
  orderId: string;
  bookingStatus: LinkedBookingStatus;
  passengerCount: number;
  route: FlightLinkLeg[] | null;
}
export interface FlightLinkPage {
  tripId: string;
  items: FlightLinkCandidate[];
  nextOffset: number | null;
  limitReached: boolean;
}
export interface AvailableFlightObservation extends FlightLinkCandidate {
  state: 'available';
  reviewRequired: boolean;
  observedAt: string;
  streamVersion: string;
}
export type FlightLinkObservation = AvailableFlightObservation | { orderId: string; state: 'unavailable' | 'notFound' };
export interface FlightLinkStatus {
  tripId: string;
  linksRevision: string;
  items: FlightLinkObservation[];
}
