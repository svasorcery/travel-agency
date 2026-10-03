import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type Event, NavigationStart, provideRouter, Router } from '@angular/router';
import type { Subject } from 'rxjs';
import { FlightsAuthService } from './flights-auth.service';
import type { FlightsAuthStatus } from './flights-auth.types';
import { SavedTravelersState } from './saved-travelers-state.service';

const id = '11111111-1111-4111-8111-111111111111';
const revision = '22222222-2222-4222-8222-222222222222';
const details = {
  title: 'mr' as const,
  givenName: 'DemoA',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'male' as const,
  email: 'demoa@example.test',
  phone: '+79161234567',
};
describe('memory-only owner profile state', () => {
  let http: HttpTestingController;
  let state: SavedTravelersState;
  const status = signal<FlightsAuthStatus>({ kind: 'authenticated', userId: 'demo-only' });
  const identityEpoch = signal(0);
  const auth = { status, identityEpoch, isDemo: true, accessToken: vi.fn().mockResolvedValue(null) };
  beforeEach(() => {
    status.set({ kind: 'authenticated', userId: 'demo-only' });
    identityEpoch.set(0);
    auth.isDemo = true;
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        SavedTravelersState,
        { provide: FlightsAuthService, useValue: auth },
      ],
    });
    state = TestBed.inject(SavedTravelersState);
    http = TestBed.inject(HttpTestingController);
    TestBed.flushEffects();
  });
  afterEach(() => http.verify({ ignoreCancelled: true }));
  it('erases profiles on an auth error at the same identity epoch and discards late completions', async () => {
    const first = state.load(0);
    await Promise.resolve();
    http
      .expectOne('/api/flights/travelers?offset=0')
      .flush({ items: [{ id, revision, details }], offset: 0, hasMore: false });
    await first;
    expect(state.items()[0].details.email).toBe(details.email);
    const late = state.read(id);
    await Promise.resolve();
    const request = http.expectOne(`/api/flights/travelers/${id}`);
    status.set({ kind: 'error', message: 'safe' });
    TestBed.flushEffects();
    expect(state.items()).toEqual([]);
    status.set({ kind: 'authenticated', userId: 'demo-only' });
    TestBed.flushEffects();
    if (!request.cancelled) request.flush({ id, revision, details }, { headers: { ETag: `"${revision}"` } });
    expect(await late).toBeNull();
    expect(state.items()).toEqual([]);
  });
  it('uses create-only PUT and retains uncertainty after malformed success and a later refusal', async () => {
    const write = state.create(details);
    const createdId = state.mutationId();
    expect(createdId).toMatch(/^[0-9a-f-]{36}$/);
    await Promise.resolve();
    const request = http.expectOne(`/api/flights/travelers/${createdId}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-None-Match')).toBe('*');
    expect(request.request.headers.get('X-Travel-Demo-Owner')).toBe('demo-only');
    expect(request.request.headers.has('Idempotency-Key')).toBe(false);
    request.flush({ id: createdId, revision }, { status: 201, statusText: 'Created' });
    expect(await write).toBeNull();
    expect(state.mutationState()).toBe('unknown');
    expect(state.uncertain()[0].id).toBe(createdId);
    await state.create(details);
    http.expectNone(() => true);
    const read = state.read(createdId ?? '');
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${createdId}`)
      .flush({ id: createdId, revision, details }, { headers: { ETag: `"${revision}"` } });
    await read;
    state.acceptReview();
    const second = state.update(createdId ?? '', revision, details);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${createdId}`)
      .flush(
        { type: 'https://travel.local/errors/Flights.TravelerPreconditionFailed' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await second;
    expect(state.mutationState()).toBe('conflict');
    expect(state.uncertain()[0].id).toBe(createdId);
    http.expectNone(() => true);
  });
  it('clears PII and invalidates reads on navigation and owner switch', async () => {
    const read = state.read(id);
    await Promise.resolve();
    const request = http.expectOne(`/api/flights/travelers/${id}`);
    (TestBed.inject(Router).events as Subject<Event>).next(new NavigationStart(1, '/flights'));
    if (!request.cancelled) request.flush({ id, revision, details }, { headers: { ETag: `"${revision}"` } });
    expect(await read).toBeNull();
    status.set({ kind: 'authenticated', userId: 'demo-other' });
    identityEpoch.update((x) => x + 1);
    TestBed.flushEffects();
    expect(state.items()).toEqual([]);
  });
  it('classifies 5xx as unknown without a resend and keeps only non-PII uncertainty metadata', async () => {
    const write = state.update(id, revision, details);
    await Promise.resolve();
    http.expectOne(`/api/flights/travelers/${id}`).flush({}, { status: 503, statusText: 'Unavailable' });
    await write;
    expect(state.mutationState()).toBe('unknown');
    expect(JSON.stringify(state.uncertain())).not.toContain(details.email);
    http.expectNone(() => true);
  });
  it('never dispatches a production profile mutation without a current bearer token', async () => {
    auth.isDemo = false;
    await state.create(details);
    http.expectNone(() => true);
    expect(state.mutationState()).toBe('refused');
    auth.isDemo = true;
  });
  it('removes a cached profile once a current-state read reports it absent without claiming deletion history', async () => {
    const list = state.load(0);
    await Promise.resolve();
    http
      .expectOne('/api/flights/travelers?offset=0')
      .flush({ items: [{ id, revision, details }], offset: 0, hasMore: false });
    await list;
    const read = state.read(id);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${id}`)
      .flush(
        { type: 'https://travel.local/errors/Flights.TravelerNotFound' },
        { status: 404, statusText: 'Not Found' },
      );
    await read;
    expect(state.items()).toEqual([]);
    expect(state.loadState()).toBe('missing');
    expect(state.message()).not.toContain('удалён');
  });
});
