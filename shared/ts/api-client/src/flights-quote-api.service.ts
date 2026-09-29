import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { decodeFlightQuoteResponse } from './flights-quote.decoder';
import type { FlightQuoteRequest, FlightQuoteResponse } from './flights-quote.types';

@Injectable({ providedIn: 'root' })
export class FlightsQuoteApiService {
  private readonly http = inject(HttpClient);

  quote(body: FlightQuoteRequest): Observable<FlightQuoteResponse> {
    return this.http
      .post<unknown>('/api/flights/orders/quote', body, { headers: { 'Content-Type': 'application/json' } })
      .pipe(timeout(15_000), map(decodeFlightQuoteResponse));
  }
}
