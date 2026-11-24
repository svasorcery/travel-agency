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
  it('explains saved whole-stay ranking and shows the selected supplier name limit', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    const quote = { ...sampleReservation(), provider: 'hotelbeds' };
    const snapshot = searchSnapshot();
    snapshot.result = {
      kind: 'Complete',
      reason: null,
      items: [
        {
          provider: 'hotelbeds',
          environment: 'Test',
          selectionKey: 'owned',
          terms: quote.accepted!,
          sourceExpiresAt: null,
          purchasable: true,
          purchaseUnavailableReason: null,
          ranking: {
            policyVersion: 'hotel-cost-first-v1',
            scope: 'WholeStay',
            reason: 'KnownWholeStayObligations',
            bucket: 'SupplierCost:GBP',
            total: { amount: '210', currency: 'GBP' },
          },
        },
      ],
    };
    fixture.componentInstance.rates.set({ ...snapshot, searchId: snapshot.id });
    fixture.componentInstance.reservation.set(quote);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('210 GBP');
    expect(fixture.nativeElement.querySelector('.guest input')?.getAttribute('maxlength')).toBe('50');
    expect(fixture.nativeElement.textContent).toContain('не более 50 символов');
  });
  it('shows one reviewed property card with separate currencies and both sources', async () => {
    TestBed.configureTestingModule({
      imports: [HotelsPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(HotelsPageComponent);
    await fixture.whenStable();
    const snapshot = searchSnapshot();
    const a = {
      provider: 'duffel-stays',
      environment: 'Test',
      selectionKey: 'local-a',
      terms: sampleReservation().accepted!,
      sourceExpiresAt: null,
      purchasable: true,
      purchaseUnavailableReason: null,
    };
    const b = { ...a, provider: 'hotelbeds', selectionKey: 'local-b' };
    snapshot.result = { kind: 'Complete', reason: null, items: [a, b] };
    snapshot.groups = [
      {
        key: 'owned-group',
        property: a.terms.property,
        representative: {
          source: { provider: 'duffel-stays', account: 'owned', environment: 'Test', profile: 'Fictional' },
          propertyId: a.terms.property.id,
        },
        offers: [a, b],
        matchReason: 'Reviewed',
        primaryBucket: 'SupplierCost:EUR',
        minima: [
          { bucket: 'SupplierCost:EUR', total: { amount: '210', currency: 'EUR' } },
          { bucket: 'SupplierCost:GBP', total: { amount: '200', currency: 'GBP' } },
        ],
        rankingPolicyVersion: 'hotel-cost-first-v1',
      },
    ];
    fixture.componentInstance.search.set(snapshot);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="hotel-property-group"]')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).toContain('210 EUR');
    expect(fixture.nativeElement.textContent).toContain('200 GBP');
    expect(fixture.nativeElement.textContent).toContain('Hotelbeds');
    expect(fixture.nativeElement.textContent).toContain('Duffel Stays');
  });
  it('keeps the purchase resource while repeating a search before admission', async () => {
    const { harness, http } = await privateHarness();
    const quote = sampleReservation();
    const page = await harness.navigateByUrl(`/hotels/reservations/${quote.id}`, HotelsPageComponent);
    await flushPrivate(http, quote);
    page.consent = true;
    const finding = page.find();
    http.expectOne('/api/hotels/search').flush(searchSnapshot());
    await finding;
    expect(page.reservation()?.id).toBe(quote.id);
    expect(page.consent).toBe(false);
  });
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

  it('offers attachment only for eligible details when discovery says it is available', async () => {
    const { harness, http } = await privateHarness();
    const value = confirmedReservation();
    const component = await harness.navigateByUrl(`/hotels/reservations/${value.id}`, HotelsPageComponent);
    await flushPrivate(http, value);
    harness.detectChanges();
    await harness.fixture.whenStable();
    http.expectOne('/api/trips/hotel-link/availability').flush(linkBytes({ code: 'Trips.NotFound' }), {
      status: 404,
      statusText: 'Not Found',
    });
    await renderLink(harness);
    expect(findButton(harness, 'Добавить в поездку')).toBeUndefined();
    component.reservation.set({ ...value, reviewReasons: ['TermsMismatch'] });
    harness.detectChanges();
    await harness.fixture.whenStable();
    http.expectNone('/api/trips/hotel-link/availability');
    expect(findButton(harness, 'Добавить в поездку')).toBeUndefined();
    http.verify();
  });

  it('uses Hotels auth for paginated trip selection, then links and navigates by trip id alone', async () => {
    const { harness, http } = await eligibleLinkHarness();
    const trip = {
      tripId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      revision: '4',
      title: 'Петербург',
      destinationId: 'saint-petersburg',
      displayName: 'Санкт-Петербург',
      startDate: '2027-06-13',
      endDate: '2027-06-15',
      updatedAt: '2026-11-24T12:00:00Z',
    };
    findButton(harness, 'Добавить в поездку')!.click();
    await harness.fixture.whenStable();
    const first = http.expectOne('/api/trips');
    expect(first.request.headers.get('Authorization')).toBe('Bearer fictional');
    first.flush(JSON.stringify({ items: [trip], nextCursor: 'next_page' }));
    await renderLink(harness);
    findButton(harness, 'Ещё поездки')!.click();
    await harness.fixture.whenStable();
    const next = http.expectOne('/api/trips?cursor=next_page');
    expect(next.request.headers.get('Authorization')).toBe('Bearer fictional');
    const selectedId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    next.flush(JSON.stringify({ items: [{ ...trip, tripId: selectedId, title: 'Моя поездка' }], nextCursor: null }));
    await renderLink(harness);
    const select = harness.routeNativeElement!.querySelector<HTMLSelectElement>('#hotel-trip-choice')!;
    expect(select.labels?.[0].textContent).toContain('Сохранённая поездка');
    select.value = selectedId;
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    const path = `/api/trips/${selectedId}/hotel-link`;
    http.expectOne({ method: 'GET', url: path }).flush(linkBytes({ hasLink: false, revision: '0' }), {
      headers: { ETag: '"hl-0"' },
    });
    await renderLink(harness);
    expect(harness.routeNativeElement!.textContent).toContain('Белые ночи');
    expect(harness.routeNativeElement!.textContent).toContain('Моя поездка · 2027-06-13 — 2027-06-15');
    findButton(harness, 'Подтвердить привязку')!.click();
    await harness.fixture.whenStable();
    const patch = http.expectOne({ method: 'PATCH', url: path });
    expect(patch.request.headers.get('Authorization')).toBe('Bearer fictional');
    expect(patch.request.headers.get('If-Match')).toBe('"hl-0"');
    expect(patch.request.body).toEqual({ op: 'add', reservationId: confirmedReservation().id });
    patch.flush(linkBytes({ hasLink: true, revision: '1' }), { headers: { ETag: '"hl-1"' } });
    await renderLink(harness);
    expect(harness.routeNativeElement!.querySelector(`a[href="/trips/${selectedId}"]`)).not.toBeNull();
    http.verify();
  });

  it.each([
    [412, {}, 'Связь изменилась'],
    [409, { code: 'Trips.HotelLinkOccupied' }, 'уже привязан другой заказ'],
    [503, {}, 'Результат записи неизвестен'],
  ])('requires a new metadata read and explicit action after HTTP %i', async (status, body, message) => {
    const { harness, http } = await eligibleLinkHarness();
    const tripId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    findButton(harness, 'Добавить в поездку')!.click();
    await harness.fixture.whenStable();
    http.expectOne('/api/trips').flush(
      JSON.stringify({
        items: [
          {
            tripId,
            revision: '1',
            title: 'Поездка',
            destinationId: 'saint-petersburg',
            displayName: 'Санкт-Петербург',
            startDate: '2027-06-13',
            endDate: '2027-06-15',
            updatedAt: '2026-11-24T12:00:00Z',
          },
        ],
        nextCursor: null,
      }),
    );
    await renderLink(harness);
    const select = harness.routeNativeElement!.querySelector<HTMLSelectElement>('#hotel-trip-choice')!;
    select.value = tripId;
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    const path = `/api/trips/${tripId}/hotel-link`;
    http
      .expectOne({ method: 'GET', url: path })
      .flush(linkBytes({ hasLink: false, revision: '0' }), { headers: { ETag: '"hl-0"' } });
    await renderLink(harness);
    findButton(harness, 'Подтвердить привязку')!.click();
    await harness.fixture.whenStable();
    http.expectOne({ method: 'PATCH', url: path }).flush(linkBytes(body), { status, statusText: 'Fictional response' });
    await renderLink(harness);
    expect(harness.routeNativeElement!.textContent).toContain(message);
    expect(findButton(harness, 'Подтвердить привязку')).toBeUndefined();
    http.expectNone(path);
    findButton(harness, 'Перечитать сведения о связи')!.click();
    await harness.fixture.whenStable();
    http
      .expectOne({ method: 'GET', url: path })
      .flush(linkBytes({ hasLink: true, revision: '1' }), { headers: { ETag: '"hl-1"' } });
    await renderLink(harness);
    expect(findButton(harness, 'Подтвердить привязку')).toBeDefined();
    http.verify();
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

function confirmedReservation(): HotelReservation {
  const value = sampleReservation();
  return {
    ...value,
    outcome: 'Confirmed',
    active: true,
    everReserved: true,
    facts: {
      bookingId: 'confirmed_fictional',
      hotelReference: null,
      actual: {
        ...value.accepted!,
        property: { ...value.accepted!.property, name: 'Белые ночи' },
        dates: { checkIn: '2027-06-13', checkOut: '2027-06-15', nights: 2 },
      },
    },
  };
}

function linkBytes(value: unknown): ArrayBuffer {
  const encoded = new TextEncoder().encode(JSON.stringify(value));
  const data = new ArrayBuffer(encoded.byteLength);
  new Uint8Array(data).set(encoded);
  return data;
}

function findButton(harness: RouterTestingHarness, text: string) {
  return Array.from(harness.routeNativeElement!.querySelectorAll('button')).find(
    (button) => button.textContent?.trim() === text,
  );
}

async function renderLink(harness: RouterTestingHarness) {
  await new Promise<void>((resolve) => setTimeout(resolve, 0));
  await harness.fixture.whenStable();
  harness.detectChanges();
}

async function eligibleLinkHarness() {
  const fixture = await privateHarness();
  const value = confirmedReservation();
  await fixture.harness.navigateByUrl(`/hotels/reservations/${value.id}`, HotelsPageComponent);
  await flushPrivate(fixture.http, value);
  fixture.harness.detectChanges();
  await fixture.harness.fixture.whenStable();
  fixture.http.expectOne('/api/trips/hotel-link/availability').flush(linkBytes({ enabled: true, canAttach: true }));
  await renderLink(fixture.harness);
  expect(findButton(fixture.harness, 'Добавить в поездку')).toBeDefined();
  return fixture;
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
