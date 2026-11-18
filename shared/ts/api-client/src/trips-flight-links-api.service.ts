import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { catchError, map, TimeoutError, throwError, timeout } from 'rxjs';
import { TripsContractError, tripGuid } from './trips.decoder';
import {
  decodeFlightCandidates,
  decodeFlightLinks,
  decodeFlightStatus,
  flightLinkRevision,
  flightLinksEnabled,
} from './trips-flight-links.decoder';
import type { FlightLinkOperation, TripFlightLinks } from './trips-flight-links.types';
import { parseTripItineraryJson } from './trips-itinerary.decoder';

type FlightLinkFailure =
  | 'authentication'
  | 'notFound'
  | 'limit'
  | 'conflict'
  | 'invalid'
  | 'unavailable'
  | 'deadline'
  | 'transport'
  | 'contract';
export class FlightLinkRequestError extends Error {
  constructor(readonly kind: FlightLinkFailure) {
    super('Flight links request failed');
  }
}
function failure(e: unknown): FlightLinkRequestError {
  if (e instanceof TimeoutError) return new FlightLinkRequestError('deadline');
  if (!(e instanceof HttpErrorResponse)) return new FlightLinkRequestError('contract');
  switch (e.status) {
    case 0:
      return new FlightLinkRequestError('transport');
    case 401:
    case 403:
      return new FlightLinkRequestError('authentication');
    case 404:
      return new FlightLinkRequestError('notFound');
    case 409:
      return new FlightLinkRequestError('limit');
    case 412:
      return new FlightLinkRequestError('conflict');
    case 504:
      return new FlightLinkRequestError('deadline');
    case 400:
    case 413:
    case 428:
      return new FlightLinkRequestError('invalid');
    default:
      return new FlightLinkRequestError('unavailable');
  }
}
@Injectable({ providedIn: 'root' })
export class TripsFlightLinksApiService {
  private readonly http = inject(HttpClient);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  private request<T>(
    url: string,
    token: string,
    max: number,
    decode: (value: unknown, etag: string | null) => T,
    patch?: { op: FlightLinkOperation; orderId: string; revision: string },
  ) {
    if (!this.browser || !token) throw new FlightLinkRequestError('authentication');
    let headers = new HttpHeaders({ 'Cache-Control': 'no-store', Authorization: 'Bearer ' + token });
    if (patch)
      headers = headers
        .set('Content-Type', 'application/json')
        .set('If-Match', '"fl-' + flightLinkRevision(patch.revision) + '"');
    return this.http
      .request(patch ? 'PATCH' : 'GET', url, {
        headers,
        body: patch ? { op: patch.op, orderId: patch.orderId } : undefined,
        cache: 'no-store',
        transferCache: false,
        observe: 'response',
        responseType: 'arraybuffer',
      })
      .pipe(
        timeout(5500),
        map((r) => {
          if (r.status !== 200 || r.body === null) throw new TripsContractError();
          return decode(parseTripItineraryJson(r.body, max), r.headers.get('ETag'));
        }),
        catchError((e: unknown) => throwError(() => failure(e))),
      );
  }
  availability(token: string) {
    return this.request('/api/trips/flight-links/availability', token, 2048, flightLinksEnabled);
  }
  metadata(id: string, token: string) {
    const tripId = tripGuid(id);
    return this.request('/api/trips/' + tripId + '/flight-links', token, 16384, (v, e) =>
      decodeFlightLinks(v, tripId, e),
    );
  }
  status(id: string, token: string) {
    const tripId = tripGuid(id);
    return this.request('/api/trips/' + tripId + '/flight-links/status', token, 32768, (v) =>
      decodeFlightStatus(v, tripId),
    );
  }
  candidates(id: string, offset: number, token: string) {
    const tripId = tripGuid(id);
    if (!Number.isInteger(offset) || offset < 0 || offset > 10000) throw new TripsContractError();
    return this.request('/api/trips/' + tripId + '/flight-order-candidates?offset=' + offset, token, 65536, (v) =>
      decodeFlightCandidates(v, tripId, offset),
    );
  }
  patch(saved: TripFlightLinks, op: FlightLinkOperation, id: string, token: string) {
    const tripId = tripGuid(saved.tripId),
      orderId = tripGuid(id);
    if (op !== 'add' && op !== 'remove') throw new TripsContractError();
    const previous = saved.links,
      exists = previous.some((link) => link.orderId === orderId);
    const changed = op === 'add' ? !exists : exists;
    return this.request(
      '/api/trips/' + tripId + '/flight-links',
      token,
      16384,
      (v, e) => {
        const result = decodeFlightLinks(v, tripId, e);
        const retained = previous.filter((link) => !(op === 'remove' && link.orderId === orderId));
        const appended = changed && op === 'add';
        if (
          BigInt(result.linksRevision) !== BigInt(saved.linksRevision) + (changed ? 1n : 0n) ||
          result.links.length !== retained.length + Number(appended) ||
          retained.some(
            (old, i) => result.links[i].orderId !== old.orderId || result.links[i].linkedAt !== old.linkedAt,
          ) ||
          (appended && result.links.at(-1)?.orderId !== orderId)
        )
          throw new TripsContractError();
        return result;
      },
      { op, orderId, revision: saved.linksRevision },
    );
  }
}
