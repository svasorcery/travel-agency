import {
  type BlockingConfirmation,
  type CancellationOperation,
  type CancellationStatusResponse,
  type CancellationTerms,
  cancellationOutcomes,
  cancellationPhases,
  cancellationReasons,
  cancellationSources,
  cancellationStages,
  refundDestinations,
} from './flights-cancellation.types';
import { decodeFlightItinerary } from './flights-search.decoder';
import type { FlightPassengerCount } from './flights-search.types';

export class FlightCancellationContractError extends Error {
  constructor(field: string) {
    super('Invalid Flights cancellation response at ' + field);
    this.name = 'FlightCancellationContractError';
  }
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function record(value: unknown, field: string): Record<string, unknown> {
  if (!isRecord(value)) throw new FlightCancellationContractError(field);
  return value;
}
function member<T extends string>(value: unknown, choices: readonly T[]): value is T {
  return typeof value === 'string' && choices.some((choice) => choice === value);
}
function enumValue<T extends string>(value: unknown, choices: readonly T[], field: string): T {
  if (!member(value, choices)) throw new FlightCancellationContractError(field);
  return value;
}
export function cancellationGuid(value: unknown, field = 'aggregateId'): string {
  if (
    typeof value !== 'string' ||
    !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) ||
    /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value)
  )
    throw new FlightCancellationContractError(field);
  return value.toLowerCase();
}
function integer(value: unknown, field: string): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0)
    throw new FlightCancellationContractError(field);
  return value;
}
function boolean(value: unknown, field: string): boolean {
  if (typeof value !== 'boolean') throw new FlightCancellationContractError(field);
  return value;
}
function text(value: unknown, field: string, pattern: RegExp): string {
  if (typeof value !== 'string' || !pattern.test(value)) throw new FlightCancellationContractError(field);
  return value;
}
function timestamp(value: unknown, field: string): string {
  const result = text(value, field, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/);
  const date = result.slice(0, 10);
  const midnight = Date.parse(date + 'T00:00:00Z');
  if (
    !Number.isFinite(Date.parse(result)) ||
    !Number.isFinite(midnight) ||
    new Date(midnight).toISOString().slice(0, 10) !== date
  )
    throw new FlightCancellationContractError(field);
  return result;
}
function nullable<T>(value: unknown, decode: (value: unknown) => T): T | null {
  return value === null ? null : decode(value);
}
function count(value: unknown): value is FlightPassengerCount {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 && value <= 9;
}
function terms(value: unknown): CancellationTerms {
  const row = record(value, 'terms');
  const passengers = row['passengerCount'];
  if (!count(passengers)) throw new FlightCancellationContractError('terms.passengerCount');
  let itinerary: CancellationTerms['wholeOrderItinerary'];
  try {
    itinerary = decodeFlightItinerary(row['wholeOrderItinerary']);
  } catch {
    throw new FlightCancellationContractError('terms.wholeOrderItinerary');
  }
  return {
    revision: integer(row['revision'], 'terms.revision'),
    hash: text(row['hash'], 'terms.hash', /^[0-9a-f]{64}$/),
    refundAmount: text(row['refundAmount'], 'terms.refundAmount', /^(?:0|[1-9]\d*)(?:\.\d+)?$/),
    refundCurrency: text(row['refundCurrency'], 'terms.refundCurrency', /^(?!XXX$|XTS$)[A-Z]{3}$/),
    financialSource: enumValue(row['financialSource'], ['SupplierApi', 'OperatorVerified'], 'terms.financialSource'),
    refundDestination: enumValue(row['refundDestination'], refundDestinations, 'terms.refundDestination'),
    expiresAt: timestamp(row['expiresAt'], 'terms.expiresAt'),
    noticeVersion: enumValue(row['noticeVersion'], ['cancellation-v1'], 'terms.noticeVersion'),
    passengerCount: passengers,
    wholeOrderItinerary: itinerary,
  };
}
function operation(value: unknown, version: number): CancellationOperation {
  const row = record(value, 'operation');
  const result: CancellationOperation = {
    operationId: cancellationGuid(row['operationId'], 'operation.operationId'),
    revision: integer(row['revision'], 'operation.revision'),
    phase: enumValue(row['phase'], cancellationPhases, 'operation.phase'),
    unknownStage: enumValue(row['unknownStage'], cancellationStages, 'operation.unknownStage'),
    outcome: enumValue(row['outcome'], cancellationOutcomes, 'operation.outcome'),
    resolutionSource: enumValue(row['resolutionSource'], cancellationSources, 'operation.resolutionSource'),
    reasonCode: enumValue(row['reasonCode'], cancellationReasons, 'operation.reasonCode'),
    dispatchState: enumValue(
      row['dispatchState'],
      ['NotDispatched', 'PreparationClaimed', 'ConfirmationClaimed'],
      'operation.dispatchState',
    ),
    terms: nullable(row['terms'], terms),
    nextRefreshAt: nullable(row['nextRefreshAt'], (value) => timestamp(value, 'operation.nextRefreshAt')),
    confirmedBookingVersion: nullable(row['confirmedBookingVersion'], (value) =>
      integer(value, 'operation.confirmedBookingVersion'),
    ),
    readPending: boolean(row['readPending'], 'operation.readPending'),
  };
  if (result.revision > version) throw new FlightCancellationContractError('operation.revision');
  if (['TermsReady', 'Accepted', 'DispatchClaimed', 'Succeeded'].includes(result.phase) && result.terms === null)
    throw new FlightCancellationContractError('operation.terms');
  if (
    result.outcome === 'Succeeded' &&
    (result.phase !== 'Succeeded' ||
      !['SupplierApi', 'OperatorVerified'].includes(result.resolutionSource) ||
      result.confirmedBookingVersion === null ||
      result.confirmedBookingVersion > version ||
      result.terms === null)
  )
    throw new FlightCancellationContractError('operation.success');
  if (result.phase === 'Succeeded' && result.outcome !== 'Succeeded')
    throw new FlightCancellationContractError('operation.success');
  if (result.phase === 'Rejected' && result.outcome !== 'Rejected')
    throw new FlightCancellationContractError('operation.rejection');
  return result;
}
function blocker(value: unknown, id: string): BlockingConfirmation {
  const row = record(value, 'blockingConfirmation');
  const kind = enumValue(row['kind'], ['LegacyHeld', 'ConfirmationAttempt'], 'blockingConfirmation.kind');
  const targetId = cancellationGuid(row['targetId'], 'blockingConfirmation.targetId');
  const revision = integer(row['revision'], 'blockingConfirmation.revision');
  if (kind === 'LegacyHeld') {
    if (
      targetId !== id ||
      row['phase'] !== 'ManualReviewRequired' ||
      row['reasonCode'] !== 'LegacyConfirmationUnverified' ||
      row['canCloseNotDispatched'] !== false
    )
      throw new FlightCancellationContractError('blockingConfirmation.legacy');
    return {
      kind,
      targetId,
      revision,
      phase: 'ManualReviewRequired',
      reasonCode: 'LegacyConfirmationUnverified',
      canCloseNotDispatched: false,
    };
  }
  return {
    kind,
    targetId,
    revision,
    phase: enumValue(
      row['phase'],
      ['Started', 'EffectsClaimed', 'Unknown', 'ManualReviewRequired'],
      'blockingConfirmation.phase',
    ),
    reasonCode: enumValue(row['reasonCode'], cancellationReasons, 'blockingConfirmation.reasonCode'),
    canCloseNotDispatched: boolean(row['canCloseNotDispatched'], 'blockingConfirmation.canCloseNotDispatched'),
  };
}
export function decodeCancellationStatus(value: unknown, expectedAggregateId: string): CancellationStatusResponse {
  const row = record(value, 'response');
  const id = cancellationGuid(row['aggregateId']);
  if (id !== cancellationGuid(expectedAggregateId)) throw new FlightCancellationContractError('aggregateId');
  const version = integer(row['bookingVersion'], 'bookingVersion');
  const requested = nullable(row['requestedOperationId'], (value) => cancellationGuid(value, 'requestedOperationId'));
  const current = nullable(row['currentOperationId'], (value) => cancellationGuid(value, 'currentOperationId'));
  const selected = nullable(row['operation'], (value) => operation(value, version));
  const isCurrent = boolean(row['isCurrentOperation'], 'isCurrentOperation');
  if (
    (selected?.operationId ?? null) !== (requested ?? current) ||
    isCurrent !== ((selected?.operationId ?? null) === current)
  )
    throw new FlightCancellationContractError('operation.identity');
  const fact = nullable(row['supplierOrderCancellation'], (value) => {
    const item = record(value, 'supplierOrderCancellation');
    return {
      cancelledAt: timestamp(item['cancelledAt'], 'supplierOrderCancellation.cancelledAt'),
      source: enumValue(item['source'], ['SupplierApi', 'OperatorVerified'], 'supplierOrderCancellation.source'),
      ownConsentConfirmed: boolean(item['ownConsentConfirmed'], 'supplierOrderCancellation.ownConsentConfirmed'),
    };
  });
  if (fact?.ownConsentConfirmed && selected?.outcome !== 'Succeeded')
    throw new FlightCancellationContractError('supplierOrderCancellation.ownConsentConfirmed');
  return {
    aggregateId: id,
    bookingStatus: enumValue(
      row['bookingStatus'],
      ['Held', 'Confirmed', 'Ticketed', 'Cancelled', 'Refunded'],
      'bookingStatus',
    ),
    bookingVersion: version,
    requestedOperationId: requested,
    currentOperationId: current,
    isCurrentOperation: isCurrent,
    operation: selected,
    blockingConfirmation: nullable(row['blockingConfirmation'], (value) => blocker(value, id)),
    serverNow: timestamp(row['serverNow'], 'serverNow'),
    supplierOrderCancellation: fact,
  };
}
