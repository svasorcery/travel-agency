import type { RailJourney } from '@travel/api-client';
export type RailTimeBucket = 'all' | 'night' | 'morning' | 'afternoon' | 'evening';
export type RailSort = 'departure' | 'arrival' | 'duration';
export interface RailViewControls {
  sort: RailSort;
  departure: RailTimeBucket;
  arrival: RailTimeBucket;
  carrier: { kind: 'all' } | { kind: 'unknown' } | { kind: 'exact'; value: string };
}
export const defaultRailView = (): RailViewControls => ({
  sort: 'departure',
  departure: 'all',
  arrival: 'all',
  carrier: { kind: 'all' },
});
export function railView(items: readonly RailJourney[], controls: RailViewControls) {
  const matches = (value: string, bucket: RailTimeBucket) =>
    bucket === 'all' ||
    ['night', 'morning', 'afternoon', 'evening'][Math.floor(Number(value.slice(11, 13)) / 6)] === bucket;
  const unknown = (value: string | null) => value === null || value.trim() === '';
  const rows = items
    .map((item, index) => ({ item, index }))
    .filter(
      ({ item }) =>
        matches(item.departure, controls.departure) &&
        matches(item.arrival, controls.arrival) &&
        (controls.carrier.kind === 'all' ||
          (controls.carrier.kind === 'unknown' ? unknown(item.carrier) : item.carrier === controls.carrier.value)),
    )
    .sort(
      (a, b) =>
        (controls.sort === 'duration'
          ? a.item.durationSeconds - b.item.durationSeconds
          : Date.parse(a.item[controls.sort]) - Date.parse(b.item[controls.sort])) || a.index - b.index,
    )
    .map((x) => x.item);
  return {
    rows,
    receivedCount: items.length,
    visibleCount: rows.length,
    carriers: [...new Set(items.map((x) => x.carrier).filter((x): x is string => !unknown(x)))],
    hasUnknown: items.some((x) => unknown(x.carrier)),
  };
}
