import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TripsBriefApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';

const request = {
  requestId: '11111111-1111-4111-8111-111111111111',
  text: 'Питер',
  locale: 'ru' as const,
  referenceZone: 'Europe/Moscow' as const,
  processingPolicyVersion: 'a'.repeat(64),
};
describe('Trips brief private HTTP boundary', () => {
  let api: TripsBriefApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TripsBriefApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });
  it('gets bounded exact capability without query, cache or private body', async () => {
    const promise = firstValueFrom(api.capability('token'));
    const req = http.expectOne('/api/trips/brief-extraction-capability');
    expect(req.request.method).toBe('GET');
    expect(req.request.body).toBeNull();
    expect(req.request.params.keys()).toEqual([]);
    expect(req.request.cache).toBe('no-store');
    expect(req.request.transferCache).toBe(false);
    expect(req.request.headers.get('Authorization')).toBe('Bearer token');
    req.flush(
      JSON.stringify({
        candidateVersion: 1,
        status: 'unconfigured',
        processorId: null,
        modelId: null,
        processingPolicyVersion: null,
      }),
    );
    expect((await promise).status).toBe('unconfigured');
  });
  it('posts only minimal bound text/zone/policy and sanitizes all raw errors without retry', async () => {
    const promise = firstValueFrom(api.candidate(request, 'token'));
    const rejection = expect(promise).rejects.toMatchObject({
      kind: 'unavailable',
      message: 'Brief extraction unavailable',
    });
    const req = http.expectOne('/api/trips/brief-candidate');
    expect(req.request.body).toEqual(request);
    expect(req.request.headers.has('If-Match')).toBe(false);
    expect(req.request.headers.get('Cache-Control')).toBe('no-store');
    expect(req.request.params.keys()).toEqual([]);
    expect(req.request.transferCache).toBe(false);
    req.flush('PRIVATE INPUT AND DIAGNOSTIC', { status: 503, statusText: 'Error' });
    await rejection;
    http.expectNone('/api/trips/brief-candidate');
  });
  it('rejects duplicate response members and total capability byte overflow', async () => {
    for (const raw of ['{"candidateVersion":1,"candidateVersion":1}', ' '.repeat(2049)]) {
      const promise = firstValueFrom(api.capability(null));
      const rejected = expect(promise).rejects.toMatchObject({ kind: 'contract-error' });
      http.expectOne('/api/trips/brief-extraction-capability').flush(raw);
      await rejected;
    }
  });
  it('observes at most5500ms, cancels observation and never repeats a possible request', async () => {
    vi.useFakeTimers();
    const promise = firstValueFrom(api.candidate(request, 'token'));
    const rejected = expect(promise).rejects.toMatchObject({ kind: 'deadline' });
    const req = http.expectOne('/api/trips/brief-candidate');
    await vi.advanceTimersByTimeAsync(5500);
    await rejected;
    expect(req.cancelled).toBe(true);
    http.expectNone('/api/trips/brief-candidate');
  });
  it('rejects too much input before dispatch', () => {
    expect(() => api.candidate({ ...request, text: 'x'.repeat(1001) }, 'token')).toThrow();
    http.expectNone('/api/trips/brief-candidate');
  });
});
describe('Trips brief SSR does not dispatch private text', () => {
  it('blocks server candidate calls before HTTP', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: 'server' }],
    });
    const api = TestBed.inject(TripsBriefApiService);
    expect(() => api.candidate(request, 'token')).toThrow();
    TestBed.inject(HttpTestingController).expectNone('/api/trips/brief-candidate');
  });
});
