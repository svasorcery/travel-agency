import { TestBed } from '@angular/core/testing';
import type { FlightFareSummary } from '@travel/api-client';
import { FlightFareSummaryComponent } from './flight-fare-summary.component';

const unknown = {
  permission: 'Unknown',
  penalty: null,
  permissionReason: 'NotProvided',
  penaltyReason: 'NotProvided',
} as const;
function richSummary(): FlightFareSummary {
  return {
    schemaVersion: 1,
    aggregateId: '11111111-1111-4111-8111-111111111111',
    offerId: '22222222-2222-4222-8222-222222222222',
    quoteRevision: '33333333-3333-4333-8333-333333333333',
    fingerprint: 'a'.repeat(64),
    state: 'Current',
    reason: null,
    provenance: 'QuotedOffer',
    sourceObservedAt: '2037-01-01T00:00:00Z',
    serverObservedAt: '2037-01-01T00:00:01Z',
    offerExpiresAt: '2037-01-01T00:30:00Z',
    firstDepartureAt: '2037-01-02T00:00:00Z',
    passengerCount: 2,
    segmentCounts: [1],
    aiAvailable: false,
    aiStatus: 'Disabled',
    presentation: 'Deterministic',
    changeBeforeDeparture: unknown,
    refundBeforeDeparture: unknown,
    sections: [],
    legs: [{ leg: 0, fareBrandName: '<b>Brand label</b>', changeBeforeDeparture: unknown }],
    passengers: [0, 1].map((adult) => ({
      leg: 0,
      segment: 0,
      adult,
      fareBasisCode: 'REAL-CODE',
      cabinClassMarketing: 'economy',
      checkedBaggageQuantity: 0,
      carryOnBaggageQuantity: null,
    })),
    missingInformation: ['Не предоставлен полный текст'],
    notices: ['Не обещает зачисления'],
  };
}

describe('FlightFareSummaryComponent', () => {
  it.each([
    'Busy',
    'BudgetExceeded',
    'InvalidOutput',
    'UsageUnknown',
    'AccountingUnavailable',
    'DeadlineExceeded',
  ] as const)('explains requested organized fallback %s without replacing facts', (aiStatus) => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    fixture.componentRef.setInput('summary', { ...richSummary(), aiStatus });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-fare-ai-status]')?.textContent).toContain(
      'Показаны структурированные условия',
    );
    expect(fixture.nativeElement.textContent).toContain('Возврат до вылета:');
  });
  it('labels organized presentation while ordinary disabled deterministic presentation stays quiet', () => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    fixture.componentRef.setInput('summary', richSummary());
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-fare-ai-status]')).toBeNull();
    fixture.componentRef.setInput('summary', { ...richSummary(), aiStatus: 'Ready', presentation: 'AiOrganized' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-fare-ai-status]')?.textContent).toContain(
      'Условия сгруппированы с AI',
    );
  });
  it('reports disabled mode after an explicit organized request but keeps saved history quiet', () => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    fixture.componentRef.setInput('summary', richSummary());
    fixture.componentRef.setInput('organizedRequested', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      'Группировка с AI отключена. Показаны структурированные условия.',
    );
    fixture.componentRef.setInput('historical', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-fare-ai-status]')).toBeNull();
  });
  it('does not reconstruct conditions from legacy flags', async () => {
    await TestBed.configureTestingModule({ imports: [FlightFareSummaryComponent] }).compileComponents();
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Подробные условия не предоставлены');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });
  it('preserves zero exact penalty and unknown distinction without financial promises', () => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    expect(
      fixture.componentInstance.condition({
        permission: 'Allowed',
        penalty: { amount: '0.00', currency: 'RUB' },
        permissionReason: 'Known',
        penaltyReason: 'Known',
      }),
    ).toBe('допускается; сбор 0.00 RUB');
    expect(
      fixture.componentInstance.condition({
        permission: 'Unknown',
        penalty: null,
        permissionReason: 'NotProvided',
        penaltyReason: 'NotProvided',
      }),
    ).toBe('не предоставлено; сбор не предоставлен');
  });
  it('groups only equal facts and disclosure remains local plain text', () => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    fixture.componentRef.setInput('summary', richSummary());
    fixture.componentRef.setInput('now', Date.parse('2037-01-01T00:01:00Z'));
    fixture.detectChanges();
    expect(fixture.componentInstance.groups()).toHaveLength(1);
    expect(fixture.componentInstance.groups()[0]?.adults).toBe('1, 2');
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = true;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('<b>Brand label</b>');
    expect(fixture.nativeElement.querySelector('b')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('REAL-CODE');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    const replacement = richSummary();
    const secondPassenger = replacement.passengers[1];
    if (secondPassenger) secondPassenger.checkedBaggageQuantity = 1;
    fixture.componentRef.setInput('summary', replacement);
    expect(fixture.componentInstance.groups()).toHaveLength(2);
  });
  it('expiry and earliest departure cannot be extended by viewing or a held deadline', () => {
    const fixture = TestBed.createComponent(FlightFareSummaryComponent);
    const summary = richSummary();
    summary.firstDepartureAt = '2037-01-01T00:02:00Z';
    fixture.componentRef.setInput('summary', summary);
    fixture.componentRef.setInput('now', Date.parse('2037-01-01T00:01:00Z'));
    expect(fixture.componentInstance.stale()).toBe(false);
    fixture.componentRef.setInput('now', Date.parse(summary.firstDepartureAt));
    expect(fixture.componentInstance.stale()).toBe(true);
    fixture.componentRef.setInput('historical', true);
    fixture.componentRef.setInput('now', 0);
    expect(fixture.componentInstance.stale()).toBe(true);
  });
});
