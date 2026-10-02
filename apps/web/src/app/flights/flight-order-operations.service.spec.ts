import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

describe('Flights operation memory', () => {
  const id = booking.oneWay.response.aggregateId;
  const owner = 'fixture-owner';
  const cancelled = {
    aggregateId: id,
    status: 'Cancelled',
    totalAmount: 100,
    currency: 'RUB',
    itinerary: booking.oneWay.response.offer.itinerary,
    ticketNumbers: [],
    bookedAt: '2030-01-01T10:00:00Z',
    ticketedAt: null,
    cancelledAt: '2030-01-01T11:00:00Z',
    refundedAt: null,
  };
  let service: FlightOrderOperationsService;
  let http: HttpTestingController;
  let auth: {
    status: ReturnType<typeof signal<FlightsAuthStatus>>;
    identityEpoch: ReturnType<typeof signal<number>>;
    accessToken: ReturnType<typeof vi.fn>;
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
    };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: FlightsAuthService, useValue: auth }],
    });
    service = TestBed.inject(FlightOrderOperationsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('keeps an unrecognized typed 409 unresolved and forbids a replacement key', async () => {
    service.startCancel(id, owner);
    await settle();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush(
        { type: 'https://travel.local/errors/Flights.UnrecognizedConflict' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    expect(service.blocksWrite(id, owner)).toBe(true);
    expect(service.startCancel(id, owner)).toBe(false);
  });
  it('reserves synchronously and preserves an unknown confirm across view destruction', async () => {
    expect(service.startConfirm(id, owner)).toBe(true);
    expect(service.startCancel(id, owner)).toBe(false);
    await settle();
    const req = http.expectOne('/api/flights/orders/confirm');
    const key = req.request.headers.get('Idempotency-Key');
    req.error(new ProgressEvent('error'));
    await settle();
    expect(service.operation(id, owner)?.state).toBe('unknown');
    expect(service.startCancel(id, owner)).toBe(false);
    expect(service.retry(id, owner)).toBe(true);
    await settle();
    const retry = http.expectOne('/api/flights/orders/confirm');
    expect(retry.request.body).toBe(req.request.body);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush({ aggregateId: id, status: 'Confirmed', paymentRef: null });
    await settle();
    expect(service.confirmed(id, owner)).toBe(true);
  });
  it('does not turn an empty success or rejected retry into evidence that an old attempt failed', async () => {
    service.startCancel(id, owner);
    await settle();
    const req = http.expectOne(`/api/flights/orders/${id}/cancel`);
    req.flush(null);
    await settle();
    expect(service.operation(id, owner)?.state).toBe('unknown');
    service.retry(id, owner);
    await settle();
    const retry = http.expectOne(`/api/flights/orders/${id}/cancel`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(req.request.headers.get('Idempotency-Key'));
    retry.flush(
      { type: 'https://travel.local/errors/Flights.OrderNotCancellable' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(service.blocksWrite(id, owner)).toBe(true);
    expect(service.cancelled(id, owner)).toBeNull();
  });
  it('retains a known terminal snapshot through stale GET and clears it on logout', async () => {
    service.startCancel(id, owner);
    await settle();
    http.expectOne(`/api/flights/orders/${id}/cancel`).flush(cancelled);
    await settle();
    expect(service.overlay({ ...cancelled, status: 'Held', cancelledAt: null } as never, owner).status).toBe(
      'Cancelled',
    );
    auth.status.set({ kind: 'anonymous' });
    TestBed.tick();
    expect(service.cancelled(id, owner)).toBeNull();
  });

  it('retains a validated Refunded advancement instead of rolling it back on a later stale GET', async () => {
    service.startCancel(id, owner);
    await settle();
    http.expectOne(`/api/flights/orders/${id}/cancel`).flush(cancelled);
    await settle();
    service.observeProjection({ ...cancelled, status: 'Refunded', refundedAt: '2030-01-01T12:00:00Z' } as never, owner);
    expect(service.overlay({ ...cancelled, status: 'Held', cancelledAt: null } as never, owner).status).toBe(
      'Refunded',
    );
  });
  it('rejects old completions even when the same identity returns in a later epoch', async () => {
    service.startCancel(id, owner);
    await settle();
    const req = http.expectOne(`/api/flights/orders/${id}/cancel`);
    auth.identityEpoch.set(2);
    auth.status.set({ kind: 'authenticated', userId: owner });
    req.flush(cancelled);
    await settle();
    expect(service.cancelled(id, owner)).toBeNull();
  });

  it('quarantines denial synchronously even when same-owner auth recovers before effects run', async () => {
    service.startCancel(id, owner);
    await settle();
    const request = http.expectOne(`/api/flights/orders/${id}/cancel`);
    service.denyAuthorization('Fixture access denied');
    auth.status.set({ kind: 'authenticated', userId: owner });
    request.flush(cancelled);
    await settle();
    expect(service.cancelled(id, owner)).toBeNull();
    expect(service.blocksWrite(id, owner)).toBe(true);
  });
  it('distinguishes pre-send expiry, in-flight conflict and expired replay window', async () => {
    auth.accessToken.mockRejectedValueOnce(new Error('expiry'));
    service.startCancel(id, owner);
    await settle();
    http.expectNone(() => true);
    expect(service.operation(id, owner)?.state).toBe('rejected');
    service.startCancel(id, owner);
    await settle();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush(
        { type: 'https://travel.local/errors/Flights.IdempotencyInFlight' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();
    expect(service.operation(id, owner)?.state).toBe('conflict');
    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 24 * 60 * 60_000 + 1);
    expect(service.retry(id, owner)).toBe(false);
  });
  it('keeps the original request quarantined on expiry and retries only for the original owner', async () => {
    service.startCancel(id, owner);
    await settle();
    const req = http.expectOne(`/api/flights/orders/${id}/cancel`);
    const key = req.request.headers.get('Idempotency-Key');
    auth.status.set({ kind: 'error', message: 'expired' });
    TestBed.tick();
    req.flush(cancelled);
    await settle();
    expect(service.cancelled(id, owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    TestBed.tick();
    expect(service.retry(id, owner)).toBe(true);
    await settle();
    const retry = http.expectOne(`/api/flights/orders/${id}/cancel`);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    retry.flush(cancelled);
    await settle();
    expect(service.cancelled(id, owner)?.status).toBe('Cancelled');
  });

  it.each(['confirm', 'cancel'] as const)(
    'does not send %s when its replay window expires during token refresh',
    async (kind) => {
      const started = 1_000_000;
      const clock = vi.spyOn(Date, 'now').mockReturnValue(started);
      const path = kind === 'cancel' ? `/api/flights/orders/${id}/cancel` : '/api/flights/orders/confirm';
      const start = () => (kind === 'cancel' ? service.startCancel(id, owner) : service.startConfirm(id, owner));
      expect(start()).toBe(true);
      await settle();
      http.expectOne(path).error(new ProgressEvent('error'));
      await settle();
      let resolveToken!: (token: string) => void;
      auth.accessToken.mockImplementationOnce(() => new Promise<string>((resolve) => (resolveToken = resolve)));
      clock.mockReturnValue(started + 24 * 60 * 60_000 - 1);
      expect(service.retry(id, owner)).toBe(true);
      clock.mockReturnValue(started + 24 * 60 * 60_000);
      resolveToken('refreshed-memory-token');
      await settle();
      http.expectNone((request) => request.method === 'POST');
      expect(service.operation(id, owner)?.state).toBe('unknown');
      expect(service.operation(id, owner)?.retryable).toBe(false);
      expect(service.blocksWrite(id, owner)).toBe(true);
      expect(service.retry(id, owner)).toBe(false);
      expect(start()).toBe(false);
    },
  );

  it.each(['confirm', 'cancel'] as const)(
    'still retries %s with its exact original request just before the replay deadline',
    async (kind) => {
      const started = 1_000_000;
      const clock = vi.spyOn(Date, 'now').mockReturnValue(started);
      const path = kind === 'cancel' ? `/api/flights/orders/${id}/cancel` : '/api/flights/orders/confirm';
      if (kind === 'cancel') service.startCancel(id, owner);
      else service.startConfirm(id, owner);
      await settle();
      const first = http.expectOne(path);
      first.error(new ProgressEvent('error'));
      await settle();
      let resolveToken!: (token: string) => void;
      auth.accessToken.mockImplementationOnce(() => new Promise<string>((resolve) => (resolveToken = resolve)));
      clock.mockReturnValue(started + 24 * 60 * 60_000 - 2);
      expect(service.retry(id, owner)).toBe(true);
      clock.mockReturnValue(started + 24 * 60 * 60_000 - 1);
      resolveToken('refreshed-memory-token');
      await settle();
      const retry = http.expectOne(path);
      expect(retry.request.headers.get('Idempotency-Key')).toBe(first.request.headers.get('Idempotency-Key'));
      expect(retry.request.body).toBe(first.request.body);
      retry.flush(kind === 'cancel' ? cancelled : { aggregateId: id, status: 'Confirmed', paymentRef: null });
      await settle();
      expect(service.operation(id, owner)?.state).toBe('success');
    },
  );
});
