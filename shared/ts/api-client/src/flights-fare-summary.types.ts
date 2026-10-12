export type FlightFareSummaryAiStatus =
  | 'Disabled'
  | 'NotRequested'
  | 'NotAuthenticated'
  | 'UnsupportedMode'
  | 'InputUnavailable'
  | 'Busy'
  | 'ModelUnavailable'
  | 'InferenceFailed'
  | 'InvalidOutput'
  | 'UsageUnknown'
  | 'AccountingUnavailable'
  | 'DeadlineExceeded'
  | 'BudgetExceeded'
  | 'SourceStale'
  | 'FreshnessUnknown'
  | 'Ready';

export interface FlightFareCondition {
  permission: 'Unknown' | 'Allowed' | 'NotAllowed';
  penalty: { amount: string; currency: string } | null;
  permissionReason: 'Known' | 'NotProvided' | 'InvalidSupplierData';
  penaltyReason: 'Known' | 'NotProvided' | 'InvalidSupplierData';
}
export interface FlightFareSummary {
  schemaVersion: 1;
  aggregateId: string;
  offerId: string | null;
  quoteRevision: string | null;
  fingerprint: string | null;
  state: 'Current' | 'Stale' | 'Unavailable';
  reason: string | null;
  provenance: 'QuotedOffer' | 'Historical';
  sourceObservedAt: string | null;
  offerExpiresAt: string | null;
  firstDepartureAt: string | null;
  serverObservedAt: string;
  passengerCount: number;
  segmentCounts: number[];
  aiAvailable: boolean;
  aiStatus: FlightFareSummaryAiStatus;
  presentation: 'Deterministic' | 'AiOrganized';
  changeBeforeDeparture: FlightFareCondition | null;
  refundBeforeDeparture: FlightFareCondition | null;
  sections: { kind: 'change' | 'refund' | 'baggage'; lines: { factIds: string[]; scope: string; text: string }[] }[];
  legs: { leg: number; fareBrandName: string | null; changeBeforeDeparture: FlightFareCondition }[];
  passengers: {
    leg: number;
    segment: number;
    adult: number;
    fareBasisCode: string | null;
    cabinClassMarketing: string | null;
    checkedBaggageQuantity: number | null;
    carryOnBaggageQuantity: number | null;
  }[];
  missingInformation: string[];
  notices: string[];
}
export interface FlightFareSummaryBinding {
  aggregateId: string;
  offerId?: string;
  quoteRevision?: string;
  offerExpiresAt?: string;
  firstDepartureAt?: string;
  passengerCount: number;
  segmentCounts?: number[];
  historical?: boolean;
}
