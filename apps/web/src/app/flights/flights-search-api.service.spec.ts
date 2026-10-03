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

  it('posts ordered explicit criteria to v2 and rejects a wrong later airport/date/count or chronology', () => {
    const base = structuredClone(fixtures.roundTrip.response.offers[0]);
    Object.assign(base.itinerary, { journeyKind: 'round-trip' });
    const body = {
      legs: base.itinerary.slices.map((slice) => ({
        origin: slice.origin,
        destination: slice.destination,
        departureDate: slice.segments[0].departAt.slice(0, 10),
      })),
      passengerCount: 1 as const,
      cabinClass: 'economy' as const,
    };
    let result: FlightSearchResponse | undefined;
    service.searchMultiLeg(body).subscribe((value) => (result = value));
    const request = http.expectOne('/api/flights/search/v2?currency=RUB');
    expect(request.request.body).toEqual(body);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ offers: [base], partialFailures: [], skippedProviders: [] });
    expect(result?.offers).toHaveLength(1);
    for (const mutate of [
      (offer: typeof base) => {
        offer.itinerary.slices[1].origin = 'VKO';
        offer.itinerary.slices[1].segments[0].origin = 'VKO';
        Object.assign(offer.itinerary, { journeyKind: 'multi-leg', isRoundTrip: false });
      },
      (offer: typeof base) => {
        offer.itinerary.slices[1].segments[0].departAt = '2030-06-18T08:00:00+03:00';
      },
      (offer: typeof base) => {
        offer.passengerCount = 2;
      },
      (offer: typeof base) => {
        offer.itinerary.slices[1].segments[0].departAt = offer.itinerary.slices[0].segments[0].departAt;
      },
      (offer: typeof base) => {
        offer.itinerary.slices[1].segments[0].cabinClass = 'business';
      },
      (offer: typeof base) => {
        offer.itinerary.slices[1].duration = '03:00:00';
      },
      (offer: typeof base) => {
        offer.itinerary.totalDuration = '05:00:00';
      },
    ]) {
      const changed = structuredClone(base);
      mutate(changed);
      let rejected = false;
      service.searchMultiLeg(body).subscribe({ error: () => (rejected = true) });
      http
        .expectOne('/api/flights/search/v2?currency=RUB')
        .flush({ offers: [changed], partialFailures: [], skippedProviders: [] });
      expect(rejected).toBe(true);
    }
  });

  it('rejects a fresh mirrored overlap even when all requested local dates and airports match', () => {
    const offer = structuredClone(fixtures.roundTrip.response.offers[0]);
    Object.assign(offer.itinerary, { journeyKind: 'round-trip' });
    offer.itinerary.slices[1].segments[0].departAt = '2030-06-10T11:00:00+03:00';
    offer.itinerary.slices[1].segments[0].arriveAt = '2030-06-10T13:00:00+03:00';
    const criteria = {
      legs: offer.itinerary.slices.map((slice) => ({
        origin: slice.origin,
        destination: slice.destination,
        departureDate: slice.segments[0].departAt.slice(0, 10),
      })),
      passengerCount: 1 as const,
      cabinClass: 'economy' as const,
    };
    let rejected = false;
    service.searchMultiLeg(criteria).subscribe({ error: () => (rejected = true) });
    http
      .expectOne('/api/flights/search/v2?currency=RUB')
      .flush({ offers: [offer], partialFailures: [], skippedProviders: [] });
    expect(rejected).toBe(true);
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
  it('consumes a supplied group total without multiplication and rejects a singleton response for group intent', () => {
    const groupRequest = { ...fixtures.groupTwo.request, passengerCount: 2 as const, cabinClass: 'economy' as const };
    let amount = 0;
    service.search(groupRequest).subscribe((response) => (amount = response.offers[0].totalAmount));
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.groupTwo.response);
    expect(amount).toBe(20750);
    let rejected = false;
    service.search(groupRequest).subscribe({ error: () => (rejected = true) });
    http.expectOne('/api/flights/search?currency=RUB').flush(fixtures.oneWay.response);
    expect(rejected).toBe(true);
  });
});
