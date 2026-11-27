import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { parseTripsJson, TripsContractError, tripBound, tripGuid, tripRecord } from './trips.decoder';
import type { TripBrief } from './trips.types';
import {
  decodeOptionEnvelope,
  decodeSavedTripOption,
  decodeTripOptionAvailability,
  type TripOptionAvailability,
} from './trips-option.decoder';

function options(token: string | null) {
  let headers = new HttpHeaders({ 'Cache-Control': 'no-store', 'Content-Type': 'application/json' });
  if (token !== null) headers = headers.set('Authorization', 'Bearer ' + token);
  return {
    headers,
    transferCache: false,
    cache: 'no-store' as const,
    responseType: 'text' as const,
    observe: 'response' as const,
  };
}
@Injectable({ providedIn: 'root' })
export class TripsOptionApiService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/trips/option-preview';
  availability(token: string | null) {
    return this.http.get(this.url + '/availability', options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeTripOptionAvailability(parseTripsJson(r.body, 2048));
      }),
    );
  }
  preview(brief: TripBrief, discovery: TripOptionAvailability, token: string | null) {
    const request = options(token);
    request.headers = request.headers.set('X-Trips-Option-Save', '1');
    return this.http.post(this.url, { brief }, request).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeOptionEnvelope(r.body, brief, discovery);
      }),
    );
  }
}
@Injectable({ providedIn: 'root' })
export class TripsSavedOptionsApiService {
  private readonly http = inject(HttpClient);
  save(selectionToken: string, title: string | undefined, expectedId: string, token: string | null) {
    const body = { selectionToken, ...(title !== undefined ? { title } : {}) };
    tripBound(body);
    return this.http.post('/api/trips/saved-options', body, options(token)).pipe(
      timeout(10000),
      map((r) => {
        if ((r.status !== 201 && r.status !== 200) || r.body === null) throw new TripsContractError();
        const id = tripGuid(tripRecord(parseTripsJson(r.body, 1024), ['tripId'])['tripId']);
        if (id !== expectedId) throw new TripsContractError();
        return id;
      }),
    );
  }
  read(id: string, token: string | null) {
    return this.http.get(`/api/trips/${tripGuid(id)}/saved-option`, options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeSavedTripOption(parseTripsJson(r.body, 128 * 1024));
      }),
    );
  }
}
