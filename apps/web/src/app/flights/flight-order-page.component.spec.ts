import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import type { FlightQuoteResponse } from '@travel/api-client';
import { BehaviorSubject } from 'rxjs';
// Shared fictional fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightOrderPageComponent } from './flight-order-page.component';
import { FlightOrdersFeedService } from './flight-orders-feed.service';
import { FlightsAuthService } from './flights-auth.service';

const id = booking.oneWay.response.aggregateId;
const order = {
  aggregateId: id,
  status: 'Held',
  totalAmount: booking.oneWay.response.offer.totalAmount,
  currency: booking.oneWay.response.offer.currency,
  itinerary: booking.oneWay.response.offer.itinerary,
  passengerCount: 1 as const,
  ticketNumbers: [],
  bookedAt: '2030-06-01T10:00:00Z',
  ticketedAt: null,
  cancelledAt: null,
  refundedAt: null,
};

describe('FlightOrderPageComponent', () => {
  let http: HttpTestingController;
  let routeId: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let auth: {
    status: ReturnType<typeof signal>;
    accessToken: ReturnType<typeof vi.fn>;
    beginLogin: ReturnType<typeof vi.fn>;
    hasOidcCallback: ReturnType<typeof vi.fn>;
    initializeFromCallback: ReturnType<typeof vi.fn>;
    isTestEnvironment: ReturnType<typeof vi.fn>;
    identityEpoch: ReturnType<typeof signal<number>>;
  };

  beforeEach(async () => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    routeId = new BehaviorSubject(convertToParamMap({ aggregateId: id }));
    auth = {
      status: signal({ kind: 'authenticated', userId: 'demo-owner' }),
      accessToken: vi.fn().mockResolvedValue('memory-only-token'),
      beginLogin: vi.fn().mockResolvedValue(true),
      hasOidcCallback: vi.fn().mockReturnValue(false),
      initializeFromCallback: vi.fn().mockResolvedValue(false),
      isTestEnvironment: vi.fn().mockReturnValue(true),
      identityEpoch: signal(0),
    };
    await TestBed.configureTestingModule({
      imports: [FlightOrderPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: routeId.asObservable() } },
        { provide: FlightsAuthService, useValue: auth },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  function createPage() {
    const fixture = TestBed.createComponent(FlightOrderPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  it('renders all four owned legs and cabins with airport offsets', async () => {
    const current = structuredClone(order);
    Object.assign(current.itinerary, {
      journeyKind: 'multi-leg',
      slices: Array.from({ length: 4 }, () => structuredClone(current.itinerary.slices[0])),
    });
    current.itinerary.slices[3].segments[0].cabinClass = 'business';
    current.itinerary.slices[3].segments[0].departAt = '2030-06-10T00:30:00+14:00';
    current.itinerary.slices[3].segments[0].arriveAt = '2030-06-10T04:30:00+13:00';
    const { fixture, root } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(current);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelectorAll('.order-card__route')).toHaveLength(4);
    expect(root.textContent).toContain('Участок 4');
    expect(root.textContent).toContain('Бизнес');
    expect(root.textContent).toContain('10.06.2030, 00:30 UTC+14:00');
    expect(root.textContent).toContain('10.06.2030, 04:30 UTC+13:00');
    expect(root.textContent).toContain('Самостоятельное перемещение DME → LED не входит в билет');
  });

  it('navigates to a new search through the SPA router without browser reload', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const { fixture, root } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    const link = root.querySelector('.order-page__header a[href="/flights"]') as HTMLAnchorElement;
    const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 });
    link.dispatchEvent(click);
    expect(click.defaultPrevented).toBe(true);
    expect(navigate).toHaveBeenCalledWith(expect.objectContaining({}), expect.any(Object));
    const destination = navigate.mock.calls[0][0];
    expect(router.serializeUrl(destination as import('@angular/router').UrlTree)).toBe('/flights');
  });

  it('shows observed Held without offering confirmation of an unknown hold attempt', async () => {
    const operations = TestBed.inject(FlightOrderOperationsService);
    operations.startHold(
      {
        aggregateId: id,
        quoteRevision: '11111111-1111-4111-8111-111111111111',
        passengers: [
          {
            bookingPassengerId: '22222222-2222-4222-8222-222222222222',
            title: 'mr' as const,
            givenName: 'Demo',
            familyName: 'Traveler',
            dateOfBirth: '1990-04-12',
            gender: 'male',
            email: 'demo@example.test',
            phone: '+79161234567',
          },
        ],
      },
      booking.oneWay.response as FlightQuoteResponse,
      true,
      'demo-owner',
      true,
    );
    http.expectOne('/api/flights/orders/hold').error(new ProgressEvent('error'));
    const { fixture, root } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
    expect(root.querySelector('[data-action="confirm-order"]')).toBeNull();
    expect(root.textContent).toContain('Наблюдаемое состояние');
  });

  it('keeps the immediate Confirmed outcome while the owner projection returns 404 then Held', async () => {
    TestBed.inject(FlightOrderHandoffService).rememberConfirmed(id, 'demo-owner');
    const { fixture, page, root } = createPage();
    expect(root.textContent).toContain('Заказ подтверждён');
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Обновляем статус');
    expect(root.textContent).not.toContain('не найден');

    await page.refresh();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ подтверждён');
    expect(root.textContent).not.toContain('Подтвердить заказ');
  });

  it('requires cancellation consent, blocks double click and preserves success over stale projection', async () => {
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    page.requestCancellation();
    http.expectNone((request) => request.method === 'POST');
    fixture.detectChanges();
    expect(root.textContent).toContain('Отменить этот заказ?');
    page.acceptCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    const request = http.expectOne(`/api/flights/orders/${id}/cancel`);
    expect(request.request.body).toBeNull();
    request.flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
    page.refresh();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
    expect(root.textContent).toContain('проекции');
  });

  it('keeps an unknown confirm as a cancellation barrier after a route round trip', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.confirm();
    await fixture.whenStable();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('error'));
    await fixture.whenStable();
    routeId.next(convertToParamMap({ aggregateId: '11111111-1111-1111-1111-111111111111' }));
    await fixture.whenStable();
    http
      .expectOne('/api/flights/orders/11111111-1111-1111-1111-111111111111')
      .flush({ ...order, aggregateId: '11111111-1111-1111-1111-111111111111' });
    routeId.next(convertToParamMap({ aggregateId: id }));
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    expect(page.canCancel()).toBe(false);
    expect(page.confirmState()).toBe('unknown');
  });

  it('requires a successful status refresh before fresh consent after known rejection', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.requestCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush(
        { type: 'https://travel.local/errors/Flights.ProviderCancellationNotSupported' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.canCancel()).toBe(false);
    page.refresh();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.canCancel()).toBe(true);
  });

  it.each([401, 403])('hides and quarantines command/feed details after GET %s', async (status) => {
    const { fixture, page, root } = createPage();
    const feed = TestBed.inject(FlightOrdersFeedService);
    feed.save('demo-owner', {
      items: [{ ...order, status: 'Held' }],
      nextOffset: 20,
      hasMore: true,
      anchorId: id,
      anchorTop: 100,
      scrollY: 400,
      focusId: id,
    });
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.requestCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await fixture.whenStable();
    page.refresh();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush({}, { status, statusText: 'Denied' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.visibleOrder()).toBeNull();
    expect(feed.restore('demo-owner')).toBeNull();
    expect(root.textContent).not.toContain('10 800');
    expect(root.textContent).not.toContain('LED → DME');
  });

  it('discards a pending POST receipt after GET authorization denial', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.requestCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    const post = http.expectOne(`/api/flights/orders/${id}/cancel`);
    page.refresh();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush({}, { status: 403, statusText: 'Denied' });
    await fixture.whenStable();
    post.flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await fixture.whenStable();
    expect(page.visibleOrder()).toBeNull();
  });

  it('closes cancellation consent when Held changes to Confirmed', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.requestCancellation();
    page.refresh();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Confirmed' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.cancellationReview()).toBe(false);
    page.acceptCancellation();
    http.expectNone((request) => request.method === 'POST');
  });

  it('does not restart projection waiting when the cancellation advances to Refunded', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await fixture.whenStable();
    page.requestCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await fixture.whenStable();
    page.refresh();
    await fixture.whenStable();
    http
      .expectOne(`/api/flights/orders/${id}`)
      .flush({ ...order, status: 'Refunded', refundedAt: '2030-06-01T12:00:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Refunded');
    expect(page.viewState()).toBe('ready');
  });

  it('keeps a successful Held handoff through an early projection 404, then shows the projected order', async () => {
    TestBed.inject(FlightOrderHandoffService).rememberHeld(id, 'demo-owner');
    const { fixture, page, root } = createPage();
    expect(root.textContent).toContain('Заказ удержан');
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Обновляем удержание');
    expect(root.textContent).not.toContain('Заказ не найден');
    expect(root.querySelector('[data-action="refresh-order"]')).not.toBeNull();

    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
    expect(root.textContent).toContain('Точный срок удержания недоступен');
    expect(root.querySelector('[data-action="confirm-order"]')).not.toBeNull();
  });

  it('treats owner-scoped 404 on a direct link as unavailable', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
    expect(root.textContent).not.toContain('Обновляем статус');
    expect(root.querySelector('[data-action="refresh-order"]')).not.toBeNull();
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
  });

  it('does not show a confirmation handoff from a different signed-in owner', async () => {
    TestBed.inject(FlightOrderHandoffService).rememberConfirmed(id, 'previous-owner');
    const { fixture, root } = createPage();
    expect(root.textContent).not.toContain('Заказ подтверждён');
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
  });

  it('removes previously loaded ticket details after a later owner denial', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-PRIVATE'] });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('TKT-PRIVATE');

    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 403 }, { status: 403, statusText: 'Forbidden' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Доступ к заказу запрещён');
    expect(root.textContent).not.toContain('TKT-PRIVATE');

    auth.status.set({ kind: 'authenticated', userId: 'demo-owner' });
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-PRIVATE'] });
    await Promise.resolve();
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
    expect(root.textContent).not.toContain('TKT-PRIVATE');
  });

  it('hides the previous owner view as soon as the signed-in identity changes', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-PRIVATE'] });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('TKT-PRIVATE');

    auth.status.set({ kind: 'authenticated', userId: 'other-owner' });
    fixture.detectChanges();
    expect(root.textContent).not.toContain('TKT-PRIVATE');
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
    await page.login();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 404 }, { status: 404, statusText: 'Not Found' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
  });

  it('discards raw detail data from an earlier identity epoch even if the same user returns', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    const request = http.expectOne(`/api/flights/orders/${id}`);
    auth.identityEpoch.set(2);
    request.flush({ ...order, status: 'Ticketed', ticketNumbers: ['FICTIONAL-TICKET'] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.visibleOrder()).toBeNull();
    expect(page.viewState()).toBe('auth');
  });

  it('discards an in-flight GET response if the authenticated owner changes before it arrives', async () => {
    const { fixture, root } = createPage();
    await Promise.resolve();
    const pending = http.expectOne(`/api/flights/orders/${id}`);
    auth.status.set({ kind: 'authenticated', userId: 'other-owner' });
    pending.flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-PRIVATE'] });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
    expect(root.textContent).not.toContain('TKT-PRIVATE');
  });

  it('shows ticket numbers only for Ticketed and does not invent a Held deadline', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
    expect(root.textContent).not.toContain('Удержано до');

    await page.refresh();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-001'] });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Билет выписан');
    expect(root.textContent).toContain('TKT-001');
    expect(root.textContent).not.toContain('SU SU101');
  });

  it('confirms a recovered Held order with a new stable key after explicit review', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Точный срок удержания недоступен');
    page.confirm();
    await Promise.resolve();
    const request = http.expectOne('/api/flights/orders/confirm');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
    expect(request.request.body).toBe(JSON.stringify({ aggregateId: id }));
    request.flush({ aggregateId: id, status: 'Confirmed', paymentRef: null });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ подтверждён');
    expect(root.textContent).not.toContain('Билет выписан');
  });

  it('does not repeat an unknown confirmation after observing a Held GET', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    page.confirm();
    await Promise.resolve();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('error'));
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Исход подтверждения неизвестен');
    page.retryConfirm();
    await Promise.resolve();
    http.expectNone('/api/flights/orders/confirm');
    expect(root.querySelector('[data-action="retry-confirm-order"]')).toBeNull();
  });

  it('asks for login on direct reload and returns to that order path', async () => {
    auth.status.set({ kind: 'anonymous' });
    auth.beginLogin.mockImplementation(async () => {
      auth.status.set({ kind: 'authenticated', userId: 'demo-owner' });
      return true;
    });
    const { page, root } = createPage();
    http.expectNone(() => true);
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
    await page.login();
    expect(auth.beginLogin).toHaveBeenCalledWith(`/flights/orders/${id}`);
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
  });

  it('does not reopen an old order when login finishes after route navigation', async () => {
    auth.status.set({ kind: 'anonymous' });
    let completeLogin: ((authenticated: boolean) => void) | undefined;
    auth.beginLogin.mockImplementation(
      () =>
        new Promise<boolean>((resolve) => {
          completeLogin = resolve;
        }),
    );
    const { page } = createPage();
    const login = page.login();
    const nextId = '11111111-1111-1111-1111-111111111111';
    routeId.next(convertToParamMap({ aggregateId: nextId }));
    auth.status.set({ kind: 'authenticated', userId: 'other-owner' });
    completeLogin?.(true);
    await login;
    await Promise.resolve();
    expect(page.aggregateId()).toBe(nextId);
    http.expectNone(() => true);
  });

  it('cancels an old GET when the route changes and ignores its result', async () => {
    const { fixture, root } = createPage();
    await Promise.resolve();
    const oldRequest = http.expectOne(`/api/flights/orders/${id}`);
    const nextId = '11111111-1111-1111-1111-111111111111';
    routeId.next(convertToParamMap({ aggregateId: nextId }));
    expect(oldRequest.cancelled).toBe(true);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/orders/${nextId}`)
      .flush({ ...order, aggregateId: nextId, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ отменён');
    expect(root.textContent).toContain(nextId);
  });

  it('stops automatic polling after the bounded window and still allows manual refresh', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-01T10:00:00Z'));
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Confirmed' });
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(2_000);
    const second = http.expectOne(`/api/flights/orders/${id}`);
    second.flush({ ...order, status: 'Confirmed' });
    await Promise.resolve();
    vi.setSystemTime(new Date('2030-06-01T10:00:32Z'));
    await vi.advanceTimersByTimeAsync(2_000);
    http.expectNone(`/api/flights/orders/${id}`);
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Автоматическое обновление остановлено');
    vi.advanceTimersByTime(5_000);
    http.expectNone(`/api/flights/orders/${id}`);
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Confirmed' });
  });

  it('ends cancellation projection polling at the absolute deadline while retaining command evidence', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-01T10:00:00Z'));
    const { fixture, page } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    fixture.detectChanges();
    page.requestCancellation();
    page.acceptCancellation();
    await Promise.resolve();
    http
      .expectOne(`/api/flights/orders/${id}/cancel`)
      .flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T10:00:00Z' });
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(2_000);
    const poll = http.expectOne(`/api/flights/orders/${id}`);
    await vi.advanceTimersByTimeAsync(28_001);
    expect(poll.cancelled).toBe(true);
    expect(page.pollingEnded()).toBe(true);
    expect(page.displayStatus()).toBe('Cancelled');
  });

  it('separates forbidden and unreadable responses from an owner 404', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 403 }, { status: 403, statusText: 'Forbidden' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Доступ к заказу запрещён');
    expect(root.textContent).not.toContain('Заказ не найден');

    auth.status.set({ kind: 'authenticated', userId: 'demo-owner' });
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ aggregateId: id, status: 'Ticketed' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Ответ о заказе не удалось прочитать');
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
  });

  it('shows Cancelled and Refunded as distinct terminal outcomes', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http
      .expectOne(`/api/flights/orders/${id}`)
      .flush({ ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ отменён');
    page.refresh();
    await Promise.resolve();
    http
      .expectOne(`/api/flights/orders/${id}`)
      .flush({ ...order, status: 'Refunded', refundedAt: '2030-06-01T12:00:00Z' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Возврат оформлен');
  });
});
