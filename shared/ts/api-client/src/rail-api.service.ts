import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { decodeRailSearch, decodeRailSuggestions, type RailExpectedAuthority } from './rail.decoder';
import type { RailJourneyDetailsRequest, RailRequest } from './rail.types';
import { decodeRailDetails } from './rail-details.decoder';
@Injectable({ providedIn: 'root' })
export class RailApiService {
  private readonly http = inject(HttpClient);
  details(request: RailJourneyDetailsRequest, authority: RailExpectedAuthority) {
    return this.http.post<unknown>('/api/rail/journey-details', request, { transferCache: false }).pipe(
      timeout(10000),
      map((value) => decodeRailDetails(value, request, Date.now(), authority)),
    );
  }
  stations(q: string) {
    return this.http.get<unknown>('/api/rail/stations', { params: { q }, transferCache: false }).pipe(
      timeout(25000),
      map((value) => decodeRailSuggestions(value)),
    );
  }
  search(request: RailRequest, authority: RailExpectedAuthority) {
    return this.http.post<unknown>('/api/rail/search', request, { transferCache: false }).pipe(
      timeout(10000),
      map((value) => decodeRailSearch(value, request, Date.now(), authority)),
    );
  }
}
