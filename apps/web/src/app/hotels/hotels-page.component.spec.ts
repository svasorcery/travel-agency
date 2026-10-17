import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { HotelReservation } from '@travel/api-client';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { HotelsAuthService } from './hotels-auth.service';
import { HotelsPageComponent } from './hotels-page.component';

describe('Hotels page', () => {
  it('offers bounded curated destinations and explicit fictional labelling', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Выдуманный');
    expect(fixture.nativeElement.querySelectorAll('select[name="area"] option').length).toBe(4);
    expect(fixture.nativeElement.querySelector('input[type="date"]')).toBeTruthy();
  });

  it('dispatches the captured RAM party before real Router navigation destroys the form', async () => {
    const auth = {
      isDemo: true,
      status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: '785e39a4-b13f-4fd8-835b-1ab619cd0e14' }),
      identityEpoch: signal(0),
      initializeFromCallback: async () => false,
      accessToken: async () => 'fictional',
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'hotels', component: HotelsPageComponent },
          { path: 'hotels/reservations/:id', component: HotelsPageComponent },
        ]),
        { provide: HotelsAuthService, useValue: auth },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/hotels', HotelsPageComponent);
    const quote = sampleReservation();
    page.reservation.set(quote);
    page.party = {
      guests: [{ givenName: 'Fictional', familyName: 'Traveller' }],
      email: 'fictional@example.invalid',
      phone: '+44000000000',
      adultsConfirmed: true,
      checkInConfirmed: true,
    };
    page.consent = true;
    const pending = page.buy();
    await Promise.resolve();
    await Promise.resolve();
    const http = TestBed.inject(HttpTestingController);
    const sent = http.expectOne(`/api/hotels/reservations/${quote.id}/purchase`);
    expect(sent.request.body.party.guests[0].givenName).toBe('Fictional');
    expect(sent.request.body.party.email).toBe('fictional@example.invalid');
    expect(page.party.email).toBe('fictional@example.invalid');
    sent.flush({
      ...quote,
      attemptId: '871c43a2-48ad-44f1-ad7f-9ad5d4712b62',
      outcome: 'Unknown',
      reviewReasons: ['TransportUnknown'],
    });
    await pending;
    expect(page.party.email).toBe('');
    expect(page.consent).toBe(false);
    expect(harness.routeDebugElement?.componentInstance).not.toBe(page);
    expect(harness.routeDebugElement?.componentInstance.detailRoute).toBe(true);
    const list = http.match('/api/hotels/reservations');
    list.forEach((request) => {
      request.flush([]);
    });
    await Promise.resolve();
    await Promise.resolve();
    http.match(`/api/hotels/reservations/${quote.id}`).forEach((request) => {
      request.flush({
        ...quote,
        attemptId: '871c43a2-48ad-44f1-ad7f-9ad5d4712b62',
        outcome: 'Unknown',
        reviewReasons: ['TransportUnknown'],
      });
    });
    http.expectNone((request) => request.method === 'POST');
  });
});

function sampleReservation(): HotelReservation {
  return {
    id: 'c3f04101-c153-4d73-b564-9c5096077444',
    quoteRevision: 1,
    validUntil: new Date(Date.now() + 300000).toISOString(),
    attemptId: null,
    outcome: 'Quoted',
    reviewReasons: [],
    everReserved: false,
    active: false,
    facts: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    privateContentAvailable: true,
    accepted: {
      property: { id: 'fictional', name: 'Fictional hotel', address: null, rating: null, ratingSource: null },
      room: 'Double',
      rateId: 'rate_fictional',
      dates: { checkIn: '2027-01-03', checkOut: '2027-01-05', nights: 2 },
      occupancy: { adults: 1 },
      total: { amount: '120.25', currency: 'GBP' },
      mandatoryLocalPayments: [],
      board: [],
      cancellation: { kind: 'NonRefundable', rules: [] },
      conditions: [],
      eligible: true,
      paymentTiming: 'ImmediateTest',
    },
  };
}
