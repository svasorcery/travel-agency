import { isPlatformBrowser } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, PLATFORM_ID } from '@angular/core';
import { catchError, map, TimeoutError, throwError, timeout } from 'rxjs';
import { TripsContractError } from './trips.decoder';
import {
  decodeTripBriefCandidate,
  decodeTripBriefCapability,
  parseTripBriefJson,
  tripBriefRequest,
} from './trips-brief.decoder';
import type { TripBriefRequest } from './trips-brief.types';
export class TripsBriefRequestError extends Error {
  constructor(
    readonly kind:
      | 'disabled'
      | 'unauthenticated'
      | 'forbidden'
      | 'invalid'
      | 'oversize'
      | 'busy'
      | 'deadline'
      | 'contract-error'
      | 'unavailable',
  ) {
    super('Brief extraction ' + kind);
  }
}
@Injectable({ providedIn: 'root' })
export class TripsBriefApiService {
  private readonly http = inject(HttpClient);
  private readonly platform = inject(PLATFORM_ID);
  private options(token: string | null) {
    let headers = new HttpHeaders({ 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    if (token !== null) headers = headers.set('Authorization', 'Bearer ' + token);
    return {
      headers,
      observe: 'response' as const,
      responseType: 'text' as const,
      transferCache: false,
      cache: 'no-store' as const,
    };
  }
  capability(token: string | null) {
    return this.http.get('/api/trips/brief-extraction-capability', this.options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeTripBriefCapability(parseTripBriefJson(r.body, 2048));
      }),
      catchError((e) => throwError(() => this.safe(e))),
    );
  }
  candidate(input: TripBriefRequest, token: string | null) {
    if (!isPlatformBrowser(this.platform)) throw new TripsBriefRequestError('unavailable');
    const request = tripBriefRequest(input);
    return this.http.post('/api/trips/brief-candidate', request, this.options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeTripBriefCandidate(parseTripBriefJson(r.body, 16384), request);
      }),
      catchError((e) => throwError(() => this.safe(e))),
    );
  }
  private safe(e: unknown): TripsBriefRequestError {
    if (e instanceof TripsContractError) return new TripsBriefRequestError('contract-error');
    if (e instanceof TimeoutError) return new TripsBriefRequestError('deadline');
    const status = e instanceof HttpErrorResponse ? e.status : 0;
    return new TripsBriefRequestError(
      (
        {
          400: 'invalid',
          401: 'unauthenticated',
          403: 'forbidden',
          404: 'disabled',
          413: 'oversize',
          429: 'busy',
          504: 'deadline',
        } as const
      )[status as 400] ?? 'unavailable',
    );
  }
}
