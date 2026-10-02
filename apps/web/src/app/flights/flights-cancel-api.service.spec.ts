import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FlightBookingContractError, FlightsBookingApiService } from '@travel/api-client';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';

describe('Cancellation HTTP contract', () => {
  const id = booking.oneWay.response.aggregateId;
  const key = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
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
  let http: HttpTestingController;
  let api: FlightsBookingApiService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(FlightsBookingApiService);
  });
  afterEach(() => http.verify());
  it('sends canonical bodyless retry with identical key and a refreshed bearer', () => {
    for (const token of ['first-memory-token', 'refreshed-memory-token']) {
      api.cancel(id.toUpperCase(), key, token).subscribe();
      const req = http.expectOne(`/api/flights/orders/${id}/cancel`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toBeNull();
      expect(req.request.headers.get('Idempotency-Key')).toBe(key);
      expect(req.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
      req.flush(cancelled);
    }
  });
  it('rejects empty 2xx without emitting success and omits bearer in demo', () => {
    let failure: unknown;
    api.cancel(id, key, null).subscribe({ error: (error: unknown) => (failure = error) });
    const req = http.expectOne(`/api/flights/orders/${id}/cancel`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(failure).toBeInstanceOf(FlightBookingContractError);
  });
});
