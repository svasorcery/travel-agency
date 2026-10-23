import { describe, expect, it } from 'vitest';
import { railFixture } from '../../../test-fixtures/rail-fixture';
import { defaultRailView, type RailTimeBucket, railView } from './rail-view';

describe('Rail local view', () => {
  it('orders by instants, keeps stable source ties and never changes exact fares', () => {
    const { result } = railFixture();
    const original = JSON.stringify(result);
    const a = result.items[0];
    const b = result.items[1];
    const items = [
      {
        ...a,
        key: 'a',
        departure: '2026-11-02T01:00:00+04:00',
        arrival: '2026-11-02T03:00:00+04:00',
        durationSeconds: 7200,
      },
      {
        ...b,
        key: 'b',
        departure: '2026-11-01T23:00:00+01:00',
        arrival: '2026-11-02T00:00:00+01:00',
        durationSeconds: 3600,
      },
      {
        ...a,
        key: 'c',
        departure: '2026-11-02T00:00:00+03:00',
        arrival: '2026-11-02T02:00:00+03:00',
        durationSeconds: 7200,
      },
    ];
    expect(railView(items, defaultRailView()).rows.map((x) => x.key)).toEqual(['a', 'c', 'b']);
    expect(railView(items, { ...defaultRailView(), sort: 'duration' }).rows.map((x) => x.key)).toEqual(['b', 'a', 'c']);
    expect(railView(items, { ...defaultRailView(), sort: 'arrival' }).rows.map((x) => x.key)).toEqual(['a', 'b', 'c']);
    expect(JSON.stringify(result)).toBe(original);
    expect(railView(items, defaultRailView()).rows[0].tickets.fares[0].price.amount).toBe('9007199254740993.05');
  });
  it.each([
    ['00:00:00', 'night'],
    ['05:59:59', 'night'],
    ['06:00:00', 'morning'],
    ['12:00:00', 'afternoon'],
    ['18:00:00', 'evening'],
    ['23:59:59', 'evening'],
  ] as const)('uses half-open local buckets at %s', (time, bucket) => {
    const item = railFixture().result.items[0];
    for (const candidate of ['night', 'morning', 'afternoon', 'evening'] as RailTimeBucket[])
      expect(
        railView([{ ...item, departure: `2026-11-01T${time}+14:00` }], { ...defaultRailView(), departure: candidate })
          .visibleCount,
      ).toBe(candidate === bucket ? 1 : 0);
  });
  it('uses AND and whole snapshot carrier options, distinguishing unknown from literal label', () => {
    const item = railFixture().result.items[0];
    const items = [null, '', ' ', 'Не указан', 'A', 'a'].map((carrier, i) => ({ ...item, key: `${i}`, carrier }));
    const view = railView(items, { ...defaultRailView(), departure: 'night', carrier: { kind: 'exact', value: 'A' } });
    expect(view.visibleCount).toBe(0);
    expect(view.receivedCount).toBe(6);
    expect(view.carriers).toEqual(['Не указан', 'A', 'a']);
    expect(view.hasUnknown).toBe(true);
    expect(railView(items, { ...defaultRailView(), carrier: { kind: 'unknown' } }).visibleCount).toBe(3);
    expect(railView(items, { ...defaultRailView(), carrier: { kind: 'exact', value: 'Не указан' } }).visibleCount).toBe(
      1,
    );
  });
});
