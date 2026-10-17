import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { HotelReservation, HotelSearchSnapshot } from '@travel/api-client';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { HotelsAuthService } from './hotels-auth.service';
import { HotelsPageComponent } from './hotels-page.component';

describe('Hotels page', () => {
  it('clears consent and guests before loading a different draft through the reused route', async () => {
    const { harness, http } = await privateHarness();
    const a = sampleReservation();
    const b = { ...sampleReservation(), id: 'aaaaaaaa-1111-4111-8111-111111111111' };
    const page = await harness.navigateByUrl(`/hotels/reservations/${a.id}`, HotelsPageComponent);
    await flushPrivate(http, a);
    page.party.email = 'previous@example.invalid';
    page.party.guests[0].givenName = 'Previous';
    page.consent = true;
    const reused = await harness.navigateByUrl(`/hotels/reservations/${b.id}`, HotelsPageComponent);
    expect(reused).toBe(page);
    expect(page.consent).toBe(false);
    expect(page.party.email).toBe('');
    await flushPrivate(http, b);
    expect(page.reservation()?.id).toBe(b.id);
    expect(page.party.guests[0].givenName).toBe('');
    await page.buy();
    http.expectNone((request) => request.method === 'POST');
  });

  it('restores two empty guest fields for a saved draft and offers a working fresh-search route', async () => {
    const { harness, http } = await privateHarness();
    const quote = sampleReservation();
    quote.accepted!.occupancy.adults = 2;
    const page = await harness.navigateByUrl(`/hotels/reservations/${quote.id}`, HotelsPageComponent);
    await flushPrivate(http, quote);
    harness.detectChanges();
    expect(page.reservation(), page.message()).not.toBeNull();
    expect(page.party.guests).toHaveLength(2);
    expect(harness.routeNativeElement?.querySelectorAll('.guest input')).toHaveLength(4);
    expect(page.consent).toBe(false);
    expect(harness.routeNativeElement?.textContent).not.toContain('Обновить условия');
    expect(
      harness.routeNativeElement?.querySelector('a[data-testid="restart-hotel-search"]')?.getAttribute('href'),
    ).toBe('/hotels');
  });

  it('clears consent when the same reservation gains a new quote revision', async () => {
    const { harness, http } = await privateHarness();
    const quote = sampleReservation();
    const page = await harness.navigateByUrl(`/hotels/reservations/${quote.id}`, HotelsPageComponent);
    await flushPrivate(http, quote);
    page.party.email = 'previous@example.invalid';
    page.consent = true;
    const loading = page.loadPrivate();
    await flushPrivate(http, { ...quote, quoteRevision: 2 });
    await loading;
    expect(page.consent).toBe(false);
    expect(page.party.email).toBe('');
    expect(page.changedTerms()).toBe(true);
  });

  it('does not clear the new draft when an old purchase finishes after route replacement', async () => {
    const { harness, http } = await privateHarness();
    const a = sampleReservation();
    const b = { ...sampleReservation(), id: 'aaaaaaaa-1111-4111-8111-111111111111' };
    const page = await harness.navigateByUrl(`/hotels/reservations/${a.id}`, HotelsPageComponent);
    await flushPrivate(http, a);
    page.consent = true;
    const purchase = page.buy();
    await Promise.resolve();
    const sent = http.expectOne(`/api/hotels/reservations/${a.id}/purchase`);
    await harness.navigateByUrl(`/hotels/reservations/${b.id}`, HotelsPageComponent);
    await flushPrivate(http, b);
    page.party.email = 'new-draft@example.invalid';
    page.consent = true;
    sent.flush({ ...a, attemptId: '871c43a2-48ad-44f1-ad7f-9ad5d4712b62', outcome: 'Unknown' });
    await purchase;
    expect(page.reservation()?.id).toBe(b.id);
    expect(page.party.email).toBe('new-draft@example.invalid');
    expect(page.consent).toBe(true);
    http.expectNone((request) => request.method === 'POST');
  });

  it('cannot repopulate private data from a request completed after session loss', async () => {
    const { harness, http, auth } = await privateHarness();
    const quote = sampleReservation();
    const page = await harness.navigateByUrl(`/hotels/reservations/${quote.id}`, HotelsPageComponent);
    await flushPrivate(http, quote);
    page.consent = true;
    page.party.email = 'private@example.invalid';
    const loading = page.loadPrivate();
    await Promise.resolve();
    http.expectOne('/api/hotels/reservations').flush([quote]);
    await Promise.resolve();
    const late = http.expectOne(`/api/hotels/reservations/${quote.id}`);
    auth.status.set({ kind: 'anonymous' });
    auth.identityEpoch.update((epoch) => epoch + 1);
    harness.detectChanges();
    late.flush(quote);
    await loading;
    expect(page.reservation()).toBeNull();
    expect(page.reservations()).toHaveLength(0);
    expect(page.party.email).toBe('');
    expect(page.consent).toBe(false);
  });

  it('invalidates an in-flight result as soon as displayed search criteria change', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    const page = fixture.componentInstance;
    const pending = page.find();
    const http = TestBed.inject(HttpTestingController);
    const old = http.expectOne('/api/hotels/search');
    const input = fixture.nativeElement.querySelector('input[name="checkIn"]') as HTMLInputElement;
    input.value = '2027-02-03';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    old.flush(searchSnapshot());
    await pending;
    expect(page.search()).toBeNull();
    expect(page.rates()).toBeNull();
    expect(page.consent).toBe(false);
    expect(page.busy()).toBe(false);
  });

  it('shows configuration and purchase restriction reasons without exposing arbitrary provider text', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    fixture.componentInstance.search.set(searchSnapshot('NotConfigured'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('не настроен');
    fixture.componentInstance.search.set(searchSnapshot('AdultsUnsupported'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('число гостей');
    const snapshot = searchSnapshot();
    snapshot.result.items = [
      {
        provider: 'duffel-stays',
        environment: 'Test',
        selectionKey: 'rate_fictional',
        terms: sampleReservation().accepted!,
        sourceExpiresAt: snapshot.expiresAt,
        purchasable: false,
        purchaseUnavailableReason: 'LocalAmountsUnknown',
      },
    ];
    fixture.componentInstance.rates.set({ ...snapshot, searchId: snapshot.id });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Обязательные платежи в отеле пока неизвестны');
    snapshot.result.items[0].purchaseUnavailableReason = '<script>private-source-value</script>';
    fixture.componentInstance.rates.set({ ...snapshot, searchId: snapshot.id });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('private-source-value');
  });

  it('shows retained mismatch evidence separately from matching current terms', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    const value = sampleReservation();
    value.outcome = 'Confirmed';
    value.active = value.everReserved = true;
    value.reviewReasons = ['TermsMismatch'];
    value.facts = { bookingId: 'bok_fictional', hotelReference: null, actual: value.accepted };
    const payload = {
      ...value,
      firstMismatch: {
        observedAt: '2026-10-17T10:00:00Z',
        actual: { ...value.accepted!, room: 'Different room reported earlier' },
      },
    };
    fixture.componentInstance.reservation.set(payload);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Different room reported earlier');
  });

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

async function privateHarness() {
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
  return { harness: await RouterTestingHarness.create(), http: TestBed.inject(HttpTestingController), auth };
}

async function flushPrivate(http: HttpTestingController, value: HotelReservation) {
  await Promise.resolve();
  await Promise.resolve();
  http.match('/api/hotels/reservations').forEach((request) => {
    request.flush([value]);
  });
  await Promise.resolve();
  await Promise.resolve();
  http.match(`/api/hotels/reservations/${value.id}`).forEach((request) => {
    request.flush(value);
  });
  await Promise.resolve();
  await Promise.resolve();
}

function searchSnapshot(reason: string | null = null): HotelSearchSnapshot {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    result: { kind: reason ? 'Unsupported' : 'Complete', items: [], reason },
    statuses: [{ provider: 'duffel-stays', kind: reason ? 'Unsupported' : 'Complete', reason }],
    expiresAt: new Date(Date.now() + 300000).toISOString(),
  };
}

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
