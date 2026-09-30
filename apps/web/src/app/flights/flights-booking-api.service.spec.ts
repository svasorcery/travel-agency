import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FlightsBookingApiService, type HoldFlightOrderRequest } from '@travel/api-client';

describe('FlightsBookingApiService', () => {
  let api: FlightsBookingApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(FlightsBookingApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends hold with a bearer token and a v4 key, preserving JSON request bytes', () => {
    const body: HoldFlightOrderRequest = {
      aggregateId: '88b83d41-0194-2098-c1f6-fe7351d41cf2',
      passengers: [
        {
          givenName: 'Demo',
          familyName: 'Traveler',
          dateOfBirth: '1990-04-12',
          gender: 'unspecified' as const,
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
