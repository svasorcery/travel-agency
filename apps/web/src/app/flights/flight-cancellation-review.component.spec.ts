import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CancellationStatusResponse } from '@travel/api-client';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightCancellationService } from './flight-cancellation.service';
import { FlightCancellationReviewComponent } from './flight-cancellation-review.component';

describe('whole order consent review', () => {
  const id = booking.oneWay.response.aggregateId;
  let view: ReturnType<typeof signal<CancellationStatusResponse | null>>;
  let model: {
    snapshot: typeof view;
    terms: () => unknown;
    accepted: ReturnType<typeof signal<boolean>>;
    busy: ReturnType<typeof signal<boolean>>;
    message: ReturnType<typeof signal<string>>;
    loadState: ReturnType<typeof signal<string>>;
    termsFresh: () => boolean;
    canConsent: () => boolean;
    canAbandon: () => boolean;
    canPrepare: () => boolean;
    canRefresh: () => boolean;
    consent: ReturnType<typeof vi.fn>;
    abandon: ReturnType<typeof vi.fn>;
    prepare: ReturnType<typeof vi.fn>;
    refresh: ReturnType<typeof vi.fn>;
    read: ReturnType<typeof vi.fn>;
  };
  beforeEach(async () => {
    const itinerary = {
      ...booking.oneWay.response.offer.itinerary,
      journeyKind: 'multi-leg' as const,
      slices: Array.from({ length: 4 }, () => structuredClone(booking.oneWay.response.offer.itinerary.slices[0])),
    };
    view = signal<CancellationStatusResponse | null>({
      aggregateId: id,
      bookingStatus: 'Ticketed',
      bookingVersion: 6,
      requestedOperationId: null,
      currentOperationId: '22222222-2222-4222-8222-222222222222',
      isCurrentOperation: true,
      blockingConfirmation: null,
      serverNow: '2030-06-01T12:00:00Z',
      supplierOrderCancellation: null,
      operation: {
        operationId: '22222222-2222-4222-8222-222222222222',
        revision: 3,
        phase: 'TermsReady',
        unknownStage: 'None',
        outcome: 'None',
        resolutionSource: 'None',
        reasonCode: 'None',
        dispatchState: 'PreparationClaimed',
        nextRefreshAt: null,
        confirmedBookingVersion: null,
        readPending: false,
        terms: {
          revision: 1,
          hash: 'a'.repeat(64),
          refundAmount: '17.25',
          refundCurrency: 'USD',
          financialSource: 'SupplierApi',
          refundDestination: 'Balance',
          expiresAt: '2030-06-01T13:00:00Z',
          noticeVersion: 'cancellation-v1',
          passengerCount: 9,
          wholeOrderItinerary: itinerary,
        },
      },
    });
    const accepted = signal(false);
    model = {
      snapshot: view,
      terms: () => view()?.operation?.terms ?? null,
      accepted,
      busy: signal(false),
      message: signal(''),
      loadState: signal('ready'),
      termsFresh: () => true,
      canConsent: () => accepted(),
      canAbandon: () => true,
      canPrepare: () => false,
      canRefresh: () => false,
      consent: vi.fn(),
      abandon: vi.fn(),
      prepare: vi.fn(),
      refresh: vi.fn(),
      read: vi.fn(),
    };
    await TestBed.configureTestingModule({
      imports: [FlightCancellationReviewComponent],
      providers: [{ provide: FlightCancellationService, useValue: model }],
    }).compileComponents();
  });
  it('shows all legs, all adults, exact provider amount and separate customer payout notice before consent', async () => {
    const fixture = TestBed.createComponent(FlightCancellationReviewComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelectorAll('.cancellation-route')).toHaveLength(4);
    expect(root.textContent).toContain('9');
    expect(root.textContent).toContain('17.25 USD');
    expect(root.textContent).toContain('Выплата клиенту');
    expect(root.textContent).toContain('всего заказа');
    const button = root.querySelector('[data-action="consent-cancellation"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    const checkbox = root.querySelector('input[type="checkbox"]') as HTMLInputElement;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    button.click();
    expect(model.consent).toHaveBeenCalledTimes(1);
  });
  it('never calls preparation or confirmation while displaying unknown/manual state', () => {
    view.update((value) =>
      value === null
        ? null
        : {
            ...value,
            operation: {
              ...value.operation!,
              phase: 'ManualReviewRequired',
              outcome: 'Unknown',
              unknownStage: 'Confirmation',
              terms: null,
            },
          },
    );
    model.canAbandon = () => false;
    const fixture = TestBed.createComponent(FlightCancellationReviewComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('ручная проверка');
    expect(root.querySelector('[data-action="consent-cancellation"]')).toBeNull();
    expect(model.prepare).not.toHaveBeenCalled();
    expect(model.consent).not.toHaveBeenCalled();
  });
  it('labels operator confirmation separately and does not imply a customer payment', () => {
    view.update((value) =>
      value === null
        ? null
        : {
            ...value,
            bookingStatus: 'Cancelled',
            operation: {
              ...value.operation!,
              phase: 'Succeeded',
              outcome: 'Succeeded',
              resolutionSource: 'OperatorVerified',
              confirmedBookingVersion: 6,
              terms: { ...value.operation!.terms!, financialSource: 'OperatorVerified' },
            },
          },
    );
    const fixture = TestBed.createComponent(FlightCancellationReviewComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('оператором');
    expect(text).toContain('Финансовые условия проверены оператором.');
    expect(text).toContain('Выплата клиенту');
  });
});
