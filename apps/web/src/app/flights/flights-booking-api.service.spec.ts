import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  FlightBookingContractError,
  type FlightOrderListResponse,
  FlightsBookingApiService,
  type HoldFlightOrderRequest,
} from '@travel/api-client';
import { TimeoutError } from 'rxjs';

describe('FlightsBookingApiService', () => {
  let api: FlightsBookingApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(FlightsBookingApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('requests a lookahead page with the token only in the bearer header', () => {
    let result: FlightOrderListResponse | undefined;
    api.listOrders(20, 'memory-only-access-token').subscribe((response) => (result = response));
    const request = http.expectOne('/api/flights/orders?limit=21&offset=20');
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer memory-only-access-token');
    expect(request.request.headers.has('Idempotency-Key')).toBe(false);
    expect(request.request.headers.has('Content-Type')).toBe(false);
    expect(request.request.body).toBeNull();
    request.flush({ items: [], limit: 21, offset: 20 });
    expect(result).toEqual({ items: [], limit: 21, offset: 20 });
  });

  it('omits Authorization in demo and does not reuse a previous request token', () => {
    api.listOrders(0, 'memory-only-access-token').subscribe();
    http.expectOne('/api/flights/orders?limit=21&offset=0').flush({ items: [], limit: 21, offset: 0 });
    api.listOrders(0, null).subscribe();
    const request = http.expectOne('/api/flights/orders?limit=21&offset=0');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ items: [], limit: 21, offset: 0 });
  });

  it.each([-20, 1, 20.5, Number.NaN, Number.POSITIVE_INFINITY, Number.MAX_SAFE_INTEGER, 2_147_483_660])(
    'rejects invalid list offset %s before sending HTTP',
    (offset) => {
      expect(() => api.listOrders(offset, null)).toThrow(FlightBookingContractError);
      http.expectNone(() => true);
    },
  );

  it('accepts the greatest page offset within a C# int', () => {
    api.listOrders(2_147_483_640, null).subscribe();
    http
      .expectOne('/api/flights/orders?limit=21&offset=2147483640')
      .flush({ items: [], limit: 21, offset: 2_147_483_640 });
  });

  it('passes a response contract error without emitting a partial page', () => {
    let failure: unknown;
    let result: FlightOrderListResponse | undefined;
    api
      .listOrders(0, null)
      .subscribe({ next: (response) => (result = response), error: (error: unknown) => (failure = error) });
    http.expectOne('/api/flights/orders?limit=21&offset=0').flush({ items: [{}], limit: 21, offset: 0 });
    expect(failure).toBeInstanceOf(FlightBookingContractError);
    expect(result).toBeUndefined();
  });

  it.each([401, 403, 500])('passes HTTP %s errors to the caller', (status) => {
    let failure: unknown;
    api.listOrders(0, null).subscribe({ error: (error: unknown) => (failure = error) });
    http.expectOne('/api/flights/orders?limit=21&offset=0').flush({}, { status, statusText: 'fixture error' });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect((failure as HttpErrorResponse).status).toBe(status);
  });

  it('cancels list HTTP when the caller leaves the page', () => {
    const subscription = api.listOrders(0, null).subscribe();
    const request = http.expectOne('/api/flights/orders?limit=21&offset=0');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
  });

  it('bounds an unanswered list request at fifteen seconds', () => {
    vi.useFakeTimers();
    let failure: unknown;
    api.listOrders(0, null).subscribe({ error: (error: unknown) => (failure = error) });
    const request = http.expectOne('/api/flights/orders?limit=21&offset=0');
    vi.advanceTimersByTime(14_999);
    expect(failure).toBeUndefined();
    vi.advanceTimersByTime(2);
    expect(failure).toBeInstanceOf(TimeoutError);
    expect(request.cancelled).toBe(true);
  });

  it('sends hold with a bearer token and a v4 key, preserving JSON request bytes', () => {
    const body: HoldFlightOrderRequest = {
      aggregateId: '88b83d41-0194-2098-c1f6-fe7351d41cf2',
      quoteRevision: '11111111-1111-4111-8111-111111111111',
      passengers: [
        {
          bookingPassengerId: '22222222-2222-4222-8222-222222222222',
          title: 'mr' as const,
          givenName: 'Demo',
          familyName: 'Traveler',
          dateOfBirth: '1990-04-12',
          gender: 'male' as const,
          email: 'demo@example.test',
          phone: '+79161234567',
        },
      ],
    };
    let result = '';
    api
      .hold(body, '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac', 'memory-only-access-token')
      .subscribe((held) => (result = held.providerOrderId));

    const request = http.expectOne('/api/flights/orders/hold');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBe(JSON.stringify(body));
    expect(request.request.headers.get('Authorization')).toBe('Bearer memory-only-access-token');
    expect(request.request.headers.get('Idempotency-Key')).toBe('4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac');
    request.flush({
      aggregateId: body.aggregateId,
      providerOrderId: 'fake-provider-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
    expect(result).toBe('fake-provider-order');
  });

  it('does not add Authorization to the isolated demo booking request', () => {
    api
      .confirm({ aggregateId: '88b83d41-0194-2098-c1f6-fe7351d41cf2' }, '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac', null)
      .subscribe();

    const request = http.expectOne('/api/flights/orders/confirm');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ aggregateId: '88b83d41-0194-2098-c1f6-fe7351d41cf2', status: 'Confirmed', paymentRef: null });
  });

  it('reads one owner-scoped order with a bearer token and no write headers', () => {
    const id = '88b83d41-0194-2098-c1f6-fe7351d41cf2';
    let received = '';
    api.getOrder(id, 'memory-only-access-token').subscribe((order) => (received = order.status));
    const request = http.expectOne(`/api/flights/orders/${id}`);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer memory-only-access-token');
    expect(request.request.headers.has('Idempotency-Key')).toBe(false);
    request.flush({
      aggregateId: id,
      status: 'Held',
      totalAmount: 5400,
      currency: 'RUB',
      itinerary: {
        slices: [
          {
            origin: 'LED',
            destination: 'DME',
            duration: '02:00:00',
            segments: [
              {
                origin: 'LED',
                destination: 'DME',
                departAt: '2030-06-10T10:00:00+03:00',
                arriveAt: '2030-06-10T12:00:00+03:00',
                carrierCode: 'SU',
                flightNumber: '100',
                cabinClass: 'economy',
              },
            ],
          },
        ],
        totalDuration: '02:00:00',
        isRoundTrip: false,
      },
      passengerCount: 1 as const,
      ticketNumbers: [],
      bookedAt: '2030-06-01T10:00:00Z',
      ticketedAt: null,
      cancelledAt: null,
      refundedAt: null,
    });
    expect(received).toBe('Held');
  });

  it('refuses an invalid order URL before making a request', () => {
    expect(() => api.getOrder('../other', null)).toThrow();
    http.expectNone(() => true);
  });
});
