import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CancellationPhase, CancellationStatusResponse } from '@travel/api-client';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightCancellationService } from './flight-cancellation.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

const id = booking.oneWay.response.aggregateId;
const owner = 'fixture-owner';
const op = '22222222-2222-4222-8222-222222222222';
function status(phase: CancellationPhase = 'TermsReady', version = 6): CancellationStatusResponse {
  return {
    aggregateId: id,
    bookingStatus: 'Held',
    bookingVersion: version,
    requestedOperationId: null,
    currentOperationId: op,
    isCurrentOperation: true,
    blockingConfirmation: null,
    serverNow: new Date().toISOString(),
    supplierOrderCancellation: null,
    operation: {
      operationId: op,
      revision: 3,
      phase,
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
        expiresAt: new Date(Date.now() + 600_000).toISOString(),
        noticeVersion: 'cancellation-v1',
        passengerCount: 2,
        wholeOrderItinerary: booking.oneWay.response.offer.itinerary,
      },
    },
  };
}
describe('server cancellation view and tab-local consent', () => {
  let service: FlightCancellationService;
  let http: HttpTestingController;
  let auth: {
    status: ReturnType<typeof signal<FlightsAuthStatus>>;
    identityEpoch: ReturnType<typeof signal<number>>;
    accessToken: ReturnType<typeof vi.fn>;
    isDemo: boolean;
  };
  const settle = async () => {
    await Promise.resolve();
    await Promise.resolve();
    TestBed.tick();
  };
  beforeEach(() => {
    auth = {
      status: signal<FlightsAuthStatus>({ kind: 'authenticated', userId: owner }),
      identityEpoch: signal(0),
      accessToken: vi.fn().mockResolvedValue('memory-token'),
      isDemo: false,
    };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: FlightsAuthService, useValue: auth }],
    });
    service = TestBed.inject(FlightCancellationService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      vi.useRealTimers();
      vi.restoreAllMocks();
      TestBed.resetTestingModule();
    }
  });
  async function open(value: CancellationStatusResponse = status()) {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/cancellation').flush(value);
    await settle();
  }
  it('never exposes the prior owner cache while switching before Angular effects flush', async () => {
    await open();
    auth.identityEpoch.set(1);
    auth.status.set({ kind: 'authenticated', userId: 'other' });
    service.open(id, 'other');
    expect(service.snapshot()).toBeNull();
    await settle();
    const requests = http.match('/api/flights/orders/' + id + '/cancellation');
    for (const request of requests)
      if (!request.cancelled) request.flush({ ...status(), operation: null, currentOperationId: null });
    await settle();
  });
  it('re-enables an explicit manual refresh after the shared cooldown without automatic HTTP', async () => {
    vi.useFakeTimers();
    const manual: CancellationStatusResponse = {
      ...status('ManualReviewRequired'),
      operation: {
        ...status().operation!,
        phase: 'ManualReviewRequired',
        outcome: 'Unknown',
        unknownStage: 'Confirmation',
        nextRefreshAt: new Date(Date.now() + 60_000).toISOString(),
      },
    };
    await open(manual);
    expect(service.canRefresh()).toBe(false);
    await vi.advanceTimersByTimeAsync(60_001);
    TestBed.tick();
    expect(service.canRefresh()).toBe(true);
    http.expectNone((request) => request.method === 'GET');
  });
  it('reload reads persisted status only and never restores consent from browser storage', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem');
    await open();
    expect(service.snapshot()?.operation?.phase).toBe('TermsReady');
    expect(service.accepted()).toBe(false);
    expect(storage).not.toHaveBeenCalled();
    http.expectNone((request) => request.method === 'POST');
  });
  it('echoes exact terms with one consent request under rapid duplicate clicks', async () => {
    await open();
    service.accepted.set(true);
    expect(service.consent()).toBe(true);
    expect(service.consent()).toBe(false);
    await settle();
    const request = http.expectOne('/api/flights/cancellations/consent');
    const body = JSON.parse(request.request.body);
    expect(body).toMatchObject({
      aggregateId: id,
      operationId: op,
      termsRevision: 1,
      termsHash: 'a'.repeat(64),
      noticeVersion: 'cancellation-v1',
      accepted: true,
    });
    expect(request.request.headers.get('Authorization')).toBe('Bearer memory-token');
    request.flush({
      ...status('Accepted'),
      bookingVersion: 7,
      operation: { ...status().operation, phase: 'Accepted', revision: 4 },
    });
    await settle();
    expect(service.accepted()).toBe(false);
    expect(service.snapshot()?.operation?.phase).toBe('Accepted');
  });
  it('clears prior acceptance when server terms change', async () => {
    await open();
    service.accepted.set(true);
    service.read();
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/cancellation').flush({
      ...status(),
      bookingVersion: 7,
      operation: {
        ...status().operation,
        revision: 4,
        terms: { ...status().operation!.terms, revision: 2, hash: 'b'.repeat(64) },
      },
    });
    await settle();
    expect(service.accepted()).toBe(false);
  });
  it('ignores lower booking evidence and never lets a stale Held projection erase cancellation', async () => {
    const final: CancellationStatusResponse = {
      ...status('Succeeded', 10),
      bookingStatus: 'Cancelled',
      operation: {
        ...status().operation!,
        phase: 'Succeeded',
        outcome: 'Succeeded',
        resolutionSource: 'SupplierApi',
        confirmedBookingVersion: 10,
      },
    };
    await open(final);
    service.read();
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/cancellation').flush(status());
    await settle();
    expect(service.snapshot()?.bookingStatus).toBe('Cancelled');
    expect(service.overlayStatus('Held')).toBe('Cancelled');
  });
  it('drops A-to-B-to-A callbacks using the identity epoch while accepting same-owner token refresh', async () => {
    service.open(id, owner);
    await settle();
    const request = http.expectOne('/api/flights/orders/' + id + '/cancellation');
    auth.identityEpoch.set(1);
    auth.status.set({ kind: 'authenticated', userId: 'other' });
    auth.identityEpoch.set(2);
    auth.status.set({ kind: 'authenticated', userId: owner });
    TestBed.tick();
    if (!request.cancelled) request.flush(status());
    await settle();
    expect(service.snapshot()).toBeNull();
    await open();
    auth.accessToken.mockResolvedValue('new-memory-token');
    service.read();
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/cancellation').flush(status());
    await settle();
    expect(service.snapshot()?.operation?.operationId).toBe(op);
  });
  it('shows confirmation and legacy blockers even without cancellation operation', async () => {
    const legacy: CancellationStatusResponse = {
      ...status(),
      currentOperationId: null,
      operation: null,
      blockingConfirmation: {
        kind: 'LegacyHeld',
        targetId: id,
        revision: 3,
        phase: 'ManualReviewRequired',
        reasonCode: 'LegacyConfirmationUnverified',
        canCloseNotDispatched: false,
      },
    };
    await open(legacy);
    expect(service.blocksConfirmation()).toBe(true);
    expect(service.canPrepare()).toBe(false);
    expect(service.prepare()).toBe(false);
    http.expectNone((request) => request.method === 'POST');
  });
  it('keeps transport uncertainty separate and recovers by GET without automatic resend', async () => {
    const initial: CancellationStatusResponse = { ...status(), operation: null, currentOperationId: null };
    await open(initial);
    expect(service.prepare()).toBe(true);
    await settle();
    const mutation = http.expectOne('/api/flights/cancellations/prepare');
    const frozen = mutation.request.body;
    mutation.error(new ProgressEvent('error'));
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/cancellation').flush({
      ...status(),
      operation: { ...status().operation, phase: 'Unknown', outcome: 'Unknown', unknownStage: 'Preparation' },
    });
    await settle();
    expect(service.snapshot()?.operation?.outcome).toBe('Unknown');
    http.expectNone('/api/flights/cancellations/prepare');
    expect(frozen).toContain('operationId');
  });
});
