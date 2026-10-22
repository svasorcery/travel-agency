import type { RailJourneyDetailsResult, RailSearchResult, RailSuggestions } from '@travel/api-client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { railDetailsFixture } from '../../../test-fixtures/rail-details-fixture';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { RailState, type RailTransport } from './rail-state';

function suggestions(code: string, generation = 'a'.repeat(32)): RailSuggestions {
  const now = new Date().toISOString();
  return {
    kind: 'complete',
    reasons: [],
    suggestions: [{ reference: { source: 'yandex-rasp', code }, name: code, settlement: null, region: null }],
    hasMore: false,
    catalogueGeneration: generation,
    source: 'yandex-rasp',
    profile: 'fictional',
    profileEpoch: 'b'.repeat(32),
    receivedAt: now,
    expiresAt: new Date(Date.now() + 10000).toISOString(),
    attribution: {
      frameUrl: '/rail-fictional-banner.html',
      text: 'fictional',
      url: 'https://rasp.yandex.ru/',
      width: 240,
      height: 130,
    },
  };
}
afterEach(() => vi.useRealTimers());
describe('Rail current component memory', () => {
  it('cannot restore a bundle from another pending field after attribution failure', async () => {
    vi.useFakeTimers();
    let resolve: ((v: RailSuggestions) => void) | null = null;
    let signal: AbortSignal | null = null;
    const state = new RailState({
      stations: (_q, abort) => {
        signal = abort;
        return new Promise((done) => {
          resolve = done;
        });
      },
      search: vi.fn(),
    });
    state.input('destination', 'DDD');
    await vi.advanceTimersByTimeAsync(300);
    state.attributionFailed();
    expect(signal!.aborted).toBe(true);
    resolve!(suggestions('s2'));
    await Promise.resolve();
    expect(state.reference).toBeNull();
    expect(state.destination.suggestions).toHaveLength(0);
    state.dispose();
  });
  it('clears source facts after a detected attribution failure without auto querying', () => {
    const stations = vi.fn();
    const state = new RailState({ stations, search: vi.fn() });
    state.reference = suggestions('s1');
    state.origin.selected = state.reference.suggestions[0];
    state.attributionFailed();
    expect(state.reference).toBeNull();
    expect(state.origin.selected).toBeNull();
    expect(state.message).toContain('источнике');
    expect(stations).not.toHaveBeenCalled();
    state.dispose();
  });
  it('does not let an old independent-field profile failure clear a newer selection', async () => {
    vi.useFakeTimers();
    const pending: ((r: RailSuggestions) => void)[] = [];
    const state = new RailState({ stations: () => new Promise((resolve) => pending.push(resolve)), search: vi.fn() });
    state.input('origin', 'AAA');
    state.input('destination', 'DDD');
    await vi.advanceTimersByTimeAsync(300);
    const newer = suggestions('s1');
    newer.profileEpoch = 'c'.repeat(32);
    pending[0](newer);
    await Promise.resolve();
    state.select('origin', state.origin.suggestions[0]);
    pending[1]({
      ...suggestions('s2'),
      kind: 'failed',
      reasons: ['transport'],
      suggestions: [],
      catalogueGeneration: null,
      receivedAt: null,
      expiresAt: null,
      attribution: null,
    });
    await Promise.resolve();
    expect(state.origin.selected?.reference.code).toBe('s1');
    expect(state.reference?.profileEpoch).toBe('c'.repeat(32));
    state.dispose();
  });
  it('keeps field epochs independent and rejects late first A after A B A', async () => {
    vi.useFakeTimers();
    const pending: { q: string; resolve: (v: RailSuggestions) => void }[] = [];
    const api: RailTransport = {
      stations: (q) => new Promise((resolve) => pending.push({ q, resolve })),
      search: vi.fn(),
    };
    const state = new RailState(api);
    state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    state.input('destination', 'DDD');
    await vi.advanceTimersByTimeAsync(300);
    state.input('origin', 'BBB');
    await vi.advanceTimersByTimeAsync(300);
    state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    expect(pending).toHaveLength(4);
    pending[3].resolve(suggestions('s4'));
    pending[1].resolve(suggestions('s2'));
    await Promise.resolve();
    expect(state.destination.suggestions[0].reference.code).toBe('s2');
    pending[0].resolve(suggestions('s1'));
    await Promise.resolve();
    expect(state.origin.suggestions[0].reference.code).toBe('s4');
    state.dispose();
  });
  it('clears both selections on generation change and absolute expiry without auto request', async () => {
    vi.useFakeTimers();
    let generation = 'a'.repeat(32);
    const call = vi.fn(async () => suggestions('s1', generation));
    const state = new RailState({ stations: call, search: vi.fn() });
    state.input('origin', 'AAA');
    await vi.advanceTimersByTimeAsync(300);
    state.select('origin', state.origin.suggestions[0]);
    generation = 'c'.repeat(32);
    state.input('destination', 'DDD');
    await vi.advanceTimersByTimeAsync(300);
    expect(state.origin.selected).toBeNull();
    state.select('destination', state.destination.suggestions[0]);
    await vi.advanceTimersByTimeAsync(10000);
    expect(state.destination.selected).toBeNull();
    expect(state.reference).toBeNull();
    expect(call).toHaveBeenCalledTimes(2);
    state.dispose();
  });
});

async function searchable(
  transform?: (result: RailSearchResult) => void,
  referenceTtlMs?: number,
  details?: RailTransport['details'],
) {
  vi.useFakeTimers();
  const { result } = railFixture(Date.now());
  transform?.(result);
  const api = {
    stations: vi.fn(async (q: string) => ({
      ...suggestions(q === 'AAA' ? 's1' : 's2'),
      profile: result.profile,
      attribution: result.attribution,
      expiresAt:
        referenceTtlMs === undefined
          ? result.expiresAt
          : new Date(Date.parse(result.receivedAt!) + referenceTtlMs).toISOString(),
    })),
    search: vi.fn(async () => result),
    details,
  };
  const state = new RailState(api);
  state.input('origin', 'AAA');
  await vi.advanceTimersByTimeAsync(300);
  state.select('origin', state.origin.suggestions[0]);
  state.input('destination', 'BBB');
  await vi.advanceTimersByTimeAsync(300);
  state.select('destination', state.destination.suggestions[0]);
  state.date = result.request.departureDate;
  await state.search();
  return { state, result, api };
}
describe('Rail click-time fictional selection', () => {
  it('captures a current member and refuses an equal clone or expiry before the timer runs', async () => {
    const { state, result } = await searchable();
    expect(state.captureSelection(structuredClone(result.items[0]))).toBeNull();
    const choice = state.captureSelection(result.items[0]);
    expect(choice?.isCurrent()).toBe(true);
    vi.setSystemTime(Date.parse(result.expiresAt!) + 1);
    expect(choice?.isCurrent()).toBe(false);
    expect(state.captureSelection(result.items[0])).toBeNull();
    state.dispose();
  });
  it.each([
    'null-result-expiry',
    'null-reference-reception',
    'damaged',
    'missing-attribution',
    'request',
    'future',
    'real',
  ])('refuses %s at the current row boundary', async (fault) => {
    const { state, result } = await searchable();
    const choice = state.captureSelection(result.items[0]);
    if (fault === 'null-result-expiry') result.expiresAt = null;
    if (fault === 'null-reference-reception') state.reference!.receivedAt = null;
    if (fault === 'damaged') result.items[0].tickets.damaged = true;
    if (fault === 'missing-attribution') result.attribution = null;
    if (fault === 'request') state.date = '2027-01-01';
    if (fault === 'future') state.reference!.receivedAt = new Date(Date.now() + 1001).toISOString();
    if (fault === 'real') result.profile = 'real';
    expect(choice?.isCurrent()).toBe(false);
    expect(state.captureSelection(result.items[0])).toBeNull();
    state.dispose();
  });
  it('clears a same-profile latest failed station reference and does not resurrect an earlier selection', async () => {
    const { state, result, api } = await searchable();
    const choice = state.captureSelection(result.items[0]);
    api.stations.mockResolvedValueOnce({
      ...suggestions('s2'),
      kind: 'failed',
      reasons: ['transport'],
      suggestions: [],
      hasMore: false,
      catalogueGeneration: null,
      receivedAt: null,
      expiresAt: null,
      attribution: null,
    });
    state.input('destination', 'CCC');
    await vi.advanceTimersByTimeAsync(300);
    expect(state.reference).toBeNull();
    expect(choice?.isCurrent()).toBe(false);
    state.dispose();
  });
  it.each(['page-limit', 'interval-service-unsupported', 'unrecognized-limit'])(
    'allows only safe selected-mode limitation %s',
    async (reason) => {
      const { state, result } = await searchable();
      // This is a new source reply, never a mutation of an already admitted snapshot.
      state.criteriaChanged();
      result.kind = 'limited';
      result.reasons = [reason];
      result.modes[0].kind = 'limited';
      result.modes[0].reasons = [reason];
      await state.search();
      expect(state.captureSelection(result.items[0]) !== null).toBe(reason !== 'unrecognized-limit');
      state.dispose();
    },
  );
});

describe('Rail retained source receipt edges', () => {
  it('keeps a complete chosen mode eligible when the other mode fails, with warning intact', async () => {
    const { state, result } = await searchable((result) => {
      result.kind = 'limited';
      result.reasons = ['transport'];
      result.items = result.items.slice(0, 1);
      result.normalizedCount = 1;
      result.modes[1] = {
        transport: 'suburban',
        kind: 'failed',
        reasons: ['transport'],
        originalCount: 0,
        normalizedCount: 0,
        receivedAt: null,
      };
    });
    expect(state.captureSelection(result.items[0])?.isCurrent()).toBe(true);
    expect(state.result?.kind).toBe('limited');
    expect(state.result?.modes[1].kind).toBe('failed');
    state.dispose();
  });
  it('keeps an intact row eligible in a damaged mixed result using its original shorter expiry', async () => {
    const { state, result } = await searchable((result) => {
      result.items[1].tickets.damaged = true;
      result.items[1].tickets.electronicTicket = 'unknown';
      result.items[1].tickets.electronicTicketReason = 'invalid-indicator';
      result.expiresAt = new Date(Date.now() + 30000).toISOString();
    });
    const choice = state.captureSelection(result.items[0]);
    expect(choice?.isCurrent()).toBe(true);
    expect(state.captureSelection(result.items[1])).toBeNull();
    vi.setSystemTime(Date.parse(result.expiresAt!));
    expect(choice?.isCurrent()).toBe(false);
    state.dispose();
  });
  it('does not revive an old choice after criteria A-B-A and an identical subsequent reply', async () => {
    const { state, result } = await searchable();
    const old = state.captureSelection(result.items[0])!;
    state.date = '2027-01-01';
    state.criteriaChanged();
    state.date = result.request.departureDate;
    state.criteriaChanged();
    await state.search();
    expect(state.captureSelection(result.items[0])?.isCurrent()).toBe(true);
    expect(old.isCurrent()).toBe(false);
    state.dispose();
  });
  it.each([
    'reference-expiry',
    'reference-profile',
    'generation',
    'row-reception',
    'missing-row',
    'unsafe-attribution',
    'selected-mode-failure',
  ] as const)('refuses %s even before timer cleanup', async (fault) => {
    const { state, result } = await searchable();
    const choice = state.captureSelection(result.items[0])!;
    if (fault === 'reference-expiry') state.reference!.expiresAt = null;
    if (fault === 'reference-profile') state.reference!.profileEpoch = 'c'.repeat(32);
    if (fault === 'generation') state.reference!.catalogueGeneration = 'd'.repeat(32);
    if (fault === 'row-reception') result.items[0].receivedAt = new Date(Date.now() + 1001).toISOString();
    if (fault === 'missing-row') result.items[0] = structuredClone(result.items[0]);
    if (fault === 'unsafe-attribution') result.attribution!.frameUrl = 'https://other.example/frame';
    if (fault === 'selected-mode-failure') result.modes[0].kind = 'failed';
    expect(choice.isCurrent()).toBe(false);
    state.dispose();
  });
  it('clears a current read exception and rejects a late search after disposal', async () => {
    const { state, api } = await searchable();
    api.stations.mockRejectedValueOnce(new Error('read failed'));
    state.input('origin', 'CCC');
    await vi.advanceTimersByTimeAsync(300);
    expect(state.reference).toBeNull();
    state.dispose();
    expect(state.result).toBeNull();
  });
});

it('clears reference authority when a concurrent current field read throws after another field succeeds', async () => {
  vi.useFakeTimers();
  let success!: (r: RailSuggestions) => void, fail!: (error: Error) => void;
  const state = new RailState({
    stations: (q) =>
      q === 'AAA' ? new Promise((resolve) => (success = resolve)) : new Promise((_resolve, reject) => (fail = reject)),
    search: vi.fn(),
  });
  state.input('origin', 'AAA');
  state.input('destination', 'BBB');
  await vi.advanceTimersByTimeAsync(300);
  success(suggestions('s1'));
  await Promise.resolve();
  expect(state.reference).not.toBeNull();
  fail(new Error('unreadable current source'));
  await Promise.resolve();
  expect(state.reference).toBeNull();
  state.dispose();
});

it.each(['fictional', 'real'] as const)(
  'drops every state-owned expired %s admission while retaining the fresh reference and stations',
  async (profile) => {
    const { state, result, api } = await searchable(
      (result) => {
        result.profile = profile;
        if (profile === 'real')
          result.attribution!.frameUrl = 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html';
      },
      30 * 60 * 1000,
    );
    const reference = state.reference,
      from = state.origin.selected,
      to = state.destination.selected,
      choice = state.captureSelection(result.items[0]);
    expect(Reflect.get(state, 'admitted').resultJson).toContain('9007199254740993.05');
    if (profile === 'fictional') expect(choice?.isCurrent()).toBe(true);
    await vi.advanceTimersByTimeAsync(Date.parse(result.expiresAt!) - Date.now());
    expect(state.result).toBeNull();
    expect(Reflect.get(state, 'admitted')).toBeNull();
    expect(state.reference).toBe(reference);
    expect(state.origin.selected).toBe(from);
    expect(state.destination.selected).toBe(to);
    expect(Date.parse(state.reference!.expiresAt!)).toBeGreaterThan(Date.now());
    expect(api.search).toHaveBeenCalledTimes(1);
    expect(api.stations).toHaveBeenCalledTimes(2);
    expect(choice?.isCurrent() ?? false).toBe(false);
    const fresh = railFixture(Date.now()).result;
    fresh.profile = profile;
    if (profile === 'real') fresh.attribution!.frameUrl = 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html';
    api.search.mockResolvedValueOnce(fresh);
    await state.search();
    expect(state.result).toBe(fresh);
    expect(api.search).toHaveBeenCalledTimes(2);
    expect(choice?.isCurrent() ?? false).toBe(false);
    state.dispose();
  },
);

describe('Rail standalone details and Trips selection lifetimes', () => {
  it('delayed resume revokes expired search admission and aborts a pending route without rereading', async () => {
    let complete!: (reply: RailJourneyDetailsResult) => void;
    const signals: AbortSignal[] = [];
    const details = vi.fn((_request: unknown, signal: AbortSignal) => {
      signals.push(signal);
      return new Promise<RailJourneyDetailsResult>((resolve) => {
        complete = resolve;
      });
    });
    const { state, result, api } = await searchable(
      (reply) => {
        reply.expiresAt = new Date(Date.now() + 10000).toISOString();
      },
      30 * 60 * 1000,
      details,
    );
    const choice = state.captureSelection(result.items[0]);
    expect(choice?.isCurrent()).toBe(true);
    const pending = state.openDetails(result.items[0]);
    expect(state.detailKey).toBe(result.items[0].key);
    vi.setSystemTime(Date.parse(result.expiresAt!) + 1);
    state.resume();
    expect(state.result).toBeNull();
    expect(Reflect.get(state, 'admitted')).toBeNull();
    expect(state.detailKey).toBeNull();
    expect(signals[0].aborted).toBe(true);
    expect(choice?.isCurrent()).toBe(false);
    complete(railDetailsFixture(Date.now()).reply);
    await pending;
    expect(state.detail).toBeNull();
    expect(api.search).toHaveBeenCalledTimes(1);
    expect(api.stations).toHaveBeenCalledTimes(2);
    expect(details).toHaveBeenCalledTimes(1);
    state.dispose();
  });
  it('detail-only expiry retains the original fictional search selection without copying route facts or rereading', async () => {
    const details = vi.fn(async () => ({
      ...railDetailsFixture(Date.now()).reply,
      expiresAt: new Date(Date.now() + 1000).toISOString(),
    }));
    const { state, result, api } = await searchable(undefined, 30 * 60 * 1000, details);
    const before = JSON.stringify(result);
    const choice = state.captureSelection(result.items[0]);
    await state.openDetails(result.items[0]);
    expect(state.detail?.stops.length).toBeGreaterThan(0);
    vi.setSystemTime(Date.parse(state.detail!.expiresAt!) + 1);
    state.resume();
    expect(state.detail).toBeNull();
    expect(state.detailKey).toBeNull();
    expect(state.result).toBe(result);
    expect(choice?.isCurrent()).toBe(true);
    expect(JSON.stringify(result)).toBe(before);
    expect(api.search).toHaveBeenCalledTimes(1);
    expect(api.stations).toHaveBeenCalledTimes(2);
    expect(details).toHaveBeenCalledTimes(1);
    state.dispose();
  });
});
