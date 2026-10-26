import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, map, TimeoutError, throwError, timeout } from 'rxjs';
import { tripGuid, tripRevision } from './trips.decoder';
import { type TripCity, tripCities } from './trips.types';
import {
  decodeTripItineraryAvailability,
  decodeTripItineraryAvailabilityV2,
  decodeTripItineraryCandidate,
  decodeTripItineraryCandidateV2,
  decodeTripItineraryProblem,
  TripsItineraryContractError,
  tripItineraryProblemKind,
} from './trips-itinerary.decoder';
import type { TripItineraryErrorKind, TripItineraryProblem } from './trips-itinerary.types';
export class TripsItineraryRequestError extends Error {
  constructor(
    readonly kind: TripItineraryErrorKind,
    readonly status: number,
    readonly problem: TripItineraryProblem | null = null,
  ) {
    super('Trips itinerary request failed.');
    this.name = 'TripsItineraryRequestError';
  }
}
@Injectable({ providedIn: 'root' })
export class TripsItineraryApiService {
  private readonly http = inject(HttpClient);
  private headers(token: string | null, revision?: string, protocol: 1 | 2 = 1): HttpHeaders {
    let headers = new HttpHeaders({ 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
    if (token !== null) headers = headers.set('Authorization', `Bearer ${token}`);
    if (revision !== undefined) headers = headers.set('If-Match', `"${tripRevision(revision)}"`);
    if (protocol === 2) headers = headers.set('X-Trips-Itinerary-Version', '2');
    return headers;
  }
  availability(token: string | null) {
    return this.availabilityRequest(token).pipe(
      map((result) => {
        if (result.status !== 'ready') throw new TripsItineraryContractError();
        return result;
      }),
    );
  }
  availabilityFor(destinationId: TripCity, token: string | null) {
    if (!tripCities.includes(destinationId)) throw new TripsItineraryContractError();
    return this.availabilityRequest(token, destinationId);
  }
  private availabilityRequest(token: string | null, destinationId?: TripCity) {
    return this.http
      .get('/api/trips/itinerary-availability', {
        headers: this.headers(token, undefined, destinationId === undefined ? 1 : 2),
        params: destinationId === undefined ? undefined : { destinationId },
        observe: 'response',
        responseType: 'arraybuffer',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          if (response.status !== 200 || response.body === null) throw new TripsItineraryContractError();
          const result =
            destinationId === undefined
              ? decodeTripItineraryAvailability(response.body)
              : decodeTripItineraryAvailabilityV2(response.body);
          if (destinationId !== undefined && result.destinationId !== destinationId)
            throw new TripsItineraryContractError();
          return result;
        }),
        catchError((error: unknown) => this.failure(error, true)),
      );
  }
  candidate(tripId: string, revision: string, token: string | null) {
    return this.candidateRequest(tripId, revision, token, 1);
  }
  candidateV2(tripId: string, revision: string, token: string | null) {
    return this.candidateRequest(tripId, revision, token, 2);
  }
  private candidateRequest(tripId: string, revision: string, token: string | null, protocol: 1 | 2) {
    return this.http
      .post(`/api/trips/${tripGuid(tripId)}/itinerary-candidate`, '{}', {
        headers: this.headers(token, revision, protocol),
        observe: 'response',
        responseType: 'arraybuffer',
        transferCache: false,
        cache: 'no-store',
      })
      .pipe(
        timeout(15000),
        map((response) => {
          if (response.status !== 200 || response.body === null) throw new TripsItineraryContractError();
          return protocol === 1
            ? decodeTripItineraryCandidate(response.body)
            : decodeTripItineraryCandidateV2(response.body);
        }),
        catchError((error: unknown) => this.failure(error)),
      );
  }
  private failure(error: unknown, availability = false) {
    return throwError(() => {
      if (error instanceof TimeoutError) return new TripsItineraryRequestError('timeout', 0);
      if (error instanceof HttpErrorResponse) {
        if (error.status === 0) return new TripsItineraryRequestError('transport', 0);
        if (!(error.error instanceof ArrayBuffer)) return new TripsItineraryContractError();
        try {
          const problem = decodeTripItineraryProblem(error.error, error.status);
          const kind = tripItineraryProblemKind(problem);
          return new TripsItineraryRequestError(
            availability && kind === 'not-found' ? 'disabled' : kind,
            error.status,
            problem,
          );
        } catch {
          return new TripsItineraryContractError();
        }
      }
      return error instanceof TripsItineraryContractError ? error : new TripsItineraryContractError();
    });
  }
}
