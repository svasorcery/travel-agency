import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FlightCustomerRefundService } from './flight-customer-refund.service';
import { FlightsAuthService, type FlightsAuthStatus } from './flights-auth.service';

const id = '00000000-0000-0000-0000-000000000123';
const owner = 'fictional-owner';
const status = (version = 40) => ({
  aggregateId: id,
  bookingVersion: version,
  caseRevision: 3,
  state: 'Sent',
  amount: { amount: '80', currency: 'GBP' },
  destination: 'OriginalFictionalPaymentMethod',
  source: 'FictionalGateway',
  reason: 'None',
  supplierRefund: null,
  supplierSettlement: 'NotEstablished',
  customerCredit: 'NotEstablished',
  needsManualReview: false,
  observedAt: '2030-06-01T12:00:09Z',
  supplierReport: null,
});
describe('owner-bound customer refund reads', () => {
  let service: FlightCustomerRefundService;
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
      accessToken: vi.fn().mockResolvedValue('memory-only-token'),
    };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: FlightsAuthService, useValue: auth }],
    });
    service = TestBed.inject(FlightCustomerRefundService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    service.close();
    http.verify();
    TestBed.resetTestingModule();
  });
  it('reads the retained receipt with GET and retains sent across a later read error', async () => {
    service.open(id, owner);
    await settle();
    const req = http.expectOne('/api/flights/orders/' + id + '/customer-refund');
    expect(req.request.method).toBe('GET');
    req.flush(status());
    await settle();
    expect(service.snapshot()?.state).toBe('Sent');
    service.read();
    await settle();
    http
      .expectOne('/api/flights/orders/' + id + '/customer-refund')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await settle();
    expect(service.snapshot()?.state).toBe('Sent');
    expect(service.message()).not.toBe('');
  });
  it('pauses leaf GET without erasing Sent and routes old workflow triggers through the page gate', async () => {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    const coordinate = vi.fn();
    service.setReadCoordinator(coordinate);
    service.read();
    expect(coordinate).toHaveBeenCalledOnce();
    const read = service.readForLiveUpdates('memory-only-token', new AbortController().signal);
    await settle();
    const pending = http.expectOne('/api/flights/orders/' + id + '/customer-refund');
    service.setObservationPaused(true);
    expect(pending.cancelled).toBe(true);
    expect((await read).outcome).toBe('stopped');
    expect(service.snapshot()?.state).toBe('Sent');
    service.read();
    expect(coordinate).toHaveBeenCalledOnce();
    service.setObservationPaused(false);
    const unchanged = service.readForLiveUpdates('memory-only-token', new AbortController().signal);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    expect(await unchanged).toMatchObject({ outcome: 'ok', version: 40 });
    expect(service.message()).toBe('');
  });
  it('does not let an older version or another owner overwrite proven money', async () => {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    service.read();
    await settle();
    http
      .expectOne('/api/flights/orders/' + id + '/customer-refund')
      .flush({ ...status(39), state: 'Unknown', source: 'None' });
    await settle();
    expect(service.snapshot()?.state).toBe('Sent');
    auth.status.set({ kind: 'authenticated', userId: 'another-owner' });
    auth.identityEpoch.set(1);
    await settle();
    expect(service.snapshot()).toBeNull();
  });
  it('drops a token callback after the identity epoch changes', async () => {
    let release!: (token: string) => void;
    auth.accessToken.mockReturnValue(
      new Promise<string>((resolve) => {
        release = resolve;
      }),
    );
    service.open(id, owner);
    await settle();
    auth.identityEpoch.set(1);
    await settle();
    release('obsolete-memory-token');
    await settle();
    http.expectNone('/api/flights/orders/' + id + '/customer-refund');
    expect(service.snapshot()).toBeNull();
    expect(service.busy()).toBe(false);
  });
  it('rejects a lower case revision even with a higher booking version', async () => {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    service.read();
    await settle();
    http
      .expectOne('/api/flights/orders/' + id + '/customer-refund')
      .flush({ ...status(41), caseRevision: 2, state: 'Unknown', source: 'None' });
    await settle();
    expect(service.snapshot()?.bookingVersion).toBe(40);
    expect(service.snapshot()?.state).toBe('Sent');
  });
  it('cancels an outstanding GET when the order view closes', async () => {
    service.open(id, owner);
    await settle();
    const request = http.expectOne('/api/flights/orders/' + id + '/customer-refund');
    service.close();
    await settle();
    expect(request.cancelled).toBe(true);
    expect(service.snapshot()).toBeNull();
    expect(service.busy()).toBe(false);
  });
  it('stops polling at its fixed window and permits an explicit read', async () => {
    vi.useFakeTimers();
    try {
      const start = Date.now();
      service.open(id, owner);
      await settle();
      http
        .expectOne('/api/flights/orders/' + id + '/customer-refund')
        .flush({ ...status(), state: 'Unknown', source: 'None' });
      await settle();
      vi.setSystemTime(start + 190_000);
      await vi.advanceTimersByTimeAsync(2_000);
      await settle();
      http.expectNone('/api/flights/orders/' + id + '/customer-refund');
      expect(service.pollingEnded()).toBe(true);
      service.read();
      await settle();
      http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status(41));
      await settle();
      expect(service.snapshot()?.state).toBe('Sent');
    } finally {
      service.close();
      vi.useRealTimers();
    }
  });
  it('clears evidence on logout and reload reads the authoritative resource again', async () => {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    auth.status.set({ kind: 'anonymous' });
    await settle();
    expect(service.snapshot()).toBeNull();
    auth.status.set({ kind: 'authenticated', userId: owner });
    auth.identityEpoch.set(2);
    await settle();
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    expect(service.snapshot()?.state).toBe('Sent');
  });
  it('clears the protected receipt on a forbidden response', async () => {
    service.open(id, owner);
    await settle();
    http.expectOne('/api/flights/orders/' + id + '/customer-refund').flush(status());
    await settle();
    service.read();
    await settle();
    http
      .expectOne('/api/flights/orders/' + id + '/customer-refund')
      .flush({}, { status: 403, statusText: 'Forbidden' });
    await settle();
    expect(service.snapshot()).toBeNull();
    expect(service.message()).toContain('Доступ');
  });
});
