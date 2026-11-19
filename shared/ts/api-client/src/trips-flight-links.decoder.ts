import { TripsContractError, tripDate, tripGuid, tripRecord, tripRevision } from './trips.decoder';
import type {
  FlightLinkCandidate,
  FlightLinkLeg,
  FlightLinkObservation,
  FlightLinkPage,
  FlightLinkStatus,
  TripFlightLinks,
} from './trips-flight-links.types';

function requireValue(condition: unknown): asserts condition {
  if (!condition) throw new TripsContractError();
}
export function flightLinkRevision(value: unknown): string {
  return value === '0' ? value : tripRevision(value);
}
export function flightLinksEnabled(value: unknown): boolean {
  const { enabled } = tripRecord(value, ['enabled']);
  requireValue(typeof enabled === 'boolean');
  return enabled;
}
function timestamp(value: unknown, utc = false): string {
  requireValue(
    typeof value === 'string' &&
      /^\d{4}-\d\d-\d\dT(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(
        value,
      ),
  );
  tripDate(value.slice(0, 10));
  requireValue(!utc || /(?:Z|\+00:00)$/.test(value));
  return value;
}
/** Exact instant of an already validated flight link timestamp, in 100 ns ticks. */
export function flightLinkInstant(value: string): bigint {
  const [_, base, fraction, zone] = /^(.*T\d\d:\d\d:\d\d)(?:\.(\d+))?(Z|[+-]\d\d:\d\d)$/.exec(value)!;
  return BigInt(Date.parse(base + zone)) * 10000n + BigInt((fraction ?? '').padEnd(7, '0'));
}
function leg(value: unknown): FlightLinkLeg {
  const { origin, destination, departAt, arriveAt } = tripRecord(value, [
    'origin',
    'destination',
    'departAt',
    'arriveAt',
  ]);
  requireValue(
    typeof origin === 'string' &&
      /^[A-Z]{3}$/.test(origin) &&
      typeof destination === 'string' &&
      /^[A-Z]{3}$/.test(destination),
  );
  const departure = timestamp(departAt),
    arrival = timestamp(arriveAt);
  const duration = flightLinkInstant(arrival) - flightLinkInstant(departure);
  requireValue(duration > 0n && duration < 2n * 86400n * 10000000n);
  return { origin, destination, departAt: departure, arriveAt: arrival };
}
const summaryKeys = ['orderId', 'bookingStatus', 'passengerCount', 'route'];
function summary(row: Record<string, unknown>): FlightLinkCandidate {
  const { bookingStatus, passengerCount, route } = row;
  requireValue(
    bookingStatus === 'Held' ||
      bookingStatus === 'Confirmed' ||
      bookingStatus === 'Ticketed' ||
      bookingStatus === 'Cancelled' ||
      bookingStatus === 'Refunded',
  );
  requireValue(
    typeof passengerCount === 'number' &&
      Number.isInteger(passengerCount) &&
      passengerCount >= 1 &&
      passengerCount <= 9,
  );
  requireValue(route === null || (Array.isArray(route) && route.length >= 1 && route.length <= 4));
  const legs = route?.map(leg) ?? null;
  requireValue(
    !legs ||
      legs.every((item, i) => i === 0 || flightLinkInstant(item.departAt) >= flightLinkInstant(legs[i - 1].arriveAt)),
  );
  return { orderId: tripGuid(row['orderId']), bookingStatus, passengerCount, route: legs };
}
function unique<T extends { orderId: string }>(items: T[]): T[] {
  requireValue(new Set(items.map((i) => i.orderId)).size === items.length);
  return items;
}
export function decodeFlightLinks(value: unknown, expectedId: string, etag: string | null): TripFlightLinks {
  const row = tripRecord(value, ['tripId', 'linksRevision', 'links']);
  const tripId = tripGuid(row['tripId']),
    linksRevision = flightLinkRevision(row['linksRevision']),
    links = row['links'];
  requireValue(
    tripId === expectedId &&
      etag === '"fl-' + linksRevision + '"' &&
      Array.isArray(links) &&
      links.length <= 4 &&
      BigInt(linksRevision) >= BigInt(links.length),
  );
  return {
    tripId,
    linksRevision,
    links: unique(
      links.map((v) => {
        const { orderId, linkedAt } = tripRecord(v, ['orderId', 'linkedAt']);
        return { orderId: tripGuid(orderId), linkedAt: timestamp(linkedAt, true) };
      }),
    ),
  };
}
export function decodeFlightCandidates(value: unknown, expectedId: string, offset: number): FlightLinkPage {
  const { tripId, items, nextOffset, limitReached } = tripRecord(value, [
    'tripId',
    'items',
    'nextOffset',
    'limitReached',
  ]);
  requireValue(
    tripGuid(tripId) === expectedId && Array.isArray(items) && items.length <= 20 && typeof limitReached === 'boolean',
  );
  requireValue(
    nextOffset === null || (nextOffset === offset + 20 && nextOffset <= 10000 && items.length === 20 && !limitReached),
  );
  requireValue(!limitReached || (items.length === 20 && offset + 20 > 10000));
  return {
    tripId: expectedId,
    items: unique(items.map((v) => summary(tripRecord(v, summaryKeys)))),
    nextOffset,
    limitReached,
  };
}
export function decodeFlightStatus(value: unknown, expectedId: string): FlightLinkStatus {
  const row = tripRecord(value, ['tripId', 'linksRevision', 'items']);
  const linksRevision = flightLinkRevision(row['linksRevision']),
    items = row['items'];
  requireValue(
    tripGuid(row['tripId']) === expectedId &&
      Array.isArray(items) &&
      items.length <= 4 &&
      BigInt(linksRevision) >= BigInt(items.length),
  );
  return {
    tripId: expectedId,
    linksRevision,
    items: unique(
      items.map((v): FlightLinkObservation => {
        if (v?.state === 'notFound' || v?.state === 'unavailable') {
          const { orderId } = tripRecord(v, ['orderId', 'state']);
          return { orderId: tripGuid(orderId), state: v.state };
        }
        const r = tripRecord(v, [...summaryKeys, 'state', 'reviewRequired', 'observedAt', 'streamVersion']);
        requireValue(r['state'] === 'available' && typeof r['reviewRequired'] === 'boolean');
        return {
          ...summary(r),
          state: 'available',
          reviewRequired: r['reviewRequired'],
          observedAt: timestamp(r['observedAt'], true),
          streamVersion: tripRevision(r['streamVersion']),
        };
      }),
    ),
  };
}
