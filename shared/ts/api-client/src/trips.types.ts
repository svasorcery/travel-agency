export const tripCities = ['moscow', 'saint-petersburg', 'kaliningrad'] as const;
export const tripCurrencies = ['RUB', 'EUR', 'USD'] as const;
export const tripInterests = ['architecture', 'culture', 'food', 'nature', 'history'] as const;
export const tripPaces = ['relaxed', 'balanced', 'active'] as const;
export const tripWarnings = ['OverlappingTimes', 'KnownEstimateExceedsBudget'] as const;
export type TripCity = (typeof tripCities)[number];
export type TripCurrency = (typeof tripCurrencies)[number];
export type TripInterest = (typeof tripInterests)[number];
export type TripPace = (typeof tripPaces)[number];
export type TripWarning = (typeof tripWarnings)[number];
export interface TripBrief {
  destinationId: TripCity;
  originId: TripCity | null;
  startDate: string;
  endDate: string;
  adults: number;
  currency: TripCurrency;
  totalBudget: string | null;
  interests: TripInterest[];
  pace: TripPace;
}
export interface TripEstimate {
  amount: string;
  currency: TripCurrency;
}
export interface TripBlock {
  blockId: string;
  title: string;
  note: string | null;
  startTime: string | null;
  endTime: string | null;
  estimate: TripEstimate | null;
}
export interface TripDay {
  date: string;
  blocks: TripBlock[];
}
export interface CreateTrip {
  title: string;
  brief: TripBrief;
}
export interface ReplaceTrip extends CreateTrip {
  days: TripDay[];
}
export interface TripDestination {
  destinationId: TripCity;
  displayName: string;
  timeZone: 'Europe/Moscow' | 'Europe/Kaliningrad';
  catalogueVersion: string;
}
export interface TripCatalogue {
  catalogueVersion: string;
  items: TripDestination[];
}
export interface TripPlan extends ReplaceTrip {
  tripId: string;
  revision: string;
  destination: TripDestination;
  createdAt: string;
  updatedAt: string;
  knownUserEstimateSubtotal: string;
  unknownEstimateCount: number;
  warnings: TripWarning[];
}
export interface TripSummary {
  tripId: string;
  revision: string;
  title: string;
  destinationId: TripCity;
  displayName: string;
  startDate: string;
  endDate: string;
  updatedAt: string;
}
export interface TripPage {
  items: TripSummary[];
  nextCursor: string | null;
}
