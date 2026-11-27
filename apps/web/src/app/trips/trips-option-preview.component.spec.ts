import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import {
  decodeOptionEnvelope,
  decodeSavedTripOption,
  type TripBrief,
  TripsApiService,
  TripsFlightLinksApiService,
  TripsHotelLinkApiService,
  tripItineraryReleases,
} from '@travel/api-client';
import { tripsExample } from '@travel/api-client/testing/trips';
import { BehaviorSubject, of } from 'rxjs';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TripsOptionSaveState } from './trips-option-save-state.service';
import { TripsPageComponent } from './trips-page.component';

const url = '/api/trips/option-preview';
const discovery = {
  profileId: 'spb-option',
  dataMode: 'fictional',
  asOfUtc: '2026-10-27T12:00:00Z',
  startDate: '2026-10-28',
  endDate: '2026-10-30',
} as const;
const brief: TripBrief = {
  originId: 'moscow',
  destinationId: 'saint-petersburg',
  startDate: discovery.startDate,
  endDate: discovery.endDate,
  adults: 2,
  interests: ['architecture'],
  pace: 'relaxed',
  totalBudget: null,
  currency: 'RUB',
};
const release = tripItineraryReleases['saint-petersburg'];
const common = {
  asOfUtc: '2026-10-27T12:00:00+00:00',
  catalogueVersion: 'A',
  policyVersion: 'v1',
  datasetId: release.datasetId,
  datasetVersion: release.version,
  datasetHash: release.hash,
  dataUrl: release.offer,
  attribution: '© OpenStreetMap contributors',
};
const stop = {
  id: 'spb-general-staff',
  title: 'Генеральный штаб',
  start: '14:20',
  end: '14:50',
  dwellMinutes: 30,
  incomingEdgeId: null,
  walkFromPreviousMinutes: 0,
  accessHoursKnown: false,
};
const option = {
  flight: {
    id: 'RT1',
    adults: 2,
    outbound: {
      fromAirport: 'SVO',
      toAirport: 'LED',
      departsAt: '2026-10-28T07:30:00+03:00',
      arrivesAt: '2026-10-28T09:00:00+03:00',
    },
    inbound: {
      fromAirport: 'LED',
      toAirport: 'SVO',
      departsAt: '2026-10-30T11:00:00+03:00',
      arrivesAt: '2026-10-30T12:30:00+03:00',
    },
  },
  hotel: {
    id: 'H1',
    name: 'Невский двор',
    adults: 2,
    nights: 2,
    checkInAt: '2026-10-28T14:00:00+03:00',
    checkOutAt: '2026-10-30T08:00:00+03:00',
  },
  days: [
    {
      date: brief.startDate,
      windowStart: '14:20',
      windowEnd: '17:40',
      programmeMinutes: 30,
      stops: [stop],
      emptyReason: null,
    },
    {
      date: '2026-10-29',
      windowStart: '10:20',
      windowEnd: '17:40',
      programmeMinutes: 0,
      stops: [],
      emptyReason: 'noEligibleStops',
    },
    {
      date: brief.endDate,
      windowStart: null,
      windowEnd: null,
      programmeMinutes: 0,
      stops: [],
      emptyReason: 'noWalkingWindow',
    },
  ],
  price: {
    currency: 'RUB',
    knownSubtotal: '30000.00',
    flightAndStayComplete: false,
    budgetStatus: 'notSpecified',
    components: [
      { kind: 'travelerMandatory', component: 'flightBase', amount: '18000.00', currency: 'RUB' },
      { kind: 'travelerMandatory', component: 'hotelBase', amount: '12000.00', currency: 'RUB' },
      { kind: 'unknownMandatory', component: 'localFee', amount: null, currency: null },
    ],
    unpricedExpenses: ['meals', 'transfers'],
  },
  coveredInterests: ['architecture'],
  unmetInterests: [],
  selectionReason: 'lowerComparablePrice',
  arrivalTransferMinutes: 90,
  departureTransferMinutes: 180,
  hotelToWalkMinutes: 20,
  walkToHotelMinutes: 20,
};
const success = { ...common, outcome: 'option', reasons: [], option };
const selection = {
  tripId: tripsExample.tripId,
  selectionToken: 'demo+/bounded==',
  expiresAtUtc: '2099-01-01T12:15:00Z',
};
const history = { savedOption: { version: 1, brief, preview: success, savedAtUtc: '2026-11-27T09:15:00Z' } };
const createUrl = '/api/trips/saved-options';
const historyUrl = `/api/trips/${selection.tripId}/saved-option`;

async function setup() {
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner-a' }),
    identityEpoch: signal(0),
    hotelReadAccess: signal({ allowed: false }),
    accessToken: vi.fn().mockResolvedValue('access'),
    hasOidcCallback: () => false,
    deny: vi.fn(() => {
      auth.identityEpoch.update((n) => n + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
    logout: vi.fn(async () => {
      auth.identityEpoch.update((n) => n + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
  };
  const api = {
    list: vi.fn(() => of({ items: [], nextCursor: null })),
    detail: vi.fn(() => of(tripsExample)),
    create: vi.fn(),
    replace: vi.fn(),
  };
  const params = new BehaviorSubject(convertToParamMap({}));
  TestBed.configureTestingModule({
    imports: [TripsPageComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: params, snapshot: { paramMap: params.value } } },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsApiService, useValue: api },
      { provide: TripsFlightLinksApiService, useValue: { availability: () => of(false) } },
      { provide: TripsHotelLinkApiService, useValue: { availability: () => of({ enabled: false, canAttach: false }) } },
      {
        provide: TripsAvailabilityService,
        useValue: {
          status: signal('ready'),
          enabled: () => true,
          catalogue: signal({
            catalogueVersion: '1',
            items: [
              tripsExample.destination,
              { ...tripsExample.destination, destinationId: 'moscow', displayName: 'Москва' },
            ],
          }),
          initialize: vi.fn().mockResolvedValue(undefined),
          clearPrivate: vi.fn(),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(TripsPageComponent),
    page = fixture.componentInstance,
    http = TestBed.inject(HttpTestingController),
    root = fixture.nativeElement as HTMLElement;
  async function render() {
    for (let n = 0; n < 3; n++) {
      fixture.detectChanges();
      for (let i = 0; i < 12; i++) await Promise.resolve();
    }
    fixture.detectChanges();
  }
  await render();
  const availability = http.expectOne(url + '/availability');
  expect(availability.request.method).toBe('GET');
  expect(availability.request.headers.get('Authorization')).toBe('Bearer access');
  const action = () => root.querySelector<HTMLButtonElement>('#trip-option-preview');
  const result = () => root.querySelector<HTMLElement>('#trip-option-result');
  async function enable() {
    availability.flush(JSON.stringify(discovery));
    await render();
  }
  async function fill() {
    page.patchBrief('originId', brief.originId);
    page.patchBrief('adults', brief.adults);
    page.patchBrief('pace', brief.pace);
    page.toggleInterest('architecture', true);
    for (const [id, value] of [
      ['trip-start', brief.startDate],
      ['trip-end', brief.endDate],
    ]) {
      const field = root.querySelector<HTMLInputElement>('#' + id)!;
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    }
    await render();
  }
  const send = async () => {
    action()!.click();
    await render();
    return http.expectOne(url);
  };
  return { auth, api, params, page, http, root, render, availability, enable, fill, action, result, send };
}
describe('Trip option in the unsaved creation form', () => {
  it('sends the visible unapplied brief once, renders the private result and preserves the ordinary create action', async () => {
    const s = await setup();
    expect(s.action()).toBeNull();
    await s.enable();
    expect(s.root.querySelector('#trip-option-example')?.textContent).toContain('27 октября 2026');
    expect(s.root.querySelector('#trip-option-example time')?.getAttribute('datetime')).toBe(discovery.asOfUtc);
    const apply = vi.spyOn(s.page, 'applyBrief'),
      save = vi.spyOn(s.page.state, 'save'),
      create = vi.spyOn(s.page.state, 'create').mockResolvedValue();
    await s.fill();
    const foundation = structuredClone(s.page.state.buffer());
    expect(foundation?.title).toBe('');
    expect(foundation?.brief.startDate).toBe('');
    expect(s.action()!.type).toBe('button');
    expect(s.action()!.tabIndex).toBe(0);
    s.action()!.focus();
    expect(document.activeElement).toBe(s.action());
    const req = await s.send();
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('X-Trips-Option-Save')).toBe('1');
    expect(req.request.body).toEqual({ brief });
    expect(req.request.cache).toBe('no-store');
    expect(req.request.headers.get('Authorization')).toBe('Bearer access');
    s.action()!.click();
    s.http.expectNone(url);
    req.flush(JSON.stringify(success));
    await s.render();
    const result = s.result()!;
    expect(s.root.querySelector('#trip-option-save')).toBeNull();
    expect(result.querySelector('[data-option-flight]')?.textContent).toContain('RT1');
    expect(result.querySelector('[data-option-hotel]')?.textContent).toContain('2 ночи');
    expect(result.querySelectorAll('time[datetime$="+03:00"]')).toHaveLength(6);
    expect(result.querySelector('[data-option-stop]')?.textContent).toContain('spb-general-staff');
    expect(result.textContent).toContain('Часы доступа неизвестны');
    expect(result.querySelector('[data-option-window]')?.textContent).toContain('14:20–17:40');
    expect(result.textContent).toMatch(/30\s000\s₽/);
    expect(result.textContent).toContain('сумма неизвестна');
    expect(result.textContent).toContain('Итоговая стоимость поездки неизвестна');
    expect(result.querySelector('a')?.getAttribute('href')).toBe(release.offer);
    expect(s.page.state.buffer()).toEqual(foundation);
    expect(s.page.briefDraft()).toEqual(brief);
    expect(apply).not.toHaveBeenCalled();
    expect(save).not.toHaveBeenCalled();
    expect(create).not.toHaveBeenCalled();
    expect(s.api.create).not.toHaveBeenCalled();
    expect(s.api.replace).not.toHaveBeenCalled();
    s.http.verify();
    s.page.title('Петербург');
    await s.render();
    expect(s.result()).toBeNull();
    s.root.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    await s.render();
    expect(apply).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledTimes(1);
  });
  it('preserves the manual form after disabled discovery; a 401 expires the matching owner session', async () => {
    const s = await setup();
    s.availability.flush('', { status: 404, statusText: 'Not Found' });
    await s.render();
    expect(s.action()).toBeNull();
    expect(s.root.querySelector('button[type=submit]')).not.toBeNull();
    await s.page.newPlan();
    await s.render();
    s.http.expectOne(url + '/availability').flush('', { status: 401, statusText: 'Unauthorized' });
    await s.render();
    expect(s.auth.deny).toHaveBeenCalledWith(0);
    expect(s.action()).toBeNull();
  });
  it('shows the known whole-party RUB total when the budget is 1 EUR, without a zero euro price or affordability claim', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('currency', 'EUR');
    s.page.patchBrief('totalBudget', '1');
    await s.render();
    const req = await s.send();
    expect(req.request.body.brief).toMatchObject({ currency: 'EUR', totalBudget: '1' });
    const price = {
      ...option.price,
      currency: 'EUR',
      knownSubtotal: '0.00',
      budgetStatus: 'indeterminate',
      components: [
        ...option.price.components.slice(0, 2),
        { kind: 'included', component: 'includedTax', amount: '1200.00', currency: 'RUB' },
      ],
    };
    req.flush(JSON.stringify({ ...success, option: { ...option, price } }));
    await s.render();
    const card = s.result()!;
    expect(card.querySelector('[data-option-subtotal] [data-currency=RUB]')?.textContent).toMatch(/30\s000\s₽/);
    expect(card.querySelector('[data-option-subtotal] [data-currency=EUR]')).toBeNull();
    expect(card.querySelector('[data-option-budget]')?.textContent).toMatch(/1\s€ без пересчёта валют нельзя/);
    expect(card.textContent).not.toMatch(/укладывается в бюджет|Полная цена перелёта и гостиницы неизвестна/);
    expect(card.textContent).toContain('Итоговая стоимость поездки неизвестна');
  });
  it('groups mandatory prices in their original currencies and excludes supplier-only amounts, included tax and unknown fees', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('totalBudget', '20000.00');
    await s.render();
    const price = {
      ...option.price,
      knownSubtotal: '18000.00',
      budgetStatus: 'indeterminate',
      components: [
        option.price.components[0],
        { kind: 'travelerMandatory', component: 'hotelBase', amount: '118.99', currency: 'EUR' },
        { kind: 'travelerMandatory', component: 'localFee', amount: '1.02', currency: 'EUR' },
        { kind: 'supplierNet', component: 'hotelBase', amount: '95.00', currency: 'USD' },
        { kind: 'included', component: 'includedTax', amount: '12.00', currency: 'EUR' },
        option.price.components[2],
      ],
    };
    (await s.send()).flush(JSON.stringify({ ...success, option: { ...option, price } }));
    await s.render();
    const card = s.result()!;
    const amounts = card.querySelector('[data-option-subtotal]')!;
    expect(amounts.querySelector('[data-currency=RUB]')?.textContent).toMatch(/18\s000\s₽/);
    expect(amounts.querySelector('[data-currency=EUR]')?.textContent).toMatch(/120,01\s€/);
    expect(amounts.querySelector('[data-currency=USD]')).toBeNull();
    expect(card.textContent).toContain('Полная цена перелёта и гостиницы неизвестна');
    expect(card.querySelector('[data-option-budget]')?.textContent).toContain('без пересчёта валют нельзя');
    expect(card.querySelector('[data-price-component=localFee]:last-child')?.textContent).toContain('сумма неизвестна');
  });
  it('distinguishes a genuine zero traveler price from the absence of comparable amounts', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('totalBudget', '100');
    await s.render();
    const net = { kind: 'supplierNet', component: 'hotelBase', amount: '5000.00', currency: 'RUB' };
    const price = {
      ...option.price,
      knownSubtotal: '0.00',
      budgetStatus: 'indeterminate',
      components: [net, option.price.components[2]],
    };
    (await s.send()).flush(JSON.stringify({ ...success, option: { ...option, price } }));
    await s.render();
    let card = s.result()!;
    expect(card.querySelector('[data-option-subtotal]')?.textContent).toContain('Нет известных обязательных сумм');
    expect(card.querySelector('[data-option-subtotal] [data-currency]')).toBeNull();
    expect(card.querySelector('[data-option-budget]')?.textContent).not.toMatch(/укладывается|пересчёта валют/);
    const zero = { kind: 'travelerMandatory', component: 'flightBase', amount: '0.00', currency: 'RUB' };
    (await s.send()).flush(
      JSON.stringify({
        ...success,
        option: { ...option, price: { ...price, components: [zero, ...price.components] } },
      }),
    );
    await s.render();
    card = s.result()!;
    expect(card.querySelector('[data-option-subtotal] [data-currency=RUB]')?.textContent).toMatch(/0\s₽/);
    expect(card.textContent).toContain('Полная цена перелёта и гостиницы неизвестна');
    expect(card.querySelector('[data-option-budget]')?.textContent).toContain('Для проверки бюджета нужны');
  });
  it('discloses the unknown same-currency fee at a budget equal to the known subtotal', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('totalBudget', '30000');
    await s.render();
    (await s.send()).flush(
      JSON.stringify({ ...success, option: { ...option, price: { ...option.price, budgetStatus: 'indeterminate' } } }),
    );
    await s.render();
    const card = s.result()!;
    expect(card.querySelector('[data-option-subtotal] [data-currency=RUB]')?.textContent).toMatch(/30\s000\s₽/);
    expect(card.querySelector('[data-price-component=localFee]')?.textContent).toContain('сумма неизвестна');
    expect(card.querySelector('[data-option-budget]')?.textContent).toContain('Для проверки бюджета нужны');
    expect(card.querySelector('[data-option-budget]')?.textContent).not.toMatch(/укладывается|пересчёта валют/);
  });
  it('shows a confirmed foreign zero without requiring a rate for comparing known expenses', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('totalBudget', '18000');
    await s.render();
    const price = {
      ...option.price,
      knownSubtotal: '18000.00',
      budgetStatus: 'indeterminate',
      components: [
        option.price.components[0],
        { kind: 'travelerMandatory', component: 'hotelBase', amount: '0.00', currency: 'EUR' },
      ],
    };
    (await s.send()).flush(JSON.stringify({ ...success, option: { ...option, price } }));
    await s.render();
    const card = s.result()!;
    const amounts = card.querySelector('[data-option-subtotal]')!;
    expect(amounts.querySelector('[data-currency=RUB]')?.textContent).toMatch(/18\s000\s₽/);
    expect(amounts.querySelector('[data-currency=EUR]')?.textContent).toMatch(/0\s€/);
    expect(card.querySelector('[data-option-budget]')?.textContent).not.toContain('без пересчёта валют нельзя');
    expect(card.querySelector('[data-option-budget]')?.textContent).toContain('Для проверки бюджета нужны');
    expect(card.textContent).not.toContain('Полная цена перелёта и гостиницы неизвестна');
  });
  it('asks for a departure city, decodes no-match reasons and rejects an answer with a different reference date', async () => {
    const s = await setup();
    await s.enable();
    s.action()!.click();
    await s.render();
    expect(s.root.textContent).toContain('Укажите город отправления');
    s.http.expectNone(url);
    await s.fill();
    (await s.send()).flush(
      JSON.stringify({ ...common, outcome: 'noMatch', option: null, reasons: ['hotelUnavailable'] }),
    );
    await s.render();
    expect(s.result()?.textContent).toContain('нет номера для всей группы');
    (await s.send()).flush(JSON.stringify({ ...success, asOfUtc: '2026-10-28T12:00:00Z' }));
    await s.render();
    expect(s.result()).toBeNull();
    expect(s.root.textContent).toContain('не соответствует данным примера');
  });
  it('explains every unsupported field in the reported order without displaying a trip option', async () => {
    const s = await setup();
    await s.enable();
    await s.fill();
    s.page.patchBrief('originId', 'kaliningrad');
    s.page.patchBrief('endDate', brief.startDate);
    s.page.patchBrief('adults', 3);
    await s.render();
    const req = await s.send();
    expect(req.request.body.brief).toMatchObject({ originId: 'kaliningrad', endDate: brief.startDate, adults: 3 });
    req.flush(
      JSON.stringify({
        ...common,
        outcome: 'unsupported',
        option: null,
        reasons: ['directionUnsupported', 'dateRangeUnsupported', 'groupUnsupported'],
      }),
    );
    await s.render();
    const card = s.result()!;
    expect(card.querySelector('h3')?.textContent?.trim()).toBe('Параметры вне исторического примера');
    expect(Array.from(card.querySelectorAll('li'), (row) => row.textContent.trim())).toEqual([
      'Пример доступен для поездки из Москвы в Санкт-Петербург и обратно.',
      'Для примера выберите два или три календарных дня с одной или двумя ночами.',
      'В одной комнате примера могут разместиться один или два взрослых.',
    ]);
    expect(
      card.querySelector('[data-option-flight], [data-option-hotel], [data-option-day], [data-option-subtotal]'),
    ).toBeNull();
  });
  it.each(['edit', 'owner', 'logout', 'saved plan'] as const)('discards the late response after %s', async (kind) => {
    const s = await setup();
    await s.enable();
    await s.fill();
    const req = await s.send();
    if (kind === 'edit') s.page.patchBrief('endDate', '2026-10-29');
    if (kind === 'owner') s.auth.status.set({ kind: 'authenticated', userId: 'owner-b' });
    if (kind === 'logout') void s.page.logout();
    if (kind === 'saved plan') s.params.next(convertToParamMap({ id: tripsExample.tripId }));
    // Flush before the UI cancellation effect runs, just as a late completion may race with rendering.
    if (!req.cancelled) req.flush(JSON.stringify(success));
    await s.render();
    expect(s.result()).toBeNull();
    expect(s.api.create).not.toHaveBeenCalled();
    if (kind !== 'edit') expect(s.action()).toBeNull();
    else {
      const response = await s.send();
      expect(response.request.body.brief.endDate).toBe('2026-10-29');
      response.flush(
        JSON.stringify({ ...common, outcome: 'unsupported', option: null, reasons: ['dateRangeUnsupported'] }),
      );
      await s.render();
      expect(s.result()?.textContent).toContain('с одной или двумя ночами');
    }
  });
});
type Scene = Awaited<ReturnType<typeof setup>>;
const button = (s: Scene, id: string) => s.root.querySelector<HTMLButtonElement>('#' + id)!;
async function offered() {
  const scene = await setup();
  await scene.enable();
  await scene.fill();
  (await scene.send()).flush(JSON.stringify({ preview: success, selection }));
  await scene.render();
  return scene;
}
async function lostResponse() {
  const s = await offered();
  button(s, 'trip-option-save').click();
  await s.render();
  const write = s.http.expectOne(createUrl);
  write.error(new ProgressEvent('error'));
  await s.render();
  expect(s.root.textContent).toContain('Результат сохранения неизвестен');
  return s;
}

describe('Saving one owner-private displayed option', () => {
  it('does not acquire an access token or start saving on the server', async () => {
    const auth = {
      status: signal({ kind: 'authenticated', userId: 'owner-a' }),
      identityEpoch: signal(1),
      accessToken: vi.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: PLATFORM_ID, useValue: 'server' },
        { provide: TripsAuthService, useValue: auth },
      ],
    });
    const service = TestBed.inject(TripsOptionSaveState);
    const card = decodeOptionEnvelope(JSON.stringify(success), brief, discovery).preview;
    expect(await service.begin(selection, '', brief, card)).toBeNull();
    expect(service.phase()).toBeNull();
    expect(auth.accessToken).not.toHaveBeenCalled();
  });
  it.each(['Петербург вечером', ''])(
    'sends one explicit save with the form title "%s" and opens the issued ID',
    async (title) => {
      const s = await offered();
      s.page.title(title);
      await s.render();
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      const save = button(s, 'trip-option-save');
      expect(save.type).toBe('button');
      expect(save.tabIndex).toBe(0);
      save.focus();
      expect(document.activeElement).toBe(save);
      save.click();
      save.click();
      await s.render();
      const write = s.http.expectOne(createUrl);
      expect(write.request.method).toBe('POST');
      expect(write.request.body).toEqual({ selectionToken: selection.selectionToken, ...(title ? { title } : {}) });
      expect(write.request.headers.get('Authorization')).toBe('Bearer access');
      expect(write.request.transferCache).toBe(false);
      expect(write.request.cache).toBe('no-store');
      expect(s.api.create).not.toHaveBeenCalled();
      expect(s.root.innerHTML).not.toContain(selection.selectionToken);
      write.flush(JSON.stringify({ tripId: selection.tripId }), { status: 201, statusText: 'Created' });
      await s.render();
      expect(navigate).toHaveBeenCalledExactlyOnceWith(['/trips', selection.tripId]);
      s.http.verify();
    },
  );

  it('checks only the issued ID and opens the matching historical option after a lost response', async () => {
    const storageWrite = vi.spyOn(Storage.prototype, 'setItem');
    const s = await lostResponse();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    button(s, 'trip-option-check').click();
    await s.render();
    const check = s.http.expectOne(historyUrl);
    expect(check.request.method).toBe('GET');
    expect(check.request.body).toBeNull();
    expect(check.request.transferCache).toBe(false);
    check.flush(JSON.stringify(history));
    await s.render();
    expect(navigate).toHaveBeenCalledExactlyOnceWith(['/trips', selection.tripId]);
    expect(storageWrite.mock.calls.flat().join(' ')).not.toContain(selection.selectionToken);
    expect(location.href).not.toContain(selection.selectionToken);
    s.http.expectNone(createUrl);
    s.http.verify();
  });

  it('retains the same operation through a pre-commit 404, a list refresh, a null result and a disabled write', async () => {
    const s = await lostResponse();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    for (const outcome of ['404', 'empty', 'different']) {
      button(s, 'trip-option-check').click();
      await s.render();
      const check = s.http.expectOne(historyUrl);
      if (outcome === '404') check.flush('', { status: 404, statusText: 'Not Found' });
      else
        check.flush(
          JSON.stringify(
            outcome === 'empty'
              ? { savedOption: null }
              : { savedOption: { ...history.savedOption, preview: { ...success, catalogueVersion: 'B' } } },
          ),
        );
      await s.render();
      await s.page.state.loadList();
      expect(s.page.state.optionSave.phase()).toBe('uncertain');
      expect(s.root.querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(true);
      await s.page.state.create();
      s.page.optionPreview()?.select();
      s.http.expectNone(url);
      s.http.expectNone(createUrl);
      expect(navigate).not.toHaveBeenCalled();
    }
    button(s, 'trip-option-retry').click();
    await s.render();
    const disabled = s.http.expectOne(createUrl);
    expect(disabled.request.body).toEqual({ selectionToken: selection.selectionToken });
    disabled.flush('', { status: 404, statusText: 'Not Found' });
    await s.render();
    button(s, 'trip-option-retry').click();
    await s.render();
    const retry = s.http.expectOne(createUrl);
    expect(retry.request.body).toEqual(disabled.request.body);
    retry.flush(JSON.stringify({ tripId: selection.tripId }));
    await s.render();
    expect(navigate).toHaveBeenCalledExactlyOnceWith(['/trips', selection.tripId]);
    expect(s.api.create).not.toHaveBeenCalled();
    s.http.verify();
  });

  it('requires a separate abandonment before a new preview and clears the old action', async () => {
    const s = await lostResponse();
    expect(s.action()?.disabled).toBe(true);
    button(s, 'trip-option-check').click();
    await s.render();
    const check = s.http.expectOne(historyUrl);
    button(s, 'trip-option-abandon').click();
    await s.render();
    expect(check.cancelled).toBe(true);
    expect(s.root.querySelector('#trip-option-retry, #trip-option-save')).toBeNull();
    expect(s.action()?.disabled).toBe(false);
    expect(s.root.querySelector<HTMLButtonElement>('button[type=submit]')?.disabled).toBe(false);
    const response = await s.send();
    response.flush(JSON.stringify({ preview: success, selection: null }));
    await s.render();
    expect(s.root.querySelector('#trip-option-save')).toBeNull();
  });

  it.each(['logout', 'owner', 'pagehide'])('ignores a late write after %s', async (event) => {
    const s = await offered();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    button(s, 'trip-option-save').click();
    await s.render();
    const write = s.http.expectOne(createUrl);
    if (event === 'owner') s.auth.status.set({ kind: 'authenticated', userId: 'owner-b' });
    else if (event === 'logout') await s.page.logout();
    else window.dispatchEvent(new Event('pagehide'));
    if (!write.cancelled) write.flush(JSON.stringify({ tripId: selection.tripId }));
    await s.render();
    expect(navigate).not.toHaveBeenCalled();
    expect(s.page.state.optionSave.phase()).toBeNull();
    expect(s.root.innerHTML).not.toContain(selection.selectionToken);
  });

  it('reads historical values with an independent hash, with no live preview or discovery on the saved route', async () => {
    const s = await setup();
    s.availability.flush('', { status: 404, statusText: 'Not Found' });
    s.params.next(convertToParamMap({ id: selection.tripId }));
    await s.render();
    s.http.expectOne(historyUrl).flush(
      JSON.stringify({
        savedOption: {
          ...history.savedOption,
          preview: { ...success, datasetHash: 'f'.repeat(64) },
        },
      }),
    );
    await s.render();
    const card = s.root.querySelector('#trip-saved-option')!;
    expect(card.querySelector('[data-option-flight]')?.textContent).toContain('Запланировано');
    expect(card.querySelector('[data-option-hotel]')?.textContent).toContain('Запланировано');
    expect(card.querySelector('time[data-saved-at]')?.getAttribute('datetime')).toBe(history.savedOption.savedAtUtc);
    expect(card.querySelectorAll('[data-option-day]')).toHaveLength(3);
    expect(card.querySelector('[data-option-subtotal]')?.textContent).toMatch(/30\s000\s₽/);
    expect(s.root.querySelector('.estimates')?.textContent).not.toMatch(/30\s000/);
    expect(card.textContent).toContain('исходные дни');
    s.http.expectNone(url);
    s.http.expectNone(url + '/availability');
  });

  it('shows unavailability for an unreadable snapshot and no historical layer for an explicit NULL', async () => {
    const s = await setup();
    s.availability.flush('', { status: 404, statusText: 'Not Found' });
    s.params.next(convertToParamMap({ id: selection.tripId }));
    await s.render();
    const contradictoryPrice = { ...option.price, flightAndStayComplete: true };
    for (const savedOption of [
      { ...history.savedOption, version: 2 },
      { ...history.savedOption, preview: { ...success, option: { ...option, price: contradictoryPrice } } },
    ]) {
      s.http.expectOne(historyUrl).flush(JSON.stringify({ savedOption }));
      await s.render();
      expect(s.root.textContent).toContain('Сохранённый вариант сейчас недоступен');
      expect(s.root.querySelector('#trip-saved-option')).toBeNull();
      s.root.querySelector<HTMLButtonElement>('app-trips-saved-option button')!.click();
      await s.render();
    }
    s.http.expectOne(historyUrl).flush(JSON.stringify({ savedOption: null }));
    await s.render();
    expect(s.root.textContent).not.toContain('Сохранённый вариант сейчас недоступен');
    expect(s.root.querySelector('#trip-saved-option')).toBeNull();
    expect(s.page.state.baseline()?.tripId).toBe(selection.tripId);
  });

  it('reopens a one-night RT3/H3 snapshot with net hotel price and a EUR budget without conversion', async () => {
    const s = await setup();
    s.availability.flush('', { status: 404, statusText: 'Not Found' });
    s.params.next(convertToParamMap({ id: selection.tripId }));
    await s.render();
    const endDate = '2026-10-29';
    const savedBrief = { ...brief, endDate, totalBudget: '1.00', currency: 'EUR' };
    const short = {
      ...option,
      flight: {
        ...option.flight,
        id: 'RT3',
        inbound: {
          ...option.flight.inbound,
          departsAt: endDate + 'T11:00:00+03:00',
          arrivesAt: endDate + 'T12:30:00+03:00',
        },
      },
      hotel: { ...option.hotel, id: 'H3', nights: 1, checkOutAt: endDate + 'T08:00:00+03:00' },
      days: [option.days[0], { ...option.days[2], date: endDate }],
      price: {
        ...option.price,
        currency: 'EUR',
        knownSubtotal: '0.00',
        budgetStatus: 'indeterminate',
        components: [
          option.price.components[0],
          { kind: 'supplierNet', component: 'hotelBase', amount: '5000.00', currency: 'RUB' },
        ],
      },
    };
    s.http.expectOne(historyUrl).flush(
      JSON.stringify({
        savedOption: { ...history.savedOption, brief: savedBrief, preview: { ...success, option: short } },
      }),
    );
    await s.render();
    const card = s.root.querySelector('#trip-saved-option')!;
    expect(card.querySelector('[data-option-hotel]')?.textContent).toMatch(/H3.*1 ночь/s);
    expect(card.querySelector('[data-price-component=hotelBase]')?.textContent).toContain('цена поставщика');
    expect(card.querySelector('[data-option-subtotal]')?.textContent).toMatch(/18\s000\s₽/);
    expect(card.querySelector('[data-option-budget]')?.textContent).toMatch(/1\s€ без пересчёта валют нельзя/);
  });

  it('bounds legacy and negotiated payloads, tokens and historical structures and restricts attribution links', () => {
    const raw = JSON.stringify(success);
    const padBytes = (text: string, size: number) => text + ' '.repeat(size - new TextEncoder().encode(text).length);
    expect(decodeOptionEnvelope(padBytes(raw, 32768), brief, discovery).selection).toBeNull();
    expect(() => decodeOptionEnvelope(padBytes(raw, 32769), brief, discovery)).toThrow();
    const envelope = (n: number) =>
      JSON.stringify({ preview: success, selection: { ...selection, selectionToken: 'x'.repeat(n) } });
    expect(decodeOptionEnvelope(padBytes(envelope(65536), 131072), brief, discovery).selection).not.toBeNull();
    expect(() => decodeOptionEnvelope(padBytes(envelope(65536), 131073), brief, discovery)).toThrow();
    expect(() => decodeOptionEnvelope(envelope(65537), brief, discovery)).toThrow();
    expect(() =>
      decodeOptionEnvelope(
        JSON.stringify({ preview: { ...common, outcome: 'noMatch', reasons: ['noWalks'], option: null }, selection }),
        brief,
        discovery,
      ),
    ).toThrow();
    for (const preview of [
      { ...success, dataUrl: 'https://foreign.example/option' },
      { ...success, option: { ...option, price: { ...option.price, knownSubtotal: '0.00' } } },
    ]) {
      expect(() => decodeSavedTripOption({ savedOption: { ...history.savedOption, preview } })).toThrow();
    }
  });
});
