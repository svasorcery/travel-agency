import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { decodeHotelRates, decodeHotelReservation, decodeHotelSearch, hotelGuid } from './hotels.decoder';
import type { HotelGuestParty, HotelSearchRequest } from './hotels.types';

@Injectable({ providedIn: 'root' })
export class HotelsApiService {
  private readonly http = inject(HttpClient);
  private headers(token: string | null) {
    return token === null ? new HttpHeaders() : new HttpHeaders({ Authorization: `Bearer ${token}` });
  }
  search(body: HotelSearchRequest) {
    return this.http.post<unknown>('/api/hotels/search', body).pipe(timeout(20_000), map(decodeHotelSearch));
  }
  rates(searchId: string, selectionKey: string) {
    return this.http
      .post<unknown>('/api/hotels/rates', { searchId: hotelGuid(searchId), selectionKey })
      .pipe(timeout(20_000), map(decodeHotelRates));
  }
  quote(id: string, ratesId: string, selectionKey: string, expectedRevision: number | null, token: string | null) {
    return this.http
      .put<unknown>(
        `/api/hotels/reservations/${hotelGuid(id)}/quote`,
        { ratesId: hotelGuid(ratesId), selectionKey, expectedRevision },
        { headers: this.headers(token) },
      )
      .pipe(timeout(20_000), map(decodeHotelReservation));
  }
  buy(id: string, actionId: string, quoteRevision: number, party: HotelGuestParty, token: string | null) {
    return this.http
      .post<unknown>(
        `/api/hotels/reservations/${hotelGuid(id)}/purchase`,
        { actionId: hotelGuid(actionId), quoteRevision, consent: true, party },
        { headers: this.headers(token), transferCache: false },
      )
      .pipe(timeout(150_000), map(decodeHotelReservation));
  }
  detail(id: string, token: string | null) {
    return this.http
      .get<unknown>(`/api/hotels/reservations/${hotelGuid(id)}`, { headers: this.headers(token), transferCache: false })
      .pipe(timeout(15_000), map(decodeHotelReservation));
  }
  list(token: string | null) {
    return this.http
      .get<unknown>('/api/hotels/reservations', { headers: this.headers(token), transferCache: false })
      .pipe(
        timeout(15_000),
        map((v) => {
          if (!Array.isArray(v) || v.length > 50) throw new Error('Invalid Hotels response.');
          return v.map(decodeHotelReservation);
        }),
      );
  }
}
