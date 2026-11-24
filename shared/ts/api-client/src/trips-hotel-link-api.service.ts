import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { catchError, map, TimeoutError, throwError, timeout } from 'rxjs';
import { TripsContractError, tripGuid } from './trips.decoder';
import {
  decodeHotelLink,
  decodeHotelLinkAvailability,
  decodeHotelLinkStatus,
  hotelLinkRevision,
} from './trips-hotel-link.decoder';
import type { TripHotelLink, TripHotelLinkOperation } from './trips-hotel-link.types';
import { parseTripItineraryJson } from './trips-itinerary.decoder';

type HotelLinkFailure =
  | 'authentication'
  | 'forbidden'
  | 'notFound'
  | 'occupied'
  | 'ineligible'
  | 'conflict'
  | 'invalid'
  | 'unavailable'
  | 'deadline'
  | 'transport'
  | 'contract';

export class HotelLinkRequestError extends Error {
  constructor(readonly kind: HotelLinkFailure) {
    super('Hotel link request failed');
  }
}

function conflictCode(error: HttpErrorResponse): HotelLinkFailure {
  try {
    if (error.error instanceof ArrayBuffer) {
      const body = parseTripItineraryJson(error.error, 4096) as { code?: unknown } | null;
      if (body?.code === 'Trips.HotelLinkOccupied') return 'occupied';
      if (body?.code === 'Trips.HotelLinkOrderIneligible') return 'ineligible';
    }
  } catch {
    return 'contract';
  }
  return 'contract';
}

function failure(error: unknown): HotelLinkRequestError {
  if (error instanceof TimeoutError) return new HotelLinkRequestError('deadline');
  if (!(error instanceof HttpErrorResponse)) return new HotelLinkRequestError('contract');
  switch (error.status) {
    case 0:
      return new HotelLinkRequestError('transport');
    case 401:
      return new HotelLinkRequestError('authentication');
    case 403:
      return new HotelLinkRequestError('forbidden');
    case 404:
      return new HotelLinkRequestError('notFound');
    case 409:
      return new HotelLinkRequestError(conflictCode(error));
    case 412:
      return new HotelLinkRequestError('conflict');
    case 504:
      return new HotelLinkRequestError('deadline');
    case 400:
    case 413:
    case 428:
      return new HotelLinkRequestError('invalid');
    default:
      return new HotelLinkRequestError('unavailable');
  }
}

@Injectable({ providedIn: 'root' })
export class TripsHotelLinkApiService {
  private readonly http = inject(HttpClient);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));

  availability(token: string) {
    return this.request('/api/trips/hotel-link/availability', token, 2048, decodeHotelLinkAvailability);
  }

  metadata(id: string, token: string) {
    return this.request(`/api/trips/${tripGuid(id)}/hotel-link`, token, 2048, decodeHotelLink);
  }

  status(id: string, token: string) {
    return this.request(`/api/trips/${tripGuid(id)}/hotel-link/status`, token, 32768, decodeHotelLinkStatus);
  }

  patch(id: string, saved: TripHotelLink, operation: TripHotelLinkOperation, token: string) {
    const body: TripHotelLinkOperation =
      operation.op === 'add' ? { op: 'add', reservationId: tripGuid(operation.reservationId) } : { op: 'remove' };
    const hasLink = body.op === 'add';
    const revision = hotelLinkRevision(saved.revision);
    return this.request(
      `/api/trips/${tripGuid(id)}/hotel-link`,
      token,
      2048,
      (value, etag) => {
        const next = decodeHotelLink(value, etag);
        if (
          next.hasLink !== hasLink ||
          BigInt(next.revision) !== BigInt(revision) + (saved.hasLink === hasLink ? 0n : 1n)
        )
          throw new TripsContractError();
        return next;
      },
      { body, revision },
    );
  }

  private request<T>(
    url: string,
    token: string,
    maxBytes: number,
    decode: (value: unknown, etag: string | null) => T,
    patch?: { body: TripHotelLinkOperation; revision: string },
  ) {
    if (!this.browser || !token) throw new HotelLinkRequestError('authentication');
    let headers = new HttpHeaders({ 'Cache-Control': 'no-store', Authorization: `Bearer ${token}` });
    if (patch) headers = headers.set('Content-Type', 'application/json').set('If-Match', `"hl-${patch.revision}"`);
    return this.http
      .request(patch ? 'PATCH' : 'GET', url, {
        headers,
        body: patch?.body,
        cache: 'no-store',
        transferCache: false,
        observe: 'response',
        responseType: 'arraybuffer',
      })
      .pipe(
        timeout(5500),
        map((response) => {
          if (response.status !== 200 || response.body === null) throw new TripsContractError();
          return decode(parseTripItineraryJson(response.body, maxBytes), response.headers.get('ETag'));
        }),
        catchError((error: unknown) => throwError(() => failure(error))),
      );
  }
}
