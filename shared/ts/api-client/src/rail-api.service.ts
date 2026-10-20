import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, timeout } from 'rxjs';
import { decodeRailSearch, decodeRailSuggestions, type RailExpectedAuthority } from './rail.decoder';
import type { RailRequest } from './rail.types';
@Injectable({ providedIn: 'root' })
export class RailApiService {
  private readonly http = inject(HttpClient);
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
