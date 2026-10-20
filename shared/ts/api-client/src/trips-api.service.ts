import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import {
  decodeTripCatalogue,
  decodeTripPage,
  decodeTripPlan,
  decodeTripWrite,
  parseTripsJson,
  TripsContractError,
  tripCreate,
  tripCursor,
  tripGuid,
  tripRevision,
} from './trips.decoder';
import type { CreateTrip, ReplaceTrip } from './trips.types';
@Injectable({ providedIn: 'root' })
export class TripsApiService {
  private readonly http = inject(HttpClient);
  private headers(token: string | null, revision?: string): HttpHeaders {
    let h = new HttpHeaders({ 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    if (token !== null) h = h.set('Authorization', 'Bearer ' + token);
    if (revision !== undefined) h = h.set('If-Match', '"' + tripRevision(revision) + '"');
    return h;
  }
  destinations(token: string | null) {
    return this.http
      .get('/api/trips/destinations', {
        headers: this.headers(token),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          if (response.status !== 200 || response.body === null) throw new TripsContractError();
          return decodeTripCatalogue(parseTripsJson(response.body, 10240));
        }),
      );
  }
  list(cursor: string | null, token: string | null) {
    let params = new HttpParams();
    if (cursor !== null) params = params.set('cursor', tripCursor(cursor)!);
    return this.http
      .get('/api/trips', {
        headers: this.headers(token),
        params,
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          if (response.status !== 200 || response.body === null) throw new TripsContractError();
          return decodeTripPage(parseTripsJson(response.body));
        }),
      );
  }
  detail(id: string, token: string | null) {
    const tripId = tripGuid(id);
    return this.http
      .get('/api/trips/' + tripId, {
        headers: this.headers(token),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => this.plan(response.status, response.body, response.headers.get('ETag'), 200, tripId)),
      );
  }
  create(body: CreateTrip, token: string | null) {
    const request = tripCreate(body);
    return this.http
      .request('POST', '/api/trips', {
        body: request,
        headers: this.headers(token),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          const plan = this.plan(response.status, response.body, response.headers.get('ETag'), 201);
          if (response.headers.get('Location') !== '/api/trips/' + plan.tripId) throw new TripsContractError();
          return plan;
        }),
      );
  }
  replace(id: string, revision: string, body: ReplaceTrip, token: string | null) {
    const tripId = tripGuid(id),
      request = decodeTripWrite(body);
    return this.http
      .request('PUT', '/api/trips/' + tripId, {
        body: request,
        headers: this.headers(token, revision),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => this.plan(response.status, response.body, response.headers.get('ETag'), 200, tripId)),
      );
  }
  delete(id: string, revision: string, token: string | null) {
    return this.http
      .request('DELETE', '/api/trips/' + tripGuid(id), {
        headers: this.headers(token, revision),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          if (response.status !== 204 || (response.body !== null && response.body !== ''))
            throw new TripsContractError();
        }),
      );
  }
  private plan(status: number, raw: string | null, etag: string | null, expectedStatus: number, id?: string) {
    if (status !== expectedStatus || raw === null) throw new TripsContractError();
    const plan = decodeTripPlan(parseTripsJson(raw), id);
    if (etag !== '"' + plan.revision + '"') throw new TripsContractError();
    return plan;
  }
}
