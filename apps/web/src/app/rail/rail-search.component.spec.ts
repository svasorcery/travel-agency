import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { RailSearchResult, RailSuggestions } from '@travel/api-client';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { RailSearchComponent } from './rail-search.component';

const settle = async () => {
  for (let i = 0; i < 12; i++) await Promise.resolve();
};
afterEach(() => vi.useRealTimers());
function setup(platform = 'browser') {
  TestBed.configureTestingModule({
    imports: [RailSearchComponent],
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: platform }],
  });
  const fixture = TestBed.createComponent(RailSearchComponent);
  fixture.componentRef.setInput('selectable', true);
  fixture.detectChanges();
  return {
    fixture,
    component: fixture.componentInstance,
    http: TestBed.inject(HttpTestingController),
    root: fixture.nativeElement as HTMLElement,
  };
}
async function current(
  mutate: (r: RailSearchResult) => void = () => {},
  render = true,
  profile: 'fictional' | 'real' = 'fictional',
) {
  const s = setup();
  const { result } = railFixture(Date.now());
  result.profile = profile;
  if (profile === 'real') result.attribution!.frameUrl = 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html';
  const referenceResult = structuredClone(result);
  mutate(result);
  const reference: RailSuggestions = {
    kind: 'complete',
    reasons: [],
    suggestions: [],
    hasMore: false,
    catalogueGeneration: result.request.catalogueGeneration,
    source: 'yandex-rasp',
    profile: referenceResult.profile,
    profileEpoch: referenceResult.profileEpoch,
    receivedAt: referenceResult.receivedAt,
    expiresAt: referenceResult.expiresAt,
    attribution: referenceResult.attribution,
  };
  const state = s.component.state;
  state.reference = reference;
  state.origin.selected = { reference: result.request.from, name: 'AAA', settlement: null, region: null };
  state.destination.selected = { reference: result.request.to, name: 'BBB', settlement: null, region: null };
  state.date = result.request.departureDate;
  const pending = state.search();
  const req = s.http.expectOne('/api/rail/search');
  req.flush(result);
  await pending;
  if (render) s.fixture.detectChanges();
  return { ...s, result: state.result };
}
describe('Reusable Rail search semantic and lifecycle boundaries', () => {
  it('does not request on construction and refuses browser transport during SSR', async () => {
    const s = setup('server');
    s.http.expectNone((r) => r.url.startsWith('/api/rail/'));
    vi.useFakeTimers();
    s.component.state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    s.http.expectNone((r) => r.url.startsWith('/api/rail/'));
    expect(s.component.state.origin.suggestions).toHaveLength(0);
  });
  it('keeps keyboard lookup local and Enter chooses the station without search submission', async () => {
    const s = setup();
    vi.useFakeTimers();
    const { result } = railFixture(Date.now());
    s.component.state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    const req = s.http.expectOne((r) => r.url === '/api/rail/stations');
    req.flush({
      kind: 'complete',
      reasons: [],
      hasMore: false,
      suggestions: [{ reference: result.request.from, name: 'AAA', settlement: null, region: null }],
      catalogueGeneration: result.request.catalogueGeneration,
      source: 'yandex-rasp',
      profile: 'fictional',
      profileEpoch: result.profileEpoch,
      receivedAt: result.receivedAt,
      expiresAt: result.expiresAt,
      attribution: result.attribution,
    });
    await settle();
    s.fixture.detectChanges();
    const unrelated = document.createElement('div');
    unrelated.id = 'rail-origin-options';
    unrelated.innerHTML = '<button>outside</button>';
    document.body.prepend(unrelated);
    try {
      const input = s.root.querySelector<HTMLInputElement>('#rail-origin')!;
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
      expect(s.root.contains(document.activeElement)).toBe(true);
      const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
      input.dispatchEvent(enter);
      expect(enter.defaultPrevented).toBe(true);
      expect(s.component.state.origin.selected?.reference.code).toBe('s1');
      s.http.expectNone('/api/rail/search');
    } finally {
      unrelated.remove();
    }
  });
  it('emits only a current row at click and destroys its emitted authority on teardown', async () => {
    const s = await current(),
      events: unknown[] = [];
    s.component.journeySelected.subscribe((value) => events.push(value));
    const row = s.result!.items[0];
    s.component.choose(structuredClone(row));
    expect(events).toHaveLength(0);
    s.root.querySelector<HTMLButtonElement>('article button')!.click();
    expect(events).toHaveLength(1);
    const choice = s.component.state.captureSelection(row)!;
    s.fixture.destroy();
    expect(choice.isCurrent()).toBe(false);
    s.component.choose(row);
    expect(events).toHaveLength(1);
  });
  it.each(['complete-empty', 'limited-empty', 'skipped', 'failed'] as const)(
    'retains distinct %s outcome and invents no selectable journey',
    async (kind) => {
      const s = await current((result) => {
        result.items = [];
        result.normalizedCount = 0;
        result.modes.forEach((m) => {
          m.originalCount = 0;
          m.normalizedCount = 0;
        });
        if (kind === 'complete-empty')
          result.expiresAt = new Date(Date.parse(result.receivedAt!) + 30000).toISOString();
        if (kind === 'limited-empty') {
          result.kind = 'limited';
          result.reasons = ['page-limit'];
          result.modes[0].kind = 'limited';
          result.modes[0].reasons = ['page-limit'];
          result.expiresAt = new Date(Date.parse(result.receivedAt!) + 30000).toISOString();
        }
        if (kind === 'skipped' || kind === 'failed') {
          result.kind = kind;
          result.reasons = ['transport'];
          result.modes.forEach((m) => {
            m.kind = kind;
            m.reasons = ['transport'];
            m.receivedAt = null;
          });
          result.receivedAt = null;
          result.expiresAt = null;
          result.attribution = null;
        }
      });
      expect(s.result?.kind).toBe(kind === 'complete-empty' ? 'complete' : kind === 'limited-empty' ? 'limited' : kind);
      expect(s.root.querySelector('article button')).toBeNull();
      expect(s.component.state.message).toContain(
        kind === 'complete-empty'
          ? 'не найдено'
          : kind === 'limited-empty'
            ? 'ограниченная'
            : kind === 'skipped'
              ? 'не запущен'
              : 'Это не отсутствие',
      );
    },
  );
  it('cancels an in-flight search and ignores a late response after component teardown', async () => {
    const s = setup(),
      { result } = railFixture(Date.now());
    const state = s.component.state;
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
    s.fixture.destroy();
    expect(req.cancelled).toBe(true);
    await pending;
    expect(state.result).toBeNull();
    expect(state.reference).toBeNull();
  });
});

it('renders a valid100-row envelope without whole-result validation on each row or unrelated render', async () => {
  const s = await current((result) => {
    const templates = result.items;
    result.items = Array.from({ length: 100 }, (_, index) => ({
      ...structuredClone(templates[index % 2]),
      key: `row-${index}`,
      threadReference: `thread-${index}`,
    }));
    result.normalizedCount = 100;
    result.modes.forEach((mode) => {
      mode.originalCount = 50;
      mode.normalizedCount = 50;
    });
  }, false);
  expect(s.result?.items).toHaveLength(100);
  const stringify = vi.spyOn(JSON, 'stringify');
  try {
    const wholeEnvelopeCalls = () =>
      stringify.mock.calls.filter(([value]) => value === s.result || value === s.component.state.reference).length;
    s.fixture.detectChanges();
    expect(s.root.querySelectorAll('article')).toHaveLength(100);
    expect([...s.root.querySelectorAll<HTMLButtonElement>('article button')].every((button) => !button.disabled)).toBe(
      true,
    );
    for (let pass = 0; pass < 3; pass++) {
      s.component.state.message = 'Unrelated status ' + pass;
      s.fixture.changeDetectorRef.markForCheck();
      s.fixture.detectChanges();
    }
    expect(wholeEnvelopeCalls()).toBe(0);
    const emitted: unknown[] = [];
    s.component.journeySelected.subscribe((value) => emitted.push(value));
    s.component.choose(s.result!.items[0]);
    expect(emitted).toHaveLength(1);
    expect(wholeEnvelopeCalls()).toBeGreaterThan(0);
    s.result!.items[0].threadReference = 'changed-after-admission';
    s.component.choose(s.result!.items[0]);
    expect(emitted).toHaveLength(1);
    vi.useFakeTimers();
    vi.setSystemTime(Date.parse(s.result!.expiresAt!) + 1);
    s.component.choose(s.result!.items[1]);
    expect(emitted).toHaveLength(1);
  } finally {
    stringify.mockRestore();
  }
});

it.each([
  'expired',
  'null-ttl',
  'profile',
  'epoch',
  'generation',
  'damaged',
  'attribution',
  'unsafe-frame',
  'unknown-limit',
  'criteria',
] as const)('keeps bounded display refusal and actual click refusal for %s', async (fault) => {
  const s = await current(),
    row = s.result!.items[0],
    emitted: unknown[] = [];
  s.component.journeySelected.subscribe((value) => emitted.push(value));
  expect(s.component.selectionReason(row)).toBe('');
  if (fault === 'expired') {
    vi.useFakeTimers();
    vi.setSystemTime(Date.parse(s.result!.expiresAt!) + 1);
  }
  if (fault === 'null-ttl') s.component.state.reference!.expiresAt = null;
  if (fault === 'profile') s.result!.profile = 'real';
  if (fault === 'epoch') s.component.state.reference!.profileEpoch = 'c'.repeat(32);
  if (fault === 'generation') s.component.state.reference!.catalogueGeneration = 'd'.repeat(32);
  if (fault === 'damaged') row.tickets.damaged = true;
  if (fault === 'attribution') s.result!.attribution = null;
  if (fault === 'unsafe-frame') s.result!.attribution!.frameUrl = 'https://unapproved.example/frame';
  if (fault === 'unknown-limit') {
    s.result!.kind = 'limited';
    s.result!.modes[0].kind = 'limited';
    s.result!.modes[0].reasons = ['unrecognized-limit'];
  }
  if (fault === 'criteria') s.component.state.date = '2027-01-01';
  expect(s.component.selectionReason(row)).not.toBe('');
  s.component.choose(row);
  expect(emitted).toHaveLength(0);
});

it.each(['fictional', 'real'] as const)(
  'attributes the exact reported fare to its strict %s profile',
  async (profile) => {
    const s = await current(() => {}, true, profile);
    expect(s.result?.profile).toBe(profile);
    const fare = s.root.querySelector('[data-testid="rail-price"]');
    expect(fare?.textContent).toBe('9007199254740993.05 RUB');
    expect(fare?.parentElement?.textContent).toContain(
      profile === 'fictional' ? 'Учебная цена' : 'Цена по данным Яндекс.Расписаний',
    );
    if (profile === 'fictional') expect(fare?.parentElement?.textContent).not.toContain('по данным Яндекс');
    expect(fare?.parentElement?.textContent).toContain('область действия цены не уточнена');
    expect(s.root.textContent).toContain('Наличие мест неизвестно');
    expect(s.root.textContent).toContain('сведения о фактическом движении не предоставлены');
    expect(s.root.querySelector('[data-testid="rail-fictional"]') !== null).toBe(profile === 'fictional');
    s.http.verify();
  },
);
