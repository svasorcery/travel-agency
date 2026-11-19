import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  FlightLinkRequestError,
  type FlightLinkStatus,
  type TripFlightLinks,
  TripsFlightLinksApiService,
} from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
import { TripsFlightLinksStateService } from './trips-flight-links-state.service';
import { TripsStateService } from './trips-state.service';

const id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  order = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  other = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const now = '2026-11-17T00:00:00Z';
const meta = (rev: string, ...ids: string[]): TripFlightLinks => ({
  tripId: id,
  linksRevision: rev,
  links: ids.map((orderId) => ({ orderId, linkedAt: now })),
});
const status = (rev: string, version: string, ...ids: string[]): FlightLinkStatus => ({
  tripId: id,
  linksRevision: rev,
  items: ids.map((orderId) => ({
    orderId,
    state: 'available',
    bookingStatus: 'Held',
    passengerCount: 1,
    route: null,
    observedAt: now,
    reviewRequired: false,
    streamVersion: version,
  })),
});
async function setup() {
  const auth = {
    status: signal({ kind: 'authenticated', userId: 'fictional-a' }),
    identityEpoch: signal(0),
    accessToken: vi.fn().mockResolvedValue('token'),
  };
  const clean = signal(true);
  const editor = {
    auth,
    baseline: signal({ tripId: id }),
    selectedId: signal(id),
    canCaptureSavedFoundation: clean,
    captureSavedFoundation: () => (clean() ? {} : null),
    isSavedFoundationCurrent: clean,
  };
  const api = {
    availability: vi.fn().mockReturnValue(of(true)),
    metadata: vi.fn().mockReturnValue(of(meta('2', order, other))),
    status: vi.fn().mockReturnValue(of(status('2', '15', order, other))),
    patch: vi.fn(),
    candidates: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      TripsFlightLinksStateService,
      { provide: TripsStateService, useValue: editor },
      { provide: TripsFlightLinksApiService, useValue: api },
    ],
  });
  const state = TestBed.inject(TripsFlightLinksStateService);
  TestBed.tick();
  await vi.waitFor(() => expect(state.cards()[0]?.state).toBe('available'));
  return { state, editor, api, clean };
}
it('first applies the metadata from another tab and clears removed cards before its only observation', async () => {
  const { api, state } = await setup();
  const source = new Subject<FlightLinkStatus>();
  api.metadata.mockReturnValue(of(meta('3', other)));
  api.status.mockReturnValue(source);
  const read = state.refresh();
  await vi.waitFor(() => expect(api.status).toHaveBeenCalledTimes(2));
  expect(state.cards().map((c) => c.orderId)).toEqual([other]);
  expect(api.metadata.mock.invocationCallOrder[1]).toBeLessThan(api.status.mock.invocationCallOrder[1]);
  source.next(status('4', '17', order, other));
  await read;
  expect(state.message()).toContain('Состав поездки изменился');
  expect(state.canMutate()).toBe(false);
  expect(api.status).toHaveBeenCalledTimes(2);
  api.metadata.mockReturnValue(of(meta('4')));
  await state.refresh();
  expect(state.cards()).toEqual([]);
  expect(api.status).toHaveBeenCalledTimes(2);
  expect(api.patch).not.toHaveBeenCalled();
});
it('marks older observations or outages stale and erases prior facts for concealed missing orders', async () => {
  const { api, state } = await setup();
  api.status.mockReturnValue(of(status('2', '14', order, other)));
  await state.refresh();
  expect(state.cards()[0].facts?.streamVersion).toBe('15');
  expect(state.cards()[0].state).toBe('stale');
  api.status.mockReturnValue(throwError(() => new FlightLinkRequestError('unavailable')));
  await state.refresh();
  expect(state.canMutate()).toBe(true);
  expect(state.cards()[1].state).toBe('stale');
  api.status.mockReturnValue(
    of({
      tripId: id,
      linksRevision: '2',
      items: [
        { orderId: order, state: 'notFound' },
        { orderId: other, state: 'unavailable' },
      ],
    }),
  );
  await state.refresh();
  expect(state.cards()[0]).toEqual({ orderId: order, state: 'notFound', highestVersion: '15' });
  expect(state.cards()[1].facts).toBeDefined();
  api.status.mockReturnValue(of(status('2', '14', order, other)));
  await state.refresh();
  expect(state.cards()[0].facts).toBeUndefined();
});
it.each(['add', 'remove'] as const)('losing a %s reply blocks another write until an explicit reread', async (op) => {
  const { api, state, clean } = await setup();
  const candidate = { orderId: id, route: null, passengerCount: 1, bookingStatus: 'Held' };
  api.candidates.mockReturnValue(of({ tripId: id, items: [candidate], nextOffset: null, limitReached: false }));
  await state.loadCandidates();
  api.patch.mockReturnValue(throwError(() => new FlightLinkRequestError('contract')));
  const target = op === 'add' ? id : order;
  clean.set(false);
  await state.mutate(op, target);
  expect(api.patch).not.toHaveBeenCalled();
  clean.set(true);
  await Promise.all([state.mutate(op, target), state.mutate(op, target)]);
  expect(api.patch).toHaveBeenCalledTimes(1);
  expect(state.barrier()).toBe('unknown');
  await state.mutate(op, target);
  expect(api.patch).toHaveBeenCalledTimes(1);
  await state.refresh();
  expect(state.barrier()).toBeNull();
  expect(state.message()).toContain('прежнего запроса неизвестен');
  expect(api.patch).toHaveBeenCalledTimes(1);
});
it.each(['owner', 'trip'] as const)(
  'clears private cards and ignores late results after a %s change',
  async (change) => {
    const { api, state, editor } = await setup();
    expect(state.calendarCards()).toHaveLength(2);
    const source = new Subject<FlightLinkStatus>();
    api.status.mockReturnValue(source);
    const read = state.refresh();
    await vi.waitFor(() => expect(api.status).toHaveBeenCalledTimes(2));
    if (change === 'owner') {
      editor.auth.status.set({ kind: 'anonymous', userId: '' });
      editor.auth.identityEpoch.update((n) => n + 1);
    } else editor.selectedId.set(other);
    expect(state.visibleCapability()).toBe('checking');
    expect(state.calendarCards()).toEqual([]);
    TestBed.tick();
    source.next(status('2', '99', order, other));
    await read;
    expect(state.saved()).toBeNull();
    expect(state.cards()).toEqual([]);
    expect(state.calendarCards()).toEqual([]);
    expect(state.picker()).toBe(false);
  },
);
it.each(['metadata', 'status'] as const)(
  'drops old private facts when %s reports a deleted or concealed plan',
  async (resource) => {
    const { api, state } = await setup();
    api.candidates.mockReturnValue(of({ tripId: id, items: [], nextOffset: null, limitReached: false }));
    await state.loadCandidates();
    expect(state.picker()).toBe(true);
    api[resource].mockReturnValue(throwError(() => new FlightLinkRequestError('notFound')));
    await state.refresh();
    expect(state.saved()).toBeNull();
    expect(state.cards()).toEqual([]);
    expect(state.picker()).toBe(false);
    expect(state.canMutate()).toBe(false);
    expect(api.patch).not.toHaveBeenCalled();
  },
);
it('stale PATCH blocks further edits and requires metadata before another explicit operation', async () => {
  const { api, state } = await setup();
  api.patch.mockReturnValue(throwError(() => new FlightLinkRequestError('conflict')));
  await state.mutate('remove', order);
  expect(state.barrier()).toBe('conflict');
  expect(api.metadata).toHaveBeenCalledTimes(1);
  await state.mutate('remove', order);
  expect(api.patch).toHaveBeenCalledTimes(1);
});
