import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TripsApiService, tripWrite } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';

describe('Trips API transport', () => {
  let api: TripsApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TripsApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('sends strong lossless If-Match and only editable fields', async () => {
    const result = firstValueFrom(
      api.replace(tripsExample.tripId, '9223372036854775807', tripWrite(tripsExample as never), 'token'),
    );
    const req = http.expectOne('/api/trips/' + tripsExample.tripId);
    expect(req.request.headers.get('If-Match')).toBe('"9223372036854775807"');
    expect(req.request.body).not.toHaveProperty('revision');
    expect(req.request.body).not.toHaveProperty('destination');
    req.flush(JSON.stringify(tripsExample), { headers: { ETag: '"2"' } });
    expect((await result).revision).toBe('2');
  });
  it('does not retry a lost mutation and requires exact ETag', async () => {
    const result = firstValueFrom(api.replace(tripsExample.tripId, '2', tripWrite(tripsExample as never), 'token'));
    const check = expect(result).rejects.toThrow();
    http.expectOne('/api/trips/' + tripsExample.tripId).error(new ProgressEvent('error'));
    await check;
    http.expectNone('/api/trips/' + tripsExample.tripId);
    const invalid = firstValueFrom(api.detail(tripsExample.tripId, 'token'));
    const rejected = expect(invalid).rejects.toThrow();
    http
      .expectOne('/api/trips/' + tripsExample.tripId)
      .flush(JSON.stringify(tripsExample), { headers: { ETag: 'W/"2"' } });
    await rejected;
  });
});
