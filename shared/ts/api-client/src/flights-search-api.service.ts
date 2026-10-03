import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { decodeFlightSearchResponse, FlightSearchContractError } from './flights-search.decoder';
import type { FlightSearchRequest, FlightSearchResponse } from './flights-search.types';

@Injectable({ providedIn: 'root' })
export class FlightsSearchApiService {
  private readonly http = inject(HttpClient);

  search(body: FlightSearchRequest): Observable<FlightSearchResponse> {
    const passengerCount = body.passengerCount;
    return this.http
      .post<unknown>('/api/flights/search?currency=RUB', body, {
        headers: { 'Accept-Language': 'ru', 'Content-Type': 'application/json' },
      })
      .pipe(
        timeout(15_000),
        map((value) => {
          const response = decodeFlightSearchResponse(value);
          if (
            response.offers.some((offer) =>
              offer.providerOfferRef === null ? passengerCount !== 1 : offer.passengerCount !== passengerCount,
            )
          )
            throw new FlightSearchContractError('passengerCount');
          return response;
        }),
      );
  }
}
