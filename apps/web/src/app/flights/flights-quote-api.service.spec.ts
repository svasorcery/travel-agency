import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { type FlightQuoteResponse, FlightsQuoteApiService } from '@travel/api-client';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';

describe('FlightsQuoteApiService', () => {
  let api: FlightsQuoteApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(FlightsQuoteApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends an anonymous quote with the selected provider reference and decodes the response', () => {
    let quote: FlightQuoteResponse | undefined;
    api.quote(booking.oneWay.request).subscribe((result) => (quote = result));
    const request = http.expectOne('/api/flights/orders/quote');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(booking.oneWay.request);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(booking.oneWay.response);
    expect(quote?.offer.totalAmount).toBe(10800);
    expect(quote?.fareConditions.checkedBaggageQuantity).toBe(1);
  });
});
