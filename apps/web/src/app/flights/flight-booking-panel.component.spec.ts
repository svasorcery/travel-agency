import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
// Shared quote fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightsBookingPanelComponent } from './flight-booking-panel.component';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightsAuthService } from './flights-auth.service';

function bookingProblem(status: number, code: string, detail = code) {
  return {
    status,
    title: status === 400 ? 'Validation' : 'Conflict',
    type: `https://travel.local/errors/${code}`,
    detail,
  };
}

describe('FlightsBookingPanelComponent', () => {
  let http: HttpTestingController;
  let authMock: {
    status: ReturnType<typeof signal>;
    accessToken: ReturnType<typeof vi.fn>;
    isTestEnvironment: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    authMock = {
      status: signal({ kind: 'authenticated', userId: 'demo-owner' }),
      accessToken: vi.fn().mockResolvedValue('memory-only-access-token'),
      isTestEnvironment: vi.fn().mockReturnValue(true),
    };
    await TestBed.configureTestingModule({
      imports: [FlightsBookingPanelComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: FlightsAuthService, useValue: authMock },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    sessionStorage.clear();
  });

  afterEach(() => http.verify());

  function createPanel() {
    const fixture = TestBed.createComponent(FlightsBookingPanelComponent);
    fixture.componentRef.setInput('quote', booking.oneWay.response);
    fixture.componentRef.setInput('isDemo', true);
    fixture.detectChanges();
    return { fixture, panel: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }

  function fillPassenger(panel: FlightsBookingPanelComponent) {
    panel.passengerForm.patchValue({
      givenName: 'Demo',
      familyName: 'Traveler',
      dateOfBirth: '1990-04-12',
      gender: 'unspecified',
      email: 'demo@example.test',
      phone: '+79161234567',
    });
  }

  it('keeps the confirm barrier when B2 is destroyed before its command outcome arrives', async () => {
    const { fixture, panel } = createPanel();
    panel.heldOrder.set({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'fixture-order',
      heldUntil: '2030-06-01T11:00:00Z',
    });
    panel.confirm();
    await fixture.whenStable();
    const req = http.expectOne('/api/flights/orders/confirm');
    fixture.destroy();
    expect(
      TestBed.inject(FlightOrderOperationsService).startCancel(booking.oneWay.response.aggregateId, 'demo-owner'),
    ).toBe(false);
    req.error(new ProgressEvent('error'));
    await Promise.resolve();
    await Promise.resolve();
    expect(
      TestBed.inject(FlightOrderOperationsService).blocksWrite(booking.oneWay.response.aggregateId, 'demo-owner'),
    ).toBe(true);
  });

  it('holds one passenger and separately confirms without claiming a ticket', async () => {
    const { fixture, panel, root } = createPanel();
    const held = vi.fn();
    panel.held.subscribe(held);
    const confirmed = vi.fn();
    panel.confirmed.subscribe(confirmed);
    fillPassenger(panel);
    (root.querySelector('[data-action="hold"]') as HTMLButtonElement).click();
    const hold = http.expectOne('/api/flights/orders/hold');
    expect(hold.request.headers.has('Authorization')).toBe(false);
    expect(hold.request.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/i);
    expect(JSON.parse(hold.request.body as string).passengers).toHaveLength(1);
    hold.flush({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Удержано до');
    expect(held).toHaveBeenCalledWith({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
    expect(root.textContent).toContain('тестовый кошелёк');
    expect(root.querySelector('[data-action="open-held-order"]')?.getAttribute('href')).toBe(
      `/flights/orders/${booking.oneWay.response.aggregateId}`,
    );

    (root.querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const confirm = http.expectOne('/api/flights/orders/confirm');
    expect(confirm.request.headers.get('Idempotency-Key')).not.toBe(hold.request.headers.get('Idempotency-Key'));
    expect(JSON.parse(confirm.request.body as string)).toEqual({ aggregateId: booking.oneWay.response.aggregateId });
    confirm.flush({ aggregateId: booking.oneWay.response.aggregateId, status: 'Confirmed', paymentRef: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Заказ подтверждён');
    expect(root.textContent?.toLowerCase()).toContain('билет ещё не выписан');
    expect(root.textContent).not.toContain('Ticketed');
    expect(confirmed).toHaveBeenCalledWith({
      aggregateId: booking.oneWay.response.aggregateId,
      status: 'Confirmed',
      paymentRef: null,
    });
  });

  it('retries an unknown hold outcome with the same key and exact body', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    (root.querySelector('[data-action="hold"]') as HTMLButtonElement).click();
    const first = http.expectOne('/api/flights/orders/hold');
    const firstBody = first.request.body as string;
    const firstKey = first.request.headers.get('Idempotency-Key');
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Исход удержания неизвестен');

    (root.querySelector('[data-action="retry-hold"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const retry = http.expectOne('/api/flights/orders/hold');
    expect(retry.request.body).toBe(firstBody);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(firstKey);
  });

  it('creates only one hold attempt for repeated submit while the first request is pending', () => {
    const { panel } = createPanel();
    fillPassenger(panel);
    panel.hold();
    const first = http.expectOne('/api/flights/orders/hold');
    panel.hold();
    http.expectNone('/api/flights/orders/hold');
    first.flush({ status: 410, title: 'Flights.OfferExpired' }, { status: 410, statusText: 'Expired' });
  });

  it('retries an in-flight hold with the same key and exact body', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    const first = http.expectOne('/api/flights/orders/hold');
    const firstKey = first.request.headers.get('Idempotency-Key');
    const firstBody = first.request.body;
    first.flush(bookingProblem(409, 'Flights.IdempotencyInFlight'), { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Запрос ещё обрабатывается');

    (root.querySelector('[data-action="retry-hold"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const retry = http.expectOne('/api/flights/orders/hold');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(firstKey);
    expect(retry.request.body).toBe(firstBody);
    retry.flush({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
  });

  it('blocks a reused-key body conflict without offering another hold', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http.expectOne('/api/flights/orders/hold').flush(bookingProblem(409, 'Flights.IdempotencyConflict'), {
      status: 409,
      statusText: 'Conflict',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('другим содержимым');
    expect(root.querySelector('[data-action="retry-hold"]')).toBeNull();
    expect(root.querySelector('[data-action="hold"]')).toBeNull();
  });

  it('retries an unknown confirm outcome with the same key and exact body', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http.expectOne('/api/flights/orders/hold').flush({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    panel.confirm();
    await fixture.whenStable();
    const first = http.expectOne('/api/flights/orders/confirm');
    const firstKey = first.request.headers.get('Idempotency-Key');
    const firstBody = first.request.body;
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Исход подтверждения неизвестен');

    (root.querySelector('[data-action="retry-confirm"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const retry = http.expectOne('/api/flights/orders/confirm');
    expect(retry.request.headers.get('Idempotency-Key')).toBe(firstKey);
    expect(retry.request.body).toBe(firstBody);
    retry.flush({ aggregateId: booking.oneWay.response.aggregateId, status: 'Confirmed', paymentRef: null });
  });

  it('does not offer a second hold after the server says the quote expired', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http.expectOne('/api/flights/orders/hold').flush(bookingProblem(400, 'Flights.OfferExpired'), {
      status: 400,
      statusText: 'Bad Request',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Обновите цену перед новым оформлением');
    expect(root.querySelector('[data-action="hold"]')).toBeNull();
    expect(root.querySelector('[data-action="refresh-quote"]')).not.toBeNull();
    expect(panel.passengerForm.controls.email.value).toBe('');
    http.expectNone('/api/flights/orders/hold');
  });

  it('can return to quote review when the quote expires while entering a passenger', () => {
    const { fixture, panel, root } = createPanel();
    const refresh = vi.fn();
    panel.quoteRefreshRequested.subscribe(refresh);
    fixture.componentRef.setInput('quote', {
      ...booking.oneWay.response,
      offer: { ...booking.oneWay.response.offer, expiresAt: '2000-01-01T00:00:00Z' },
    });
    fixture.detectChanges();
    fillPassenger(panel);
    panel.hold();
    fixture.detectChanges();
    expect(root.querySelector('[data-action="hold"]')).toBeNull();
    (root.querySelector('[data-action="refresh-quote"]') as HTMLButtonElement).click();
    expect(refresh).toHaveBeenCalledOnce();
    http.expectNone('/api/flights/orders/hold');
  });

  it('does not create a new confirm key after the held order expires', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http.expectOne('/api/flights/orders/hold').flush({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    panel.confirm();
    await fixture.whenStable();
    http.expectOne('/api/flights/orders/confirm').flush(bookingProblem(409, 'Flights.HoldExpired'), {
      status: 409,
      statusText: 'Conflict',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Начните оформление заново');
    expect(root.querySelector('[data-action="confirm"]')).toBeNull();
    expect(root.querySelector('[data-action="restart-search"]')).not.toBeNull();
    http.expectNone('/api/flights/orders/confirm');
  });

  it('does not mislabel an invalid booking state as an idempotency conflict', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http.expectOne('/api/flights/orders/hold').flush(bookingProblem(409, 'Flights.InvalidState'), {
      status: 409,
      statusText: 'Conflict',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('Состояние заказа требует проверки');
    expect(root.querySelector('[data-action="retry-hold"]')).toBeNull();
    expect(root.querySelector('[data-action="hold"]')).toBeNull();
  });

  it('maps a backend passenger validation code to the matching field', async () => {
    const { fixture, panel, root } = createPanel();
    fillPassenger(panel);
    panel.hold();
    http
      .expectOne('/api/flights/orders/hold')
      .flush(bookingProblem(400, 'PassengerInfo.EmailInvalid', 'Email address is not valid.'), {
        status: 400,
        statusText: 'Bad Request',
      });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('#passenger-email-error')?.textContent).toContain('Проверьте email');
  });

  it('keeps passenger data in the form only and focuses validation before sending', () => {
    const { fixture, root } = createPanel();
    (root.querySelector('[data-action="hold"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root.querySelector('[aria-invalid="true"]')).not.toBeNull();
    expect(sessionStorage.length).toBe(0);
    http.expectNone('/api/flights/orders/hold');
  });

  it('keeps an unknown hold key and body if token refresh fails before a retry', async () => {
    const { fixture, panel, root } = createPanel();
    fixture.componentRef.setInput('isDemo', false);
    fillPassenger(panel);
    authMock.accessToken
      .mockResolvedValueOnce('first-memory-token')
      .mockRejectedValueOnce(new Error('expired'))
      .mockResolvedValueOnce('refreshed-memory-token');
    panel.hold();
    await fixture.whenStable();
    const first = http.expectOne('/api/flights/orders/hold');
    const firstKey = first.request.headers.get('Idempotency-Key');
    const firstBody = first.request.body;
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();

    (root.querySelector('[data-action="retry-hold"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('предыдущее удержание могло пройти');
    http.expectNone('/api/flights/orders/hold');

    (root.querySelector('[data-action="retry-hold"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    const retry = http.expectOne('/api/flights/orders/hold');
    expect(retry.request.body).toBe(firstBody);
    expect(retry.request.headers.get('Idempotency-Key')).toBe(firstKey);
    retry.flush({
      aggregateId: booking.oneWay.response.aggregateId,
      providerOrderId: 'demo-order',
      heldUntil: '2030-06-10T10:15:00Z',
    });
  });
});
