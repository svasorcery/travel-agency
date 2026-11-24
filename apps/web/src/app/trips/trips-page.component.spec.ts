import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import {
  type AvailableFlightObservation,
  decodeTripPlan,
  type FlightLinkObservation,
  type FlightLinkStatus,
  TripsApiService,
  TripsFlightLinksApiService,
  TripsHotelLinkApiService,
  TripsItineraryApiService,
  TripsItineraryRequestError,
} from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TripsBriefReviewComponent } from './trips-brief-review.component';
import { TripsFlightLinksComponent } from './trips-flight-links.component';
import { TripsPageComponent } from './trips-page.component';

async function setup(flightLinks: object = { availability: () => of(false) }) {
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner' }),
    identityEpoch: signal(0),
    hotelReadAccess: signal({ allowed: false }),
    accessToken: vi.fn().mockResolvedValue('token'),
    hasOidcCallback: () => false,
    initializeFromCallback: vi.fn(),
    beginLogin: vi.fn(),
    deny: vi.fn(),
    logout: vi.fn(async () => {
      auth.identityEpoch.update((e) => e + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
  };
  const plan = decodeTripPlan(structuredClone(tripsExample));
  const api = {
    detail: vi.fn().mockReturnValue(of(plan)),
    list: vi.fn().mockReturnValue(
      of({
        items: [
          {
            tripId: plan.tripId,
            revision: plan.revision,
            title: '<img src=x onerror=alert(1)>',
            destinationId: 'saint-petersburg',
            displayName: 'Санкт-Петербург',
            startDate: '2026-11-14',
            endDate: '2026-11-14',
            updatedAt: plan.updatedAt,
          },
        ],
        nextCursor: null,
      }),
    ),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn().mockReturnValue(of(undefined)),
  };
  const availability = {
    status: signal('ready'),
    enabled: () => true,
    catalogue: signal({ catalogueVersion: '1', items: [tripsExample.destination] }),
    initialize: vi.fn().mockResolvedValue(undefined),
    clearPrivate: vi.fn(),
  };
  const params = convertToParamMap({ id: plan.tripId });
  TestBed.configureTestingModule({
    imports: [TripsPageComponent],
    providers: [
      provideRouter([]),
      { provide: TripsFlightLinksApiService, useValue: flightLinks },
      { provide: TripsHotelLinkApiService, useValue: { availability: () => of({ enabled: false, canAttach: false }) } },
      {
        provide: TripsItineraryApiService,
        useValue: { availabilityFor: () => throwError(() => new TripsItineraryRequestError('disabled', 404)) },
      },
      { provide: ActivatedRoute, useValue: { paramMap: of(params), snapshot: { paramMap: params } } },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsAvailabilityService, useValue: availability },
      { provide: TripsApiService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(TripsPageComponent);
  await fixture.whenStable();
  return { fixture, page: fixture.componentInstance, api, auth, plan };
}

const schedule = (orderId: string, departAt: string, arriveAt: string): AvailableFlightObservation => ({
  orderId,
  state: 'available',
  bookingStatus: 'Ticketed',
  passengerCount: 2,
  route: [{ origin: 'KGD', destination: 'LED', departAt, arriveAt }],
  reviewRequired: false,
  observedAt: '2026-11-12T00:00:00Z',
  streamVersion: '1',
});
const linkedOrder = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const secondOrder = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const thirdOrder = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const fourthOrder = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';
const arrival = schedule(linkedOrder, '2026-11-13T23:40:00+02:00', '2026-11-14T02:10:00+03:00');
const nextDay = schedule(secondOrder, '2026-11-15T00:40:00+03:00', '2026-11-15T02:10:00+02:00');

function linkedFlights(items: FlightLinkObservation[]) {
  const metadata = {
    tripId: tripsExample.tripId,
    linksRevision: '1',
    links: items.map(({ orderId }) => ({ orderId, linkedAt: '2026-11-12T00:00:00Z' })),
  };
  return {
    availability: vi.fn().mockReturnValue(of(true)),
    metadata: vi.fn().mockReturnValue(of(metadata)),
    status: vi.fn().mockReturnValue(of({ tripId: metadata.tripId, linksRevision: metadata.linksRevision, items })),
    patch: vi.fn(),
  };
}

describe('Flights schedule in the Trips editor', () => {
  it('renders each flight in one named place and keeps the block controls and order link separate', async () => {
    const links = linkedFlights([
      nextDay,
      schedule(thirdOrder, '2026-11-13T23:40:00+02:00', '2026-11-15T02:10:00+03:00'),
      arrival,
      schedule(fourthOrder, '2026-11-13T20:40:00+02:00', '2026-11-13T23:10:00+03:00'),
    ]);
    const { fixture, api, plan, page } = await setup(links);
    const calendar = (fixture.nativeElement as HTMLElement).querySelector('.calendar')!;
    await vi.waitFor(() =>
      expect(
        Array.from(calendar.querySelectorAll('.flight-calendar-group'), (group) => group.getAttribute('aria-label')),
      ).toEqual(['До начала поездки', 'По обе стороны дат поездки', 'Расписание Flights', 'После окончания поездки']),
    );
    const day = calendar.querySelector('.day')!;
    const card = day.querySelector('article')!;
    expect(card.textContent).toContain('По расписанию из Flights');
    expect(card.textContent).toContain('02:10');
    expect(card.textContent).toContain('UTC+03:00');
    expect(card.querySelectorAll('time')[1].dateTime).toBe(arrival.route![0].arriveAt);
    expect(card.querySelector('a')?.getAttribute('href')).toBe('/flights/orders/' + linkedOrder);
    expect(card.querySelector('input, select, button, textarea')).toBeNull();
    expect(calendar.querySelector('input[type="radio"]')).toBeNull();
    expect(day.querySelector('fieldset input')?.getAttribute('id')).toBe('block-' + plan.days[0].blocks[0].blockId);
    expect(day.textContent).toContain('1/8 блоков');
    expect(page.state.totals()?.subtotal).toBe('500.00');
    expect(links.availability).toHaveBeenCalledTimes(1);
    expect(links.metadata).toHaveBeenCalledTimes(1);
    expect(links.status).toHaveBeenCalledTimes(1);
    expect(api.replace).not.toHaveBeenCalled();
    expect(links.patch).not.toHaveBeenCalled();
  });

  it('reprojects applied draft dates, discarded dates, a saved range and fresher observations without link writes', async () => {
    const links = linkedFlights([nextDay]);
    const { fixture, page, api, plan } = await setup(links);
    const root = fixture.nativeElement as HTMLElement;
    const dayCards = () => root.querySelectorAll('.day .flight-calendar-card').length;
    const afterTrip = () => root.querySelector('.flight-calendar-group[aria-label="После окончания поездки"]');
    await vi.waitFor(() => expect(afterTrip()).not.toBeNull());
    page.patchBrief('endDate', '2026-11-15');
    await fixture.whenStable();
    expect(dayCards()).toBe(0);
    await page.applyBrief();
    await fixture.whenStable();
    expect(dayCards()).toBe(1);
    expect(afterTrip()).toBeNull();
    expect(api.replace).not.toHaveBeenCalled();
    await page.state.refreshDetail();
    page.state.useCurrentServer();
    await fixture.whenStable();
    expect(dayCards()).toBe(0);
    expect(afterTrip()).not.toBeNull();
    page.patchBrief('endDate', '2026-11-15');
    await page.applyBrief();
    const edited = page.state.buffer()!;
    api.replace.mockReturnValue(of({ ...plan, ...edited, revision: '3' }));
    await page.submit();
    await fixture.whenStable();
    expect(dayCards()).toBe(1);
    expect(api.replace).toHaveBeenCalledTimes(1);
    expect(api.replace.mock.calls[0][2].days).toEqual(edited.days);
    expect(links.metadata).toHaveBeenCalledTimes(1);
    expect(links.status).toHaveBeenCalledTimes(1);

    links.status.mockReturnValue(
      of({
        tripId: plan.tripId,
        linksRevision: '1',
        items: [{ ...nextDay, streamVersion: '2', route: arrival.route }],
      }),
    );
    const panel = fixture.debugElement.query(By.directive(TripsFlightLinksComponent))
      .componentInstance as TripsFlightLinksComponent;
    await panel.state.refresh();
    await fixture.whenStable();
    expect(root.querySelector('[aria-label="День 1 2026-11-14"] .flight-calendar-card')).not.toBeNull();
    expect(root.querySelector('[aria-label="День 2 2026-11-15"] .flight-calendar-card')).toBeNull();
    expect(links.status).toHaveBeenCalledTimes(2);
    expect(links.patch).not.toHaveBeenCalled();
  });

  it('shares link refresh and unlink state, displaying stale, review and closed facts without leaking cleared routes', async () => {
    const links = linkedFlights([arrival, nextDay]);
    const { fixture, page, auth, plan } = await setup(links);
    const root = fixture.nativeElement as HTMLElement;
    const cards = () => [...root.querySelectorAll('.flight-calendar-card')];
    const panel = fixture.debugElement.query(By.directive(TripsFlightLinksComponent))
      .componentInstance as TripsFlightLinksComponent;
    links.status.mockReturnValue(
      of({
        tripId: plan.tripId,
        linksRevision: '1',
        items: [
          { ...arrival, bookingStatus: 'Cancelled', reviewRequired: true, streamVersion: '2' },
          { orderId: secondOrder, state: 'unavailable' },
        ],
      }),
    );
    await panel.state.refresh();
    await fixture.whenStable();
    expect(cards()[0].textContent).toContain('Отменён');
    expect(cards()[0].textContent).toContain('Требуется проверка');
    expect(cards()[1].textContent).toContain('Показаны прежние сведения');
    links.status.mockReturnValue(
      of({
        tripId: plan.tripId,
        linksRevision: '1',
        items: [
          { orderId: linkedOrder, state: 'notFound' },
          { ...nextDay, streamVersion: '2', route: null },
        ],
      }),
    );
    await panel.state.refresh();
    await fixture.whenStable();
    expect(cards()).toHaveLength(0);
    expect(root.textContent).toContain('Маршрут заказа недоступен.');

    links.status.mockReturnValue(
      of({
        tripId: plan.tripId,
        linksRevision: '1',
        items: [
          { ...arrival, streamVersion: '3' },
          { ...nextDay, streamVersion: '3' },
        ],
      }),
    );
    await panel.state.refresh();
    const original = panel.state.saved()!;
    links.patch.mockReturnValue(of({ ...original, linksRevision: '2', links: original.links.slice(1) }));
    await panel.mutate('remove', linkedOrder);
    await fixture.whenStable();
    expect(cards()).toHaveLength(1);
    const pending = new Subject<FlightLinkStatus>();
    links.metadata.mockReturnValue(of({ ...original, linksRevision: '2', links: original.links.slice(1) }));
    links.status.mockReturnValue(pending);
    const read = panel.state.refresh();
    await vi.waitFor(() => expect(links.status).toHaveBeenCalledTimes(5));
    await auth.logout();
    await fixture.whenStable();
    pending.next({ tripId: plan.tripId, linksRevision: '2', items: [{ ...nextDay, streamVersion: '9' }] });
    await read;
    await fixture.whenStable();
    expect(cards()).toHaveLength(0);
    expect(page.state.baseline()).toBeNull();
  });
});

describe('Trips accessible explicit workspace', () => {
  it('discards a pending unlink when the brief changes while awaiting the access token', async () => {
    const orderId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    const links = {
      availability: () => of(true),
      metadata: () =>
        of({ tripId: tripsExample.tripId, linksRevision: '1', links: [{ orderId, linkedAt: '2026-11-17T00:00:00Z' }] }),
      status: () => of({ tripId: tripsExample.tripId, linksRevision: '1', items: [{ orderId, state: 'unavailable' }] }),
      patch: vi.fn(),
    };
    const { fixture, page, auth } = await setup(links);
    const panel: TripsFlightLinksComponent = fixture.debugElement.query(
      By.directive(TripsFlightLinksComponent),
    ).componentInstance;
    await vi.waitFor(() => expect(panel.state.canMutate()).toBe(true));
    let finish!: (token: string) => void;
    auth.accessToken.mockReturnValueOnce(
      new Promise<string>((resolve) => {
        finish = resolve;
      }),
    );
    const operation = panel.mutate('remove', orderId);
    expect(panel.state.busy()).toBe('patch');
    page.patchBrief('adults', 3);
    finish('token');
    await operation;
    expect(links.patch).not.toHaveBeenCalled();
    expect(page.briefDraft()?.adults).toBe(3);
  });
  it('keeps form-only dirty wishes as comparison state when a changed GET arrives', async () => {
    const { page, fixture, api, plan } = await setup();
    page.patchBrief('adults', 3);
    expect(page.state.dirty()).toBe(true);
    api.detail.mockReturnValue(of({ ...plan, revision: '3', brief: { ...plan.brief, adults: 4 } }));
    await page.state.refreshDetail();
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(3);
    expect(page.state.comparison()?.brief.adults).toBe(4);
    expect(page.state.buffer()?.brief.adults).toBe(2);
    expect(page.state.dirty()).toBe(true);
  });
  it('clears form-only dirtiness on actual revert while retaining independent block and title edits', async () => {
    const { page } = await setup();
    page.patchBrief('adults', 3);
    expect(page.state.dirty()).toBe(true);
    page.patchBrief('adults', 2);
    expect(page.state.dirty()).toBe(false);
    page.title('Local title');
    page.patchBrief('adults', 3);
    page.patchBrief('adults', 2);
    expect(page.state.dirty()).toBe(true);
    expect(page.state.buffer()?.title).toBe('Local title');
  });
  it('explicit server reconciliation discards unapplied brief edits from the form and the next PUT', async () => {
    const { page, fixture, api, plan } = await setup();
    page.patchBrief('adults', 3);
    await page.state.refreshDetail();
    await fixture.whenStable();
    const choose = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: unknown) => (b as HTMLElement).textContent?.trim() === 'Заменить редактор сохранённым планом',
    ) as HTMLButtonElement;
    expect(choose).toBeDefined();
    choose.click();
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(2);
    expect(page.briefDirty()).toBe(false);
    page.title('After explicit discard');
    api.replace.mockReturnValue(of({ ...plan, title: 'After explicit discard', revision: '3' }));
    await page.submit();
    expect(api.replace.mock.calls[0][2].brief.adults).toBe(2);
  });
  it('focuses the invalid block time rather than the plan title', async () => {
    const { page, fixture, api, plan } = await setup();
    const block = plan.days[0].blocks[0];
    page.state.updateBlock(block.blockId, { endTime: '09:00' });
    await page.submit();
    await fixture.whenStable();
    expect(api.replace).not.toHaveBeenCalled();
    expect(document.activeElement?.id).toBe('start-' + block.blockId);
    expect(fixture.nativeElement.querySelector('#start-' + block.blockId).getAttribute('aria-invalid')).toBe('true');
  });
  it('renders private markup literally, labels every field and explains partial estimates', async () => {
    const { fixture } = await setup();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('img')).toBeNull();
    expect(root.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(root.textContent).toContain('Прогулочные предложения выключены');
    expect(root.textContent).toContain('500.00 RUB');
    expect(root.textContent).toContain('не полная цена');
    expect(root.querySelector('[aria-live=polite]')).not.toBeNull();
    for (const field of root.querySelectorAll<HTMLInputElement>('input:not([type=checkbox]),select,textarea'))
      expect(root.querySelector(`label[for="${field.id}"]`)).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('#trip-budget')!.type).toBe('text');
    expect(root.querySelector('#generate-itinerary')).toBeNull();
  });
  it('announces invalid dates and focuses the corrective input without losing valid text', async () => {
    const { page, fixture, api } = await setup();
    page.patchBrief('endDate', '2026-11-13');
    page.title('Сохранённый ввод');
    await page.submit();
    await fixture.whenStable();
    expect(api.replace).not.toHaveBeenCalled();
    expect(page.state.buffer()?.title).toBe('Сохранённый ввод');
    expect(page.state.message()).toContain('от 1 до 14');
    expect(fixture.nativeElement.querySelector('#trip-start').getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement?.id).toBe('trip-start');
  });
  it('requires explicit confirmation before deletion', async () => {
    const { page, fixture, api } = await setup();
    const remove = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: unknown) => (b as HTMLElement).textContent?.trim() === 'Удалить план',
    ) as HTMLButtonElement;
    remove.click();
    await fixture.whenStable();
    expect(api.delete).not.toHaveBeenCalled();
    expect(page.deleteConfirmation()).toBe(true);
    page.deleteConfirmation.set(false);
    expect(api.delete).not.toHaveBeenCalled();
  });
  it('retains brief B entered while save A is pending', async () => {
    const { page, fixture, api, plan } = await setup();
    page.title('A');
    const pending = new Subject<typeof plan>();
    api.replace.mockReturnValue(pending);
    const saving = page.submit();
    await vi.waitFor(() => expect(api.replace).toHaveBeenCalledTimes(1));
    page.patchBrief('adults', 3);
    pending.next({ ...plan, title: 'A', revision: '3' });
    pending.complete();
    await saving;
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(3);
    expect(page.briefDirty()).toBe(true);
    expect(page.state.baseline()?.brief.adults).toBe(2);
  });
  it('warns on dirty navigation, retains data when cancelled, and clears on logout', async () => {
    const { page, fixture, auth } = await setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    page.title('Частный черновик');
    expect(page.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalledOnce();
    expect(page.state.buffer()?.title).toBe('Частный черновик');
    await page.logout();
    await fixture.whenStable();
    expect(page.state.buffer()).toBeNull();
    expect(page.briefDraft()).toBeNull();
    expect(auth.status().kind).toBe('anonymous');
    confirm.mockRestore();
  });
});

describe('Trips private brief navigation guards', () => {
  it('protects raw text on a clean saved plan synchronously without marking M1 dirty', async () => {
    const { page, fixture } = await setup();
    const review = fixture.debugElement.query(By.directive(TripsBriefReviewComponent))
      .componentInstance as TripsBriefReviewComponent;
    const before = structuredClone(page.state.buffer());
    review.flow.textChanged('Частное пожелание до отправки');
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    expect(page.state.dirty()).toBe(false);
    expect(page.briefDirty()).toBe(false);
    expect(page.canLeave()).toBe(false);
    expect(review.flow.text()).toBe('Частное пожелание до отправки');
    expect(page.state.buffer()).toEqual(before);
    const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    page.beforeUnload(event);
    expect(event.defaultPrevented).toBe(true);
    confirm.mockRestore();
  });
  it('keeps private text when New plan is cancelled and discards explicitly accepted navigation', async () => {
    const { page, fixture } = await setup();
    const review = fixture.debugElement.query(By.directive(TripsBriefReviewComponent))
      .componentInstance as TripsBriefReviewComponent;
    review.flow.textChanged('Оставить до решения');
    const before = page.state.selectedId();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await page.newPlan();
    expect(page.state.selectedId()).toBe(before);
    expect(review.flow.text()).toBe('Оставить до решения');
    confirm.mockReturnValue(true);
    expect(page.canLeave()).toBe(true);
    expect(review.flow.text()).toBe('');
    expect(confirm).toHaveBeenCalledTimes(2);
    confirm.mockRestore();
  });
  it('clears private text synchronously on logout without a navigation confirmation', async () => {
    const { page, fixture } = await setup();
    const review = fixture.debugElement.query(By.directive(TripsBriefReviewComponent))
      .componentInstance as TripsBriefReviewComponent;
    review.flow.textChanged('Удалить при выходе');
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const logout = page.logout();
    expect(review.flow.text()).toBe('');
    expect(confirm).not.toHaveBeenCalled();
    await logout;
    expect(page.state.buffer()).toBeNull();
    confirm.mockRestore();
  });
});
