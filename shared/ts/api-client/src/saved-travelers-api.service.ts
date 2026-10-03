import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable, timeout } from 'rxjs';
import {
  decodeSavedTraveler,
  decodeSavedTravelerDetails,
  decodeSavedTravelerPage,
  decodeSavedTravelerReceipt,
  isTravelerGuid,
  SavedTravelerContractError,
  validTravelerOffset,
} from './saved-travelers.decoder';
import type {
  SavedTraveler,
  SavedTravelerDetails,
  SavedTravelerPage,
  SavedTravelerReceipt,
} from './saved-travelers.types';
export type DemoTravelerOwner = 'demo-only' | 'demo-other';
@Injectable({ providedIn: 'root' })
export class SavedTravelersApiService {
  private readonly http = inject(HttpClient);
  private headers(token: string | null, demoOwner?: DemoTravelerOwner): HttpHeaders {
    if (token === null && demoOwner === undefined) throw new SavedTravelerContractError();
    let headers = token === null ? new HttpHeaders() : new HttpHeaders({ Authorization: `Bearer ${token}` });
    if (token === null && demoOwner !== undefined) headers = headers.set('X-Travel-Demo-Owner', demoOwner);
    return headers;
  }
  private path(id: string): string {
    if (!isTravelerGuid(id)) throw new SavedTravelerContractError();
    return `/api/flights/travelers/${id.toLowerCase()}`;
  }
  list(offset: number, token: string | null, demoOwner?: DemoTravelerOwner): Observable<SavedTravelerPage> {
    if (!validTravelerOffset(offset)) throw new SavedTravelerContractError();
    return this.http
      .get<unknown>(`/api/flights/travelers?offset=${offset}`, { headers: this.headers(token, demoOwner) })
      .pipe(
        timeout(15_000),
        map((value) => decodeSavedTravelerPage(value, offset)),
      );
  }
  get(id: string, token: string | null, demoOwner?: DemoTravelerOwner): Observable<SavedTraveler> {
    return this.http.get<unknown>(this.path(id), { headers: this.headers(token, demoOwner), observe: 'response' }).pipe(
      timeout(15_000),
      map((response) => {
        const traveler = decodeSavedTraveler(response.body, id);
        if (response.status !== 200 || response.headers.get('ETag') !== `"${traveler.revision}"`)
          throw new SavedTravelerContractError();
        return traveler;
      }),
    );
  }
  create(
    id: string,
    details: SavedTravelerDetails,
    token: string | null,
    demoOwner?: DemoTravelerOwner,
  ): Observable<SavedTravelerReceipt> {
    return this.write(id, null, details, token, demoOwner);
  }
  update(
    id: string,
    revision: string,
    details: SavedTravelerDetails,
    token: string | null,
    demoOwner?: DemoTravelerOwner,
  ): Observable<SavedTravelerReceipt> {
    if (!isTravelerGuid(revision)) throw new SavedTravelerContractError();
    return this.write(id, revision, details, token, demoOwner);
  }
  private write(
    id: string,
    revision: string | null,
    details: SavedTravelerDetails,
    token: string | null,
    demoOwner?: DemoTravelerOwner,
  ): Observable<SavedTravelerReceipt> {
    const headers = this.headers(token, demoOwner).set(
      revision === null ? 'If-None-Match' : 'If-Match',
      revision === null ? '*' : `"${revision.toLowerCase()}"`,
    );
    return this.http
      .put<unknown>(this.path(id), decodeSavedTravelerDetails(details), { headers, observe: 'response' })
      .pipe(
        timeout(15_000),
        map((response) =>
          decodeSavedTravelerReceipt(
            response.body,
            id,
            response.status,
            revision === null ? 201 : 200,
            response.headers.get('ETag'),
          ),
        ),
      );
  }
  delete(id: string, revision: string, token: string | null, demoOwner?: DemoTravelerOwner): Observable<void> {
    if (!isTravelerGuid(revision)) throw new SavedTravelerContractError();
    return this.http
      .delete(this.path(id), {
        headers: this.headers(token, demoOwner).set('If-Match', `"${revision.toLowerCase()}"`),
        observe: 'response',
        responseType: 'text',
      })
      .pipe(
        timeout(15_000),
        map((response) => {
          if (response.status !== 204 || (response.body !== '' && response.body !== null))
            throw new SavedTravelerContractError();
        }),
      );
  }
}
