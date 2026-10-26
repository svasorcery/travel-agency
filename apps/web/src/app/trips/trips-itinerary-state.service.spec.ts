import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  decodeTripItineraryCandidateV2 as decodeTripItineraryCandidate,
  decodeTripPlan,
  type TripPlan,
  TripsApiService,
} from '@travel/api-client';
import { tripsExample } from '@travel/api-client/testing/trips';
import {
  candidate,
  empty,
  partial,
  petersburgCandidate,
  petersburgReady,
  ready,
  unsupported,
} from '@travel/trips-itinerary-v2-fixtures';
import { of, Subject, throwError } from 'rxjs';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TRIPS_ITINERARY_CLOCK, TripsItineraryStateService } from './trips-itinerary-state.service';
import { TripsStateService } from './trips-state.service';

const id = tripsExample.tripId,
  other = '33333333-3333-4333-8333-333333333333';
const availabilityUrl = '/api/trips/itinerary-availability?destinationId=moscow';
const candidateUrl = `/api/trips/${id}/itinerary-candidate`;
const bytes = (value: unknown) => {
  const data = new TextEncoder().encode(JSON.stringify(value)),
    result = new ArrayBuffer(data.length);
  new Uint8Array(result).set(data);
  return result;
};
const settle = async () => {
  for (let i = 0; i < 12; i++) await Promise.resolve();
};
function required<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('Required test fixture is absent.');
  return value;
}
function plan() {
  return decodeTripPlan({
    ...structuredClone(tripsExample),
    revision: '1',
    title: 'Private title',
    brief: {
      ...tripsExample.brief,
      destinationId: 'moscow',
      originId: 'saint-petersburg',
      startDate: '2026-10-22',
      endDate: '2026-10-22',
      interests: ['architecture'],
    },
    destination: { ...tripsExample.destination, destinationId: 'moscow', displayName: 'Москва' },
    days: [{ date: '2026-10-22', blocks: [] }],
    knownUserEstimateSubtotal: '0.00',
  });
}
async function setup(p = plan(), platform = 'browser') {
  let now = Date.parse('2026-10-21T00:01:00Z');
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner' }),
    identityEpoch: signal(0),
    accessToken: vi.fn().mockResolvedValue('token'),
    hasOidcCallback: () => false,
    initializeFromCallback: vi.fn(),
    deny: vi.fn(),
    logout: vi.fn(),
  };
  const api = {
    detail: vi.fn().mockImplementation((tripId: string) => of({ ...p, tripId })),
    list: vi.fn().mockReturnValue(of({ items: [], nextCursor: null })),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: PLATFORM_ID, useValue: platform },
      { provide: TripsApiService, useValue: api },
      { provide: TripsAuthService, useValue: auth },
      { provide: TRIPS_ITINERARY_CLOCK, useValue: () => now },
      {
        provide: TripsAvailabilityService,
        useValue: { status: signal('ready'), initialize: vi.fn().mockResolvedValue(undefined), clearPrivate: vi.fn() },
      },
    ],
  });
  const editor = TestBed.inject(TripsStateService),
    state = TestBed.inject(TripsItineraryStateService),
    http = TestBed.inject(HttpTestingController);
  await editor.enter(id);
  return {
    editor,
    state,
    http,
    api,
    auth,
    setNow: (v: string) => {
      now = Date.parse(v);
    },
  };
}
async function activate(s: Awaited<ReturnType<typeof setup>>) {
  const promise = s.state.activate();
  await settle();
  s.http.expectOne(availabilityUrl).flush(bytes(ready));
  await promise;
}
async function generate(s: Awaited<ReturnType<typeof setup>>, value: unknown = candidate) {
  const promise = s.state.generate();
  await settle();
  s.http.expectOne(candidateUrl).flush(bytes(value));
  await promise;
}
describe('Trips ephemeral candidate and retained M1 adoption', () => {
  it('uses the saved Petersburg destination for generation and both adoption checks', async () => {
    const p = plan();
    p.brief.destinationId = 'saint-petersburg';
    p.destination = {
      destinationId: 'saint-petersburg',
      displayName: 'Санкт-Петербург',
      timeZone: 'Europe/Moscow',
      catalogueVersion: '1',
    };
    const s = await setup(p);
    const activation = s.state.activate();
    await settle();
    const cityUrl = '/api/trips/itinerary-availability?destinationId=saint-petersburg';
    const available = s.http.expectOne(cityUrl);
    expect(available.request.headers.get('X-Trips-Itinerary-Version')).toBe('2');
    available.flush(bytes(petersburgReady));
    await activation;
    const generation = s.state.generate();
    await settle();
    s.http.expectOne(candidateUrl).flush(bytes(petersburgCandidate));
    await generation;
    expect(s.state.canAdopt()).toBe(true);
    const adoption = s.state.adopt();
    await settle();
    s.http.expectOne(cityUrl).flush(bytes(petersburgReady));
    expect(await adoption).toBe(true);
    expect(s.editor.dirty()).toBe(true);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('keeps unsupported city distinct while preserving the manual editor', async () => {
    const p = plan();
    p.brief.destinationId = 'kaliningrad';
    p.destination = {
      destinationId: 'kaliningrad',
      displayName: 'Калининград',
      timeZone: 'Europe/Kaliningrad',
      catalogueVersion: '1',
    };
    const s = await setup(p);
    const activation = s.state.activate();
    await settle();
    s.http.expectOne('/api/trips/itinerary-availability?destinationId=kaliningrad').flush(
      bytes({
        candidateVersion: 2,
        status: 'unsupported',
        destinationId: 'kaliningrad',
        reason: 'destinationUnsupported',
      }),
    );
    await activation;
    expect(s.state.availability().kind).toBe('unsupported');
    expect(s.state.canGenerate()).toBe(false);
    expect(s.editor.baseline()).toEqual(p);
    expect(s.editor.mutationBlocked()).toBe(false);
  });
  it('invalidates pending availability across saved destination A to B to A on the same plan', async () => {
    const s = await setup();
    const pending = s.state.activate();
    await settle();
    const request = s.http.expectOne(availabilityUrl);
    const petersburg = {
      ...plan(),
      revision: '2',
      brief: { ...plan().brief, destinationId: 'saint-petersburg' as const },
      destination: { ...tripsExample.destination },
    };
    s.api.replace.mockReturnValueOnce(of(petersburg));
    expect(s.editor.changeBrief(petersburg.brief)).toBe(true);
    await s.editor.save();
    expect(s.editor.baseline()?.destination.destinationId).toBe('saint-petersburg');
    expect(s.editor.baseline()?.revision).toBe('2');
    const moscow = { ...plan(), revision: '3' };
    s.api.replace.mockReturnValueOnce(of(moscow));
    expect(s.editor.changeBrief(moscow.brief)).toBe(true);
    await s.editor.save();
    expect(s.editor.baseline()?.destination.destinationId).toBe('moscow');
    expect(s.editor.baseline()?.revision).toBe('3');
    if (!request.cancelled) request.flush(bytes(ready));
    await pending;
    expect(s.state.availability().kind).toBe('unknown');
    expect(s.state.preview()).toBeNull();
  });
  afterEach(() => {
    try {
      TestBed.inject(HttpTestingController).verify();
    } finally {
      vi.useRealTimers();
      TestBed.resetTestingModule();
    }
  });
  it('explicitly previews actual API bytes without touching the editor or writing a plan', async () => {
    const s = await setup();
    await activate(s);
    const before = structuredClone(s.editor.buffer());
    expect(s.state.canGenerate()).toBe(true);
    await generate(s);
    expect(s.state.status()).toBe('candidate');
    expect(s.state.preview()?.outcome).toBe('candidate');
    expect(s.editor.buffer()).toEqual(before);
    expect(s.editor.dirty()).toBe(false);
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it.each(['owner-first', 'availability-first'])(
    'stages only after BOTH successful reads (%s), with ordinary IDs and no autosave',
    async (order) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const before = structuredClone(s.editor.buffer()),
        owner = new Subject<TripPlan>();
      s.api.detail.mockReturnValueOnce(owner);
      const adopted = s.state.adopt();
      await settle();
      const request = s.http.expectOne(availabilityUrl);
      expect(s.state.pending()).toBe('checking-adoption');
      expect(await s.state.adopt()).toBe(false);
      expect(s.api.detail).toHaveBeenCalledTimes(2);
      if (order === 'owner-first') owner.next(plan());
      else request.flush(bytes(ready));
      await settle();
      expect(s.editor.buffer()).toEqual(before);
      if (order === 'owner-first') request.flush(bytes(ready));
      else owner.next(plan());
      expect(await adopted).toBe(true);
      expect(s.editor.buffer()?.title).toBe(before?.title);
      expect(s.editor.buffer()?.brief).toEqual(before?.brief);
      expect(s.editor.baseline()?.revision).toBe('1');
      expect(s.editor.buffer()?.days[0].blocks[0]).toEqual({
        blockId: '8fd322c5-822e-45b0-aeee-707fbca031d5',
        title: 'FICTIONAL place p0',
        note: candidate.days[0].blocks[0].note,
        startTime: null,
        endTime: null,
        estimate: null,
      });
      expect(s.editor.dirty()).toBe(true);
      expect(s.state.preview()).toBeNull();
      expect(s.state.status()).toBe('staged');
      expect(s.api.replace).not.toHaveBeenCalled();
      s.api.replace.mockReturnValue(of({ ...plan(), revision: '2', days: required(s.editor.buffer()).days }));
      await s.editor.save();
      expect(s.api.replace.mock.calls[0][1]).toBe('1');
      expect(s.api.replace.mock.calls[0][2].days[0].blocks[0]).not.toHaveProperty('facts');
    },
  );
  it.each([
    'form',
    'revert',
    'refresh',
    'plan-aba',
    'owner-aba',
    'logout',
    'delete-failure',
    'delete',
    'save-failure',
  ] as const)('drops generation after %s including completed and reverted changes', async (action) => {
    const s = await setup();
    await activate(s);
    const generation = s.state.generate();
    await settle();
    const request = s.http.expectOne(candidateUrl);
    if (action === 'form') s.editor.touch();
    if (action === 'revert') {
      const original = required(s.editor.buffer());
      s.editor.edit({ ...original, title: 'changed' });
      s.editor.edit(original);
    }
    if (action === 'refresh') await s.editor.refreshDetail();
    if (action === 'plan-aba') {
      await s.editor.enter(other);
      await s.editor.enter(id);
    }
    if (action === 'owner-aba') {
      s.auth.identityEpoch.update((x) => x + 1);
      s.auth.status.set({ kind: 'authenticated', userId: 'second' });
      s.auth.identityEpoch.update((x) => x + 1);
      s.auth.status.set({ kind: 'authenticated', userId: 'owner' });
    }
    if (action === 'logout') {
      s.auth.identityEpoch.update((x) => x + 1);
      s.auth.status.set({ kind: 'anonymous' });
    }
    if (action === 'delete-failure') {
      s.api.delete.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
      await s.editor.deletePlan();
    }
    if (action === 'delete') {
      s.api.delete.mockReturnValue(of(undefined));
      await s.editor.deletePlan();
    }
    if (action === 'save-failure') {
      s.editor.touch();
      s.api.replace.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
      await s.editor.save();
      s.editor.touch(false);
    }
    expect(s.state.preview()).toBeNull();
    if (!request.cancelled) request.flush(bytes(candidate));
    await generation;
    expect(s.state.preview()).toBeNull();
    expect(s.state.canAdopt()).toBe(false);
    expect(s.editor.buffer()?.days.flatMap((d) => d.blocks) ?? []).toEqual([]);
  });
  it.each(['edit', 'revert', 'dismiss', 'expiry', 'mutation', 'navigation', 'logout'] as const)(
    'never copies when %s occurs between successful adoption replies',
    async (action) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const owner = new Subject<TripPlan>();
      s.api.detail.mockReturnValueOnce(owner);
      const adoption = s.state.adopt();
      await settle();
      const available = s.http.expectOne(availabilityUrl);
      owner.next(plan());
      await settle();
      if (action === 'edit') s.editor.touch();
      if (action === 'revert') {
        const original = required(s.editor.buffer());
        s.editor.edit({ ...original, title: 'changed' });
        s.editor.edit(original);
      }
      if (action === 'dismiss') s.state.dismiss();
      if (action === 'expiry') s.setNow(candidate.expiresAt);
      if (action === 'mutation') {
        s.api.delete.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
        await s.editor.deletePlan();
      }
      if (action === 'navigation') {
        await s.editor.enter(other);
        await s.editor.enter(id);
      }
      if (action === 'logout') {
        s.auth.identityEpoch.update((x) => x + 1);
        s.auth.status.set({ kind: 'anonymous' });
      }
      if (!available.cancelled) available.flush(bytes(ready));
      expect(await adoption).toBe(false);
      expect(s.editor.buffer()?.days.flatMap((d) => d.blocks) ?? []).toEqual([]);
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it('does not advance the M1 baseline or comparison when guarded owner GET returns a new revision', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const before = structuredClone(s.editor.buffer());
    s.api.detail.mockReturnValueOnce(of({ ...plan(), revision: '2', title: 'Remote' }));
    const adoption = s.state.adopt();
    await settle();
    const request = s.http.match(availabilityUrl)[0];
    if (request && !request.cancelled) request.flush(bytes(ready));
    expect(await adoption).toBe(false);
    expect(s.editor.baseline()?.revision).toBe('1');
    expect(s.editor.buffer()).toEqual(before);
    expect(s.editor.comparison()).toBeNull();
  });
  it.each(['datasetId', 'datasetVersion', 'datasetHash', 'policyVersion', 'releaseValidUntil'] as const)(
    'invalidates unchanged plan on availability %s change',
    async (key) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const adoption = s.state.adopt();
      await settle();
      const changed = {
        ...ready,
        [key]:
          key === 'datasetHash'
            ? 'b'.repeat(64)
            : key === 'releaseValidUntil'
              ? '2026-11-09T00:00:00.0000000Z'
              : `${ready[key]}-changed`,
      };
      s.http.expectOne(availabilityUrl).flush(bytes(changed));
      expect(await adoption).toBe(false);
      expect(s.state.status()).toBe(key === 'releaseValidUntil' ? 'stale' : 'unavailable');
      expect(s.editor.dirty()).toBe(false);
    },
  );
  it.each([
    [404, 'Trips.NotFound', 'disabled'],
    [401, 'Trips.Unauthenticated', 'unauthenticated'],
    [403, 'Trips.Forbidden', 'forbidden'],
    [503, 'Trips.ItineraryUnavailable', 'unavailable'],
  ] as const)(
    'blocks adoption after availability %s without breaking manual editing',
    async (code, problem, status) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const adoption = s.state.adopt();
      await settle();
      s.http
        .expectOne(availabilityUrl)
        .flush(bytes({ type: 'about:blank', title: 'Unavailable', status: code, code: problem }), {
          status: code,
          statusText: 'Error',
        });
      expect(await adoption).toBe(false);
      expect(s.state.status()).toBe(status);
      expect(s.editor.dirty()).toBe(false);
      s.editor.edit({ ...required(s.editor.buffer()), title: 'Manual works' });
      expect(s.editor.buffer()?.title).toBe('Manual works');
    },
  );
  it.each(['date', 'revision', 'zone', 'catalogue', 'interests', 'pace', 'origin', 'budget'] as const)(
    'rejects standalone valid candidate from a different saved %s context',
    async (change) => {
      const p = plan();
      if (change === 'date') {
        p.brief.startDate = p.brief.endDate = '2026-10-23';
        p.days = [{ date: '2026-10-23', blocks: [] }];
      }
      if (change === 'revision') p.revision = '2';
      if (change === 'zone') p.destination.timeZone = 'Europe/Kaliningrad';
      if (change === 'catalogue') p.destination.catalogueVersion = '2';
      if (change === 'interests') p.brief.interests = ['culture'];
      if (change === 'pace') p.brief.pace = 'balanced';
      if (change === 'origin') p.brief.originId = null;
      if (change === 'budget') p.brief.totalBudget = null;
      const s = await setup(p);
      await activate(s);
      await generate(s);
      expect(s.state.status()).toBe('unavailable');
      expect(s.state.preview()).toBeNull();
      expect(s.editor.dirty()).toBe(false);
    },
  );
  it.each(['partial', 'empty', 'unsupported'] as const)(
    'preserves explicit %s semantics without copying',
    async (kind) => {
      const p = plan();
      if (kind === 'partial') p.brief.interests = ['architecture', 'food'];
      if (kind === 'unsupported') {
        p.brief.endDate = '2026-10-25';
        p.days = Array.from({ length: 4 }, (_, index) => ({ date: `2026-10-${22 + index}`, blocks: [] }));
      }
      const s = await setup(p);
      await activate(s);
      await generate(s, kind === 'partial' ? partial : kind === 'empty' ? empty : unsupported);
      expect(s.state.status()).toBe(kind);
      expect(s.state.canAdopt()).toBe(kind === 'partial');
      expect(s.editor.dirty()).toBe(false);
    },
  );
  it('blocks generation with form-only dirty or a nonempty saved plan', async () => {
    const s = await setup();
    await activate(s);
    s.editor.touch();
    await s.state.generate();
    s.http.expectNone(candidateUrl);
    expect(s.state.canGenerate()).toBe(false);
  });
  it('does not duplicate pending generation and permits explicit replacement after dismissal', async () => {
    const s = await setup();
    await activate(s);
    const first = s.state.generate();
    await settle();
    const old = s.http.expectOne(candidateUrl);
    await s.state.generate();
    s.http.expectNone(candidateUrl);
    s.state.dismiss();
    expect(old.cancelled).toBe(true);
    await first;
    await generate(s);
    expect(s.state.preview()?.outcome).toBe('candidate');
  });
  it('hides private preview synchronously before auth effects flush', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    s.auth.identityEpoch.update((x) => x + 1);
    s.auth.status.set({ kind: 'anonymous' });
    expect(s.state.preview()).toBeNull();
    expect(s.state.canAdopt()).toBe(false);
    expect(s.state.pending()).toBeNull();
  });
  it('does not issue private requests or expose preview under SSR', async () => {
    const s = await setup(plan(), 'server');
    await s.state.activate();
    await s.state.generate();
    expect(await s.state.adopt()).toBe(false);
    expect(s.state.preview()).toBeNull();
    s.http.expectNone(() => true);
    expect(s.api.detail).not.toHaveBeenCalled();
  });

  it.each(['owner-first', 'availability-first'])(
    'keeps both buffers untouched if the second read fails (%s)',
    async (order) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const before = structuredClone(s.editor.buffer()),
        owner = new Subject<TripPlan>();
      s.api.detail.mockReturnValueOnce(owner);
      const adoption = s.state.adopt();
      await settle();
      const request = s.http.expectOne(availabilityUrl);
      if (order === 'owner-first') {
        owner.next(plan());
        await settle();
        request.error(new ProgressEvent('offline'));
      } else {
        request.flush(bytes(ready));
        await settle();
        owner.error(new HttpErrorResponse({ status: 503 }));
      }
      expect(await adoption).toBe(false);
      expect(s.state.status()).toBe('unavailable');
      expect(s.editor.buffer()).toEqual(before);
      expect(s.editor.baseline()?.revision).toBe('1');
      expect(s.editor.comparison()).toBeNull();
      expect(s.api.replace).not.toHaveBeenCalled();
    },
  );
  it('checks the clock after both replies have arrived, before the adoption continuation', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const owner = new Subject<TripPlan>();
    s.api.detail.mockReturnValueOnce(owner);
    const adoption = s.state.adopt();
    await settle();
    owner.next(plan());
    s.http.expectOne(availabilityUrl).flush(bytes(ready));
    s.setNow(candidate.expiresAt);
    expect(await adoption).toBe(false);
    expect(s.state.status()).toBe('expired');
    expect(s.editor.dirty()).toBe(false);
  });
  it('expires accessors synchronously without polling or effect flushing', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    s.setNow(candidate.expiresAt);
    expect(s.state.canAdopt()).toBe(false);
    expect(s.state.status()).toBe('expired');
    s.state.checkExpiry();
    expect(s.state.preview()).toBeNull();
    expect(s.editor.dirty()).toBe(false);
  });
  it('retains conflict and uncertainty after cleanup effects and never retries a lost M1 mutation', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    s.api.delete.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await s.editor.deletePlan();
    TestBed.tick();
    expect(s.state.status()).toBe('uncertain');
    expect(s.state.preview()).toBeNull();
    expect(s.state.canGenerate()).toBe(false);
    await s.state.generate();
    await s.state.adopt();
    await s.editor.deletePlan();
    expect(s.api.delete).toHaveBeenCalledTimes(1);
  });
  it('reports an M1 conflict after cleanup and retains the original empty local basis', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    s.api.delete.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 412 })));
    await s.editor.deletePlan();
    TestBed.tick();
    expect(s.state.status()).toBe('conflict');
    expect(s.editor.baseline()?.revision).toBe('1');
    expect(s.editor.comparison()).toBeNull();
    expect(s.state.canAdopt()).toBe(false);
  });
  it('cancels stale availability, then permits only the newest explicit read to become ready', async () => {
    const s = await setup();
    const old = s.state.activate();
    await settle();
    const oldRequest = s.http.expectOne(availabilityUrl);
    const latest = s.state.refreshAvailability();
    await settle();
    const newRequest = s.http.expectOne(availabilityUrl);
    expect(oldRequest.cancelled).toBe(true);
    newRequest.flush(bytes(ready));
    await latest;
    await old;
    expect(s.state.availability()).toMatchObject({ kind: 'ready', value: { datasetVersion: '1' } });
  });
  it('does not resurrect availability across plan A to B to A before cleanup', async () => {
    const s = await setup();
    const old = s.state.activate();
    await settle();
    const request = s.http.expectOne(availabilityUrl);
    await s.editor.enter(other);
    await s.editor.enter(id);
    if (!request.cancelled) request.flush(bytes(ready));
    await old;
    expect(s.state.availability().kind).toBe('unknown');
    await activate(s);
    expect(s.state.canGenerate()).toBe(true);
  });
  it('rechecks foundation after token refresh before dispatching a candidate', async () => {
    const s = await setup();
    await activate(s);
    let resolve!: (token: string) => void;
    s.auth.accessToken.mockReturnValueOnce(
      new Promise<string>((r) => {
        resolve = r;
      }),
    );
    const generation = s.state.generate();
    s.editor.touch();
    resolve('token');
    await generation;
    s.http.expectNone(candidateUrl);
    expect(s.state.preview()).toBeNull();
    expect(s.editor.dirty()).toBe(true);
  });
  it('prevents an old adoption from overwriting a dismissed and newly generated preview', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const owner = new Subject<TripPlan>();
    s.api.detail.mockReturnValueOnce(owner);
    const adoption = s.state.adopt();
    await settle();
    const request = s.http.expectOne(availabilityUrl);
    s.state.dismiss();
    await generate(s);
    expect(request.cancelled).toBe(true);
    owner.next(plan());
    owner.complete();
    expect(await adoption).toBe(false);
    expect(s.state.status()).toBe('candidate');
    expect(s.state.canAdopt()).toBe(true);
    expect(s.editor.dirty()).toBe(false);
  });
  it('rejects an independently valid requested-interest partition that omits actual requested tags', async () => {
    const p = plan();
    p.brief.interests = ['architecture', 'culture'];
    const s = await setup(p);
    await activate(s);
    await generate(s);
    expect(s.state.status()).toBe('unavailable');
    expect(s.state.preview()).toBeNull();
  });
  it('rejects empty wire for a different requested date without replacing the editor', async () => {
    const p = plan();
    p.days.push({ date: '2026-10-23', blocks: [] });
    p.brief.endDate = '2026-10-23';
    const s = await setup(p);
    await activate(s);
    await generate(s, empty);
    expect(s.state.status()).toBe('unavailable');
    expect(s.editor.buffer()?.days).toHaveLength(2);
  });
  it('refuses a valid standalone wire exceeding the saved pace walking envelope', async () => {
    const s = await setup();
    await activate(s);
    const wire = structuredClone(candidate);
    wire.days[0].edges[0].minimumMinutes = 40;
    wire.days[0].edges[0].maximumMinutes = 69;
    wire.days[0].edges[0].modelledWalk.pathLengthMillimetres = '3000000';
    expect(() => decodeTripItineraryCandidate(JSON.stringify(wire))).not.toThrow();
    await generate(s, wire);
    expect(s.state.status()).toBe('unavailable');
    expect(s.state.preview()).toBeNull();
  });
  it('refuses a valid standalone wire exceeding the saved pace combined dwell envelope', async () => {
    const s = await setup();
    await activate(s);
    const wire = structuredClone(candidate);
    wire.days[0].blocks.forEach((block) => {
      block.dwellMinutes = 120;
    });
    expect(() => decodeTripItineraryCandidate(JSON.stringify(wire))).not.toThrow();
    await generate(s, wire);
    expect(s.state.status()).toBe('unavailable');
    expect(s.state.preview()).toBeNull();
  });
  it('rejects nonempty saved foundation even when its removal is only local', async () => {
    const p = plan();
    p.days[0].blocks = [
      {
        blockId: '22222222-2222-4222-8222-222222222222',
        title: 'Existing',
        note: null,
        startTime: null,
        endTime: null,
        estimate: null,
      },
    ];
    const s = await setup(p);
    await activate(s);
    expect(s.state.canGenerate()).toBe(false);
    await s.state.generate();
    s.http.expectNone(candidateUrl);
    s.editor.removeBlock(p.days[0].blocks[0].blockId);
    expect(s.state.canGenerate()).toBe(false);
  });
  it('does not persist private preview text in browser storage', async () => {
    const local = vi.spyOn(Storage.prototype, 'setItem');
    const s = await setup();
    await activate(s);
    await generate(s);
    s.state.dismiss();
    s.state.leave();
    expect(local).not.toHaveBeenCalled();
    local.mockRestore();
  });
  it.each([
    [429, 'Trips.ItineraryBusy', 'busy'],
    [504, 'Trips.ItineraryDeadline', 'deadline'],
    [412, 'Trips.RevisionMismatch', 'stale'],
    [409, 'Trips.ItineraryRequiresEmptyPlan', 'conflict'],
    [503, 'Trips.ItineraryUnavailable', 'unavailable'],
    [404, 'Trips.NotFound', 'stale'],
  ] as const)(
    'keeps candidate %s distinct without retry or falsely disabling availability',
    async (code, problem, status) => {
      const s = await setup();
      await activate(s);
      const generation = s.state.generate();
      await settle();
      s.http
        .expectOne(candidateUrl)
        .flush(bytes({ type: 'about:blank', title: 'Error', status: code, code: problem }), {
          status: code,
          statusText: 'Error',
        });
      await generation;
      expect(s.state.status()).toBe(status);
      expect(s.state.preview()).toBeNull();
      expect(s.state.availability().kind).toBe('ready');
      s.http.expectNone(candidateUrl);
      expect(s.editor.dirty()).toBe(false);
    },
  );
  it('times out an availability adoption read without retry and cancels the waiting owner read', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const owner = new Subject<TripPlan>();
    s.api.detail.mockReturnValueOnce(owner);
    vi.useFakeTimers();
    const adoption = s.state.adopt();
    await settle();
    const request = s.http.expectOne(availabilityUrl);
    await vi.advanceTimersByTimeAsync(15001);
    expect(await adoption).toBe(false);
    expect(request.cancelled).toBe(true);
    expect(owner.observed).toBe(false);
    expect(s.state.status()).toBe('unavailable');
    expect(s.editor.dirty()).toBe(false);
  });

  it('does not dispatch either adoption read after dismissal while token refresh is pending', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    let resolve!: (token: string) => void;
    const token = new Promise<string>((r) => {
      resolve = r;
    });
    s.auth.accessToken.mockReturnValue(token);
    const adoption = s.state.adopt();
    await settle();
    s.state.dismiss();
    resolve('token');
    expect(await adoption).toBe(false);
    await settle();
    expect(s.api.detail).toHaveBeenCalledTimes(1);
    s.http.expectNone(availabilityUrl);
    expect(s.editor.dirty()).toBe(false);
  });

  it('uses the actual M1 HTTP decoder for the guarded owner GET and stages only after independent availability', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const ownerClient = TestBed.runInInjectionContext(() => new TripsApiService());
    s.api.detail.mockImplementation((tripId: string, token: string) => ownerClient.detail(tripId, token));
    const adoption = s.state.adopt();
    await settle();
    const owner = s.http.expectOne(`/api/trips/${id}`),
      available = s.http.expectOne(availabilityUrl);
    expect(owner.request.method).toBe('GET');
    expect(owner.request.transferCache).toBe(false);
    expect(owner.request.headers.get('Authorization')).toBe('Bearer token');
    owner.flush(JSON.stringify(plan()), { headers: { ETag: '"1"' } });
    await settle();
    expect(s.editor.dirty()).toBe(false);
    available.flush(bytes(ready));
    expect(await adoption).toBe(true);
    expect(s.editor.baseline()?.revision).toBe('1');
    expect(s.api.replace).not.toHaveBeenCalled();
  });
  it('times out the actual owner GET even when availability already succeeded', async () => {
    const s = await setup();
    await activate(s);
    await generate(s);
    const ownerClient = TestBed.runInInjectionContext(() => new TripsApiService());
    s.api.detail.mockImplementation((tripId: string, token: string) => ownerClient.detail(tripId, token));
    vi.useFakeTimers();
    const adoption = s.state.adopt();
    await settle();
    const owner = s.http.expectOne(`/api/trips/${id}`);
    s.http.expectOne(availabilityUrl).flush(bytes(ready));
    await vi.advanceTimersByTimeAsync(15001);
    expect(await adoption).toBe(false);
    expect(owner.cancelled).toBe(true);
    expect(s.state.status()).toBe('unavailable');
    expect(s.editor.dirty()).toBe(false);
    s.http.expectNone(`/api/trips/${id}`);
  });
  it.each(['nonempty', 'title', 'brief', 'destination', 'tripId'] as const)(
    'rejects guarded owner GET with changed %s even if its revision string is unchanged',
    async (change) => {
      const s = await setup();
      await activate(s);
      await generate(s);
      const remote = plan();
      if (change === 'nonempty')
        remote.days[0].blocks = [
          {
            blockId: '22222222-2222-4222-8222-222222222222',
            title: 'Other',
            note: null,
            startTime: null,
            endTime: null,
            estimate: null,
          },
        ];
      if (change === 'title') remote.title = 'Remote title';
      if (change === 'brief') remote.brief.adults = 3;
      if (change === 'destination') remote.destination.catalogueVersion = '2';
      if (change === 'tripId') remote.tripId = other;
      s.api.detail.mockReturnValueOnce(of(remote));
      const adoption = s.state.adopt();
      await settle();
      s.http.expectOne(availabilityUrl).flush(bytes(ready));
      expect(await adoption).toBe(false);
      expect(s.state.status()).toBe('stale');
      expect(s.editor.buffer()?.title).toBe('Private title');
      expect(s.editor.dirty()).toBe(false);
    },
  );
});
