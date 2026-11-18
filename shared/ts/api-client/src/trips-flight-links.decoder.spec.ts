import { describe, expect, it } from 'vitest';
import { decodeFlightCandidates, decodeFlightLinks, decodeFlightStatus } from './trips-flight-links.decoder';

const tripId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const orderId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const linkedAt = '2026-11-17T01:02:03.4567890Z';
const leg = {
  origin: 'LED',
  destination: 'SVO',
  departAt: '2026-11-18T01:15:00.1234567+03:00',
  arriveAt: '2026-11-18T01:15:00.1234568+03:00',
};
const candidate = { orderId, route: [leg], bookingStatus: 'Held', passengerCount: 1 };
const page = { tripId, items: [candidate], nextOffset: null, limitReached: false };
const observation = {
  ...candidate,
  state: 'available',
  observedAt: linkedAt,
  streamVersion: '9223372036854775807',
  reviewRequired: true,
};

describe('flight links strict wire representations', () => {
  it.each(['0', '11', '9223372036854775807'])('reads exact empty collection revision %s', (linksRevision) => {
    const value = { tripId, linksRevision, links: [] };
    expect(decodeFlightLinks(value, tripId, '"fl-' + linksRevision + '"')).toEqual(value);
  });
  it('rejects mismatched trip, revision, tags, nonempty virtual zero, duplicate IDs and fifth item', () => {
    const value = { tripId, linksRevision: '1', links: [{ orderId, linkedAt }] };
    for (const tag of [null, '"1"', 'W/"fl-1"', '"fl-2"', '"fl-1", "fl-1"'])
      expect(() => decodeFlightLinks(value, tripId, tag)).toThrow();
    for (const revision of ['00', '-1', '9223372036854775808', 1])
      expect(() => decodeFlightLinks({ ...value, linksRevision: revision }, tripId, '"fl-' + revision + '"')).toThrow();
    expect(() => decodeFlightLinks(value, orderId, '"fl-1"')).toThrow();
    expect(() => decodeFlightLinks({ ...value, linksRevision: '0' }, tripId, '"fl-0"')).toThrow();
    for (const count of [2, 5])
      expect(() =>
        decodeFlightLinks({ ...value, linksRevision: '9', links: Array(count).fill(value.links[0]) }, tripId, '"fl-9"'),
      ).toThrow();
  });
  it('retains exact offset, precision and all legs; rejects malformed facts', () => {
    expect(decodeFlightCandidates(page, tripId, 0)).toEqual(page);
    for (const route of [
      [],
      Array(5).fill(leg),
      [{ ...leg, departAt: leg.arriveAt }],
      [{ ...leg, origin: 'ZZ' }],
      [{ ...leg, arriveAt: '2026-02-31T00:00:00Z' }],
    ]) {
      expect(() => decodeFlightCandidates({ ...page, items: [{ ...candidate, route }] }, tripId, 0)).toThrow();
    }
    expect(
      decodeFlightCandidates({ ...page, items: [{ ...candidate, route: null }] }, tripId, 0).items[0].route,
    ).toBeNull();
    expect(() => decodeFlightCandidates({ ...page, nextOffset: 20 }, tripId, 0)).toThrow();
  });
  it('separates partial observations from empty and rejects private facts in unavailable outcomes', () => {
    const second = { orderId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', state: 'unavailable' };
    const status = { tripId, linksRevision: '2', items: [observation, second] };
    expect(decodeFlightStatus(status, tripId)).toEqual(status);
    expect(() => decodeFlightStatus({ ...status, items: [{ ...second, bookingStatus: 'Held' }] }, tripId)).toThrow();
    expect(() => decodeFlightStatus({ ...status, items: [{ ...observation, streamVersion: '0' }] }, tripId)).toThrow();
  });
});
