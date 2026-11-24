import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type TripHotelLinkStatus, TripsHotelLinkApiService } from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
import { projectTripHotelCalendar } from './trips-hotel-calendar';
import { TripsHotelLinkStateService } from './trips-hotel-link-state.service';
import { TripsStateService } from './trips-state.service';

const id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const confirmed = {
  state: 'confirmed',
  revision: '3',
  propertyName: '<b>Белые ночи</b>',
  reservationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  checkIn: '2027-06-13',
  checkOut: '2027-06-15',
} as const;

async function settle() {
  for (let i = 0; i < 20; i++) await Promise.resolve();
}

function setup() {
  const selectedId = signal(id);
  const identityEpoch = signal(1);
  const hotelReadAccess = signal({ allowed: true });
  const auth = {
    status: signal({ kind: 'authenticated', userId: id }),
    identityEpoch,
    hotelReadAccess,
    accessToken: vi.fn(async () => 'trips-session'),
    deny: vi.fn(),
  };
  const status = vi.fn(() => of<TripHotelLinkStatus>(confirmed));
  const patch = vi.fn(() => of({ hasLink: false, revision: '4' }));
  TestBed.configureTestingModule({
    providers: [
      TripsHotelLinkStateService,
      {
        provide: TripsStateService,
        useValue: { auth, selectedId, baseline: signal({ tripId: id }), status: signal('ready') },
      },
      {
        provide: TripsHotelLinkApiService,
        useValue: {
          availability: () => of({ enabled: true, canAttach: false }),
          metadata: () => of({ hasLink: true, revision: '3' }),
          status,
          patch,
        },
      },
    ],
  });
  const state = TestBed.inject(TripsHotelLinkStateService);
  TestBed.tick();
  return { state, status, selectedId, identityEpoch, hotelReadAccess, patch };
}

it('projects local day strings and clips to the currently displayed days', () => {
  const visible = ['2027-06-13', '2027-06-14', '2027-06-15'].map((date) => ({ date }));
  expect([...projectTripHotelCalendar(visible, confirmed)]).toEqual([
    ['2027-06-13', 'Заезд'],
    ['2027-06-14', 'Проживание'],
    ['2027-06-15', 'Выезд'],
  ]);
  expect([...projectTripHotelCalendar(visible.slice(0, 2), confirmed)]).toHaveLength(2);
  expect([...projectTripHotelCalendar(visible.slice(2), confirmed)]).toEqual([['2027-06-15', 'Выезд']]);
  expect([...projectTripHotelCalendar([{ date: '2027-06-16' }], confirmed)]).toEqual([]);
});

it('clears dated facts as refresh starts and keeps only metadata after a source failure', async () => {
  const { state, status } = setup();
  await settle();
  expect(state.confirmed()).toEqual(confirmed);
  status.mockReturnValueOnce(throwError(() => new Error('unavailable')));
  const refresh = state.refresh();
  expect(state.confirmed()).toBeNull();
  await refresh;
  expect(state.confirmed()).toBeNull();
  expect(state.canUnlink()).toBe(true);
});

it('ignores a delayed private response after the current scope changes', async () => {
  const { state, status, selectedId } = setup();
  await settle();
  const delayed = new Subject<TripHotelLinkStatus>();
  status.mockReturnValueOnce(delayed);
  const refresh = state.refresh();
  await settle();
  selectedId.set('cccccccc-cccc-4ccc-8ccc-cccccccccccc');
  expect(state.confirmed()).toBeNull();
  delayed.next(confirmed);
  await refresh;
  expect(state.confirmed()).toBeNull();
});

it('hides confirmed hotel facts immediately on scope loss without discarding unlink metadata', async () => {
  const { state, status, identityEpoch, hotelReadAccess } = setup();
  await settle();
  expect(state.confirmed()).toEqual(confirmed);
  hotelReadAccess.set({ allowed: false });
  expect(identityEpoch()).toBe(1);
  expect(state.confirmed()).toBeNull();
  expect(state.status()).toEqual({ revision: '3', state: 'unavailable' });
  expect(state.canUnlink()).toBe(true);
  await state.refresh();
  expect(status).toHaveBeenCalledTimes(1);
  expect(state.canUnlink()).toBe(true);
});

it('cancels an in-flight status on scope loss and requires a fresh read after scope returns', async () => {
  const { state, status, hotelReadAccess } = setup();
  await settle();
  const delayed = new Subject<TripHotelLinkStatus>();
  status.mockReturnValueOnce(delayed);
  const refresh = state.refresh();
  await settle();
  expect(delayed.observed).toBe(true);
  hotelReadAccess.set({ allowed: false });
  TestBed.tick();
  expect(delayed.observed).toBe(false);
  hotelReadAccess.set({ allowed: true });
  delayed.next(confirmed);
  await refresh;
  expect(state.confirmed()).toBeNull();
  expect(state.canUnlink()).toBe(true);
  await state.refresh();
  expect(state.confirmed()).toEqual(confirmed);
});

it('removes the association using only Trips authority while the Hotels source is unavailable', async () => {
  const { state, status, patch } = setup();
  status.mockReturnValueOnce(of({ revision: '3', state: 'unavailable' }));
  await settle();
  await state.unlink();
  expect(patch).toHaveBeenCalledExactlyOnceWith(
    id,
    { revision: '3', hasLink: true },
    { op: 'remove' },
    'trips-session',
  );
  expect(state.canUnlink()).toBe(false);
  expect(state.confirmed()).toBeNull();
  expect(status).toHaveBeenCalledTimes(1);
});
