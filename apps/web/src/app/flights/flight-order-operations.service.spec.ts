import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { FlightQuoteResponse, HoldFlightOrderRequest } from '@travel/api-client';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

describe('Flights operation memory', async () => {
  const id = booking.oneWay.response.aggregateId;
  const owner = 'fixture-owner';
  const quote = booking.oneWay.response as FlightQuoteResponse;
  const holdBody = (): HoldFlightOrderRequest => ({
    aggregateId: id,
    quoteRevision: '11111111-1111-4111-8111-111111111111',
    passengers: [
      {
        bookingPassengerId: '22222222-2222-4222-8222-222222222222',
        title: 'mr' as const,
        givenName: 'Demo',
        familyName: 'Traveler',
        dateOfBirth: '1990-04-12',
        gender: 'male',
        email: 'demo@example.test',
        phone: '+79161234567',
      },
    ],
  });
  const held = { aggregateId: id, providerOrderId: 'fixture-order', heldUntil: '2030-06-10T10:15:00Z' };
  const cancelled = {
    aggregateId: id,
    status: 'Cancelled',
    totalAmount: 100,
    currency: 'RUB',
    itinerary: booking.oneWay.response.offer.itinerary,
    passengerCount: 1 as const,
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
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FlightsAuthService, useValue: auth },
        provideRouter([]),
      ],
    });
    service = TestBed.inject(FlightOrderOperationsService);
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
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

  it('freezes the body before waiting for a token and reserves one hold across aggregates', async () => {
    let resolve!: (token: string) => void;
    auth.accessToken.mockImplementationOnce(
      () =>
        new Promise<string>((done) => {
          resolve = done;
        }),
    );
    const body = holdBody();
    const bytes = JSON.stringify(body);
    expect(service.startHold(body, quote, true, owner, false)).toBe(true);
    body.passengers[0].email = 'changed@example.test';
    expect(
      service.startHold(
        { ...holdBody(), aggregateId: booking.roundTrip.response.aggregateId },
        booking.roundTrip.response as FlightQuoteResponse,
        true,
        owner,
        false,
      ),
    ).toBe(false);
    resolve('memory-token');
    await settle();
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    expect(request.request.body).toBe(bytes);
    expect(request.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
    request.flush(held);
    await settle();
    expect(service.holdOperation(owner)?.result).toEqual(held);
  });

  it('does not dispatch an unaccepted quote or one that expires during token refresh', async () => {
    expect(service.startHold(holdBody(), quote, false, owner, false)).toBe(false);
    const clock = vi.spyOn(Date, 'now').mockReturnValue(Date.parse(quote.offer.expiresAt) - 100);
    let resolve!: (token: string) => void;
    auth.accessToken.mockImplementationOnce(
      () =>
        new Promise<string>((done) => {
          resolve = done;
        }),
    );
    expect(service.startHold(holdBody(), quote, true, owner, false)).toBe(true);
    clock.mockReturnValue(Date.parse(quote.offer.expiresAt));
    resolve('memory-token');
    await settle();
    http.expectNone('/api/flights/orders/hold');
    expect(service.holdOperation(owner)?.state).toBe('rejected');
  });

  it.each(['Ticketed', 'Cancelled', 'Refunded'] as const)(
    'preserves truthful terminal confirm replay %s over an old Held row',
    async (status) => {
      service.startConfirm(id, owner);
      await settle();
      http.expectOne('/api/flights/orders/confirm').flush({ aggregateId: id, status, paymentRef: null });
      await settle();
      const projected = { ...cancelled, status: 'Held' as const, cancelledAt: null };
      expect(service.overlay(projected, owner).status).toBe(status);
      if (status === 'Cancelled' || status === 'Refunded') expect(service.confirmed(id, owner)).toBe(false);
    },
  );
  it.each([400, 401, 403, 422, 503])('keeps an unknown hold after a late %s in the same session', async (status) => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    service.denyAuthorization('Transient expiry');
    auth.status.set({ kind: 'authenticated', userId: owner });
    request.flush({ type: 'https://travel.local/errors/Flights.OfferExpired' }, { status, statusText: 'Rejected' });
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(false);
    http.expectNone('/api/flights/orders/hold');
  });

  it('rejects the exact pre-effect request-size response and allows corrected hold data', async () => {
    const oversized = holdBody();
    oversized.passengers[0].givenName = 'D'.repeat(17_000);
    service.startHold(oversized, quote, true, owner, true);
    await settle();
    const first = http.expectOne('/api/flights/orders/hold');
    first.flush(
      { type: 'https://travel.local/errors/Flights.RequestTooLarge' },
      { status: 413, statusText: 'Payload Too Large' },
    );
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('rejected');
    expect(service.holdOperation(owner)?.message).toContain('размер');
    expect(service.blocksBookingInSession()).toBe(false);
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(true);
    await settle();
    const corrected = http.expectOne('/api/flights/orders/hold');
    expect(corrected.request.headers.get('Idempotency-Key')).not.toBe(first.request.headers.get('Idempotency-Key'));
    expect(JSON.parse(corrected.request.body).passengers[0].givenName).toBe('Demo');
    corrected.flush(held);
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('success');
  });

  it('does not let a late request-size rejection clear an already unknown hold', async () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(Date.now());
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    clock.mockReturnValue(Date.now() + 24 * 60 * 60_000);
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    request.flush(
      { type: 'https://travel.local/errors/Flights.RequestTooLarge' },
      { status: 413, statusText: 'Payload Too Large' },
    );
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(false);
    http.expectNone('/api/flights/orders/hold');
  });

  it.each([
    [400, 'Flights.RequestTooLarge'],
    [413, 'Flights.OtherTooLarge'],
  ])('does not infer pre-effect rejection from a mismatched size status/code %s %s', async (status, code) => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http
      .expectOne('/api/flights/orders/hold')
      .flush({ type: `https://travel.local/errors/${code}` }, { status: status as number, statusText: 'Rejected' });
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('unknown');
  });

  it.each([
    ['hold', 'success'],
    ['hold', 'rejected'],
    ['confirm', 'success'],
    ['confirm', 'rejected'],
  ] as const)('preserves a known %s %s through same-epoch transient auth loss', async (kind, outcome) => {
    if (kind === 'hold') service.startHold(holdBody(), quote, true, owner, true);
    else {
      service.startConfirm(id, owner);
      await settle();
    }
    await settle();
    const request = http.expectOne(`/api/flights/orders/${kind}`);
    if (outcome === 'success')
      request.flush(kind === 'hold' ? held : { aggregateId: id, status: 'Confirmed', paymentRef: null });
    else
      request.flush(
        { type: 'https://travel.local/errors/Flights.CommandInvalid' },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle();
    const known = kind === 'hold' ? service.holdOperation(owner) : service.operation(id, owner);
    expect(known?.state).toBe(outcome);
    service.denyAuthorization('Same-session token refresh failed');
    expect(service.holdOperation(owner)).toBeNull();
    expect(service.operation(id, owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    await settle();
    const recovered = kind === 'hold' ? service.holdOperation(owner) : service.operation(id, owner);
    expect(recovered).toEqual(known);
    expect(service.blocksBookingInSession()).toBe(false);
    if (kind === 'confirm' && outcome === 'success') {
      expect(service.confirmed(id, owner)).toBe(true);
      expect(service.startConfirm(id, owner)).toBe(false);
    }
  });

  it('preserves a successful hold after pre-dispatch confirmation token failure and safely resumes', async () => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http.expectOne('/api/flights/orders/hold').flush(held);
    await settle();
    auth.accessToken.mockImplementationOnce(async () => {
      auth.status.set({ kind: 'error', message: 'Token refresh failed before confirm dispatch' });
      throw new Error('Fictional token refresh failure');
    });
    expect(service.startConfirm(id, owner)).toBe(true);
    await settle();
    http.expectNone('/api/flights/orders/confirm');
    expect(service.holdOperation(owner)).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    await settle();
    expect(service.holdOperation(owner)?.result).toEqual(held);
    expect(service.blocksBookingInSession()).toBe(false);
    expect(service.startConfirm(id, owner)).toBe(true);
    await settle();
    http.expectOne('/api/flights/orders/confirm').flush({ aggregateId: id, status: 'Confirmed', paymentRef: null });
    await settle();
    expect(service.confirmed(id, owner)).toBe(true);
  });

  it('does not infer the original hold outcome from a Held GET or elapsed retention horizon', async () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(Date.now());
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http.expectOne('/api/flights/orders/hold').error(new ProgressEvent('error'));
    await settle();
    service.observeProjection({ ...cancelled, status: 'Held', cancelledAt: null } as never, owner);
    clock.mockReturnValue(Date.now() + 24 * 60 * 60_000);
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(false);
  });

  it('accepts an attributable late own hold success after the PII horizon', async () => {
    const clock = vi.spyOn(Date, 'now').mockReturnValue(Date.now());
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    clock.mockReturnValue(Date.now() + 24 * 60 * 60_000);
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    request.flush(held);
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('success');
  });

  it('does not confirm an observed order while its hold is still unknown', async () => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http.expectOne('/api/flights/orders/hold').error(new ProgressEvent('error'));
    await settle();
    expect(service.startConfirm(id, owner)).toBe(false);
    await settle();
    http.expectNone('/api/flights/orders/confirm');
  });

  it('does not replace an unknown confirmation with a hold on a new aggregate', async () => {
    service.startConfirm(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('error'));
    await settle();
    expect(
      service.startHold(
        { ...holdBody(), aggregateId: booking.roundTrip.response.aggregateId },
        booking.roundTrip.response as FlightQuoteResponse,
        true,
        owner,
        true,
      ),
    ).toBe(false);
    http.expectNone('/api/flights/orders/hold');
  });

  it.each([
    [422, 'Flights.OfferExpired'],
    [503, 'Flights.HoldOutcomeUnknown'],
    [409, 'Flights.ConcurrencyConflict'],
  ])('treats the first possibly dispatched hold %s %s as unknown', async (status, code) => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http
      .expectOne('/api/flights/orders/hold')
      .flush({ type: `https://travel.local/errors/${code}` }, { status: status as number, statusText: 'Rejected' });
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(false);
  });

  it.each(['hold', 'confirm'] as const)(
    'clears the old %s context when epoch changes during transient auth error',
    async (kind) => {
      if (kind === 'hold') service.startHold(holdBody(), quote, true, owner, true);
      else {
        service.startConfirm(id, owner);
        await settle();
      }
      const path = kind === 'hold' ? '/api/flights/orders/hold' : '/api/flights/orders/confirm';
      await settle();
      const request = http.expectOne(path);
      auth.identityEpoch.set(1);
      auth.status.set({ kind: 'error', message: 'Transient auth state after identity reset' });
      TestBed.tick();
      auth.status.set({ kind: 'authenticated', userId: owner });
      request.flush(kind === 'hold' ? held : { aggregateId: id, status: 'Confirmed', paymentRef: null });
      await settle();
      expect(service.holdOperation(owner)).toBeNull();
      expect(service.operation(id, owner)).toBeNull();
      expect(service.blocksBooking(owner)).toBe(false);
    },
  );

  it('rejects a hold receipt when the same owner returned in a later identity epoch', async () => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    auth.identityEpoch.set(2);
    request.flush(held);
    await settle();
    expect(service.holdOperation(owner)).toBeNull();
  });

  it('clears hold ownership on explicit logout and never publishes its old completion', async () => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    const request = http.expectOne('/api/flights/orders/hold');
    auth.status.set({ kind: 'anonymous' });
    TestBed.tick();
    auth.status.set({ kind: 'authenticated', userId: owner });
    request.flush(held);
    await settle();
    expect(service.holdOperation(owner)).toBeNull();
  });

  it('quarantines a pending confirmation on transient auth and forbids replay after recovery', async () => {
    service.startConfirm(id, owner);
    await settle();
    const request = http.expectOne('/api/flights/orders/confirm');
    service.denyAuthorization('Transient expiry');
    auth.status.set({ kind: 'authenticated', userId: owner });
    request.flush({ type: 'https://travel.local/errors/Flights.HoldExpired' }, { status: 409, statusText: 'Conflict' });
    await settle();
    expect(service.operation(id, owner)?.state).toBe('unknown');
    expect(service.retry(id, owner)).toBe(false);
    expect(service.operation(id, owner)?.message).toContain('Исход подтверждения неизвестен');
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
  it('reserves synchronously and forbids confirm replay after uncertainty', async () => {
    expect(service.startConfirm(id, owner)).toBe(true);
    expect(service.startCancel(id, owner)).toBe(false);
    await settle();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('error'));
    await settle();
    expect(service.operation(id, owner)?.state).toBe('unknown');
    expect(service.startCancel(id, owner)).toBe(false);
    expect(service.retry(id, owner)).toBe(false);
    await settle();
    http.expectNone('/api/flights/orders/confirm');
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

  it.each(['cancel'] as const)('does not send %s when its replay window expires during token refresh', async (kind) => {
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
  });

  it.each(['cancel'] as const)(
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
  it.each([
    [400, 'Flights.QuoteBindingRequired'],
    [400, 'Flights.QuoteBindingInvalid'],
    [400, 'Flights.HoldNotSupported'],
    [400, 'Flights.IdentityDocumentsRequired'],
    [400, 'Flights.PassengerInvalid'],
    [409, 'Flights.QuoteRevisionMismatch'],
    [409, 'Flights.PassengerCountMismatch'],
    [409, 'Flights.PassengerSlotsMismatch'],
  ])('allows correction only for first pre-effect rejection %s %s', async (status, code) => {
    expect(service.startHold(holdBody(), quote, true, owner, true)).toBe(true);
    await settle();
    http
      .expectOne('/api/flights/orders/hold')
      .flush({ type: `https://travel.local/errors/${code}` }, { status: Number(status), statusText: 'Rejected' });
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('rejected');
    expect(service.blocksBooking(owner)).toBe(false);
  });
  it.each([
    [400, 'Flights.Unrecognized'],
    [409, 'Flights.PassengerInvalid'],
    [400, 'Flights.QuoteRevisionMismatch'],
    [409, 'Flights.InvalidState'],
  ])('retains uncertainty for unmatched rejection %s %s', async (status, code) => {
    service.startHold(holdBody(), quote, true, owner, true);
    await settle();
    http
      .expectOne('/api/flights/orders/hold')
      .flush({ type: `https://travel.local/errors/${code}` }, { status: Number(status), statusText: 'Rejected' });
    await settle();
    expect(service.holdOperation(owner)?.state).toBe('unknown');
    expect(service.blocksBooking(owner)).toBe(true);
  });
  it('refuses mismatched revision, duplicate or missing members before freezing', async () => {
    expect(
      service.startHold(
        { ...holdBody(), quoteRevision: '33333333-3333-4333-8333-333333333333' },
        quote,
        true,
        owner,
        true,
      ),
    ).toBe(false);
    expect(service.startHold({ ...holdBody(), passengers: [] }, quote, true, owner, true)).toBe(false);
    expect(
      service.startHold(
        { ...holdBody(), passengers: [...holdBody().passengers, ...holdBody().passengers] },
        quote,
        true,
        owner,
        true,
      ),
    ).toBe(false);
    http.expectNone('/api/flights/orders/hold');
  });
});
