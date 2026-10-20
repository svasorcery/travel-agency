import type { RailSuggestions } from '@travel/api-client';
import { afterEach, describe, expect, it, vi } from 'vitest';
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
