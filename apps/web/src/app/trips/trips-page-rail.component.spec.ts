import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import type { RailSearchResult, RailSuggestions } from '@travel/api-client';
import {
  decodeTripPlan,
  TripsApiService,
  TripsItineraryApiService,
  TripsItineraryRequestError,
} from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';
import walkingCandidate from '../../../../../tests/fixtures/trips-itinerary-v1/candidate.json';
import walkingReady from '../../../../../tests/fixtures/trips-itinerary-v1/ready.json';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { RailSearchComponent } from '../rail/rail-search.component';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TRIPS_ITINERARY_CLOCK } from './trips-itinerary-state.service';
import { TripsPageComponent } from './trips-page.component';

async function setup(p = decodeTripPlan(structuredClone(tripsExample)), walking = false) {
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner' }),
    identityEpoch: signal(0),
    accessToken: vi.fn().mockResolvedValue('token'),
    hasOidcCallback: () => false,
    initializeFromCallback: vi.fn(),
    beginLogin: vi.fn(),
    deny: vi.fn(),
    logout: vi.fn(async () => {
      auth.identityEpoch.update((e) => e + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
  };
  const plan = p;
  const api = {
    detail: vi.fn().mockReturnValue(of(plan)),
    list: vi.fn().mockReturnValue(
      of({
        items: [
          {
            tripId: plan.tripId,
            revision: plan.revision,
            title: '<img src=x onerror=alert(1)>',
            destinationId: 'saint-petersburg',
            displayName: 'Санкт-Петербург',
            startDate: '2026-11-14',
            endDate: '2026-11-14',
            updatedAt: plan.updatedAt,
          },
        ],
        nextCursor: null,
      }),
    ),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn().mockReturnValue(of(undefined)),
  };
  const availability = {
    status: signal('ready'),
    enabled: () => true,
    catalogue: signal({ catalogueVersion: '1', items: [p.destination] }),
    initialize: vi.fn().mockResolvedValue(undefined),
    clearPrivate: vi.fn(),
  };
  const params = convertToParamMap({ id: plan.tripId });
  TestBed.configureTestingModule({
    imports: [TripsPageComponent],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      ...(walking
        ? [{ provide: TRIPS_ITINERARY_CLOCK, useValue: () => Date.parse('2026-10-21T00:01:00Z') }]
        : [
            {
              provide: TripsItineraryApiService,
              useValue: { availability: () => throwError(() => new TripsItineraryRequestError('disabled', 404)) },
            },
          ]),
      { provide: ActivatedRoute, useValue: { paramMap: of(params), snapshot: { paramMap: params } } },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsAvailabilityService, useValue: availability },
      { provide: TripsApiService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(TripsPageComponent);
  if (walking) {
    await settle();
    fixture.detectChanges();
  } else await fixture.whenStable();
  return {
    fixture,
    page: fixture.componentInstance,
    api,
    auth,
    plan,
    http: TestBed.inject(HttpTestingController),
    root: fixture.nativeElement as HTMLElement,
  };
}

function required<T>(value: T | undefined | null): T {
  expect(value).not.toBeNull();
  expect(value).not.toBeUndefined();
  return value!;
}
function button(root: HTMLElement, label: string): HTMLButtonElement {
  return required([...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === label));
}
describe('Trips Rail form boundary', () => {
  it('opens a reusable semantic sibling form with no request or private inferred criteria', async () => {
    const s = await setup();
    button(s.root, 'Выбрать поезд').click();
    await s.fixture.whenStable();
    const picker = required(s.root.querySelector('app-rail-search'));
    expect(picker.closest('form')).toBeNull();
    expect(picker.querySelectorAll('main,h1')).toHaveLength(0);
    expect(s.root.querySelectorAll('h1')).toHaveLength(1);
    expect(s.root.querySelectorAll('form')).toHaveLength(2);
    expect(picker.querySelector<HTMLInputElement>('input[name=departureDate]')?.value).toBe('');
    expect(s.page.state.dirty()).toBe(false);
    s.http.expectNone((r) => r.url.startsWith('/api/rail/'));
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('does not offer opening from an unapplied brief edit', async () => {
    const s = await setup();
    s.page.patchBrief('adults', 3);
    await s.fixture.whenStable();
    expect(button(s.root, 'Выбрать поезд').disabled).toBe(true);
  });
});

const settle = async () => {
  for (let i = 0; i < 12; i++) await Promise.resolve();
};
type Setup = Awaited<ReturnType<typeof setup>>;
afterEach(() => vi.useRealTimers());
async function open(s: Setup) {
  button(s.root, 'Выбрать поезд').click();
  await s.fixture.whenStable();
  return s.fixture.debugElement.query(By.directive(RailSearchComponent)).componentInstance as RailSearchComponent;
}
async function search(s: Setup, mutate: (r: RailSearchResult) => void = () => {}) {
  const rail = await open(s);
  vi.useFakeTimers();
  const { result } = railFixture(Date.now());
  mutate(result);
  for (const [field, q, code] of [
    ['origin', 'AAA', result.request.from.code],
    ['destination', 'BBB', result.request.to.code],
  ] as const) {
    rail.state.input(field, q);
    await vi.advanceTimersByTimeAsync(300);
    const req = s.http.expectOne((r) => r.url === '/api/rail/stations' && r.params.get('q') === q);
    expect(req.request.params.keys()).toEqual(['q']);
    expect(req.request.transferCache).toBe(false);
    const ref: RailSuggestions = {
      kind: 'complete',
      reasons: [],
      hasMore: false,
      catalogueGeneration: result.request.catalogueGeneration,
      suggestions: [{ reference: { source: 'yandex-rasp', code }, name: q, settlement: null, region: null }],
      source: 'yandex-rasp',
      profile: result.profile,
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    };
    req.flush(ref);
    await settle();
    rail.state.select(field, rail.state[field].suggestions[0]);
  }
  rail.state.date = result.request.departureDate;
  s.fixture.detectChanges();
  required(s.root.querySelector('app-rail-search form')).dispatchEvent(
    new Event('submit', { bubbles: true, cancelable: true }),
  );
  await settle();
  const req = s.http.expectOne('/api/rail/search');
  expect(req.request.body).toEqual(result.request);
  expect(req.request.transferCache).toBe(false);
  expect(s.api.replace).not.toHaveBeenCalled();
  req.flush(result);
  await settle();
  s.fixture.detectChanges();
  return { rail, result: rail.state.result! };
}
function target(s: Setup, date = s.plan.days[0].date) {
  const input = required(s.root.querySelector<HTMLSelectElement>('#trip-rail-day'));
  input.value = date;
  input.dispatchEvent(new Event('change'));
  s.fixture.detectChanges();
}
describe('Trips Rail actual shared client and ordinary editor consumer', () => {
  it('requires an explicit plan day, then appends once to a nonempty plan and only persists on explicit Save', async () => {
    const s = await setup(),
      before = structuredClone(s.page.state.buffer()!);
    const { rail, result } = await search(s);
    expect(s.root.textContent).toContain('9007199254740993.05 RUB');
    expect(s.root.textContent).toContain('IANA неизвестны');
    const choice = required(rail.state.captureSelection(result.items[0]));
    button(s.root, 'Добавить намерение в план').click();
    expect(s.page.state.buffer()).toEqual(before);
    expect(s.page.railMessage()).toContain('Выберите день');
    target(s);
    expect(s.page.state.dirty()).toBe(false);
    expect(rail.state.date).toBe(result.request.departureDate);
    button(s.root, 'Добавить намерение в план').click();
    rail.journeySelected.emit(choice);
    await settle();
    s.fixture.detectChanges();
    const staged = required(s.page.state.buffer()),
      block = required(staged.days[0].blocks.at(-1));
    expect(staged.days[0].blocks.slice(0, -1)).toEqual(before.days[0].blocks);
    expect(staged.brief).toEqual(before.brief);
    expect(staged.title).toBe(before.title);
    expect(block.title).toBe('Поезд 001 (демо)');
    expect(block.note).toContain('Вымышленное демо; историческое намерение');
    expect(block.note).toContain('Источник=yandex-rasp; профиль=fictional.');
    expect(block.note).toContain('Получено=' + result.items[0].receivedAt);
    expect(block.startTime).toBeNull();
    expect(block.endTime).toBeNull();
    expect(block.estimate).toBeNull();
    expect(s.api.replace).not.toHaveBeenCalled();
    expect(s.api.detail).toHaveBeenCalledTimes(1);
    expect(s.root.querySelector('app-rail-search')).toBeNull();
    expect(s.page.state.dirty()).toBe(true);
    vi.useRealTimers();
    await s.fixture.whenStable();
    expect(document.activeElement?.id).toBe('block-' + block.blockId);
    s.api.replace.mockReturnValue(of({ ...s.plan, ...staged, revision: '3' }));
    button(s.root, 'Сохранить').click();
    await s.fixture.whenStable();
    expect(s.api.replace).toHaveBeenCalledTimes(1);
    expect(s.api.replace.mock.calls[0][1]).toBe(s.plan.revision);
    s.api.detail.mockReturnValue(of({ ...s.plan, ...staged, revision: '3' }));
    await s.page.state.refreshDetail();
    await s.fixture.whenStable();
    expect(s.page.state.buffer()?.days[0].blocks.at(-1)?.note).toBe(block.note);
    expect(s.root.querySelector('app-rail-search')).toBeNull();
    expect(s.root.textContent).not.toContain('Источник и условия данных');
    s.http.verify();
  });
  it('keeps target date independent of departure date and preserves every other day', async () => {
    const p = decodeTripPlan(structuredClone(tripsExample));
    p.brief.endDate = '2026-11-15';
    p.days.push({ date: '2026-11-15', blocks: [] });
    const s = await setup(p),
      before = structuredClone(s.page.state.buffer()!);
    const { rail, result } = await search(s);
    target(s, '2026-11-15');
    expect(s.page.state.buffer()).toEqual(before);
    expect(rail.state.date).not.toBe('2026-11-15');
    button(s.root, 'Добавить намерение в план').click();
    await settle();
    s.fixture.detectChanges();
    expect(s.page.state.buffer()?.days[0]).toEqual(before.days[0]);
    expect(s.page.state.buffer()?.days[1].blocks).toHaveLength(1);
    expect(s.page.state.buffer()?.days[1].blocks[0].note).toContain(result.items[0].departure);
  });
  it('keeps real source view-only and never converts it into fictional plan text', async () => {
    const s = await setup(),
      before = structuredClone(s.page.state.buffer());
    const { rail, result } = await search(s, (r) => {
      r.profile = 'real';
      r.attribution!.frameUrl = 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html';
    });
    target(s);
    expect(button(s.root, 'Добавить намерение в план').disabled).toBe(true);
    expect(s.root.textContent).toContain('только для просмотра');
    expect(rail.state.captureSelection(result.items[0])).toBeNull();
    expect(s.page.state.buffer()).toEqual(before);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it.each(['form', 'edit', 'read', 'list', 'owner', 'logout', 'leave'] as const)(
    'destroys the picker and refuses saved callback after %s',
    async (action) => {
      const s = await setup();
      const { rail, result } = await search(s);
      target(s);
      const choice = required(rail.state.captureSelection(result.items[0]));
      if (action === 'form') s.page.patchBrief('adults', 3);
      if (action === 'edit') s.page.title('Правка владельца');
      if (action === 'read') await s.page.state.refreshDetail();
      if (action === 'list') await s.page.state.loadList();
      if (action === 'owner') s.auth.identityEpoch.update((x) => x + 1);
      if (action === 'logout') await s.page.logout();
      if (action === 'leave') s.page.state.leave();
      const before = structuredClone(s.page.state.buffer());
      rail.journeySelected.emit(choice);
      await settle();
      s.fixture.detectChanges();
      expect(s.page.state.buffer()).toEqual(before);
      expect(s.root.querySelector('app-rail-search')).toBeNull();
      expect(choice.isCurrent()).toBe(false);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it('cancels pending station read on close, restores focus and reopens empty without a request', async () => {
    const s = await setup();
    const rail = await open(s);
    expect(document.activeElement?.id).toBe('rail-origin');
    vi.useFakeTimers();
    rail.state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    const req = s.http.expectOne((r) => r.url === '/api/rail/stations');
    button(s.root, 'Закрыть поиск поезда').click();
    await settle();
    s.fixture.detectChanges();
    expect(req.cancelled).toBe(true);
    vi.useRealTimers();
    await s.fixture.whenStable();
    expect(document.activeElement?.id).toBe('open-rail');
    const fresh = await open(s);
    expect(fresh.state.origin.query).toBe('');
    expect(fresh.state.date).toBe('');
    expect(fresh.state.result).toBeNull();
    s.http.expectNone((r) => r.url.startsWith('/api/rail/'));
  });
  it.each(['expired', 'null-ttl', 'banner', 'csp', 'criteria'] as const)(
    'refuses %s at actual choose and leaves the plan byte-for-byte unchanged',
    async (fault) => {
      const s = await setup(),
        before = structuredClone(s.page.state.buffer());
      const { rail, result } = await search(s);
      target(s);
      const choice = required(rail.state.captureSelection(result.items[0]));
      if (fault === 'expired') vi.setSystemTime(Date.parse(result.expiresAt!) + 1);
      if (fault === 'null-ttl') result.expiresAt = null;
      if (fault === 'banner') required(s.root.querySelector('iframe')).dispatchEvent(new Event('error'));
      if (fault === 'csp') {
        const event = new Event('securitypolicyviolation');
        Object.defineProperties(event, {
          effectiveDirective: { value: 'frame-src' },
          disposition: { value: 'enforce' },
          blockedURI: { value: new URL('/rail-fictional-banner.html', document.baseURI).href },
        });
        document.dispatchEvent(event);
      }
      if (fault === 'criteria') {
        rail.state.date = '2027-01-01';
        rail.state.criteriaChanged();
      }
      rail.choose(result.items[0]);
      rail.journeySelected.emit(choice);
      expect(s.page.state.buffer()).toEqual(before);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it('retains ordinary conflict and lost-PUT barriers after staging and cannot reopen until resolved', async () => {
    const s = await setup();
    await search(s);
    target(s);
    button(s.root, 'Добавить намерение в план').click();
    await settle();
    s.fixture.detectChanges();
    const staged = structuredClone(s.page.state.buffer());
    s.api.replace.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    vi.useRealTimers();
    button(s.root, 'Сохранить').click();
    await s.fixture.whenStable();
    expect(s.page.state.status()).toBe('uncertain');
    expect(s.page.state.buffer()).toEqual(staged);
    expect(button(s.root, 'Выбрать поезд').disabled).toBe(true);
    await s.page.submit();
    expect(s.api.replace).toHaveBeenCalledTimes(1);
    s.http.expectNone((r) => r.url.startsWith('/api/rail/'));
  });
});

describe('Trips Rail bounded error UI', () => {
  it('refuses a full selected day without a placeholder or draft mutation', async () => {
    const p = decodeTripPlan(structuredClone(tripsExample)),
      base = p.days[0].blocks[0];
    p.days[0].blocks = Array.from({ length: 8 }, (_, i) => ({
      ...base,
      blockId: `11111111-1111-4111-8111-${String(i + 1).padStart(12, '0')}`,
    }));
    const s = await setup(p),
      before = structuredClone(s.page.state.buffer());
    await search(s);
    target(s);
    button(s.root, 'Добавить намерение в план').click();
    expect(s.page.railMessage()).toContain('8 блоков');
    expect(s.page.state.buffer()).toEqual(before);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('refuses exact essential note overflow through source/client/component/codec without dropping a source field', async () => {
    const s = await setup(),
      before = structuredClone(s.page.state.buffer());
    await search(s, (r) => {
      r.request.from.code = 's' + '1'.repeat(127);
      r.request.to.code = 's' + '2'.repeat(127);
      r.items.forEach((j) => {
        j.threadReference = 't'.repeat(100);
      });
    });
    target(s);
    button(s.root, 'Добавить намерение в план').click();
    expect(s.page.railMessage()).toContain('500 символов');
    expect(s.page.state.buffer()).toEqual(before);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('cancels a pending search after a real form edit and leaves late work unable to append or steal focus', async () => {
    const s = await setup();
    const rail = await open(s);
    const { result } = railFixture(Date.now());
    const state = rail.state;
    state.reference = {
      kind: 'complete',
      reasons: [],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: result.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    };
    state.origin.selected = { reference: result.request.from, name: 'A', settlement: null, region: null };
    state.destination.selected = { reference: result.request.to, name: 'B', settlement: null, region: null };
    state.date = result.request.departureDate;
    const pending = state.search();
    const req = s.http.expectOne('/api/rail/search');
    const input = s.root.querySelector<HTMLInputElement>('#trip-title')!;
    input.focus();
    s.page.title('Поздняя правка');
    await s.fixture.whenStable();
    expect(req.cancelled).toBe(true);
    await pending;
    expect(s.page.state.buffer()?.title).toBe('Поздняя правка');
    expect(s.page.state.buffer()?.days[0].blocks).toHaveLength(s.plan.days[0].blocks.length);
    expect(s.root.querySelector('app-rail-search')).toBeNull();
    expect(document.activeElement).toBe(input);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
});

it('binds a retained old output callback to its original foundation across close and reopen before teardown', async () => {
  const s = await setup();
  const { rail, result } = await search(s);
  target(s);
  const choice = required(rail.state.captureSelection(result.items[0]));
  const before = structuredClone(s.page.state.buffer());
  s.page.closeRail();
  s.page.openRail();
  s.page.railTargetDay.set(s.plan.days[0].date);
  rail.journeySelected.emit(choice);
  expect(s.page.state.buffer()).toEqual(before);
  expect(s.api.replace).not.toHaveBeenCalled();
  await settle();
  s.fixture.detectChanges();
  const fresh = s.fixture.debugElement.query(By.directive(RailSearchComponent))
    .componentInstance as RailSearchComponent;
  expect(fresh).not.toBe(rail);
  expect(fresh.state.result).toBeNull();
  expect(choice.isCurrent()).toBe(false);
});

function walkingBasis() {
  return decodeTripPlan({
    ...structuredClone(tripsExample),
    revision: '1',
    brief: {
      ...tripsExample.brief,
      destinationId: 'moscow',
      originId: 'saint-petersburg',
      startDate: '2026-10-22',
      endDate: '2026-10-22',
      interests: ['architecture'],
    },
    destination: { ...tripsExample.destination, destinationId: 'moscow', displayName: 'Москва' },
    days: [{ date: '2026-10-22', blocks: [] }],
    knownUserEstimateSubtotal: '0.00',
  });
}
function walkingBytes(value: unknown) {
  const data = new TextEncoder().encode(JSON.stringify(value)),
    bytes = new ArrayBuffer(data.length);
  new Uint8Array(bytes).set(data);
  return bytes;
}
async function walkingPreview(s: Setup) {
  s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
  await s.fixture.whenStable();
  const generating = s.page.itinerary.generate();
  await settle();
  s.http.expectOne(`/api/trips/${s.plan.tripId}/itinerary-candidate`).flush(walkingBytes(walkingCandidate));
  await generating;
  await s.fixture.whenStable();
  expect(s.page.itinerary.canAdopt()).toBe(true);
}
describe('Coupled actual walking owner read and Rail exclusion', () => {
  it.each(['success', 'failure', 'cancelled'] as const)(
    'invalidates pre-opened Rail immediately and preserves walking owner-read %s semantics',
    async (outcome) => {
      const s = await setup(walkingBasis(), true);
      await walkingPreview(s);
      const { rail, result } = await search(s);
      target(s);
      const choice = required(rail.state.captureSelection(result.items[0])),
        oldRail = required(s.page.state.captureSavedFoundation()),
        before = structuredClone(s.page.state.buffer());
      const owner = new Subject<typeof s.plan>();
      s.api.detail.mockReturnValueOnce(owner);
      const adoption = s.page.itinerary.adopt();
      // No render/teardown occurs between read start and these real editor/action guards.
      expect(s.page.state.canCaptureSavedFoundation()).toBe(false);
      expect(s.page.state.captureSavedFoundation()).toBeNull();
      expect(s.page.state.isSavedFoundationCurrent(oldRail)).toBe(false);
      expect(s.page.railOpen()).toBe(false);
      rail.journeySelected.emit(choice);
      expect(s.page.state.buffer()).toEqual(before);
      s.page.openRail();
      expect(s.page.railOpen()).toBe(false);
      await settle();
      expect(s.api.detail).toHaveBeenCalledTimes(2);
      expect(owner.observed).toBe(true);
      expect(s.page.itinerary.pending()).toBe('checking-adoption');
      s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
      await settle();
      s.fixture.detectChanges();
      expect(s.root.querySelector('app-rail-search')).toBeNull();
      expect(choice.isCurrent()).toBe(false);
      expect(s.page.itinerary.pending()).toBe('checking-adoption');
      if (outcome === 'success') {
        owner.next(s.plan);
        owner.complete();
      }
      if (outcome === 'failure') owner.error(new HttpErrorResponse({ status: 503 }));
      if (outcome === 'cancelled') s.page.itinerary.dismiss();
      expect(await adoption).toBe(outcome === 'success');
      await settle();
      s.fixture.detectChanges();
      expect(s.page.state.isSavedFoundationCurrent(oldRail)).toBe(false);
      expect(s.api.replace).not.toHaveBeenCalled();
      expect(s.api.detail).toHaveBeenCalledTimes(2);
      if (outcome === 'success') {
        expect(s.page.itinerary.status()).toBe('staged');
        expect(s.page.state.baseline()).toEqual(s.plan);
        expect(s.page.state.buffer()?.title).toBe(s.plan.title);
        expect(s.page.state.buffer()?.brief).toEqual(s.plan.brief);
        expect(s.page.state.buffer()?.days[0].blocks.map((block) => block.blockId)).toEqual(
          walkingCandidate.days[0].blocks.map((block) => block.proposedBlockId),
        );
      } else {
        expect(s.page.state.buffer()).toEqual(before);
        s.page.openRail();
        await settle();
        s.fixture.detectChanges();
        expect(s.page.railOpen()).toBe(true);
        const fresh = s.fixture.debugElement.query(By.directive(RailSearchComponent))
          .componentInstance as RailSearchComponent;
        expect(fresh).not.toBe(rail);
        expect(fresh.state.result).toBeNull();
        expect(s.page.state.isSavedFoundationCurrent(oldRail)).toBe(false);
      }
      vi.useRealTimers();
      s.http.verify();
    },
  );
  it('cannot open or capture Rail when the owner read started without a picker', async () => {
    const s = await setup(walkingBasis(), true);
    await walkingPreview(s);
    const owner = new Subject<typeof s.plan>();
    s.api.detail.mockReturnValueOnce(owner);
    const adoption = s.page.itinerary.adopt();
    s.page.openRail();
    expect(s.page.railOpen()).toBe(false);
    expect(s.page.state.captureSavedFoundation()).toBeNull();
    await settle();
    expect(owner.observed).toBe(true);
    s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
    owner.next(s.plan);
    owner.complete();
    expect(await adoption).toBe(true);
    expect(s.api.detail).toHaveBeenCalledTimes(2);
    expect(s.api.replace).not.toHaveBeenCalled();
    s.http.verify();
  });
});

it('focuses the visible itinerary error after the actual page adoption owner GET503 without an unrelated edit', async () => {
  const s = await setup(walkingBasis(), true);
  await walkingPreview(s);
  const before = structuredClone(s.page.state.buffer()),
    owner = new Subject<typeof s.plan>();
  s.api.detail.mockReturnValueOnce(owner);
  const action = required(s.root.querySelector<HTMLButtonElement>('#adopt-itinerary'));
  action.focus();
  const adoption = s.page.adoptItinerary();
  await settle();
  expect(owner.observed).toBe(true);
  expect(s.api.detail).toHaveBeenCalledTimes(2);
  s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
  owner.error(new HttpErrorResponse({ status: 503 }));
  await adoption;
  await s.fixture.whenStable();
  expect(s.page.itinerary.status()).toBe('unavailable');
  expect(s.root.querySelector('#itinerary-preview')).toBeNull();
  expect(document.activeElement?.id).toBe('itinerary-status');
  expect(s.page.state.buffer()).toEqual(before);
  expect(s.api.replace).not.toHaveBeenCalled();
  s.http.verify();
});

it.each(['edit', 'dismiss', 'direct-dismiss', 'newer-intent', 'navigation', 'logout'] as const)(
  'does not steal focus from %s after the actual page adoption owner read was dispatched',
  async (action) => {
    const s = await setup(walkingBasis(), true);
    await walkingPreview(s);
    const owner = new Subject<typeof s.plan>();
    s.api.detail.mockReturnValueOnce(owner);
    required(s.root.querySelector<HTMLButtonElement>('#adopt-itinerary')).focus();
    const adoption = s.page.adoptItinerary();
    await settle();
    expect(owner.observed).toBe(true);
    s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
    await settle();
    const input = required(s.root.querySelector<HTMLInputElement>('#trip-title'));
    input.focus();
    if (action === 'edit') s.page.title('Late owner edit');
    if (action === 'dismiss') s.page.dismissItinerary();
    if (action === 'direct-dismiss') s.page.itinerary.dismiss();
    if (action === 'newer-intent') s.page.confirmDelete();
    if (action === 'navigation') {
      const other = '33333333-3333-4333-8333-333333333333';
      s.api.detail.mockImplementation((tripId: string) => of({ ...s.plan, tripId }));
      // Exercise the authoritative route context without advancing the page's focusIntent.
      // This isolates the owning-foundation guard instead of relying on a newer UI intent.
      await s.page.state.enter(other);
      expect(s.page.state.selectedId()).toBe(other);
    }
    if (action === 'logout') await s.page.logout();
    owner.error(new HttpErrorResponse({ status: 503 }));
    await adoption;
    await s.fixture.whenStable();
    if (action === 'edit' || action === 'direct-dismiss') expect(document.activeElement).toBe(input);
    if (action === 'dismiss') expect(document.activeElement?.id).toBe('generate-itinerary');
    if (action === 'newer-intent') expect(document.activeElement?.id).toBe('confirm-delete');
    expect(document.activeElement?.id).not.toBe('itinerary-status');
    expect(s.api.replace).not.toHaveBeenCalled();
    expect(s.api.delete).not.toHaveBeenCalled();
    s.http.verify();
  },
);
it.each(['edit', 'direct-dismiss', 'newer-intent'] as const)(
  'rechecks the scheduled error-focus continuation after %s at the render boundary',
  async (action) => {
    const s = await setup(walkingBasis(), true);
    await walkingPreview(s);
    const owner = new Subject<typeof s.plan>();
    s.api.detail.mockReturnValueOnce(owner);
    const input = required(s.root.querySelector<HTMLInputElement>('#trip-title'));
    const target = s.page as unknown as { focus(id: string, intent?: number, valid?: () => boolean): void },
      originalFocus = target.focus.bind(s.page);
    let boundaryReached = false;
    const scheduling = vi.spyOn(target, 'focus').mockImplementation((id, intent, valid) => {
      if (id !== 'itinerary-status') {
        originalFocus(id, intent, valid);
        return;
      }
      if (action === 'newer-intent') {
        originalFocus(id, intent, valid);
        boundaryReached = true;
        s.page.confirmDelete();
        return;
      }
      originalFocus(id, intent, () => {
        boundaryReached = true;
        input.focus();
        if (action === 'edit') s.page.title('Edit after focus request');
        else s.page.itinerary.dismiss();
        return valid?.() ?? true;
      });
    });
    try {
      const adoption = s.page.adoptItinerary();
      await settle();
      expect(owner.observed).toBe(true);
      s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
      owner.error(new HttpErrorResponse({ status: 503 }));
      await adoption;
      await s.fixture.whenStable();
      expect(boundaryReached).toBe(true);
      if (action === 'newer-intent') expect(document.activeElement?.id).toBe('confirm-delete');
      else expect(document.activeElement).toBe(input);
      expect(document.activeElement?.id).not.toBe('itinerary-status');
      expect(s.api.replace).not.toHaveBeenCalled();
      s.http.verify();
    } finally {
      scheduling.mockRestore();
    }
  },
);

describe('final itinerary render-focus ownership', () => {
  for (const outcome of ['generation-error', 'availability-error', 'generation-success'] as const) {
    it.each(['unchanged', 'edit', 'brief-edit', 'direct-dismiss', 'newer-intent'] as const)(
      outcome + ' rechecks %s at the deterministic render boundary',
      async (action) => {
        const s = await setup(walkingBasis(), true);
        s.http.expectOne('/api/trips/itinerary-availability').flush(walkingBytes(walkingReady));
        await s.fixture.whenStable();
        const input = required(s.root.querySelector<HTMLInputElement>('#trip-title'));
        const target = s.page as unknown as { focus(id: string, intent?: number, valid?: () => boolean): void },
          originalFocus = target.focus.bind(s.page);
        const focusId = outcome === 'generation-success' ? 'itinerary-preview-title' : 'itinerary-status';
        let boundaryReached = false;
        const scheduling = vi.spyOn(target, 'focus').mockImplementation((id, intent, valid) => {
          if (id !== focusId) return originalFocus(id, intent, valid);
          if (action === 'newer-intent') {
            originalFocus(id, intent, valid);
            boundaryReached = true;
            s.page.confirmDelete();
            return;
          }
          originalFocus(id, intent, () => {
            boundaryReached = true;
            input.focus();
            if (action === 'edit') s.page.title('Edit after scheduling');
            if (action === 'brief-edit') s.page.patchBrief('adults', 3);
            if (action === 'direct-dismiss') s.page.itinerary.dismiss();
            return valid?.() ?? true;
          });
        });
        try {
          const pending =
            outcome === 'availability-error' ? s.page.refreshItineraryAvailability() : s.page.generateItinerary();
          await settle();
          const request = s.http.expectOne(
            outcome === 'availability-error'
              ? '/api/trips/itinerary-availability'
              : '/api/trips/' + s.plan.tripId + '/itinerary-candidate',
          );
          if (outcome === 'generation-success') request.flush(walkingBytes(walkingCandidate));
          else request.flush(null, { status: 503, statusText: 'Unavailable' });
          await pending;
          await s.fixture.whenStable();
          expect(boundaryReached).toBe(true);
          if (action === 'unchanged') expect(document.activeElement?.id).toBe(focusId);
          else if (action === 'newer-intent') expect(document.activeElement?.id).toBe('confirm-delete');
          else expect(document.activeElement).toBe(input);
          expect(s.api.replace).not.toHaveBeenCalled();
          expect(s.api.delete).not.toHaveBeenCalled();
          s.http.verify();
        } finally {
          scheduling.mockRestore();
        }
      },
    );
  }
});
