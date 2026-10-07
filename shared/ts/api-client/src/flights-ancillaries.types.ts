import type { FlightItinerary } from './flights-search.types';

export interface FlightMoney {
  amount: string;
  currency: string;
}
export interface FlightSegmentAddress {
  leg: number;
  segment: number;
}
export interface FlightBaggageLimits {
  maximumWeightKg: number | null;
  maximumHeightCm: number | null;
  maximumDepthCm: number | null;
  maximumLengthCm: number | null;
}
export interface FlightAncillarySelection {
  selectionKey: string;
  quantity: number;
}
export interface FlightPurchasedService {
  selectionKey: string;
  kind: 'seat' | 'checked-baggage';
  bookingPassengerId: string;
  segments: FlightSegmentAddress[];
  quantity: number;
  lineTotal: FlightMoney;
  seatDesignator: string | null;
  disclosures: string[] | null;
  baggage: FlightBaggageLimits | null;
}
export interface FlightPurchase {
  quoteRevision: string;
  baseFare: FlightMoney;
  extras: FlightMoney;
  total: FlightMoney;
  services: FlightPurchasedService[];
  expiresAt: string;
  noticeVersion: 'booking-services-v1';
}
export interface FlightAncillaryService {
  selectionKey: string;
  kind: 'seat' | 'checked-baggage' | null;
  bookingPassengerIds: string[];
  segments: FlightSegmentAddress[];
  unitPrice: FlightMoney | null;
  maximumQuantity: number;
  selectable: boolean;
  reason: string | null;
  name: string | null;
  seatDesignator: string | null;
  physicalSeat: string | null;
  disclosures: string[] | null;
  baggage: FlightBaggageLimits | null;
}
export interface FlightSeatMap {
  segment: FlightSegmentAddress;
  cabins: {
    deck: number;
    cabin: string;
    rows: {
      sections: {
        elements: { kind: string; designator: string | null; serviceKeys: string[] }[];
      }[];
    }[];
  }[];
}
export interface FlightAncillaryCatalog {
  aggregateId: string;
  quoteRevision: string;
  baseFare: FlightMoney;
  expiresAt: string;
  services: FlightAncillaryService[];
  allowances: {
    bookingPassengerId: string;
    segment: FlightSegmentAddress;
    checkedQuantity: number | null;
    carryOnQuantity: number | null;
  }[];
  seatMaps: FlightSeatMap[];
  seatsUnavailable: boolean;
  unsupportedPricing: boolean;
}
export interface FlightAncillaryCatalogRequest {
  aggregateId: string;
  quoteRevision: string;
  includeSeats: boolean;
}
export interface FlightCreationStatus {
  aggregateId: string;
  state: 'NotStarted' | 'InProgress' | 'Matches' | 'CreatedWithDifferences' | 'NotCreated' | 'ManualReviewRequired';
  bookingStatus: 'OfferQuoted' | 'Held' | 'Confirmed' | 'Ticketed' | 'Cancelled' | 'Refunded';
  passengerCount: number;
  bookingPassengerIds: string[];
  itinerary: FlightItinerary | null;
  accepted: FlightPurchase | null;
  actual: { total: FlightMoney; services: FlightPurchasedService[] } | null;
  heldUntil: string | null;
  canConfirm: boolean;
  canCancel: boolean;
  canRefresh: boolean;
  observedAt: string;
}
