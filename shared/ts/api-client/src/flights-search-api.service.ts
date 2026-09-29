import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { decodeFlightSearchResponse } from './flights-search.decoder';
import type { FlightSearchRequest, FlightSearchResponse } from './flights-search.types';

@Injectable({ providedIn: 'root' })
export class FlightsSearchApiService {
  private readonly http = inject(HttpClient);

  search(body: FlightSearchRequest): Observable<FlightSearchResponse> {
    return this.http
      .post<unknown>('/api/flights/search?currency=RUB', body, {
        headers: { 'Accept-Language': 'ru', 'Content-Type': 'application/json' },
      })
      .pipe(timeout(15_000), map(decodeFlightSearchResponse));
  }
}
