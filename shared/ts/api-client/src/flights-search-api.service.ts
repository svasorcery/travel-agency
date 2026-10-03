import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import { decodeFlightSearchResponse, FlightSearchContractError } from './flights-search.decoder';
import type { FlightSearchRequest, FlightSearchResponse, FlightSearchV2Request } from './flights-search.types';

@Injectable({ providedIn: 'root' })
export class FlightsSearchApiService {
  private readonly http = inject(HttpClient);

  searchMultiLeg(body: FlightSearchV2Request): Observable<FlightSearchResponse> {
    const criteria = { ...body, legs: body.legs.map((leg) => ({ ...leg })) };
    return this.http
      .post<unknown>('/api/flights/search/v2?currency=RUB', criteria, {
        headers: { 'Accept-Language': 'ru', 'Content-Type': 'application/json' },
      })
      .pipe(
        timeout(15_000),
        map((value) => {
          const response = decodeFlightSearchResponse(value);
          for (const offer of response.offers) {
            if (
              offer.providerOfferRef === null ||
              offer.passengerCount !== criteria.passengerCount ||
              offer.itinerary.slices.length !== criteria.legs.length
            )
              throw new FlightSearchContractError('requestedJourney');
            let previousArrival = -Infinity;
            let total = 0;
            offer.itinerary.slices.forEach((slice, index) => {
              const requested = criteria.legs[index];
              const first = slice.segments[0];
              const last = slice.segments[slice.segments.length - 1];
              if (
                slice.origin !== requested.origin ||
                slice.destination !== requested.destination ||
                first.departAt.slice(0, 10) !== requested.departureDate
              )
                throw new FlightSearchContractError(`requestedJourney.legs[${index}]`);
              for (const segment of slice.segments) {
                const departure = Date.parse(segment.departAt);
                const arrival = Date.parse(segment.arriveAt);
                if (departure < previousArrival || arrival <= departure || segment.cabinClass !== criteria.cabinClass)
                  throw new FlightSearchContractError(`requestedJourney.legs[${index}].segments`);
                previousArrival = arrival;
              }
              const elapsed = Date.parse(last.arriveAt) - Date.parse(first.departAt);
              if (elapsed >= 48 * 3600_000 || Math.abs(durationMilliseconds(slice.duration) - elapsed) >= 1)
                throw new FlightSearchContractError(`requestedJourney.legs[${index}].duration`);
              total += elapsed;
            });
            if (total >= 192 * 3600_000 || Math.abs(durationMilliseconds(offer.itinerary.totalDuration) - total) >= 1)
              throw new FlightSearchContractError('requestedJourney.totalDuration');
          }
          return response;
        }),
      );
  }

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

function durationMilliseconds(value: string): number {
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?$/.exec(value);
  if (!match) throw new FlightSearchContractError('duration');
  return (
    ((Number(match[1] ?? 0) * 24 + Number(match[2])) * 3600 +
      Number(match[3]) * 60 +
      Number(match[4]) +
      Number(`0.${match[5] ?? 0}`)) *
    1000
  );
}
