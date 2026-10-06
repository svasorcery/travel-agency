import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import {
  cancellationGuid,
  decodeCancellationStatus,
  FlightCancellationContractError,
} from './flights-cancellation.decoder';
import type { CancellationStatusResponse } from './flights-cancellation.types';

@Injectable({ providedIn: 'root' })
export class FlightsCancellationApiService {
  private readonly http = inject(HttpClient);
  status(aggregateId: string, token: string | null): Observable<CancellationStatusResponse> {
    const id = cancellationGuid(aggregateId);
    return this.http.get<unknown>('/api/flights/orders/' + id + '/cancellation', { headers: this.headers(token) }).pipe(
      timeout(15_000),
      map((value) => decodeCancellationStatus(value, id)),
    );
  }
  prepare(rawBody: string, token: string | null): Observable<CancellationStatusResponse> {
    return this.post('prepare', rawBody, token);
  }
  consent(rawBody: string, token: string | null): Observable<CancellationStatusResponse> {
    return this.post('consent', rawBody, token);
  }
  abandon(rawBody: string, token: string | null): Observable<CancellationStatusResponse> {
    return this.post('abandon', rawBody, token);
  }
  refresh(rawBody: string, token: string | null): Observable<CancellationStatusResponse> {
    return this.post('refresh', rawBody, token);
  }
  private post(stage: string, rawBody: string, token: string | null): Observable<CancellationStatusResponse> {
    let value: unknown;
    try {
      value = JSON.parse(rawBody);
    } catch {
      throw new FlightCancellationContractError('request');
    }
    if (value === null || typeof value !== 'object' || !('aggregateId' in value))
      throw new FlightCancellationContractError('request');
    const id = cancellationGuid(value.aggregateId);

    return this.http
      .post<unknown>('/api/flights/cancellations/' + stage, rawBody, { headers: this.headers(token) })
      .pipe(
        timeout(15_000),
        map((value) => decodeCancellationStatus(value, id)),
      );
  }
  private headers(token: string | null): HttpHeaders {
    const headers = new HttpHeaders({ 'Content-Type': 'application/json' });
    return token === null ? headers : headers.set('Authorization', 'Bearer ' + token);
  }
}
