import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import {
  decodeCancelledOrderResponse,
  decodeConfirmedOrderResponse,
  decodeFlightOrderListResponse,
  decodeFlightOrderResponse,
  decodeHeldOrderResponse,
  FlightBookingContractError,
} from './flights-booking.decoder';
import type {
  CancelledFlightOrderResponse,
  ConfirmedFlightOrderResponse,
  ConfirmFlightOrderRequest,
  FlightOrderListResponse,
  FlightOrderResponse,
  HeldFlightOrderResponse,
  HoldFlightOrderRequest,
} from './flights-booking.types';

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

@Injectable({ providedIn: 'root' })
export class FlightsBookingApiService {
  private readonly http = inject(HttpClient);

  listOrders(offset: number, accessToken: string | null): Observable<FlightOrderListResponse> {
    if (!Number.isSafeInteger(offset) || offset < 0 || offset % 20 !== 0 || offset > 2_147_483_647) {
      throw new FlightBookingContractError('offset');
    }
    const headers =
      accessToken === null ? new HttpHeaders() : new HttpHeaders({ Authorization: `Bearer ${accessToken}` });
    return this.http.get<unknown>(`/api/flights/orders?limit=21&offset=${offset}`, { headers }).pipe(
      timeout(15_000),
      map((response) => decodeFlightOrderListResponse(response, offset)),
    );
  }

  getOrder(aggregateId: string, accessToken: string | null): Observable<FlightOrderResponse> {
    if (
      !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(aggregateId) ||
      aggregateId === '00000000-0000-0000-0000-000000000000'
    ) {
      throw new FlightBookingContractError('aggregateId');
    }
    const headers =
      accessToken === null ? new HttpHeaders() : new HttpHeaders({ Authorization: `Bearer ${accessToken}` });
    return this.http.get<unknown>(`/api/flights/orders/${aggregateId}`, { headers }).pipe(
      timeout(15_000),
      map((response) => decodeFlightOrderResponse(response, aggregateId)),
    );
  }

  hold(
    body: HoldFlightOrderRequest,
    idempotencyKey: string,
    accessToken: string | null,
  ): Observable<HeldFlightOrderResponse> {
    this.validateIdempotencyKey(idempotencyKey);
    if (body.passengers.length !== 1) throw new FlightBookingContractError('passengers');
    return this.holdRaw(JSON.stringify(body), body.aggregateId, idempotencyKey, accessToken);
  }

  /** Dispatch the already frozen tab-local hold bytes without reserializing a mutable draft. */
  holdRaw(
    rawBody: string,
    aggregateId: string,
    idempotencyKey: string,
    accessToken: string | null,
  ): Observable<HeldFlightOrderResponse> {
    this.validateIdempotencyKey(idempotencyKey);
    return this.http
      .post<unknown>('/api/flights/orders/hold', rawBody, {
        headers: this.headers(idempotencyKey, accessToken),
      })
      .pipe(
        timeout(15_000),
        map((response) => decodeHeldOrderResponse(response, aggregateId)),
      );
  }

  confirm(
    body: ConfirmFlightOrderRequest,
    idempotencyKey: string,
    accessToken: string | null,
  ): Observable<ConfirmedFlightOrderResponse> {
    this.validateIdempotencyKey(idempotencyKey);
    return this.http
      .post<unknown>('/api/flights/orders/confirm', JSON.stringify(body), {
        headers: this.headers(idempotencyKey, accessToken),
      })
      .pipe(
        timeout(15_000),
        map((response) => decodeConfirmedOrderResponse(response, body.aggregateId)),
      );
  }

  private headers(idempotencyKey: string, accessToken: string | null): HttpHeaders {
    let headers = new HttpHeaders({
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    });
    if (accessToken !== null) headers = headers.set('Authorization', `Bearer ${accessToken}`);
    return headers;
  }

  cancel(
    aggregateId: string,
    idempotencyKey: string,
    accessToken: string | null,
  ): Observable<CancelledFlightOrderResponse> {
    this.validateIdempotencyKey(idempotencyKey);
    const id = aggregateId.toLowerCase();
    if (!/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(id) || id === '00000000-0000-0000-0000-000000000000') {
      throw new FlightBookingContractError('aggregateId');
    }
    return this.http
      .post<unknown>(`/api/flights/orders/${id}/cancel`, null, { headers: this.headers(idempotencyKey, accessToken) })
      .pipe(
        timeout(15_000),
        map((response) => decodeCancelledOrderResponse(response, id)),
      );
  }

  private validateIdempotencyKey(value: string): void {
    if (!UUID_V4.test(value)) throw new FlightBookingContractError('idempotencyKey');
  }
}
