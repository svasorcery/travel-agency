import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { FlightOrderResponse } from '@travel/api-client';
// Shared fictional fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightOrdersFeedService } from './flight-orders-feed.service';
import { FlightOrdersPageComponent } from './flight-orders-page.component';
import { FlightsAuthService } from './flights-auth.service';
import type { FlightsAuthStatus } from './flights-auth.types';

function order(index: number): FlightOrderResponse {
  return {
    aggregateId: `11111111-1111-1111-1111-${String(index).padStart(12, '0')}`,
    status: 'Confirmed',
    totalAmount: 10800,
    currency: 'RUB',
    itinerary: booking.oneWay.response.offer.itinerary,
    ticketNumbers: [],
    bookedAt: '2030-06-01T10:00:00Z',
    ticketedAt: null,
    cancelledAt: null,
    refundedAt: null,
  };
}
const url = (offset = 0) => `/api/flights/orders?limit=21&offset=${offset}`;

describe('FlightOrdersPageComponent', () => {
  let http: HttpTestingController;
  let observerCallback: IntersectionObserverCallback;
  let auth: {
    status: ReturnType<typeof signal<FlightsAuthStatus>>;
    accessToken: ReturnType<typeof vi.fn>;
    beginLogin: ReturnType<typeof vi.fn>;
    hasOidcCallback: ReturnType<typeof vi.fn>;
    initializeFromCallback: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    vi.spyOn(window, 'scrollTo').mockImplementation(() => undefined);
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(callback: IntersectionObserverCallback) {
          observerCallback = callback;
        }
        observe = vi.fn();
        disconnect = vi.fn();
      },
    );
    auth = {
      status: signal<FlightsAuthStatus>({ kind: 'authenticated', userId: 'owner-a' }),
      accessToken: vi.fn().mockResolvedValue('memory-only'),
      beginLogin: vi.fn().mockResolvedValue(true),
      hasOidcCallback: vi.fn().mockReturnValue(false),
      initializeFromCallback: vi.fn().mockResolvedValue(false),
    };
    await TestBed.configureTestingModule({
      imports: [FlightOrdersPageComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FlightsAuthService, useValue: auth },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  async function settle(fixture: ComponentFixture<FlightOrdersPageComponent>) {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();
  }
  async function create() {
    const fixture = TestBed.createComponent(FlightOrdersPageComponent);
    fixture.detectChanges();
    await settle(fixture);
    return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }
  function flush(items: FlightOrderResponse[], offset = 0) {
    http.expectOne(url(offset)).flush({ items, limit: 21, offset });
  }
  function intersect() {
    observerCallback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
  }

  it('shows login without HTTP on direct anonymous reload, then returns to the list', async () => {
    auth.status.set({ kind: 'anonymous' });
    const { fixture, page, root } = await create();
    expect(root.textContent).toContain('Войдите');
    http.expectNone(url());
    auth.beginLogin.mockImplementation(async () => {
      auth.status.set({ kind: 'authenticated', userId: 'owner-a' });
      return true;
    });
    void page.login();
    await settle(fixture);
    flush([]);
    await settle(fixture);
    expect(auth.beginLogin).toHaveBeenCalledWith('/flights/orders');
    expect(root.textContent).toContain('Пока нет отображаемых заказов');
  });

  it('processes a validated OIDC callback before reading the list', async () => {
    auth.status.set({ kind: 'anonymous' });
    auth.hasOidcCallback.mockReturnValue(true);
    auth.initializeFromCallback.mockImplementation(async () => {
      auth.status.set({ kind: 'authenticated', userId: 'owner-a' });
      return true;
    });
    const { fixture } = await create();
    flush([]);
    await settle(fixture);
    expect(auth.initializeFromCallback).toHaveBeenCalledOnce();
  });

  it('shows initial loading and an owner-safe empty result without background retries', async () => {
    const { fixture, root } = await create();
    expect(root.textContent).toContain('Загружаем заказы');
    flush([]);
    await settle(fixture);
    expect(root.textContent).toContain('Пока нет отображаемых заказов');
    expect(root.querySelector('[data-action="load-more"]')).toBeNull();
    http.expectNone(url());
  });

  it('keeps a background cancellation over stale list refresh and append data', async () => {
    const { fixture, page } = await create();
    http.expectOne(url()).flush({ items: [order(1)], limit: 21, offset: 0 });
    await settle(fixture);
    TestBed.inject(FlightOrderOperationsService).startCancel(order(1).aggregateId, 'owner-a');
    await settle(fixture);
    http
      .expectOne(`/api/flights/orders/${order(1).aggregateId}/cancel`)
      .flush({ ...order(1), status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' });
    await settle(fixture);
    expect(page.visibleOrders()[0].status).toBe('Cancelled');
    page.refresh();
    await settle(fixture);
    http.expectOne(url()).flush({ items: [order(1)], limit: 21, offset: 0 });
    await settle(fixture);
    expect(page.visibleOrders()[0].status).toBe('Cancelled');
  });

  it('appends 20 per batch, deduplicates overlapping IDs, and keeps raw server offset', async () => {
    const { fixture, page, root } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(20);
    page.loadMore();
    await settle(fixture);
    flush([order(20), ...Array.from({ length: 20 }, (_, i) => order(i + 21))], 20);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(39);
    expect(root.querySelectorAll('[data-order-id]')).toHaveLength(39);
    page.loadMore();
    await settle(fixture);
    flush([order(41)], 40);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(40);
    expect(page.hasMore()).toBe(false);
    expect(root.textContent).toContain('Все доступные заказы загружены');
    expect(root.querySelector('[data-order-link]')?.getAttribute('href')).toBe(
      `/flights/orders/${order(1).aggregateId}`,
    );
    expect(root.textContent).not.toContain('Отменить');
  });

  it('shows no Show more for exactly 20 items, and preserves order currency', async () => {
    const { fixture, page, root } = await create();
    flush(Array.from({ length: 20 }, (_, i) => ({ ...order(i + 1), currency: 'USD' })));
    await settle(fixture);
    expect(page.hasMore()).toBe(false);
    expect(root.textContent).toContain('USD');
  });

  it('automatically loads near the end once and does not move keyboard focus', async () => {
    const { fixture, root } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    const link = root.querySelector<HTMLElement>('[data-order-link]');
    if (link === null) throw new Error('Expected a rendered order link');
    link.focus();
    intersect();
    intersect();
    await settle(fixture);
    flush([order(21)], 20);
    await settle(fixture);
    expect(document.activeElement).toBe(link);
    expect(root.querySelectorAll('[data-order-id]')).toHaveLength(21);
  });

  it('holds an automatic response while the manual control has focus, then appends without a second GET', async () => {
    const { fixture, page, root } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    intersect();
    await settle(fixture);
    const button = root.querySelector<HTMLButtonElement>('[data-action="load-more"]');
    if (button === null) throw new Error('Expected manual loading control');
    button.focus();
    flush([order(21)], 20);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(20);
    expect(page.moreState()).toBe('buffered');
    page.loadMore();
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(21);
    expect((document.activeElement as HTMLElement).dataset['orderLink']).toBe(order(21).aggregateId);
    http.expectNone(url(20));
  });

  it('retains previous items on a later page error and retries exactly that offset manually', async () => {
    const { fixture, page } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    page.loadMore();
    await settle(fixture);
    http.expectOne(url(20)).flush({}, { status: 500, statusText: 'offline' });
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(20);
    expect(page.moreState()).toBe('error');
    intersect();
    await settle(fixture);
    http.expectNone(url(20));
    page.loadMore();
    await settle(fixture);
    flush([], 20);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(20);
    expect(page.hasMore()).toBe(false);
  });

  it.each([401, 403])('clears already rendered orders when a later GET is denied (%s)', async (status) => {
    const { fixture, page } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    page.loadMore();
    await settle(fixture);
    http.expectOne(url(20)).flush({}, { status, statusText: 'denied' });
    await settle(fixture);
    expect(page.visibleOrders()).toEqual([]);
    expect(page.viewState()).toBe(status === 401 ? 'auth' : 'forbidden');
    expect(TestBed.inject(FlightOrdersFeedService).restore('owner-a')).toBeNull();
  });

  it('discards an in-flight response on identity change, even before the auth effect runs', async () => {
    const { fixture, page } = await create();
    const request = http.expectOne(url());
    auth.status.set({ kind: 'authenticated', userId: 'owner-b' });
    request.flush({ items: [order(1)], limit: 21, offset: 0 });
    await settle(fixture);
    expect(page.visibleOrders()).toEqual([]);
    expect(page.viewState()).toBe('auth');
  });

  it('rejects malformed initial JSON/DTO without fabricating an empty result', async () => {
    const { fixture, page, root } = await create();
    http.expectOne(url()).flush({ items: [order(1)], limit: 50, offset: 0 });
    await settle(fixture);
    expect(page.viewState()).toBe('malformed');
    expect(root.textContent).not.toContain('Пока нет');
    page.refresh();
    await settle(fixture);
    flush([order(1)]);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(1);
  });

  it('cancels HTTP on destroy and never updates the next route', async () => {
    const { fixture } = await create();
    const pending = http.expectOne(url());
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
  });

  it('restores cached rows before position and focus, with no fresh first-page GET', async () => {
    const feed = TestBed.inject(FlightOrdersFeedService);
    feed.save('owner-a', {
      items: Array.from({ length: 40 }, (_, i) => order(i + 1)),
      nextOffset: 40,
      hasMore: true,
      anchorId: order(30).aggregateId,
      anchorTop: 90,
      scrollY: 1500,
      focusId: order(30).aggregateId,
    });
    vi.spyOn(feed, 'returningFromOrder').mockReturnValue(true);
    const { fixture, page, root } = await create();
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(40);
    http.expectNone(url());
    expect((document.activeElement as HTMLElement).dataset['orderLink']).toBe(order(30).aggregateId);
    intersect();
    await settle(fixture);
    http.expectNone(url(40));
    page.armAuto();
    intersect();
    await settle(fixture);
    flush([order(41)], 40);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(41);
    expect(root.querySelectorAll('[data-order-id]')).toHaveLength(41);
  });

  it('validates the token before exposing a cached snapshot', async () => {
    const feed = TestBed.inject(FlightOrdersFeedService);
    feed.save('owner-a', {
      items: [order(1)],
      nextOffset: 20,
      hasMore: false,
      anchorId: null,
      anchorTop: 0,
      scrollY: 0,
      focusId: null,
    });
    vi.spyOn(feed, 'returningFromOrder').mockReturnValue(true);
    auth.accessToken.mockRejectedValue(new Error('expired'));
    const { page } = await create();
    expect(page.visibleOrders()).toEqual([]);
    expect(page.viewState()).toBe('auth');
    http.expectNone(url());
  });

  it('does not overwrite the saved viewport anchor with detached DOM coordinates during destruction', async () => {
    const { fixture, page, root } = await create();
    flush([order(1)]);
    await settle(fixture);
    const row = root.querySelector<HTMLElement>('[data-order-id]');
    if (row === null) throw new Error('Expected a rendered order');
    const rect = vi.spyOn(row, 'getBoundingClientRect').mockReturnValue({ top: 100, bottom: 280 } as DOMRect);
    page.rememberPosition(order(1).aggregateId);
    rect.mockReturnValue({ top: 0, bottom: 0 } as DOMRect);
    fixture.destroy();
    expect(TestBed.inject(FlightOrdersFeedService).restore('owner-a')?.anchorTop).toBe(100);
  });

  it('preserves an owner-bound hold hint consumed by B3 and stops polling after 30 seconds', async () => {
    vi.useFakeTimers();
    const handoff = TestBed.inject(FlightOrderHandoffService);
    handoff.rememberHeld(order(1).aggregateId, 'owner-a');
    handoff.takeOutcome(order(1).aggregateId, 'owner-a');
    const { fixture, page } = await create();
    flush([]);
    await settle(fixture);
    expect(page.projectionWaiting()).toBe(true);
    for (let i = 0; i < 14; i++) {
      vi.advanceTimersByTime(2000);
      await settle(fixture);
      flush([]);
      await settle(fixture);
    }
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    http.expectNone(url());
    expect(page.pollingEnded()).toBe(true);
    expect(page.projectionWaiting()).toBe(false);
    vi.advanceTimersByTime(4000);
    await settle(fixture);
    http.expectNone(url());
    page.refresh();
    await settle(fixture);
    flush([order(1)]);
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(1);
  });

  it('does not start a projection GET beyond the deadline when the list is opened late', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    vi.advanceTimersByTime(29_000);
    const { fixture, page } = await create();
    flush([]);
    await settle(fixture);
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    http.expectNone(url());
    expect(page.projectionWaiting()).toBe(false);
    expect(page.pollingEnded()).toBe(true);
  });

  it('ends projection waiting when the user explores the next batch instead', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    const { fixture, page } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    expect(page.projectionWaiting()).toBe(true);
    page.loadMore();
    await settle(fixture);
    flush([order(21)], 20);
    await settle(fixture);
    vi.advanceTimersByTime(31_000);
    await settle(fixture);
    expect(page.projectionWaiting()).toBe(false);
    expect(page.pollingEnded()).toBe(true);
    http.expectNone(url());
  });

  it('cancels an in-flight projection GET at the absolute deadline without clearing valid rows', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    vi.advanceTimersByTime(27_000);
    const { fixture, page } = await create();
    flush([order(1)]);
    await settle(fixture);
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    const pending = http.expectOne(url());
    vi.advanceTimersByTime(1000);
    await settle(fixture);
    expect(pending.cancelled).toBe(true);
    expect(page.projectionWaiting()).toBe(false);
    expect(page.visibleOrders()).toHaveLength(1);
    expect(page.viewState()).toBe('ready');
  });

  it('does not subscribe to a polling GET when token refresh finishes after its deadline', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    vi.advanceTimersByTime(27_000);
    const { fixture, page } = await create();
    flush([order(1)]);
    await settle(fixture);
    let resolveToken: (token: string) => void = () => undefined;
    auth.accessToken.mockReturnValueOnce(
      new Promise<string>((resolve) => {
        resolveToken = resolve;
      }),
    );
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    resolveToken('memory-only');
    await settle(fixture);
    http.expectNone(url());
    expect(page.projectionWaiting()).toBe(false);
    expect(page.visibleOrders()).toHaveLength(1);
  });

  it('replaces a pending projection GET with the automatic next batch without losing the trigger', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    const { fixture, page } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    const pendingPoll = http.expectOne(url());
    intersect();
    await settle(fixture);
    expect(pendingPoll.cancelled).toBe(true);
    const nextBatch = http.expectOne(url(20));
    page.loadMore();
    await settle(fixture);
    http.expectNone(url(20));
    nextBatch.flush({ items: [order(21)], limit: 21, offset: 20 });
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(21);
    expect(page.moreState()).toBe('idle');
    expect(page.projectionWaiting()).toBe(false);
  });

  it('allows manual loading while a cancelled polling token refresh is still pending', async () => {
    vi.useFakeTimers();
    TestBed.inject(FlightOrderHandoffService).rememberHeld(order(99).aggregateId, 'owner-a');
    const { fixture, page } = await create();
    flush(Array.from({ length: 21 }, (_, i) => order(i + 1)));
    await settle(fixture);
    let resolveToken: (token: string) => void = () => undefined;
    auth.accessToken.mockReturnValueOnce(
      new Promise<string>((resolve) => {
        resolveToken = resolve;
      }),
    );
    vi.advanceTimersByTime(2000);
    await settle(fixture);
    page.loadMore();
    await settle(fixture);
    const nextBatch = http.expectOne(url(20));
    resolveToken('memory-only');
    await settle(fixture);
    http.expectNone(url());
    page.loadMore();
    await settle(fixture);
    http.expectNone(url(20));
    nextBatch.flush({ items: [order(21)], limit: 21, offset: 20 });
    await settle(fixture);
    expect(page.visibleOrders()).toHaveLength(21);
    expect(page.viewState()).toBe('ready');
  });
});
