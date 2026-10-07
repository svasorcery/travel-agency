import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { decodeFlightQuoteResponse, FlightQuoteContractError } from './flights-quote.decoder';
import type { FlightQuoteRequest, FlightQuoteResponse } from './flights-quote.types';

@Injectable({ providedIn: 'root' })
export class FlightsQuoteApiService {
  private readonly http = inject(HttpClient);

  quote(body: FlightQuoteRequest, accessToken: string | null = null): Observable<FlightQuoteResponse> {
    const passengerCount = body.passengerCount;
    return this.http
      .post<unknown>('/api/flights/orders/quote', body, {
        headers:
          accessToken === null
            ? new HttpHeaders({ 'Content-Type': 'application/json' })
            : new HttpHeaders({ 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` }),
      })
      .pipe(
        timeout(15_000),
        map((value) => {
          const quote = decodeFlightQuoteResponse(value);
          if (quote.binding.passengerCount !== passengerCount)
            throw new FlightQuoteContractError('binding.passengerCount');
          return quote;
        }),
      );
  }
}
