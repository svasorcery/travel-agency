import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { decodeTripPlan, TripsApiService, tripWrite } from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TripsStateService } from './trips-state.service';

const id = tripsExample.tripId,
  other = '33333333-3333-4333-8333-333333333333';
const plan = () => decodeTripPlan(structuredClone(tripsExample));
function setup() {
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
    detail: vi.fn().mockImplementation(() => of(plan())),
    list: vi.fn().mockReturnValue(of({ items: [], nextCursor: null })),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn(),
  };
  const availability = {
    status: signal('ready'),
    enabled: () => true,
    catalogue: signal({ catalogueVersion: '1', items: [tripsExample.destination] }),
    initialize: vi.fn().mockResolvedValue(undefined),
    clearPrivate: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: TripsApiService, useValue: api },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsAvailabilityService, useValue: availability },
    ],
  });
  return { state: TestBed.inject(TripsStateService), api, auth };
}
const wait = async () => {
  await Promise.resolve();
  await Promise.resolve();
};
describe('Trips private editor epochs and explicit mutations', () => {
  it('detaches pending mutations on plan navigation without clearing a newer plan operation', async () => {
    const { state, api } = setup();
    api.detail.mockImplementation((tripId: string) => of({ ...plan(), tripId }));
    await state.enter(id);
    state.edit({ ...state.buffer()!, title: 'A pending' });
    const a = new Subject<ReturnType<typeof plan>>();
    api.replace.mockReturnValueOnce(a);
    const saveA = state.save();
    await vi.waitFor(() => expect(api.replace).toHaveBeenCalledTimes(1));
    await state.enter(other);
    expect(state.pending()).toBeNull();
    state.edit({ ...state.buffer()!, title: 'B pending' });
    const b = new Subject<ReturnType<typeof plan>>();
    api.replace.mockReturnValueOnce(b);
    const saveB = state.save();
    await vi.waitFor(() => expect(api.replace).toHaveBeenCalledTimes(2));
    a.next({ ...plan(), title: 'A pending', revision: '3' });
    a.complete();
    await saveA;
    expect(state.pending()).toBe('save');
    expect(state.buffer()?.title).toBe('B pending');
    b.next({ ...plan(), tripId: other, title: 'B pending', revision: '3' });
    b.complete();
    await saveB;
    expect(state.pending()).toBeNull();
    expect(state.baseline()?.tripId).toBe(other);
  });
  it.each(['destinationId', 'currency'] as const)(
    'requires saving block removal before changing %s against the saved baseline',
    async (key) => {
      const { state, api } = setup();
      await state.enter(id);
      state.removeBlock(state.buffer()!.days[0].blocks[0].blockId);
      const before = JSON.stringify(state.buffer());
      const brief = {
        ...state.buffer()!.brief,
        ...(key === 'currency' ? { currency: 'EUR' as const } : { destinationId: 'kaliningrad' as const }),
      };
      expect(state.changeBrief(brief)).toBe(false);
      expect(JSON.stringify(state.buffer())).toBe(before);
      expect(state.message()).toContain('сохраните');
      api.replace.mockReturnValue(
        of({
          ...plan(),
          revision: '3',
          days: state.buffer()!.days,
          knownUserEstimateSubtotal: '0.00',
          unknownEstimateCount: 0,
        }),
      );
      await state.save();
      expect(state.changeBrief(brief)).toBe(true);
    },
  );
  it('requires saving moves off a populated saved date before shortening that date away', async () => {
    const { state, api } = setup();
    const p = plan();
    p.brief.endDate = '2026-11-15';
    p.days.push({ date: '2026-11-15', blocks: [] });
    api.detail.mockReturnValue(of(p));
    await state.enter(id);
    const block = state.buffer()!.days[0].blocks[0];
    state.moveBlock(block.blockId, '2026-11-15', 0);
    const before = JSON.stringify(state.buffer());
    const brief = { ...state.buffer()!.brief, startDate: '2026-11-15' };
    expect(state.changeBrief(brief)).toBe(false);
    expect(JSON.stringify(state.buffer())).toBe(before);
    expect(state.message()).toContain('сохраните');
    api.replace.mockReturnValue(of({ ...p, revision: '3', days: state.buffer()!.days }));
    await state.save();
    expect(state.changeBrief(brief)).toBe(true);
    expect(state.buffer()?.days[0].blocks[0].blockId).toBe(block.blockId);
  });
  it('keeps newly extended dates empty until their first explicit save', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const block = state.buffer()!.days[0].blocks[0];
    state.changeBrief({ ...state.buffer()!.brief, endDate: '2026-11-15' });
    const before = JSON.stringify(state.buffer());
    state.addBlock('2026-11-15');
    expect(JSON.stringify(state.buffer())).toBe(before);
    expect(state.message()).toContain('сохраните');
    state.moveBlock(block.blockId, '2026-11-15', 0);
    expect(JSON.stringify(state.buffer())).toBe(before);
    api.replace.mockReturnValue(
      of({ ...plan(), revision: '3', brief: state.buffer()!.brief, days: state.buffer()!.days }),
    );
    await state.save();
    state.moveBlock(block.blockId, '2026-11-15', 0);
    expect(state.buffer()?.days[1].blocks[0].blockId).toBe(block.blockId);
  });
  it('recovers a known capacity rejection only after a successful owner list reread and explicit creation', async () => {
    const { state, api } = setup();
    await state.enter(null);
    state.edit(tripWrite(plan()));
    api.create.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { type: 'about:blank', status: 409, code: 'Trips.CapacityExceeded' },
          }),
      ),
    );
    await state.create();
    expect(state.status()).toBe('conflict');
    expect(state.selectedId()).toBeNull();
    await state.create();
    expect(api.create).toHaveBeenCalledTimes(1);
    await state.loadList();
    expect(api.create).toHaveBeenCalledTimes(1);
    expect(state.mutationBlocked()).toBe(false);
    api.create.mockReturnValue(of(plan()));
    await state.create();
    expect(api.create).toHaveBeenCalledTimes(2);
    expect(state.baseline()?.tripId).toBe(id);
  });
  it('shows an invalid detail route without dispatching a private request', async () => {
    const { state, api } = setup();
    await state.enter('invalid');
    expect(state.status()).toBe('not-found');
    expect(state.buffer()).toBeNull();
    expect(api.detail).not.toHaveBeenCalled();
  });
  it('keeps storage failure distinct from an empty initial owner list', async () => {
    const { state, api } = setup();
    api.list.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    await state.enter(null);
    expect(state.status()).toBe('unavailable');
    expect(state.buffer()).toBeNull();
    expect(state.rows()).toEqual([]);
  });
  it.each(['create', 'save'] as const)(
    'retains uncertain %s after a failed reread or local validation',
    async (action) => {
      const { state, api } = setup();
      await state.enter(id);
      if (action === 'create') {
        state.newPlan();
        state.edit(tripWrite(plan()));
      } else state.edit({ ...state.buffer()!, title: 'local' });
      const method = action === 'create' ? 'create' : 'replace';
      api[method].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
      await (action === 'create' ? state.create() : state.save());
      const read = action === 'create' ? 'list' : 'detail';
      api[read].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
      await (action === 'create' ? state.loadList() : state.refreshDetail());
      state.validation('Invalid local edit');
      await (action === 'create' ? state.create() : state.save());
      expect(api[method]).toHaveBeenCalledTimes(1);
      expect(state.status()).toBe('uncertain');
    },
  );
  it('keeps B dirty when save A succeeds and uses accepted revision for the next explicit save', async () => {
    const { state, api } = setup();
    await state.enter(id);
    state.edit({ ...state.buffer()!, title: 'A' });
    const pending = new Subject<ReturnType<typeof plan>>();
    api.replace.mockReturnValue(pending);
    const save = state.save();
    await wait();
    state.edit({ ...state.buffer()!, title: 'B' });
    pending.next({ ...plan(), title: 'A', revision: '3' });
    pending.complete();
    await save;
    expect(state.baseline()?.title).toBe('A');
    expect(state.baseline()?.revision).toBe('3');
    expect(state.buffer()?.title).toBe('B');
    expect(state.dirty()).toBe(true);
    api.replace.mockReturnValue(of({ ...plan(), title: 'B', revision: '4' }));
    await state.save();
    expect(api.replace.mock.calls[1][1]).toBe('3');
    expect(state.dirty()).toBe(false);
  });
  it('drops old A responses across A to B to A navigation', async () => {
    const { state, api } = setup();
    const old = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(old);
    const first = state.enter(id);
    await vi.waitFor(() => expect(api.detail).toHaveBeenCalledTimes(1));
    await state.enter(other);
    await state.enter(id);
    old.next({ ...plan(), title: 'OLD' });
    old.complete();
    await first;
    expect(state.buffer()?.title).toBe(tripsExample.title);
  });
  it('does not regress on a GET that predates a saved newer revision', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const old = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(old);
    const read = state.refreshDetail();
    await wait();
    state.edit({ ...state.buffer()!, title: 'changed' });
    api.replace.mockReturnValue(of({ ...plan(), title: 'changed', revision: '3' }));
    await state.save();
    old.next(plan());
    old.complete();
    await read;
    expect(state.baseline()?.revision).toBe('3');
    expect(state.buffer()?.title).toBe('changed');
  });
  it('does not overwrite edits made during a GET, and requires explicit reconciliation', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const response = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(response);
    const read = state.refreshDetail();
    await wait();
    state.edit({ ...state.buffer()!, title: 'local' });
    response.next({ ...plan(), title: 'remote', revision: '3' });
    response.complete();
    await read;
    expect(state.buffer()?.title).toBe('local');
    expect(state.comparison()?.title).toBe('remote');
    expect(state.status()).toBe('conflict');
    await state.save();
    expect(api.replace).not.toHaveBeenCalled();
    state.keepLocalAgainstCurrent();
    api.replace.mockReturnValue(of({ ...plan(), title: 'local', revision: '4' }));
    await state.save();
    expect(api.replace.mock.calls[0][1]).toBe('3');
  });
  it('ignores a GET after deletion, clears memory and refreshes the owner list', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const old = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(old);
    const read = state.refreshDetail();
    await wait();
    api.delete.mockReturnValue(of(undefined));
    await state.deletePlan();
    old.next(plan());
    old.complete();
    await read;
    expect(state.buffer()).toBeNull();
    expect(state.baseline()).toBeNull();
    expect(state.selectedId()).toBeNull();
  });
  it('clears private data on logout before effects flush and discards late replies', async () => {
    const { state, api, auth } = setup();
    await state.enter(id);
    state.edit({ ...state.buffer()!, title: 'private dirty' });
    const old = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(old);
    const read = state.refreshDetail();
    await wait();
    auth.identityEpoch.update((v) => v + 1);
    auth.status.set({ kind: 'anonymous' });
    expect(state.buffer()).toBeNull();
    TestBed.tick();
    old.next(plan());
    old.complete();
    await read;
    expect(state.buffer()).toBeNull();
    expect(state.rows()).toEqual([]);
  });
  it.each(['create', 'save', 'delete'] as const)(
    'preserves unknown %s without repeat until authoritative reread',
    async (action) => {
      const { state, api } = setup();
      await state.enter(id);
      if (action === 'create') {
        state.newPlan();
        state.edit(tripWrite(plan()));
      } else state.edit({ ...state.buffer()!, title: 'changed' });
      const method = action === 'save' ? 'replace' : action === 'delete' ? 'delete' : 'create';
      api[method].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
      const invoke = () =>
        action === 'create' ? state.create() : action === 'save' ? state.save() : state.deletePlan();
      await invoke();
      expect(state.status()).toBe('uncertain');
      expect(state.buffer()).not.toBeNull();
      await invoke();
      expect(api[method]).toHaveBeenCalledTimes(1);
    },
  );
  it('blocks double submit while allowing edits during save', async () => {
    const { state, api } = setup();
    await state.enter(id);
    state.edit({ ...state.buffer()!, title: 'A' });
    const pending = new Subject<ReturnType<typeof plan>>();
    api.replace.mockReturnValue(pending);
    const a = state.save();
    await wait();
    await state.save();
    expect(api.replace).toHaveBeenCalledTimes(1);
    pending.next({ ...plan(), title: 'A', revision: '3' });
    pending.complete();
    await a;
  });
  it('preserves IDs/order and guards populated dates, city and currency without data loss', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const before = JSON.stringify(state.buffer());
    expect(state.changeBrief({ ...state.buffer()!.brief, destinationId: 'kaliningrad' })).toBe(false);
    expect(JSON.stringify(state.buffer())).toBe(before);
    expect(state.changeBrief({ ...state.buffer()!.brief, currency: 'EUR' })).toBe(false);
    expect(state.changeBrief({ ...state.buffer()!.brief, startDate: '2026-11-15', endDate: '2026-11-15' })).toBe(false);
    expect(state.changeBrief({ ...state.buffer()!.brief, endDate: '2026-11-15' })).toBe(true);
    api.replace.mockReturnValue(
      of({ ...plan(), revision: '3', brief: state.buffer()!.brief, days: state.buffer()!.days }),
    );
    await state.save();
    const block = state.buffer()!.days[0].blocks[0];
    state.moveBlock(block.blockId, '2026-11-15', 0);
    expect(state.buffer()!.days[1].blocks[0].blockId).toBe(block.blockId);
    expect(state.buffer()!.days[0].blocks).toEqual([]);
  });
});
