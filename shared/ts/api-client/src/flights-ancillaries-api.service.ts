import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import {
  ancillaryGuid,
  decodeFlightAncillaryCatalog,
  decodeFlightCreationStatus,
  FlightAncillaryContractError,
} from './flights-ancillaries.decoder';
import type {
  FlightAncillaryCatalog,
  FlightAncillaryCatalogRequest,
  FlightCreationStatus,
} from './flights-ancillaries.types';
import type { FlightQuoteResponse } from './flights-quote.types';

@Injectable({ providedIn: 'root' })
export class FlightsAncillariesApiService {
  private readonly http = inject(HttpClient);
  getCatalog(
    request: FlightAncillaryCatalogRequest,
    quote: FlightQuoteResponse,
    accessToken: string | null,
  ): Observable<FlightAncillaryCatalog> {
    if (
      request.aggregateId !== quote.aggregateId ||
      request.quoteRevision !== quote.binding.revision ||
      typeof request.includeSeats !== 'boolean'
    )
      throw new FlightAncillaryContractError('request');
    return this.http
      .post<unknown>('/api/flights/orders/ancillaries', request, { headers: this.headers(accessToken) })
      .pipe(
        timeout(15_000),
        map((value) => decodeFlightAncillaryCatalog(value, quote)),
      );
  }
  getCreation(aggregateId: string, accessToken: string | null): Observable<FlightCreationStatus> {
    if (!ancillaryGuid(aggregateId)) throw new FlightAncillaryContractError('aggregateId');
    return this.http
      .get<unknown>(`/api/flights/orders/${aggregateId}/creation`, { headers: this.headers(accessToken) })
      .pipe(
        timeout(15_000),
        map((value) => decodeFlightCreationStatus(value, aggregateId)),
      );
  }
  private headers(token: string | null): HttpHeaders {
    return token === null ? new HttpHeaders() : new HttpHeaders({ Authorization: `Bearer ${token}` });
  }
}
