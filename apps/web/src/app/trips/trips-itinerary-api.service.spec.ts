import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TripsItineraryApiService, TripsItineraryContractError } from '@travel/api-client';
import { candidate, empty, partial, error as problem, ready, unsupported } from '@travel/trips-itinerary-fixtures';
import { firstValueFrom } from 'rxjs';

// Angular's esbuild imports the same frozen JSON semantically; Node tests read its exact bytes.
const buffer = (value: Uint8Array): ArrayBuffer => {
  const result = new ArrayBuffer(value.byteLength);
  new Uint8Array(result).set(value);
  return result;
};
const bytes = (v: unknown): ArrayBuffer =>
  buffer(new TextEncoder().encode(typeof v === 'string' ? v : JSON.stringify(v)));
const guid = '11111111-1111-4111-8111-111111111111';
const endpoint = `/api/trips/${guid}/itinerary-candidate`;
describe('Trips M2 byte-preserving HTTP transport', () => {
  let api: TripsItineraryApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TripsItineraryApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });
  it.each([candidate, partial, empty, unsupported])(
    'decodes actual API outcome $outcome with exact lossless If-Match and empty body',
    async (fixture) => {
      const result = firstValueFrom(api.candidate(guid, '9223372036854775807', 'token'));
      const req = http.expectOne(endpoint);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toBe('{}');
      expect(req.request.responseType).toBe('arraybuffer');
      expect(req.request.headers.get('If-Match')).toBe('"9223372036854775807"');
      expect(req.request.headers.get('Authorization')).toBe('Bearer token');
      expect(req.request.headers.get('Content-Type')).toBe('application/json; charset=utf-8');
      expect(req.request.headers.get('Cache-Control')).toBe('no-store');
      expect(req.request.transferCache).toBe(false);
      expect(req.request.cache).toBe('no-store');
      expect(req.request.params.keys()).toEqual([]);
      req.flush(bytes(fixture));
      expect((await result).outcome).toBe(fixture.outcome);
    },
  );
  it('reads independent exact availability without query/body/token when absent', async () => {
    const result = firstValueFrom(api.availability(null));
    const req = http.expectOne('/api/trips/itinerary-availability');
    expect(req.request.method).toBe('GET');
    expect(req.request.body).toBeNull();
    expect(req.request.responseType).toBe('arraybuffer');
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(req.request.params.keys()).toEqual([]);
    req.flush(bytes(ready));
    expect(await result).toEqual(ready);
  });
  it.each([
    [401, 'Trips.Unauthenticated', 'unauthenticated'],
    [403, 'Trips.Forbidden', 'forbidden'],
    [404, 'Trips.NotFound', 'disabled'],
    [428, 'Trips.PreconditionRequired', 'precondition-required'],
    [400, 'Trips.InvalidInput', 'invalid-request'],
    [412, 'Trips.RevisionMismatch', 'stale'],
    [409, 'Trips.ItineraryRequiresEmptyPlan', 'conflict'],
    [429, 'Trips.ItineraryBusy', 'busy'],
    [503, 'Trips.ItineraryUnavailable', 'unavailable'],
    [504, 'Trips.ItineraryDeadline', 'deadline'],
  ] as const)('decodes bounded typed %s problem from bytes without retry', async (status, code, kind) => {
    const result = firstValueFrom(api.availability('token'));
    const rejected = expect(result).rejects.toMatchObject({ kind, status, problem: { code } });
    http.expectOne('/api/trips/itinerary-availability').flush(bytes({ ...problem, status, code }), {
      status,
      statusText: 'Error',
      headers: { 'Content-Type': 'application/problem+json' },
    });
    await rejected;
    http.expectNone('/api/trips/itinerary-availability');
  });
  it.each([
    new Uint8Array([0xff]),
    new Uint8Array([0xef, 0xbb, 0xbf, 0x7b, 0x7d]),
    new Uint8Array([0xc2]),
    new Uint8Array([0xe2, 0x82]),
    new TextEncoder().encode('{"status":"ready","status":"ready"}'),
    new TextEncoder().encode('{"x":"\\ud800"}'),
  ])('rejects complete invalid UTF8/BOM/JSON response bytes', async (raw) => {
    const result = firstValueFrom(api.availability(null));
    const rejected = expect(result).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne('/api/trips/itinerary-availability').flush(buffer(raw));
    await rejected;
  });
  it('checks availability exact byte cap before decoding', async () => {
    const raw = JSON.stringify(ready),
      result = firstValueFrom(api.availability(null));
    http.expectOne('/api/trips/itinerary-availability').flush(bytes(raw.padEnd(2048)));
    expect((await result).status).toBe('ready');
    const tooLarge = firstValueFrom(api.availability(null));
    const rejected = expect(tooLarge).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne('/api/trips/itinerary-availability').flush(bytes(raw.padEnd(2049)));
    await rejected;
  });
  it.each([
    '<html>proxy404</html>',
    '{"type":"about:blank","title":"error","status":404,"code":"arbitrary"}',
    JSON.stringify({ ...problem, status: 503, code: 'Trips.ItineraryUnavailable' }),
  ])('rejects malformed/mismatched problem instead of trusted disabled', async (raw) => {
    const result = firstValueFrom(api.availability(null));
    const rejected = expect(result).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne('/api/trips/itinerary-availability').flush(bytes(raw), { status: 404, statusText: 'Error' });
    await rejected;
  });
  it('checks complete candidate 128KiB boundary including multibyte UTF8', async () => {
    const json = JSON.stringify(candidate),
      length = new TextEncoder().encode(json).byteLength;
    const raw = json + ' '.repeat(131072 - length),
      result = firstValueFrom(api.candidate(guid, '1', null));
    http.expectOne(endpoint).flush(bytes(raw));
    expect((await result).outcome).toBe('candidate');
    const large = firstValueFrom(api.candidate(guid, '1', null));
    const rejected = expect(large).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne(endpoint).flush(bytes(raw + ' '));
    await rejected;
  });
  it.each([
    buffer(new Uint8Array([0xff])),
    bytes(JSON.stringify(problem).padEnd(2049)),
    bytes(JSON.stringify(problem).replace('"status":428', '"status":428,"\\u0073tatus":428')),
  ])('rejects invalid UTF8/oversize/duplicate typed problem bytes', async (raw) => {
    const result = firstValueFrom(api.availability(null));
    const rejected = expect(result).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne('/api/trips/itinerary-availability').flush(raw, { status: 428, statusText: 'Error' });
    await rejected;
  });
  it('keeps candidate404 ambiguous rather than globally declaring disabled', async () => {
    const result = firstValueFrom(api.candidate(guid, '1', null));
    const rejected = expect(result).rejects.toMatchObject({
      kind: 'not-found',
      status: 404,
      problem: { code: 'Trips.NotFound' },
    });
    http
      .expectOne(endpoint)
      .flush(bytes({ ...problem, status: 404, code: 'Trips.NotFound' }), { status: 404, statusText: 'Not Found' });
    await rejected;
    http.expectNone(endpoint);
  });
  it('separates lost transport from finite 15-second timeout and never retries', async () => {
    const lost = firstValueFrom(api.candidate(guid, '1', null));
    const transport = expect(lost).rejects.toMatchObject({ kind: 'transport', status: 0, problem: null });
    http.expectOne(endpoint).error(new ProgressEvent('error'));
    await transport;
    http.expectNone(endpoint);
    vi.useFakeTimers();
    const result = firstValueFrom(api.candidate(guid, '1', null));
    const timed = expect(result).rejects.toMatchObject({
      name: 'TripsItineraryRequestError',
      kind: 'timeout',
      status: 0,
      problem: null,
    });
    const req = http.expectOne(endpoint);
    await vi.advanceTimersByTimeAsync(15001);
    await timed;
    expect(req.cancelled).toBe(true);
    http.expectNone(endpoint);
  });
  it('rejects unexpected 2xx status and preserves manually supplied request validation', async () => {
    expect(() => api.candidate(guid, '01', null)).toThrow();
    http.expectNone(endpoint);
    const result = firstValueFrom(api.availability(null));
    const rejected = expect(result).rejects.toBeInstanceOf(TripsItineraryContractError);
    http.expectOne('/api/trips/itinerary-availability').flush(bytes(ready), { status: 201, statusText: 'Created' });
    await rejected;
  });
});
