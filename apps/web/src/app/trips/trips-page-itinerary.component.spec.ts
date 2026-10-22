import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { decodeTripPlan, type TripPlan, TripsApiService } from '@travel/api-client';
import { tripsExample } from '@travel/api-client/testing/trips';
import { candidate, empty, partial, ready, unsupported } from '@travel/trips-itinerary-fixtures';
import { BehaviorSubject, of, Subject, throwError } from 'rxjs';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TRIPS_ITINERARY_CLOCK } from './trips-itinerary-state.service';
import { TripsPageComponent } from './trips-page.component';

const availabilityUrl = '/api/trips/itinerary-availability';
const bytes = (value: unknown) => {
  const data = new TextEncoder().encode(JSON.stringify(value)),
    result = new ArrayBuffer(data.length);
  new Uint8Array(result).set(data);
  return result;
};
const settle = async () => {
  for (let i = 0; i < 16; i++) await Promise.resolve();
};
function required<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('Required fixture absent');
  return value;
}
function foundation(interests = ['architecture']) {
  return decodeTripPlan({
    ...structuredClone(tripsExample),
    revision: '1',
    title: 'Личный план',
    brief: {
      ...tripsExample.brief,
      destinationId: 'moscow',
      originId: 'saint-petersburg',
      startDate: '2026-10-22',
      endDate: '2026-10-22',
      interests,
    },
    destination: { ...tripsExample.destination, destinationId: 'moscow', displayName: 'Москва' },
    days: [{ date: '2026-10-22', blocks: [] }],
    knownUserEstimateSubtotal: '0.00',
  });
}
async function setup(p = foundation(), initial: string | null = p.tripId) {
  let now = Date.parse('2026-10-21T00:01:00Z');
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner' }),
    identityEpoch: signal(0),
    accessToken: vi.fn().mockResolvedValue('token'),
    hasOidcCallback: () => false,
    initializeFromCallback: vi.fn(),
    beginLogin: vi.fn(),
    deny: vi.fn(),
    logout: vi.fn(async () => {
      auth.identityEpoch.update((x) => x + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
  };
  const api = {
    detail: vi.fn().mockImplementation((tripId: string) => of({ ...p, tripId })),
    list: vi.fn().mockReturnValue(of({ items: [], nextCursor: null })),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn(),
  };
  const params = new BehaviorSubject(convertToParamMap(initial ? { id: initial } : {}));
  const route = { paramMap: params, snapshot: { paramMap: params.value } };
  TestBed.configureTestingModule({
    imports: [TripsPageComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: route },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsApiService, useValue: api },
      { provide: TRIPS_ITINERARY_CLOCK, useValue: () => now },
      {
        provide: TripsAvailabilityService,
        useValue: {
          status: signal('ready'),
          enabled: () => true,
          catalogue: signal({ catalogueVersion: '1', items: [p.destination] }),
          initialize: vi.fn().mockResolvedValue(undefined),
          clearPrivate: vi.fn(),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(TripsPageComponent),
    page = fixture.componentInstance,
    http = TestBed.inject(HttpTestingController);
  await settle();
  fixture.detectChanges();
  return {
    fixture,
    page,
    http,
    api,
    auth,
    p,
    root: fixture.nativeElement as HTMLElement,
    navigate: (id: string) => {
      route.snapshot.paramMap = convertToParamMap({ id });
      params.next(route.snapshot.paramMap);
    },
    setNow: (v: string) => {
      now = Date.parse(v);
    },
  };
}
type Setup = Awaited<ReturnType<typeof setup>>;
function button(s: Setup, label: string): HTMLButtonElement {
  return required([...s.root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label));
}
async function activate(s: Setup) {
  s.http.expectOne(availabilityUrl).flush(bytes(ready));
  await s.fixture.whenStable();
}
async function generate(s: Setup, value: unknown = candidate) {
  button(s, 'Предложить прогулочный маршрут').click();
  await settle();
  const req = s.http.expectOne(`/api/trips/${s.page.state.selectedId()}/itinerary-candidate`);
  expect(req.request.headers.get('If-Match')).toBe('"1"');
  req.flush(bytes(value));
  await s.fixture.whenStable();
}
async function stage(s: Setup) {
  button(s, 'Перенести в редактор').click();
  await settle();
  s.http.expectOne(availabilityUrl).flush(bytes(ready));
  await s.fixture.whenStable();
}
describe('Trips page actual itinerary state/client integration', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.useRealTimers();
  });
  it('activates M2 independently and previews actual bytes before explicit staging, editing and Save', async () => {
    const s = await setup();
    await activate(s);
    expect(button(s, 'Предложить прогулочный маршрут').disabled).toBe(false);
    const before = structuredClone(s.page.state.buffer());
    await generate(s);
    expect(s.root.querySelector('#itinerary-preview')).not.toBeNull();
    expect(s.root.textContent).toContain('Оценка по карте:');
    expect(s.root.textContent).toContain('Без полевой проверки');
    expect(s.root.textContent).toContain('Выполнимость не проверена');
    expect(s.root.textContent).toContain('Часы доступа неизвестны');
    expect(s.root.textContent).toContain('75/45');
    expect(s.root.textContent).toContain(candidate.modelSource.assumptions);
    expect(s.root.textContent).toContain('CC0');
    expect(s.root.textContent).toContain('ODbL');
    expect(document.activeElement?.id).toBe('itinerary-preview-title');
    expect(s.page.state.buffer()).toEqual(before);
    expect(s.api.replace).not.toHaveBeenCalled();
    const links = [...s.root.querySelectorAll<HTMLAnchorElement>('#itinerary-preview a')];
    expect(links.some((a) => a.getAttribute('href') === '/trips-data/moscow-outdoor/1/moscow-v1.manifest.json')).toBe(
      true,
    );
    expect(links.some((a) => a.href === 'https://creativecommons.org/publicdomain/zero/1.0/')).toBe(true);
    for (const a of links.filter((a) => a.href.startsWith('https:'))) expect(a.rel).toBe('noopener noreferrer');
    await stage(s);
    expect(s.api.detail).toHaveBeenCalledTimes(2);
    expect(s.api.replace).not.toHaveBeenCalled();
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(s.root.querySelector('#itinerary-data-credit')?.textContent).toContain('OpenStreetMap contributors');
    const staged = required(s.page.state.buffer());
    expect(staged.title).toBe(before?.title);
    expect(staged.brief).toEqual(before?.brief);
    const block = staged.days[0].blocks[0];
    expect(block.blockId).toBe(candidate.days[0].blocks[0].proposedBlockId);
    expect(block.startTime).toBeNull();
    expect(block.endTime).toBeNull();
    expect(block.estimate).toBeNull();
    expect(Array.from(required(block.note)).length).toBeLessThanOrEqual(500);
    expect(document.activeElement?.id).toBe(`block-${block.blockId}`);
    s.page.block(block, 'title', 'Моя прогулка');
    s.api.replace.mockReturnValue(of({ ...s.p, ...staged, revision: '2', days: required(s.page.state.buffer()).days }));
    button(s, 'Сохранить').click();
    await s.fixture.whenStable();
    expect(s.api.replace.mock.calls[0][1]).toBe('1');
    expect(s.api.replace.mock.calls[0][2].days[0].blocks[0].title).toBe('Моя прогулка');
    expect(s.api.replace.mock.calls[0][2].days[0].blocks[0]).not.toHaveProperty('facts');
    s.api.detail.mockReturnValue(of(required(s.page.state.baseline())));
    await s.page.state.refreshDetail();
    await s.fixture.whenStable();
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(s.root.querySelector<HTMLInputElement>(`#block-${block.blockId}`)?.value).toBe('Моя прогулка');
    expect(s.root.querySelector('#itinerary-data-credit')?.textContent).toContain('ODbL');
  });
  it('renders partial interests, exclusions, original source titles and hostile-looking text literally', async () => {
    const s = await setup(foundation(['architecture', 'food']));
    await activate(s);
    const value = structuredClone(partial);
    value.days[0].blocks[0].title = '<img src=x onerror=alert(1)>';
    value.evidence[0].rightsEvidence = '<script>alert(1)</script>';
    await generate(s, value);
    expect(s.root.textContent).toContain('Заполнено частично');
    expect(s.root.textContent).toContain('Не покрыты: Еда');
    expect(s.root.textContent).toContain('Полная стоимость неизвестна');
    expect(s.root.textContent).toContain('Переезды между днями не спланированы');
    expect(s.root.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(s.root.textContent).toContain('<script>alert(1)</script>');
    expect(s.root.querySelector('img,script')).toBeNull();
    expect(button(s, 'Перенести в редактор').disabled).toBe(false);
  });
  it.each(['empty', 'unsupported'] as const)('renders %s without adopt or fabricated route facts', async (outcome) => {
    const p = foundation();
    if (outcome === 'unsupported') {
      p.brief.destinationId = 'saint-petersburg';
      p.destination.destinationId = 'saint-petersburg';
      p.destination.displayName = 'Санкт-Петербург';
    }
    const s = await setup(p);
    await activate(s);
    const value = outcome === 'empty' ? empty : unsupported;
    await generate(s, value);
    expect(s.root.textContent).toContain(outcome === 'empty' ? 'Подходящих остановок нет' : 'Основа не поддерживается');
    expect(s.root.querySelector('#adopt-itinerary')).toBeNull();
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('keeps generation off for nonempty and form-only dirty foundations including dirty then revert', async () => {
    const s = await setup();
    await activate(s);
    s.page.patchBrief('adults', 3);
    await s.fixture.whenStable();
    expect(button(s, 'Предложить прогулочный маршрут').disabled).toBe(true);
    s.page.patchBrief('adults', 2);
    await s.fixture.whenStable();
    expect(button(s, 'Предложить прогулочный маршрут').disabled).toBe(false);
    await generate(s);
    s.page.patchBrief('adults', 3);
    s.page.patchBrief('adults', 2);
    await s.fixture.whenStable();
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    s.page.state.addBlock('2026-10-22');
    await s.fixture.whenStable();
    expect(button(s, 'Предложить прогулочный маршрут').disabled).toBe(true);
    expect(s.root.textContent).toContain('Первый маршрут доступен для пустой основы');
  });
  it('does not dispatch generation for an unsaved foundation', async () => {
    const s = await setup(foundation(), null);
    await activate(s);
    expect(button(s, 'Предложить прогулочный маршрут').disabled).toBe(true);
    expect(s.root.textContent).toContain('Сначала сохраните пустую основу');
    expect(s.api.create).not.toHaveBeenCalled();
  });
  it('keeps manual edits and Save usable during generation and drops a late candidate without focus theft', async () => {
    const s = await setup();
    await activate(s);
    button(s, 'Предложить прогулочный маршрут').click();
    await settle();
    const req = s.http.expectOne(`/api/trips/${s.p.tripId}/itinerary-candidate`);
    s.fixture.detectChanges();
    expect(button(s, 'Сохранить').disabled).toBe(false);
    const title = required(s.root.querySelector<HTMLInputElement>('#trip-title'));
    title.focus();
    s.page.title('Новый текст');
    req.flush(bytes(candidate));
    await s.fixture.whenStable();
    expect(s.page.state.buffer()?.title).toBe('Новый текст');
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(document.activeElement).toBe(title);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it.each(['package', 'policy', 'flag', 'read', 'edit', 'logout', 'plan', 'expiry'] as const)(
    'blocks staging when %s changes between the two actual adoption reads',
    async (action) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const owner = new Subject<TripPlan>();
      s.api.detail.mockReturnValueOnce(owner);
      button(s, 'Перенести в редактор').click();
      await settle();
      const req = s.http.expectOne(availabilityUrl);
      s.fixture.detectChanges();
      expect(button(s, 'Перенести в редактор').disabled).toBe(true);
      expect(s.root.textContent).toContain('Проверяем основу и пакет');
      expect(button(s, 'Сохранить').disabled).toBe(false);
      button(s, 'Перенести в редактор').click();
      expect(s.api.detail).toHaveBeenCalledTimes(2);
      if (action === 'package') req.flush(bytes({ ...ready, datasetHash: 'c'.repeat(64) }));
      else if (action === 'policy') req.flush(bytes({ ...ready, policyVersion: 'unknown' }));
      else if (action === 'flag')
        req.flush(bytes({ type: 'about:blank', title: 'disabled', status: 404, code: 'Trips.NotFound' }), {
          status: 404,
          statusText: 'Not Found',
        });
      else if (action === 'read') req.error(new ProgressEvent('error'));
      else req.flush(bytes(ready));
      await settle();
      if (action === 'edit') s.page.patchBrief('adults', 3);
      if (action === 'logout') await s.page.logout();
      if (action === 'plan') {
        s.navigate('33333333-3333-4333-8333-333333333333');
        await settle();
        s.http.expectOne(availabilityUrl).flush(bytes(ready));
      }
      if (action === 'expiry') s.setNow('2026-10-21T00:16:00Z');
      owner.next(s.p);
      owner.complete();
      await s.fixture.whenStable();
      expect(s.page.state.buffer()?.days.some((d) => d.blocks.length) ?? false).toBe(false);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it('rejects unsafe source URLs through the actual client decoder and focuses its status', async () => {
    const s = await setup();
    await activate(s);
    const value = structuredClone(candidate);
    value.evidence[0].sourceUrl = 'javascript:alert(1)';
    await generate(s, value);
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(s.root.textContent).toContain('Предложение сейчас недоступно');
    expect(document.activeElement?.id).toBe('itinerary-status');
  });
  it.each([429, 504, 503])('announces explicit %s errors without automatic retry', async (status) => {
    const s = await setup();
    await activate(s);
    button(s, 'Предложить прогулочный маршрут').click();
    await settle();
    s.http.expectOne(`/api/trips/${s.p.tripId}/itinerary-candidate`).flush(
      bytes({
        type: 'about:blank',
        title: 'not private',
        status,
        code:
          status === 429
            ? 'Trips.ItineraryBusy'
            : status === 504
              ? 'Trips.ItineraryDeadline'
              : 'Trips.ItineraryUnavailable',
      }),
      { status, statusText: 'Failure' },
    );
    await s.fixture.whenStable();
    expect(s.root.textContent).toContain(
      status === 429 ? 'Расчёт занят' : status === 504 ? 'Время расчёта истекло' : 'Предложение сейчас недоступно',
    );
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('preserves ordinary M1 412 and lost PUT barriers after staging', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    await stage(s);
    const block = required(s.page.state.buffer()).days[0].blocks[0];
    s.api.replace.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 412 })));
    await s.page.submit();
    await s.fixture.whenStable();
    expect(s.page.state.status()).toBe('conflict');
    expect(s.page.state.buffer()?.days[0].blocks[0]).toEqual(block);
    await s.page.state.refreshDetail();
    s.page.state.keepLocalAgainstCurrent();
    s.api.replace.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    await s.page.submit();
    await s.fixture.whenStable();
    expect(s.page.state.status()).toBe('uncertain');
    expect(s.page.state.buffer()?.days[0].blocks[0]).toEqual(block);
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(s.api.replace).toHaveBeenCalledTimes(2);
  });
  it('retains later local brief changes when a staged plan Save finishes', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    await stage(s);
    const pending = new Subject<TripPlan>(),
      staged = required(s.page.state.buffer());
    s.api.replace.mockReturnValueOnce(pending);
    const save = s.page.submit();
    await settle();
    s.page.patchBrief('adults', 3);
    pending.next({ ...s.p, ...staged, revision: '2' });
    pending.complete();
    await save;
    await s.fixture.whenStable();
    expect(s.page.briefDraft()?.adults).toBe(3);
    expect(s.page.briefDirty()).toBe(true);
  });
  it.each(['owner', 'availability'] as const)(
    'shows timeout of the actual %s read without staging or retry',
    async (which) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const ownerClient = TestBed.runInInjectionContext(() => new TripsApiService());
      if (which === 'owner')
        s.api.detail.mockImplementation((tripId: string, token: string) => ownerClient.detail(tripId, token));
      else s.api.detail.mockReturnValueOnce(new Subject<TripPlan>());
      vi.useFakeTimers();
      button(s, 'Перенести в редактор').click();
      await settle();
      const availability = s.http.expectOne(availabilityUrl);
      const owner = which === 'owner' ? s.http.expectOne(`/api/trips/${s.p.tripId}`) : null;
      if (owner) availability.flush(bytes(ready));
      await vi.advanceTimersByTimeAsync(15001);
      vi.useRealTimers();
      await s.fixture.whenStable();
      expect((owner ?? availability).cancelled).toBe(true);
      expect(s.root.textContent).toContain('Предложение сейчас недоступно');
      expect(s.page.state.buffer()?.days[0].blocks).toEqual([]);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it.each(['revision', 'owner-failure', 'release-expiry'] as const)(
    'refuses adoption after %s while preserving title and brief',
    async (action) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      if (action === 'revision') s.api.detail.mockReturnValueOnce(of({ ...s.p, revision: '2' }));
      if (action === 'owner-failure')
        s.api.detail.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
      button(s, 'Перенести в редактор').click();
      await settle();
      const requests = s.http.match(availabilityUrl);
      for (const request of requests)
        if (!request.cancelled)
          request.flush(
            bytes(action === 'release-expiry' ? { ...ready, releaseValidUntil: '2026-11-09T00:00:00Z' } : ready),
          );
      await s.fixture.whenStable();
      expect(s.page.state.buffer()?.days[0].blocks).toEqual([]);
      expect(s.page.state.buffer()?.title).toBe(s.p.title);
      expect(s.page.briefDraft()).toEqual(s.p.brief);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it.each([
    [404, 'Trips.NotFound', 'выключены'],
    [401, 'Trips.Unauthenticated', 'подтвердить вход'],
    [403, 'Trips.Forbidden', 'Нет доступа'],
    [503, 'Trips.ItineraryUnavailable', 'сейчас недоступно'],
  ] as const)('keeps manual M1 usable after independent M2 availability %s', async (status, code, text) => {
    const s = await setup();
    expect(s.root.textContent).toContain('Проверяем доступность');
    s.http
      .expectOne(availabilityUrl)
      .flush(bytes({ type: 'about:blank', title: 'failure', status, code }), { status, statusText: 'Failure' });
    await s.fixture.whenStable();
    expect(s.root.textContent).toContain(text);
    expect(s.root.querySelector('#generate-itinerary')).toBeNull();
    expect(button(s, 'Сохранить').disabled).toBe(false);
    expect(s.root.querySelector<HTMLInputElement>('#trip-title')?.disabled).toBe(false);
  });
  it('focuses an explicit availability recheck error without hiding or clearing manual text', async () => {
    const s = await setup();
    await activate(s);
    s.page.title('Свой текст');
    button(s, 'Проверить доступность предложений').click();
    await settle();
    s.http.expectOne(availabilityUrl).error(new ProgressEvent('error'));
    await s.fixture.whenStable();
    expect(document.activeElement?.id).toBe('itinerary-status');
    expect(s.page.state.buffer()?.title).toBe('Свой текст');
  });
  it.each(['both', 'owner', 'availability'] as const)(
    'keeps explicit dismissal focus when the mandatory %s adoption read is pending',
    async (pending) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const before = structuredClone(s.page.state.buffer());
      const ownerClient = TestBed.runInInjectionContext(() => new TripsApiService());
      s.api.detail.mockImplementation((tripId: string, token: string) => ownerClient.detail(tripId, token));
      button(s, 'Перенести в редактор').click();
      await settle();
      const owner = s.http.expectOne(`/api/trips/${s.p.tripId}`);
      const availability = s.http.expectOne(availabilityUrl);
      if (pending === 'owner') availability.flush(bytes(ready));
      if (pending === 'availability') owner.flush(JSON.stringify(s.p), { headers: { ETag: '"1"' } });
      await s.fixture.whenStable();
      expect(button(s, 'Перенести в редактор').disabled).toBe(true);
      const close = button(s, 'Закрыть предложение');
      expect(close.disabled).toBe(false);
      close.focus();
      close.click();
      await s.fixture.whenStable();
      expect(s.root.querySelector('#itinerary-preview')).toBeNull();
      expect(s.page.itinerary.pending()).toBeNull();
      expect(s.page.state.buffer()).toEqual(before);
      expect(s.api.replace).not.toHaveBeenCalled();
      if (pending !== 'availability') expect(owner.cancelled).toBe(true);
      if (pending !== 'owner') expect(availability.cancelled).toBe(true);
      expect(document.activeElement?.id).toBe('generate-itinerary');
    },
  );
  it('keeps a newer dismissal focus after direct cancellation of pending generation', async () => {
    const s = await setup();
    await activate(s);
    const generation = s.page.generateItinerary();
    await settle();
    const request = s.http.expectOne(`/api/trips/${s.p.tripId}/itinerary-candidate`);
    s.page.dismissItinerary();
    await generation;
    await s.fixture.whenStable();
    expect(request.cancelled).toBe(true);
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
    expect(s.page.state.buffer()?.days[0].blocks).toEqual([]);
    expect(s.api.replace).not.toHaveBeenCalled();
    expect(document.activeElement?.id).toBe('generate-itinerary');
  });
  it('uses native keyboard controls and restores focus when explicitly dismissing a preview', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    expect(s.root.querySelector('#itinerary-status')?.getAttribute('aria-live')).toBe('polite');
    expect(s.root.querySelector('#itinerary-preview summary')).not.toBeNull();
    const close = button(s, 'Закрыть предложение');
    close.focus();
    expect(close.type).toBe('button');
    close.click();
    await s.fixture.whenStable();
    expect(document.activeElement?.id).toBe('generate-itinerary');
    expect(s.root.querySelector('#itinerary-preview')).toBeNull();
  });
  it('does not reactivate a late A route over B preview or add a duplicate availability read', async () => {
    const s = await setup();
    await activate(s);
    const a = new Subject<TripPlan>();
    s.api.detail.mockReturnValueOnce(a);
    s.navigate('33333333-3333-4333-8333-333333333333');
    await settle();
    s.navigate(s.p.tripId);
    await settle();
    s.http.expectOne(availabilityUrl).flush(bytes(ready));
    await s.fixture.whenStable();
    await generate(s);
    const before = s.root.querySelector('#itinerary-preview')?.textContent;
    a.next({ ...s.p, tripId: '33333333-3333-4333-8333-333333333333' });
    a.complete();
    await s.fixture.whenStable();
    s.http.expectNone(availabilityUrl);
    expect(s.root.querySelector('#itinerary-preview')?.textContent).toBe(before);
  });
});

it('keeps one visible demo warning and places reproducibility metadata inside existing data details', async () => {
  const s = await setup();
  await activate(s);
  await generate(s);
  const preview = required(s.root.querySelector<HTMLElement>('#itinerary-preview'));
  const summary = required(preview.querySelector('.preview-summary'));
  expect(summary.textContent).toContain('Демонстрационная оценка по OSM');
  expect(summary.textContent).toContain('Калибровка не проводилась');
  expect(summary.textContent).toContain('качество маршрута людьми не оценивалось');
  const visiblePreview = preview.cloneNode(true) as HTMLElement;
  for (const details of visiblePreview.querySelectorAll('details')) details.remove();
  const visibleCopy = visiblePreview.textContent;
  expect(visibleCopy).not.toContain(candidate.preparation.preparedBy);
  expect(visibleCopy).not.toContain(candidate.preparation.buildAsOfUtc);
  expect(preview.querySelector('.limitations')?.textContent).not.toMatch(
    /Калибровка|Качество маршрута|Пешие переходы рассчитаны/,
  );
  const details = required(preview.querySelector('.preview-sources details'));
  expect(details.textContent).toContain(candidate.preparation.preparedBy);
  expect(details.textContent).toContain(candidate.preparation.buildAsOfUtc);
  expect(details.textContent).toContain('75/45 м/мин');
  expect(details.textContent).toContain(candidate.modelSource.assumptions);
  expect(details.hasAttribute('open')).toBe(false);
  s.http.verify();
});
