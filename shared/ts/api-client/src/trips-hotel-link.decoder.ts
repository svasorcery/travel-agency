import { TripsContractError, tripDate, tripGuid, tripRecord, tripRevision, tripText } from './trips.decoder';
import type { TripHotelLink, TripHotelLinkAvailability, TripHotelLinkStatus } from './trips-hotel-link.types';

export function hotelLinkRevision(value: unknown): string {
  return value === '0' ? value : tripRevision(value);
}

export function decodeHotelLinkAvailability(value: unknown): TripHotelLinkAvailability {
  const { enabled, canAttach } = tripRecord(value, ['enabled', 'canAttach']);
  if (typeof enabled !== 'boolean' || typeof canAttach !== 'boolean' || (!enabled && canAttach))
    throw new TripsContractError();
  return { enabled, canAttach };
}

export function decodeHotelLink(value: unknown, etag: string | null): TripHotelLink {
  const { hasLink, revision: rawRevision } = tripRecord(value, ['hasLink', 'revision']);
  const revision = hotelLinkRevision(rawRevision);
  if (typeof hasLink !== 'boolean' || (hasLink && revision === '0') || etag !== `"hl-${revision}"`)
    throw new TripsContractError();
  return { hasLink, revision };
}

export function decodeHotelLinkStay(property: unknown, start: unknown, end: unknown) {
  const propertyName = tripText(property, 4096);
  const checkIn = tripDate(start);
  const checkOut = tripDate(end);
  if (propertyName.length > 4096 || checkOut <= checkIn) throw new TripsContractError();
  return { propertyName, checkIn, checkOut };
}

export function decodeHotelLinkStatus(value: unknown): TripHotelLinkStatus {
  const keys = ['revision', 'state'];
  const fields = ['reservationId', 'propertyName', 'checkIn', 'checkOut'];
  const { revision: rawRevision, state } = tripRecord(value, keys, fields);
  const revision = hotelLinkRevision(rawRevision);
  if (state === 'confirmed') {
    const { reservationId, propertyName, checkIn, checkOut } = tripRecord(value, [...keys, ...fields]);
    if (revision === '0') throw new TripsContractError();
    return {
      revision,
      state,
      reservationId: tripGuid(reservationId),
      ...decodeHotelLinkStay(propertyName, checkIn, checkOut),
    };
  }
  tripRecord(value, keys);
  if (
    state === 'empty' ||
    (revision !== '0' && (state === 'cancelled' || state === 'reviewRequired' || state === 'unavailable'))
  )
    return { revision, state };
  throw new TripsContractError();
}
