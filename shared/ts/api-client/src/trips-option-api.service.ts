import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { parseTripsJson, TripsContractError } from './trips.decoder';
import type { TripBrief } from './trips.types';
import {
  decodeTripOptionAvailability,
  decodeTripOptionPreview,
  type TripOptionAvailability,
} from './trips-option.decoder';

@Injectable({ providedIn: 'root' })
export class TripsOptionApiService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/trips/option-preview';
  private options(token: string | null) {
    let headers = new HttpHeaders({ 'Cache-Control': 'no-store', 'Content-Type': 'application/json' });
    if (token !== null) headers = headers.set('Authorization', 'Bearer ' + token);
    return {
      headers,
      transferCache: false,
      cache: 'no-store' as const,
      responseType: 'text' as const,
      observe: 'response' as const,
    };
  }
  availability(token: string | null) {
    return this.http.get(this.url + '/availability', this.options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeTripOptionAvailability(parseTripsJson(r.body, 2048));
      }),
    );
  }
  preview(brief: TripBrief, discovery: TripOptionAvailability, token: string | null) {
    return this.http.post(this.url, { brief }, this.options(token)).pipe(
      timeout(5500),
      map((r) => {
        if (r.status !== 200 || r.body === null) throw new TripsContractError();
        return decodeTripOptionPreview(parseTripsJson(r.body, 32768), brief, discovery);
      }),
    );
  }
}
