import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import {
  type CancellationPhase,
  type CancellationStatusResponse,
  type FlightQuoteResponse,
  FlightsAncillariesApiService,
  FlightsCancellationApiService,
  FlightsCustomerRefundApiService,
} from '@travel/api-client';
import { BehaviorSubject, of, throwError } from 'rxjs';
// Shared fictional fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderLiveUpdatesService, ORDER_EVENTS_TRANSPORT } from './flight-order-live-updates.service';
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
  projectedStreamVersion: 3,
  ticketNumbers: [],
  bookedAt: '2030-06-01T10:00:00Z',
  ticketedAt: null,
  cancelledAt: null,
  refundedAt: null,
};

describe('FlightOrderPageComponent', () => {
  it('shows purchased actual seats and bag coverage on a Matches read after reload', async () => {
    const passenger = booking.oneWay.response.binding.slots[0].bookingPassengerId;
    const services = [
      {
        selectionKey: 'ser_bag',
        kind: 'checked-baggage' as const,
        bookingPassengerId: passenger,
        segments: [{ leg: 0, segment: 0 }],
        quantity: 2,
        lineTotal: { amount: '20.00', currency: 'GBP' },
        seatDesignator: null,
        disclosures: null,
        baggage: { maximumWeightKg: 23, maximumHeightCm: null, maximumDepthCm: 40, maximumLengthCm: null },
      },
      {
        selectionKey: 'ser_seat',
        kind: 'seat' as const,
        bookingPassengerId: passenger,
        segments: [{ leg: 0, segment: 0 }],
        quantity: 1,
        lineTotal: { amount: '0.00', currency: 'GBP' },
        seatDesignator: '12A',
        disclosures: ['Fictional seat conditions.'],
        baggage: null,
      },
    ];
    const accepted = {
      quoteRevision: booking.oneWay.response.binding.revision,
      baseFare: { amount: '50.00', currency: 'GBP' },
      extras: { amount: '20.00', currency: 'GBP' },
      total: { amount: '70.00', currency: 'GBP' },
      services,
      expiresAt: '2030-06-09T23:59:00Z',
      noticeVersion: 'booking-services-v1' as const,
    };
    vi.mocked(TestBed.inject(FlightsAncillariesApiService).getCreation).mockReturnValue(
      of({
        aggregateId: id,
        state: 'Matches',
        bookingStatus: 'Held',
        passengerCount: 1,
        bookingPassengerIds: [passenger],
        itinerary: order.itinerary,
        accepted,
        actual: { total: accepted.total, services },
        heldUntil: '2030-06-10T10:15:00Z',
        canConfirm: true,
        canCancel: true,
        canRefresh: false,
        observedAt: new Date().toISOString(),
      }),
    );
    const { fixture, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Место 12A');
    expect(root.textContent).toContain('Дополнительный багаж · 2');
    expect(root.textContent).toContain('Пассажир 1');
    expect(root.textContent).toContain('23 кг');
  });
  let http: HttpTestingController;
  let cancellationStatus: CancellationStatusResponse;
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
    TestBed.overrideProvider(ORDER_EVENTS_TRANSPORT, {
      useValue: (options: { signal: AbortSignal }) =>
        new Promise<void>((resolve) => options.signal.addEventListener('abort', () => resolve(), { once: true })),
    });
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
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    cancellationStatus = {
      aggregateId: id,
      bookingStatus: 'Held',
      bookingVersion: 3,
      requestedOperationId: null,
      currentOperationId: null,
      isCurrentOperation: true,
      operation: null,
      blockingConfirmation: null,
      serverNow: new Date().toISOString(),
      supplierOrderCancellation: null,
    };
    vi.spyOn(TestBed.inject(FlightsCancellationApiService), 'status').mockImplementation((aggregateId) =>
      of({ ...cancellationStatus, aggregateId: aggregateId.toLowerCase() }),
    );
    vi.spyOn(TestBed.inject(FlightsCustomerRefundApiService), 'status').mockImplementation((aggregateId) =>
      of({
        aggregateId,
        bookingVersion: cancellationStatus.bookingVersion,
        caseRevision: null,
        state: 'NotApplicable',
        amount: null,
        destination: null,
        source: 'None',
        reason: 'None',
        supplierRefund: null,
        supplierSettlement: 'NotEstablished',
        customerCredit: 'NotEstablished',
        needsManualReview: false,
        observedAt: new Date().toISOString(),
        supplierReport: null,
      }),
    );
    // These cases cover the compatible historical no-service path. Dedicated ancillaries
    // cases below control its authoritative creation facts instead of deriving them from EF.
    vi.spyOn(TestBed.inject(FlightsAncillariesApiService), 'getCreation').mockImplementation((aggregateId) =>
      of({
        aggregateId,
        state: 'NotStarted',
        bookingVersion: cancellationStatus.bookingVersion,
        bookingStatus: cancellationStatus.bookingStatus,
        passengerCount: 1,
        bookingPassengerIds: [],
        itinerary: order.itinerary,
        accepted: null,
        actual: null,
        heldUntil: null,
        canConfirm: cancellationStatus.bookingStatus === 'Held' && cancellationStatus.blockingConfirmation === null,
        canCancel: ['Held', 'Confirmed', 'Ticketed'].includes(cancellationStatus.bookingStatus),
        canRefresh: false,
        observedAt: new Date().toISOString(),
      }),
    );
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      vi.useRealTimers();
      vi.restoreAllMocks();
      TestBed.resetTestingModule();
    }
  });

  async function settleReads() {
    for (let i = 0; i < 12; i++) await Promise.resolve();
  }
  async function flushProjection(url: string, body: object | null, options?: { status: number; statusText: string }) {
    let requests = http.match(url);
    expect(requests.length).toBeGreaterThan(0);
    for (let pass = 0; pass < 4 && requests.length; pass++) {
      for (const request of requests) request.flush(body, options);
      await settleReads();
      requests = http.match(url);
    }
    expect(requests).toHaveLength(0);
  }
  function createPage() {
    const fixture = TestBed.createComponent(FlightOrderPageComponent);
    fixture.detectChanges();
    return { fixture, page: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }
  function unavailableSource() {
    const missing = () => throwError(() => new HttpErrorResponse({ status: 404 }));
    vi.mocked(TestBed.inject(FlightsAncillariesApiService).getCreation).mockImplementation(missing);
    vi.mocked(TestBed.inject(FlightsCancellationApiService).status).mockImplementation(missing);
    vi.mocked(TestBed.inject(FlightsCustomerRefundApiService).status).mockImplementation(missing);
  }

  function reply(
    phase: CancellationPhase = 'TermsReady',
    op = '22222222-2222-4222-8222-222222222222',
    version = 6,
  ): CancellationStatusResponse {
    return {
      aggregateId: id,
      bookingStatus: phase === 'Succeeded' ? 'Cancelled' : 'Held',
      bookingVersion: version,
      requestedOperationId: null,
      currentOperationId: op,
      isCurrentOperation: true,
      blockingConfirmation: null,
      serverNow: new Date().toISOString(),
      supplierOrderCancellation: null,
      operation: {
        operationId: op,
        revision: 3,
        phase,
        unknownStage: 'None',
        outcome: phase === 'Succeeded' ? 'Succeeded' : phase === 'Rejected' ? 'Rejected' : 'None',
        resolutionSource: phase === 'Succeeded' || phase === 'Rejected' ? 'SupplierApi' : 'None',
        reasonCode: 'None',
        dispatchState: 'PreparationClaimed',
        nextRefreshAt: null,
        confirmedBookingVersion: phase === 'Succeeded' ? version : null,
        readPending: false,
        terms: {
          revision: 1,
          hash: 'a'.repeat(64),
          refundAmount: '17.25',
          refundCurrency: 'USD',
          financialSource: 'SupplierApi',
          refundDestination: 'Balance',
          expiresAt: new Date(Date.now() + 600_000).toISOString(),
          noticeVersion: 'cancellation-v1',
          passengerCount: 1,
          wholeOrderItinerary: order.itinerary,
        },
      },
    };
  }
  async function prepareReview(
    fixture: ReturnType<typeof TestBed.createComponent<FlightOrderPageComponent>>,
    page: FlightOrderPageComponent,
  ) {
    page.requestCancellation();
    await fixture.whenStable();
    await settleReads();
    const request = http.expectOne('/api/flights/cancellations/prepare');
    cancellationStatus = reply('TermsReady', JSON.parse(request.request.body).operationId);
    request.flush(cancellationStatus, { status: 202, statusText: 'Accepted' });
    await fixture.whenStable();
  }
  async function consentRequest(
    fixture: ReturnType<typeof TestBed.createComponent<FlightOrderPageComponent>>,
    page: FlightOrderPageComponent,
  ) {
    page.cancellation.accepted.set(true);
    page.acceptCancellation();
    page.acceptCancellation();
    await fixture.whenStable();
    return http.expectOne('/api/flights/cancellations/consent');
  }
  it('consults persisted confirmation barrier before offering a new confirmation after reload', async () => {
    cancellationStatus = {
      ...cancellationStatus,
      blockingConfirmation: {
        kind: 'ConfirmationAttempt',
        targetId: '22222222-2222-4222-8222-222222222222',
        revision: 2,
        phase: 'ManualReviewRequired',
        reasonCode: 'ManualVerificationRequired',
        canCloseNotDispatched: false,
      },
    };
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('[data-action="confirm-order"]')).toBeNull();
    page.confirm();
    await fixture.whenStable();
    http.expectNone('/api/flights/orders/confirm');
    expect(root.textContent).toContain('Подтверждение заказа');
  });
  it('preserves known server cancellation across an old Held projection and projection404', async () => {
    cancellationStatus = { ...cancellationStatus, bookingStatus: 'Cancelled', bookingVersion: 10 };
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
    expect(root.querySelector('[data-action="confirm-order"]')).toBeNull();
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {}, { status: 404, statusText: 'Not found' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
    expect(root.textContent).toContain('Заказ отменён');
  });
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
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, current);
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
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
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
    await settleReads();
    await settleReads();
    http.expectOne('/api/flights/orders/hold').error(new ProgressEvent('error'));
    const { fixture, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
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
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Обновляем статус');
    expect(root.textContent).not.toContain('не найден');

    await page.refresh();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ подтверждён');
    expect(root.textContent).not.toContain('Подтвердить заказ');
  });

  it('requires exact terms consent, blocks double click and preserves success over stale projection', async () => {
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    fixture.detectChanges();
    expect(root.textContent).toContain('17.25 USD');
    page.acceptCancellation();
    http.expectNone('/api/flights/cancellations/consent');
    const request = await consentRequest(fixture, page);
    expect(JSON.parse(request.request.body).termsHash).toBe('a'.repeat(64));
    cancellationStatus = reply('Succeeded', cancellationStatus.currentOperationId!, 10);
    request.flush(cancellationStatus);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.displayStatus()).toBe('Cancelled');
  });

  it('keeps an unknown confirm as a cancellation barrier after a route round trip', async () => {
    const { fixture, page, root } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    page.confirm();
    await fixture.whenStable();
    const confirmation = http.expectOne('/api/flights/orders/confirm');
    fixture.detectChanges();
    expect(page.cancellation.canPrepare()).toBe(false);
    expect(root.querySelector('[data-action="cancel-order"]')).toBeNull();
    confirmation.error(new ProgressEvent('error'));
    await fixture.whenStable();
    routeId.next(convertToParamMap({ aggregateId: '11111111-1111-1111-1111-111111111111' }));
    await fixture.whenStable();
    await flushProjection('/api/flights/orders/11111111-1111-1111-1111-111111111111', {
      ...order,
      aggregateId: '11111111-1111-1111-1111-111111111111',
    });
    routeId.next(convertToParamMap({ aggregateId: id }));
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    expect(page.canCancel()).toBe(false);
    expect(page.confirmState()).toBe('unknown');
    fixture.detectChanges();
    expect(page.cancellation.canPrepare()).toBe(false);
    expect(page.cancellation.prepare()).toBe(false);
    expect(root.querySelector('[data-action="cancel-order"]')).toBeNull();
    http.expectNone('/api/flights/cancellations/prepare');
  });

  it('requires authoritative closure before a new cancellation after rejection', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    const request = await consentRequest(fixture, page);
    request.flush(
      { type: 'https://travel.local/errors/Flights.CancellationStaleProposal' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    expect(page.canCancel()).toBe(false);
    cancellationStatus = reply('Rejected', cancellationStatus.currentOperationId!, 8);
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
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
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    const mutation = await consentRequest(fixture, page);
    cancellationStatus = reply('Succeeded', cancellationStatus.currentOperationId!, 10);
    mutation.flush(cancellationStatus);
    await fixture.whenStable();
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {}, { status, statusText: 'Denied' });
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
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    const post = await consentRequest(fixture, page);
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {}, { status: 403, statusText: 'Denied' });
    await fixture.whenStable();
    post.flush(reply('Succeeded', cancellationStatus.currentOperationId!, 10));
    await fixture.whenStable();
    expect(page.visibleOrder()).toBeNull();
    expect(page.cancellation.snapshot()).toBeNull();
  });

  it('clears acceptance when authoritative booking state changes during review', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    page.cancellation.accepted.set(true);
    cancellationStatus = { ...cancellationStatus, bookingStatus: 'Confirmed', bookingVersion: 7 };
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { ...order, status: 'Confirmed' });
    await fixture.whenStable();
    expect(page.cancellation.accepted()).toBe(false);
    page.acceptCancellation();
    http.expectNone('/api/flights/cancellations/consent');
  });

  it('does not overwrite Refunded projection with an earlier cancellation', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await fixture.whenStable();
    await prepareReview(fixture, page);
    const mutation = await consentRequest(fixture, page);
    cancellationStatus = reply('Succeeded', cancellationStatus.currentOperationId!, 10);
    mutation.flush(cancellationStatus);
    await fixture.whenStable();
    page.refresh();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Refunded',
      refundedAt: '2030-06-01T12:00:00Z',
    });
    await fixture.whenStable();
    expect(page.displayStatus()).toBe('Refunded');
    expect(page.viewState()).toBe('ready');
  });

  it('keeps a successful Held handoff through an early projection 404, then shows the projected order', async () => {
    TestBed.inject(FlightOrderHandoffService).rememberHeld(id, 'demo-owner');
    const { fixture, page, root } = createPage();
    expect(root.textContent).toContain('Заказ удержан');
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Обновляем удержание');
    expect(root.textContent).not.toContain('Заказ не найден');
    expect(root.querySelector('[data-action="refresh-order"]')).not.toBeNull();

    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
    expect(root.textContent).toContain('Точный срок удержания недоступен');
    expect(root.querySelector('[data-action="confirm-order"]')).not.toBeNull();
  });

  it('treats owner-scoped 404 on a direct link as unavailable', async () => {
    unavailableSource();
    vi.mocked(TestBed.inject(FlightsCancellationApiService).status).mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
    expect(root.textContent).not.toContain('Обновляем статус');
    expect(root.querySelector('[data-action="refresh-order"]')).not.toBeNull();
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
  });

  it('does not show a confirmation handoff from a different signed-in owner', async () => {
    unavailableSource();
    vi.mocked(TestBed.inject(FlightsCancellationApiService).status).mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    TestBed.inject(FlightOrderHandoffService).rememberConfirmed(id, 'previous-owner');
    const { fixture, root } = createPage();
    expect(root.textContent).not.toContain('Заказ подтверждён');
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
  });

  it('retains proven same-owner ticket details through a temporary projection 404', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Ticketed',
      ticketNumbers: ['TKT-PRIVATE'],
    });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('TKT-PRIVATE');

    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 403 }, { status: 403, statusText: 'Forbidden' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Доступ к заказу запрещён');
    expect(root.textContent).not.toContain('TKT-PRIVATE');

    auth.status.set({ kind: 'authenticated', userId: 'demo-owner' });
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Ticketed',
      ticketNumbers: ['TKT-PRIVATE'],
    });
    await settleReads();
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).not.toContain('Заказ не найден или недоступен');
    expect(root.textContent).toContain('TKT-PRIVATE');
  });

  it('hides the previous owner view as soon as the signed-in identity changes', async () => {
    vi.mocked(TestBed.inject(FlightsCancellationApiService).status).mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Ticketed',
      ticketNumbers: ['TKT-PRIVATE'],
    });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('TKT-PRIVATE');

    auth.status.set({ kind: 'authenticated', userId: 'other-owner' });
    fixture.detectChanges();
    expect(root.textContent).not.toContain('TKT-PRIVATE');
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
    unavailableSource();
    await page.login();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 404 }, { status: 404, statusText: 'Not Found' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ не найден или недоступен');
  });

  it('discards raw detail data from an earlier identity epoch even if the same user returns', async () => {
    const { fixture, page } = createPage();
    await fixture.whenStable();
    await settleReads();
    await settleReads();
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
    await settleReads();
    await settleReads();
    await settleReads();
    const pending = http.expectOne(`/api/flights/orders/${id}`);
    auth.status.set({ kind: 'authenticated', userId: 'other-owner' });
    pending.flush({ ...order, status: 'Ticketed', ticketNumbers: ['TKT-PRIVATE'] });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
    expect(root.textContent).not.toContain('TKT-PRIVATE');
  });

  it('shows ticket numbers only for Ticketed and does not invent a Held deadline', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ удержан');
    expect(root.textContent).not.toContain('Удержано до');

    await page.refresh();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { ...order, status: 'Ticketed', ticketNumbers: ['TKT-001'] });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Билет выписан');
    expect(root.textContent).toContain('TKT-001');
    expect(root.textContent).not.toContain('SU SU101');
  });

  it('confirms a recovered Held order with a new stable key after explicit review', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Точный срок удержания недоступен');
    page.confirm();
    await settleReads();
    const request = http.expectOne('/api/flights/orders/confirm');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i,
    );
    expect(request.request.body).toBe(JSON.stringify({ aggregateId: id }));
    request.flush({ aggregateId: id, status: 'Confirmed', paymentRef: null });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ подтверждён');
    expect(root.textContent).not.toContain('Билет выписан');
  });

  it('does not repeat an unknown confirmation after observing a Held GET', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    page.confirm();
    await settleReads();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('error'));
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Исход подтверждения неизвестен');
    page.retryConfirm();
    await settleReads();
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
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
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
    await settleReads();
    expect(page.aggregateId()).toBe(nextId);
    http.expectNone(() => true);
  });

  it('cancels an old GET when the route changes and ignores its result', async () => {
    const { fixture, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    const oldRequest = http.expectOne(`/api/flights/orders/${id}`);
    const nextId = '11111111-1111-1111-1111-111111111111';
    routeId.next(convertToParamMap({ aggregateId: nextId }));
    expect(oldRequest.cancelled).toBe(true);
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${nextId}`, {
      ...order,
      aggregateId: nextId,
      status: 'Cancelled',
      cancelledAt: '2030-06-01T11:00:00Z',
    });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ отменён');
    expect(root.textContent).toContain(nextId);
  });

  it('stops automatic polling after the bounded window and still allows manual refresh', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2030-06-01T10:00:00Z'));
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { ...order, status: 'Confirmed' });
    await settleReads();
    await vi.advanceTimersByTimeAsync(2_000);
    await settleReads();
    await settleReads();
    const second = http.expectOne(`/api/flights/orders/${id}`);
    second.flush({ ...order, status: 'Confirmed' });
    await settleReads();
    vi.setSystemTime(new Date('2030-06-01T10:00:32Z'));
    await vi.advanceTimersByTimeAsync(2_000);
    http.expectNone(`/api/flights/orders/${id}`);
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Проверяем актуальность данных');
    vi.advanceTimersByTime(5_000);
    http.expectNone(`/api/flights/orders/${id}`);
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { ...order, status: 'Confirmed' });
  });

  it('keeps GET recovery for terminal cancellation without creating another operation', async () => {
    vi.useFakeTimers();
    cancellationStatus = reply('Succeeded', '22222222-2222-4222-8222-222222222222', 10);
    const { fixture, page } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, order);
    await settleReads();
    await settleReads();
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(35_000);
    expect(page.displayStatus()).toBe('Cancelled');
    const reads = http.match(`/api/flights/orders/${id}`);
    expect(reads.length).toBeGreaterThan(0);
    for (const request of reads)
      if (!request.cancelled)
        request.flush({
          ...order,
          projectedStreamVersion: 10,
          status: 'Cancelled',
          cancelledAt: '2030-06-01T11:00:00Z',
        });
    await settleReads();
    http.expectNone('/api/flights/cancellations/prepare');
    http.expectNone('/api/flights/cancellations/consent');
  });
  it('separates forbidden and unreadable responses from an owner 404', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 403 }, { status: 403, statusText: 'Forbidden' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Доступ к заказу запрещён');
    expect(root.textContent).not.toContain('Заказ не найден');

    auth.status.set({ kind: 'authenticated', userId: 'demo-owner' });
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { aggregateId: id, status: 'Ticketed' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Ответ о заказе не удалось прочитать');
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { status: 401 }, { status: 401, statusText: 'Unauthorized' });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Войдите, чтобы увидеть заказ');
  });

  it('shows Cancelled and Refunded as distinct terminal outcomes', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Cancelled',
      cancelledAt: '2030-06-01T11:00:00Z',
    });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ отменён');
    page.refresh();
    await settleReads();
    await settleReads();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Refunded',
      refundedAt: '2030-06-01T12:00:00Z',
    });
    await settleReads();
    fixture.detectChanges();
    expect(root.textContent).toContain('Историческая запись не доказывает выплату клиенту');
    expect(root.textContent).not.toContain('Возврат оформлен');
  });
  it('uses the stream report guard to prevent new confirmation and cancellation controls', async () => {
    vi.mocked(TestBed.inject(FlightsCustomerRefundApiService).status).mockImplementation((aggregateId) =>
      of({
        aggregateId,
        bookingVersion: 40,
        caseRevision: null,
        state: 'ManualReviewRequired',
        amount: null,
        destination: null,
        source: 'None',
        reason: 'SupplierReportUnverified',
        supplierRefund: null,
        supplierSettlement: 'NotEstablished',
        customerCredit: 'NotEstablished',
        needsManualReview: true,
        observedAt: new Date().toISOString(),
        supplierReport: {
          state: 'Unverified',
          source: 'SignedWebhook',
          observedAt: new Date().toISOString(),
          cancelledAt: null,
        },
      }),
    );
    const { fixture, page, root } = createPage();
    await settleReads();
    await settleReads();
    await settleReads();
    http.expectOne('/api/flights/orders/' + id).flush(order);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.bookingBlocked()).toBe(true);
    expect(page.cancellation.canPrepare()).toBe(false);
    expect(root.querySelector('[data-action="confirm-order"]')).toBeNull();
    expect(root.textContent).toContain('Фактический исход требует проверки');
  });
  it('keeps a higher checkpoint ticket after a stale response and later GET failure', async () => {
    const { fixture, page, root } = createPage();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {
      ...order,
      status: 'Ticketed',
      projectedStreamVersion: 12,
      ticketNumbers: ['DEMO-PROVEN'],
    });
    page.refresh();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, { ...order, projectedStreamVersion: 11 });
    expect(page.order()?.projectedStreamVersion).toBe(12);
    page.refresh();
    await settleReads();
    await flushProjection(`/api/flights/orders/${id}`, {}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(root.textContent).toContain('DEMO-PROVEN');
    expect(root.textContent).toContain('последний полученный результат');
    http.expectNone('/api/flights/orders/confirm');
  });
  it.each(['prepare', 'consent'] as const)(
    'gates actual leaf %s admission during a newer live hint',
    async (command) => {
      if (command === 'consent') cancellationStatus = reply('TermsReady', undefined, 3);
      const { fixture, page } = createPage();
      await settleReads();
      await flushProjection(`/api/flights/orders/${id}`, order);
      fixture.detectChanges();
      page.cancellation.accepted.set(true);
      expect(command === 'prepare' ? page.cancellation.canPrepare() : page.cancellation.canConsent()).toBe(true);
      const live = fixture.debugElement.injector.get(FlightOrderLiveUpdatesService);
      live.requestRefresh('hint', 4);
      expect(command === 'prepare' ? page.cancellation.canPrepare() : page.cancellation.canConsent()).toBe(false);
      expect(page.cancellation[command]()).toBe(false);
      await settleReads();
      await flushProjection(`/api/flights/orders/${id}`, {}, { status: 503, statusText: 'Unavailable' });
      expect(page.cancellation[command]()).toBe(false);
      http.expectNone((request) => request.method === 'POST');
    },
  );
});
