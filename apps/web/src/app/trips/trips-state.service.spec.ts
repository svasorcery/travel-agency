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

describe('Trips retained clean empty foundation seam', () => {
  function required<T>(value: T | null | undefined): T {
    if (value === null || value === undefined) throw new Error('Required test fixture is absent.');
    return value;
  }
  async function emptySetup() {
    const s = setup(),
      p = plan();
    p.days = p.days.map((day) => ({ ...day, blocks: [] }));
    p.knownUserEstimateSubtotal = '0.00';
    s.api.detail.mockReturnValue(of(p));
    await s.state.enter(id);
    return { ...s, p };
  }
  it('captures only a saved empty clean foundation and stages ordinary days without accepting a new revision', async () => {
    const { state, p, api } = await emptySetup();
    const ticket = required(state.captureEmptyFoundation());
    expect(ticket).not.toBeNull();
    const baseline = structuredClone(state.baseline()),
      before = structuredClone(state.buffer());
    api.detail.mockReturnValueOnce(of({ ...p, revision: '3', title: 'New remote title' }));
    const remote = await state.readEmptyFoundation(ticket, new Subject<void>());
    expect(remote?.revision).toBe('3');
    expect(state.baseline()).toEqual(baseline);
    expect(state.buffer()).toEqual(before);
    expect(state.comparison()).toBeNull();
    expect(state.isEmptyFoundationCurrent(ticket)).toBe(true);
    const days = plan().days;
    expect(state.stageEmptyFoundation(ticket, days)).toBe(true);
    expect(state.buffer()?.days).toEqual(days);
    expect(state.buffer()?.title).toBe(before?.title);
    expect(state.buffer()?.brief).toEqual(before?.brief);
    expect(state.baseline()?.revision).toBe('2');
    expect(state.dirty()).toBe(true);
    expect(state.isEmptyFoundationCurrent(ticket)).toBe(false);
    expect(api.replace).not.toHaveBeenCalled();
  });
  it.each(['form-revert', 'edit-revert', 'read', 'mutation-failed', 'navigation-aba'] as const)(
    'invalidates an opaque ticket permanently across %s',
    async (action) => {
      const { state, api } = await emptySetup(),
        ticket = required(state.captureEmptyFoundation());
      if (action === 'form-revert') {
        state.touch();
        state.touch(false);
      }
      if (action === 'edit-revert') {
        const original = required(state.buffer());
        state.edit({ ...original, title: 'Changed' });
        state.edit(original);
      }
      if (action === 'read') await state.refreshDetail();
      if (action === 'mutation-failed') {
        api.delete.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
        await state.deletePlan();
      }
      if (action === 'navigation-aba') {
        await state.enter(other);
        await state.enter(id);
      }
      expect(state.dirty()).toBe(false);
      expect(state.isEmptyFoundationCurrent(ticket)).toBe(false);
      expect(state.stageEmptyFoundation(ticket, plan().days)).toBe(false);
      expect(state.buffer()?.days[0].blocks).toEqual([]);
    },
  );
  it('does not dispatch the guarded read if its token refresh outlives cancellation', async () => {
    const { state, api, auth } = await emptySetup(),
      ticket = required(state.captureEmptyFoundation()),
      cancel = new Subject<void>();
    let release!: (token: string) => void;
    auth.accessToken.mockReturnValueOnce(
      new Promise<string>((r) => {
        release = r;
      }),
    );
    const read = state.readEmptyFoundation(ticket, cancel);
    const rejected = expect(read).rejects.toThrow();
    cancel.next();
    cancel.complete();
    release('token');
    await rejected;
    expect(api.detail).toHaveBeenCalledTimes(1);
    expect(state.baseline()?.revision).toBe('2');
  });
  it('rejects forged tickets and wrong-date, oversized or empty ordinary staging without touching the editor', async () => {
    const { state } = await emptySetup(),
      ticket = required(state.captureEmptyFoundation()),
      before = structuredClone(state.buffer());
    expect(state.stageEmptyFoundation({ ...ticket }, plan().days)).toBe(false);
    expect(state.stageEmptyFoundation(ticket, [])).toBe(false);
    expect(state.stageEmptyFoundation(ticket, [{ date: '2026-11-15', blocks: plan().days[0].blocks }])).toBe(false);
    const oversized = plan().days;
    oversized[0].blocks[0].note = 'Я'.repeat(501);
    expect(state.stageEmptyFoundation(ticket, oversized)).toBe(false);
    expect(state.buffer()).toEqual(before);
    expect(state.dirty()).toBe(false);
  });
  it('reads stale replies without changing baseline and returns null when the foundation changed during the read', async () => {
    const { state, api, p } = await emptySetup(),
      ticket = required(state.captureEmptyFoundation()),
      reply = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(reply);
    const read = state.readEmptyFoundation(ticket, new Subject<void>());
    await wait();
    state.touch();
    reply.next({ ...p, revision: '3' });
    expect(await read).toBeNull();
    expect(state.baseline()?.revision).toBe('2');
    expect(state.comparison()).toBeNull();
    expect(state.dirty()).toBe(true);
  });
});

describe('Trips common clean saved Rail foundation', () => {
  it('atomically appends to a nonempty saved day once, preserving every prior value without a request', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const before = structuredClone(state.buffer()!);
    const ticket = state.captureSavedFoundation()!;
    expect(ticket).not.toBeNull();
    expect(state.captureEmptyFoundation()).toBeNull();
    const block = {
      blockId: other,
      title: 'Поезд 001 (демо)',
      note: 'Вымышленное демо',
      startTime: null,
      endTime: null,
      estimate: null,
    };
    expect(state.appendSavedFoundation(ticket, before.days[0].date, block)).toBe(true);
    expect(state.buffer()).toEqual({
      ...before,
      days: [{ ...before.days[0], blocks: [...before.days[0].blocks, block] }],
    });
    expect(state.appendSavedFoundation(ticket, before.days[0].date, block)).toBe(false);
    expect(api.replace).not.toHaveBeenCalled();
    expect(api.detail).toHaveBeenCalledTimes(1);
  });
  it('rejects opening during a detail read and keeps a failed read from reviving the original ticket', async () => {
    const { state, api } = setup();
    await state.enter(id);
    const ticket = state.captureSavedFoundation()!;
    const pending = new Subject<ReturnType<typeof plan>>();
    api.detail.mockReturnValueOnce(pending);
    const reading = state.refreshDetail();
    await wait();
    expect(state.captureSavedFoundation()).toBeNull();
    expect(state.isSavedFoundationCurrent(ticket)).toBe(false);
    pending.error(new HttpErrorResponse({ status: 503 }));
    await reading;
    expect(state.isSavedFoundationCurrent(ticket)).toBe(false);
  });
  it.each(['form', 'edit', 'refresh', 'owner', 'leave'] as const)(
    'invalidates a saved ticket on %s',
    async (action) => {
      const { state, auth } = setup();
      await state.enter(id);
      const ticket = state.captureSavedFoundation()!;
      if (action === 'form') {
        state.touch();
        state.touch(false);
      }
      if (action === 'edit') state.edit(state.buffer()!);
      if (action === 'refresh') await state.refreshDetail();
      if (action === 'owner') auth.identityEpoch.update((x) => x + 1);
      if (action === 'leave') state.leave();
      expect(state.isSavedFoundationCurrent(ticket)).toBe(false);
    },
  );
  it('rejects a ninth block and invalid note atomically through the actual M1 codec', async () => {
    const { state, api } = setup();
    const p = plan();
    p.days[0].blocks = Array.from({ length: 8 }, (_, i) => ({
      ...p.days[0].blocks[0],
      blockId: `11111111-1111-4111-8111-${String(i + 1).padStart(12, '0')}`,
    }));
    api.detail.mockReturnValue(of(p));
    await state.enter(id);
    const ticket = state.captureSavedFoundation()!,
      before = structuredClone(state.buffer());
    const block = { blockId: other, title: 'Поезд (демо)', note: null, startTime: null, endTime: null, estimate: null };
    expect(state.appendSavedFoundation(ticket, p.days[0].date, block)).toBe(false);
    expect(state.buffer()).toEqual(before);
    expect(state.isSavedFoundationCurrent(ticket)).toBe(true);
  });
});

describe('Trips Rail full-write boundaries', () => {
  it.each(['note-scalars', 'note-control', 'title-scalars', 'duplicate-id'] as const)(
    'rejects %s through the real codec without even one local edit',
    async (fault) => {
      const { state } = setup();
      await state.enter(id);
      const ticket = state.captureSavedFoundation()!,
        before = structuredClone(state.buffer());
      const block = {
        blockId: other,
        title: 'Поезд (демо)',
        note: 'x'.repeat(500),
        startTime: null,
        endTime: null,
        estimate: null,
      };
      if (fault === 'note-scalars') block.note += '🚆';
      if (fault === 'note-control') block.note = 'a\tb';
      if (fault === 'title-scalars') block.title = '🚆'.repeat(101);
      if (fault === 'duplicate-id') block.blockId = state.buffer()!.days[0].blocks[0].blockId;
      const edits = vi.spyOn(state, 'edit');
      expect(state.appendSavedFoundation(ticket, state.buffer()!.days[0].date, block)).toBe(false);
      expect(state.buffer()).toEqual(before);
      expect(edits).not.toHaveBeenCalled();
      expect(state.isSavedFoundationCurrent(ticket)).toBe(true);
    },
  );
  it('accepts a 500-scalar note and a seventh-to-eighth append with one edit', async () => {
    const { state, api } = setup();
    const p = plan(),
      base = p.days[0].blocks[0];
    p.days[0].blocks = Array.from({ length: 7 }, (_, i) => ({
      ...base,
      blockId: `11111111-1111-4111-8111-${String(i + 1).padStart(12, '0')}`,
    }));
    api.detail.mockReturnValue(of(p));
    await state.enter(id);
    const ticket = state.captureSavedFoundation()!;
    const edits = vi.spyOn(state, 'edit'),
      block = { ...base, blockId: other, note: '🚆'.repeat(500) };
    expect(state.appendSavedFoundation(ticket, p.days[0].date, block)).toBe(true);
    expect(edits).toHaveBeenCalledTimes(1);
    expect(state.buffer()!.days[0].blocks).toHaveLength(8);
    expect(state.buffer()!.days[0].blocks[7].note).toBe('🚆'.repeat(500));
  });
  it('rejects a prospective UTF8 full write above 128KiB while retaining all IDs, money and notes', async () => {
    const { state, api } = setup();
    const p = plan(),
      base = p.days[0].blocks[0];
    p.brief.endDate = '2026-11-27';
    p.days = Array.from({ length: 14 }, (_, i) => ({
      date: `2026-11-${String(14 + i).padStart(2, '0')}`,
      blocks: Array.from({ length: i === 13 ? 7 : 8 }, (_, j) => ({
        ...base,
        blockId: `11111111-1111-4111-8111-${String(i * 8 + j + 1).padStart(12, '0')}`,
        note: '界'.repeat(500),
      })),
    }));
    // Actual M1 decoder determines the exact valid baseline, not a duplicate byte validator.
    let fits = false;
    for (let length = 500; length >= 0; length--) {
      try {
        tripWrite(p);
        fits = true;
        break;
      } catch {
        for (const day of p.days) for (const block of day.blocks) block.note = block.note!.slice(0, -1);
      }
    }
    expect(fits).toBe(true);
    api.detail.mockReturnValue(of(p));
    await state.enter(id);
    const ticket = state.captureSavedFoundation()!,
      before = structuredClone(state.buffer());
    const block = { ...base, blockId: other, note: '界'.repeat(500) };
    expect(state.appendSavedFoundation(ticket, p.days[13].date, block)).toBe(false);
    expect(state.buffer()).toEqual(before);
  });
});

it('rechecks source freshness after full write validation immediately before the single edit', async () => {
  const { state } = setup();
  await state.enter(id);
  const ticket = state.captureSavedFoundation()!,
    before = structuredClone(state.buffer());
  const block = { blockId: other, title: 'Поезд (демо)', note: null, startTime: null, endTime: null, estimate: null };
  expect(state.appendSavedFoundation(ticket, state.buffer()!.days[0].date, block, () => false)).toBe(false);
  expect(state.buffer()).toEqual(before);
});

describe('Walking owner-read ownership excludes competing Rail foundations', () => {
  async function emptyBasis() {
    const s = setup(),
      p = plan();
    p.days[0].blocks = [];
    p.knownUserEstimateSubtotal = '0.00';
    s.api.detail.mockReturnValue(of(p));
    await s.state.enter(id);
    return { ...s, p };
  }
  it.each(['success', 'failure', 'cancelled'] as const)(
    'invalidates prior Rail authority across actual guarded owner-read %s without invalidating the walking owner',
    async (outcome) => {
      const { state, api, p } = await emptyBasis(),
        rail = state.captureSavedFoundation()!,
        walking = state.captureEmptyFoundation()!,
        before = structuredClone(state.buffer());
      const owner = new Subject<ReturnType<typeof plan>>(),
        cancel = new Subject<void>();
      api.detail.mockReturnValueOnce(owner);
      const read = state.readEmptyFoundation(walking, cancel),
        settled = read.then(
          (value) => ({ value }),
          (error) => ({ error }),
        );
      await wait();
      expect(api.detail).toHaveBeenCalledTimes(2);
      expect(owner.observed).toBe(true);
      expect(state.canCaptureSavedFoundation()).toBe(false);
      expect(state.captureSavedFoundation()).toBeNull();
      expect(state.captureEmptyFoundation()).toBeNull();
      expect(state.isSavedFoundationCurrent(rail)).toBe(false);
      expect(state.isEmptyFoundationCurrent(walking)).toBe(true);
      const block = {
        blockId: other,
        title: 'Forbidden Rail',
        note: null,
        startTime: null,
        endTime: null,
        estimate: null,
      };
      expect(state.appendSavedFoundation(rail, p.days[0].date, block)).toBe(false);
      expect(state.buffer()).toEqual(before);
      if (outcome === 'success') {
        owner.next(p);
        owner.complete();
      }
      if (outcome === 'failure') owner.error(new HttpErrorResponse({ status: 503 }));
      if (outcome === 'cancelled') {
        cancel.next();
        expect(state.canCaptureSavedFoundation()).toBe(true);
        expect(state.isSavedFoundationCurrent(rail)).toBe(false);
      }
      const result = await settled;
      expect(state.isSavedFoundationCurrent(rail)).toBe(false);
      expect(state.buffer()).toEqual(before);
      expect(state.baseline()).toEqual(p);
      if (outcome === 'success') {
        expect('value' in result && result.value).toEqual(p);
        expect(state.isEmptyFoundationCurrent(walking)).toBe(true);
        expect(state.stageEmptyFoundation(walking, plan().days)).toBe(true);
      } else expect(state.captureSavedFoundation()).not.toBeNull();
      expect(api.replace).not.toHaveBeenCalled();
    },
  );
});

it('does not let the old walking owner-read cleanup clear a newer real detail read', async () => {
  const { state, api } = setup(),
    p = plan();
  p.days[0].blocks = [];
  p.knownUserEstimateSubtotal = '0.00';
  api.detail.mockReturnValue(of(p));
  await state.enter(id);
  const walking = state.captureEmptyFoundation()!,
    rail = state.captureSavedFoundation()!,
    owner = new Subject<ReturnType<typeof plan>>(),
    fresh = new Subject<ReturnType<typeof plan>>();
  api.detail.mockReturnValueOnce(owner).mockReturnValueOnce(fresh);
  const read = state.readEmptyFoundation(walking, new Subject<void>());
  await wait();
  const refresh = state.refreshDetail();
  await wait();
  expect(state.isEmptyFoundationCurrent(walking)).toBe(false);
  owner.next(p);
  owner.complete();
  expect(await read).toBeNull();
  expect(state.captureSavedFoundation()).toBeNull();
  fresh.next(p);
  fresh.complete();
  await refresh;
  expect(state.captureSavedFoundation()).not.toBeNull();
  expect(state.isSavedFoundationCurrent(rail)).toBe(false);
});
