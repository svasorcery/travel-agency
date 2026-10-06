import type { FlightOrderStatus } from './flights-booking.types';
import type { FlightItinerary, FlightPassengerCount } from './flights-search.types';

export const cancellationPhases = [
  'Preparing',
  'TermsReady',
  'Accepted',
  'DispatchClaimed',
  'Unknown',
  'ManualReviewRequired',
  'Succeeded',
  'Rejected',
  'Abandoned',
  'Expired',
  'UnsupportedTerms',
] as const;
export const cancellationOutcomes = ['None', 'Succeeded', 'Rejected', 'Unknown'] as const;
export const cancellationSources = ['None', 'TravelAdmission', 'SupplierApi', 'OperatorVerified'] as const;
export const cancellationStages = ['None', 'Preparation', 'Confirmation'] as const;
export const cancellationReasons = [
  'None',
  'MissingTerms',
  'UnsupportedFinancialTerms',
  'TermsExpired',
  'ProviderUnavailable',
  'IdentityMismatch',
  'Pending',
  'Uncorrelated',
  'StaleProposal',
  'NotCancellable',
  'AlreadyCancelled',
  'InconsistentEvidence',
  'ManualVerificationRequired',
  'InvalidResponse',
  'RefreshTooSoon',
] as const;
export const refundDestinations = [
  'Balance',
  'Card',
  'ArcBspCash',
  'AwaitingPayment',
  'OriginalFormOfPayment',
] as const;
export type CancellationPhase = (typeof cancellationPhases)[number];
export type CancellationOutcome = (typeof cancellationOutcomes)[number];
export type CancellationSource = (typeof cancellationSources)[number];
export interface CancellationTerms {
  revision: number;
  hash: string;
  refundAmount: string;
  refundCurrency: string;
  financialSource: 'SupplierApi' | 'OperatorVerified';
  refundDestination: (typeof refundDestinations)[number];
  expiresAt: string;
  noticeVersion: 'cancellation-v1';
  passengerCount: FlightPassengerCount;
  wholeOrderItinerary: FlightItinerary;
}
export interface CancellationOperation {
  operationId: string;
  revision: number;
  phase: CancellationPhase;
  unknownStage: (typeof cancellationStages)[number];
  outcome: CancellationOutcome;
  resolutionSource: CancellationSource;
  reasonCode: (typeof cancellationReasons)[number];
  dispatchState: 'NotDispatched' | 'PreparationClaimed' | 'ConfirmationClaimed';
  terms: CancellationTerms | null;
  nextRefreshAt: string | null;
  confirmedBookingVersion: number | null;
  readPending: boolean;
}
export type BlockingConfirmation =
  | {
      kind: 'LegacyHeld';
      targetId: string;
      revision: number;
      phase: 'ManualReviewRequired';
      reasonCode: 'LegacyConfirmationUnverified';
      canCloseNotDispatched: false;
    }
  | {
      kind: 'ConfirmationAttempt';
      targetId: string;
      revision: number;
      phase: 'Started' | 'EffectsClaimed' | 'Unknown' | 'ManualReviewRequired';
      reasonCode: (typeof cancellationReasons)[number];
      canCloseNotDispatched: boolean;
    };
export interface CancellationStatusResponse {
  aggregateId: string;
  bookingStatus: FlightOrderStatus;
  bookingVersion: number;
  requestedOperationId: string | null;
  currentOperationId: string | null;
  isCurrentOperation: boolean;
  operation: CancellationOperation | null;
  blockingConfirmation: BlockingConfirmation | null;
  serverNow: string;
  supplierOrderCancellation: {
    cancelledAt: string;
    source: 'SupplierApi' | 'OperatorVerified';
    ownConsentConfirmed: boolean;
  } | null;
}
export interface PrepareCancellationRequest {
  aggregateId: string;
  operationId: string;
  expectedBookingVersion: number;
}
export interface ConsentCancellationRequest {
  aggregateId: string;
  operationId: string;
  expectedOperationRevision: number;
  termsRevision: number;
  termsHash: string;
  noticeVersion: 'cancellation-v1';
  accepted: true;
}
export interface AbandonCancellationRequest {
  aggregateId: string;
  operationId: string;
  expectedOperationRevision: number;
}
export interface RefreshCancellationRequest extends AbandonCancellationRequest {
  refreshRequestId: string;
}
