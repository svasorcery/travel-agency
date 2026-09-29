import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { type FlightSearchResponse, FlightsSearchApiService } from '@travel/api-client';
import { TimeoutError } from 'rxjs';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-search.json';

const criteria = {
  origin: 'LED',
  destination: 'DME',
  departureDate: '2030-06-10',
  returnDate: null,
  passengerCount: 1,
  cabinClass: 'economy',
} as const;

describe('FlightsSearchApiService', () => {
  let service: FlightsSearchApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FlightsSearchApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('sends the anonymous search contract and decodes the HTTP response', () => {
    let result: FlightSearchResponse | undefined;
    service.search(criteria).subscribe((response) => (result = response));
    const request = http.expectOne('/api/flights/search?currency=RUB');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(criteria);
    expect(request.request.headers.get('Content-Type')).toBe('application/json');
    expect(request.request.headers.get('Accept-Language')).toBe('ru');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(fixtures.oneWay.response);
    expect(result?.offers).toHaveLength(2);
  });

  it('passes HTTP errors to the caller without turning them into empty results', () => {
    let failure: unknown;
    service.search(criteria).subscribe({ error: (error: unknown) => (failure = error) });
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.unavailable.response, {
      status: 500,
      statusText: 'Internal Server Error',
    });
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect((failure as HttpErrorResponse).status).toBe(500);
  });

  it('cancels a request when the caller leaves the search', () => {
    const subscription = service.search(criteria).subscribe();
    const request = http.expectOne('/api/flights/search?currency=RUB');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
  });

  it('bounds a request that never receives a response', () => {
    vi.useFakeTimers();
    let failure: unknown;
    service.search(criteria).subscribe({ error: (error: unknown) => (failure = error) });
    const request = http.expectOne('/api/flights/search?currency=RUB');
    vi.advanceTimersByTime(15_001);
    expect(failure).toBeInstanceOf(TimeoutError);
    expect(request.cancelled).toBe(true);
  });
});
