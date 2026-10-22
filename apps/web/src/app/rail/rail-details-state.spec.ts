import type { RailJourneyDetailsResult, RailSuggestions } from '@travel/api-client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { railDetailsFixture } from '../../../test-fixtures/rail-details-fixture';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { RailState } from './rail-state';

function setup(details = vi.fn()) {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-10-20T12:00:00Z'));
  const f = railFixture(Date.now());
  const d = railDetailsFixture(Date.now());
  const search = vi.fn();
  const state = new RailState({ stations: vi.fn(), search, details });
  state.result = f.result;
  state.reference = {
    ...f.result,
    suggestions: [],
    hasMore: false,
    catalogueGeneration: f.request.catalogueGeneration,
    expiresAt: d.authority.expiresAt,
  } as RailSuggestions;
  return { state, f, d, search, details };
}
afterEach(() => vi.useRealTimers());
describe('Rail single detail authority', () => {
  it('an accepted new search resets sort and filters while expiry remains source-owned', async () => {
    const c = setup();
    c.search.mockResolvedValue(c.f.result);
    c.state.origin.selected = { reference: c.f.request.from, name: 'A', settlement: null, region: null };
    c.state.destination.selected = { reference: c.f.request.to, name: 'B', settlement: null, region: null };
    c.state.date = c.f.request.departureDate;
    c.state.setView({ sort: 'arrival', departure: 'night', carrier: { kind: 'unknown' } });
    await c.state.search();
    expect(c.state.view).toEqual({ sort: 'departure', departure: 'all', arrival: 'all', carrier: { kind: 'all' } });
    expect(c.state.result?.expiresAt).toBe(c.f.result.expiresAt);
    expect(c.search).toHaveBeenCalledTimes(1);
    expect(c.details).not.toHaveBeenCalled();
    c.state.dispose();
  });
  it('opens one bound panel and closes hidden selection; sort/reset do zero HTTP', async () => {
    const c = setup();
    c.details.mockResolvedValue(c.d.reply);
    await c.state.openDetails(c.f.result.items[0]);
    expect(c.state.detail?.kind).toBe('complete');
    c.state.setView({ sort: 'duration' });
    expect(c.state.detailKey).toBe(c.f.result.items[0].key);
    c.state.setView({ departure: 'night' });
    expect(c.state.detail).toBeNull();
    expect(c.state.detailKey).toBeNull();
    c.state.resetFilters();
    expect(c.state.visible.visibleCount).toBe(2);
    expect(c.state.view.sort).toBe('duration');
    expect(c.details).toHaveBeenCalledTimes(1);
    expect(c.search).not.toHaveBeenCalled();
    c.state.dispose();
  });
  it('late first A never applies after close/reopen A or criteria change', async () => {
    const c = setup();
    const pending: ((v: RailJourneyDetailsResult) => void)[] = [];
    const signals: AbortSignal[] = [];
    c.details.mockImplementation((_request, signal) => {
      signals.push(signal);
      return new Promise((resolve) => pending.push(resolve));
    });
    const a = c.state.openDetails(c.f.result.items[0]);
    c.state.closeDetails();
    const b = c.state.openDetails(c.f.result.items[1]);
    c.state.closeDetails();
    const again = c.state.openDetails(c.f.result.items[0]);
    expect(signals[0].aborted).toBe(true);
    expect(signals[1].aborted).toBe(true);
    pending[0](c.d.reply);
    await a;
    expect(c.state.detail).toBeNull();
    pending[2](c.d.reply);
    await again;
    expect(c.state.detail).not.toBeNull();
    c.state.criteriaChanged();
    expect(c.state.detail).toBeNull();
    pending[1](c.d.reply);
    await b;
    expect(c.state.detail).toBeNull();
    c.state.dispose();
  });
  it('earlier search expiry and delayed timer checks clear data before apply/open/resume', async () => {
    const c = setup();
    c.state.result!.expiresAt = new Date(Date.now() + 10000).toISOString();
    c.details.mockResolvedValue(c.d.reply);
    const fare = c.state.result!.items[0].tickets.fares[0].price.amount;
    await c.state.openDetails(c.f.result.items[0]);
    expect(c.state.result!.items[0].tickets.fares[0].price.amount).toBe(fare);
    vi.setSystemTime(Date.now() + 10001);
    c.state.resume();
    expect(c.state.detail).toBeNull();
    expect(c.state.result).toBeNull();
    await c.state.openDetails(c.f.result.items[0]);
    expect(c.details).toHaveBeenCalledTimes(1);
    c.state.dispose();
  });
  it('profile notice clears both contours, route failure retains usable search, attribution clears all', async () => {
    const c = setup();
    c.details.mockRejectedValue({ code: 'Rail.Profile.Changed' });
    await c.state.openDetails(c.f.result.items[0]);
    expect(c.state.result).toBeNull();
    expect(c.state.reference).toBeNull();
    c.state.dispose();
    const b = setup();
    b.details.mockRejectedValue(new Error('no route'));
    await b.state.openDetails(b.f.result.items[0]);
    expect(b.state.result).not.toBeNull();
    b.state.attributionFailed();
    expect(b.state.detail).toBeNull();
    expect(b.state.result).toBeNull();
    b.state.dispose();
  });
});
