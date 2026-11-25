import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import {
  decodeTripCatalogue,
  decodeTripPage,
  decodeTripPlan,
  decodeTripWorkspacePlan,
  decodeTripWorkspaceWrite,
  parseTripsJson,
  TripsContractError,
  tripCreate,
  tripCursor,
  tripGuid,
  tripRevision,
} from './trips.decoder';
import type { CreateTrip, ReplaceTripWorkspace, TripPlan } from './trips.types';
import { parseTripItineraryJson } from './trips-itinerary.decoder';
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
      .get('/api/trips/' + tripId + '/workspace-v2', {
        headers: this.headers(token),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) =>
          this.plan(response.status, response.body, response.headers.get('ETag'), 200, this.workspacePlan, tripId),
        ),
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
          const plan = this.plan(response.status, response.body, response.headers.get('ETag'), 201, (raw) =>
            decodeTripPlan(parseTripsJson(raw)),
          );
          if (response.headers.get('Location') !== '/api/trips/' + plan.tripId) throw new TripsContractError();
          return plan;
        }),
      );
  }
  replace(id: string, revision: string, body: ReplaceTripWorkspace, token: string | null) {
    const tripId = tripGuid(id),
      request = decodeTripWorkspaceWrite(body);
    return this.http
      .request('PUT', '/api/trips/' + tripId + '/workspace-v2', {
        body: request,
        headers: this.headers(token, revision),
        observe: 'response',
        responseType: 'text',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) =>
          this.plan(response.status, response.body, response.headers.get('ETag'), 200, this.workspacePlan, tripId),
        ),
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
  private workspacePlan(raw: string, id?: string) {
    try {
      return decodeTripWorkspacePlan(parseTripItineraryJson(raw), id);
    } catch {
      throw new TripsContractError();
    }
  }
  private plan<T extends TripPlan>(
    status: number,
    raw: string | null,
    etag: string | null,
    expectedStatus: number,
    decode: (raw: string, id?: string) => T,
    id?: string,
  ): T {
    if (status !== expectedStatus || raw === null) throw new TripsContractError();
    const plan = decode(raw, id);
    if (etag !== '"' + plan.revision + '"') throw new TripsContractError();
    return plan;
  }
}
