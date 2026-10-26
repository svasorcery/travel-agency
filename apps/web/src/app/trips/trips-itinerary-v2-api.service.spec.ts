import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TripsItineraryApiService, TripsItineraryContractError } from '@travel/api-client';
import { ready as legacyReady } from '@travel/trips-itinerary-fixtures';
import { candidate, ready } from '@travel/trips-itinerary-v2-fixtures';
import { firstValueFrom } from 'rxjs';

const bytes = (value: unknown) => {
  const data = new TextEncoder().encode(JSON.stringify(value));
  const buffer = new ArrayBuffer(data.length);
  new Uint8Array(buffer).set(data);
  return buffer;
};
describe('Trips v2 transport opt-in', () => {
  let api: TripsItineraryApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TripsItineraryApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('requests exact destination with v2 header and no body or cache', async () => {
    const result = firstValueFrom(api.availabilityFor('moscow', 'token'));
    const req = http.expectOne('/api/trips/itinerary-availability?destinationId=moscow');
    expect(req.request.headers.get('X-Trips-Itinerary-Version')).toBe('2');
    expect(req.request.body).toBeNull();
    expect(req.request.cache).toBe('no-store');
    expect(req.request.transferCache).toBe(false);
    req.flush(bytes(ready));
    expect(await result).toEqual(ready);
  });
  it('opts candidate into v2 without adding a city to body or query', async () => {
    const result = firstValueFrom(api.candidateV2('11111111-1111-4111-8111-111111111111', '1', 'token'));
    const req = http.expectOne('/api/trips/11111111-1111-4111-8111-111111111111/itinerary-candidate');
    expect(req.request.headers.get('X-Trips-Itinerary-Version')).toBe('2');
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    expect(req.request.body).toBe('{}');
    expect(req.request.params.keys()).toEqual([]);
    req.flush(bytes(candidate));
    expect((await result).candidateVersion).toBe(2);
  });
  it.each(['legacy', 'other-city'])('refuses %s availability without fallback or retry', async (mode) => {
    const result = firstValueFrom(api.availabilityFor(mode === 'legacy' ? 'moscow' : 'saint-petersburg', null));
    const refused = expect(result).rejects.toBeInstanceOf(TripsItineraryContractError);
    const req = http.expectOne((r) => r.url === '/api/trips/itinerary-availability');
    req.flush(bytes(mode === 'legacy' ? legacyReady : ready));
    await refused;
    http.expectNone((r) => r.url === '/api/trips/itinerary-availability');
  });
  it('retains source-free unsupported as a successful distinct result', async () => {
    const value = {
      candidateVersion: 2,
      status: 'unsupported',
      destinationId: 'kaliningrad',
      reason: 'destinationUnsupported',
    };
    const result = firstValueFrom(api.availabilityFor('kaliningrad', null));
    http.expectOne('/api/trips/itinerary-availability?destinationId=kaliningrad').flush(bytes(value));
    expect(await result).toEqual(value);
  });
});
