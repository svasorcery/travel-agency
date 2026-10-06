// Shared fictional HTTP fixture; historical booking examples remain intact.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-booking.json';
import { decodeCancellationStatus, FlightCancellationContractError } from './flights-cancellation.decoder';

const id = '11111111-1111-1111-1111-111111111111';
const op = '22222222-2222-2222-2222-222222222222';
const current = {
  aggregateId: id,
  bookingStatus: 'Held',
  bookingVersion: 6,
  requestedOperationId: null,
  currentOperationId: op,
  isCurrentOperation: true,
  blockingConfirmation: null,
  serverNow: '2026-10-05T12:00:00Z',
  supplierOrderCancellation: null,
  operation: {
    operationId: op,
    revision: 3,
    phase: 'TermsReady',
    unknownStage: 'None',
    outcome: 'None',
    resolutionSource: 'None',
    reasonCode: 'None',
    dispatchState: 'PreparationClaimed',
    nextRefreshAt: null,
    confirmedBookingVersion: null,
    readPending: false,
    terms: {
      revision: 1,
      hash: 'a'.repeat(64),
      refundAmount: '17.25',
      refundCurrency: 'USD',
      financialSource: 'SupplierApi',
      refundDestination: 'Balance',
      expiresAt: '2026-10-05T12:10:00Z',
      noticeVersion: 'cancellation-v1',
      passengerCount: 2,
      wholeOrderItinerary: fixtures.oneWay.response.offer.itinerary,
    },
  },
};

describe('whole order cancellation contract', () => {
  it('retains operator financial terms provenance separately from API cancellation confirmation', () => {
    const response = decodeCancellationStatus(
      {
        ...current,
        bookingStatus: 'Cancelled',
        bookingVersion: 10,
        operation: {
          ...current.operation,
          phase: 'Succeeded',
          outcome: 'Succeeded',
          resolutionSource: 'SupplierApi',
          confirmedBookingVersion: 10,
          terms: { ...current.operation.terms, financialSource: 'OperatorVerified' },
        },
      },
      id,
    );
    expect(response.operation?.terms?.financialSource).toBe('OperatorVerified');
    expect(response.operation?.resolutionSource).toBe('SupplierApi');
  });
  it('reads exact decimal amount and every journey leg without calculating consent hash', () => {
    const response = decodeCancellationStatus(current, id);
    expect(response.operation?.terms?.refundAmount).toBe('17.25');
    expect(response.operation?.terms?.hash).toBe('a'.repeat(64));
    const itinerary = {
      ...current.operation.terms.wholeOrderItinerary,
      journeyKind: 'multi-leg',
      slices: Array.from({ length: 4 }, () => structuredClone(current.operation.terms.wholeOrderItinerary.slices[0])),
    };
    expect(
      decodeCancellationStatus(
        {
          ...current,
          operation: { ...current.operation, terms: { ...current.operation.terms, wholeOrderItinerary: itinerary } },
        },
        id,
      ).operation?.terms?.wholeOrderItinerary.slices,
    ).toHaveLength(4);
  });
  it.each([
    null,
    {},
    { ...current, aggregateId: op },
    { ...current, bookingVersion: 0 },
    { ...current, bookingVersion: Number.MAX_SAFE_INTEGER + 1 },
    { ...current, bookingStatus: 'invented' },
    { ...current, operation: { ...current.operation, revision: 1.5 } },
    { ...current, operation: { ...current.operation, phase: 'invented' } },
    { ...current, operation: { ...current.operation, readPending: 'true' } },
    { ...current, serverNow: '2026-02-31T12:00:00Z' },
    { ...current, operation: { ...current.operation, terms: null } },
    { ...current, operation: { ...current.operation, terms: { ...current.operation.terms, refundAmount: 17.25 } } },
    { ...current, operation: { ...current.operation, terms: { ...current.operation.terms, refundAmount: '-1' } } },
    { ...current, operation: { ...current.operation, terms: { ...current.operation.terms, refundAmount: '1e2' } } },
    { ...current, operation: { ...current.operation, terms: { ...current.operation.terms, refundCurrency: null } } },
    {
      ...current,
      operation: { ...current.operation, terms: { ...current.operation.terms, refundDestination: 'invented' } },
    },
    {
      ...current,
      operation: {
        ...current.operation,
        outcome: 'Succeeded',
        phase: 'Succeeded',
        resolutionSource: 'None',
        confirmedBookingVersion: 6,
      },
    },
    {
      ...current,
      operation: {
        ...current.operation,
        outcome: 'Succeeded',
        phase: 'Succeeded',
        resolutionSource: 'SupplierApi',
        confirmedBookingVersion: null,
      },
    },
  ])('rejects invalid or unproven result %#', (input) => {
    expect(() => decodeCancellationStatus(input, id)).toThrow(FlightCancellationContractError);
  });
  it('allows legacy terminal order with no operation and a separate legacy Held blocker', () => {
    const empty = { ...current, operation: null, currentOperationId: null };
    expect(decodeCancellationStatus({ ...empty, bookingStatus: 'Cancelled' }, id).operation).toBeNull();
    const blocker = {
      kind: 'LegacyHeld',
      targetId: id,
      revision: 3,
      phase: 'ManualReviewRequired',
      reasonCode: 'LegacyConfirmationUnverified',
      canCloseNotDispatched: false,
    };
    expect(decodeCancellationStatus({ ...empty, blockingConfirmation: blocker }, id).blockingConfirmation?.kind).toBe(
      'LegacyHeld',
    );
    expect(() =>
      decodeCancellationStatus({ ...empty, blockingConfirmation: { ...blocker, targetId: op } }, id),
    ).toThrow(FlightCancellationContractError);
  });
  it('keeps old receipt historical rather than making it current after a newer prepare', () => {
    const next = '44444444-4444-4444-4444-444444444444';
    const historical = { ...current, requestedOperationId: op, currentOperationId: next, isCurrentOperation: false };
    expect(decodeCancellationStatus(historical, id).isCurrentOperation).toBe(false);
    expect(() => decodeCancellationStatus({ ...historical, isCurrentOperation: true }, id)).toThrow(
      FlightCancellationContractError,
    );
  });
  it('reads unknown/manual and preserves a separately known cancelled order without own success', () => {
    const input = {
      ...current,
      bookingStatus: 'Cancelled',
      supplierOrderCancellation: {
        cancelledAt: '2026-10-05T11:59:00Z',
        source: 'SupplierApi',
        ownConsentConfirmed: false,
      },
      operation: {
        ...current.operation,
        phase: 'ManualReviewRequired',
        outcome: 'Unknown',
        unknownStage: 'Confirmation',
        readPending: true,
      },
    };
    expect(decodeCancellationStatus(input, id).operation?.outcome).toBe('Unknown');
    expect(() =>
      decodeCancellationStatus(
        { ...input, supplierOrderCancellation: { ...input.supplierOrderCancellation, ownConsentConfirmed: true } },
        id,
      ),
    ).toThrow(FlightCancellationContractError);
  });
  it('accepts expired saved terms for history without granting new consent', () => {
    expect(
      decodeCancellationStatus(
        {
          ...current,
          operation: { ...current.operation, terms: { ...current.operation.terms, expiresAt: '2026-10-01T12:00:00Z' } },
        },
        id,
      ).operation?.terms?.expiresAt,
    ).toBe('2026-10-01T12:00:00Z');
  });
});
