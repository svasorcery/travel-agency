import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
// Shared fictional fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderPageComponent } from './flight-order-page.component';
import { FlightsAuthService } from './flights-auth.service';

const id = booking.oneWay.response.aggregateId;
const order = {
  aggregateId: id,
  status: 'Held',
  totalAmount: booking.oneWay.response.offer.totalAmount,
  currency: booking.oneWay.response.offer.currency,
  itinerary: booking.oneWay.response.offer.itinerary,
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
  };

  beforeEach(async () => {
    routeId = new BehaviorSubject(convertToParamMap({ aggregateId: id }));
    auth = {
      status: signal({ kind: 'authenticated', userId: 'demo-owner' }),
      accessToken: vi.fn().mockResolvedValue('memory-only-token'),
      beginLogin: vi.fn().mockResolvedValue(true),
      hasOidcCallback: vi.fn().mockReturnValue(false),
      initializeFromCallback: vi.fn().mockResolvedValue(false),
      isTestEnvironment: vi.fn().mockReturnValue(true),
    };
    await TestBed.configureTestingModule({
      imports: [FlightOrderPageComponent],
      providers: [
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

  it('retries an unknown confirm with the same key and exact body', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush(order);
    await Promise.resolve();
    page.confirm();
    await Promise.resolve();
    const first = http.expectOne('/api/flights/orders/confirm');
    const key = first.request.headers.get('Idempotency-Key');
    const body = first.request.body;
    first.error(new ProgressEvent('error'));
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Исход подтверждения неизвестен');
    page.retryConfirm();
    await Promise.resolve();
    const retry = http.expectOne('/api/flights/orders/confirm');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(key);
    expect(retry.request.body).toBe(body);
    retry.flush({ aggregateId: id, status: 'Confirmed', paymentRef: null });
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
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Confirmed' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Автоматическое обновление остановлено');
    vi.advanceTimersByTime(5_000);
    http.expectNone(`/api/flights/orders/${id}`);
    page.refresh();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ ...order, status: 'Confirmed' });
  });

  it('separates forbidden and unreadable responses from an owner 404', async () => {
    const { fixture, page, root } = createPage();
    await Promise.resolve();
    http.expectOne(`/api/flights/orders/${id}`).flush({ status: 403 }, { status: 403, statusText: 'Forbidden' });
    await Promise.resolve();
    fixture.detectChanges();
    expect(root.textContent).toContain('Доступ к заказу запрещён');
    expect(root.textContent).not.toContain('Заказ не найден');

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
