import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CustomerRefundStatus } from '@travel/api-client';
import { FlightCustomerRefundComponent } from './flight-customer-refund.component';
import { FlightCustomerRefundService } from './flight-customer-refund.service';

const sent: CustomerRefundStatus = {
  aggregateId: '00000000-0000-0000-0000-000000000123',
  bookingVersion: 40,
  caseRevision: 3,
  state: 'Sent',
  amount: { amount: '80', currency: 'GBP' },
  destination: 'OriginalFictionalPaymentMethod',
  source: 'FictionalGateway',
  reason: 'None',
  supplierRefund: { amount: { amount: '80', currency: 'GBP' }, destination: 'Balance', financialSource: 'SupplierApi' },
  supplierSettlement: 'NotEstablished',
  customerCredit: 'NotEstablished',
  needsManualReview: false,
  observedAt: '2030-06-01T12:00:09Z',
  supplierReport: null,
};
describe('customer refund card', () => {
  const snapshot = signal<CustomerRefundStatus | null>(null);
  const view = { snapshot, busy: signal(false), message: signal(''), pollingEnded: signal(false), read: vi.fn() };
  beforeEach(async () => {
    snapshot.set(sent);
    view.message.set('');
    view.pollingEnded.set(false);
    await TestBed.configureTestingModule({
      imports: [FlightCustomerRefundComponent],
      providers: [{ provide: FlightCustomerRefundService, useValue: view }],
    }).compileComponents();
  });
  afterEach(() => TestBed.resetTestingModule());
  const render = () => {
    const f = TestBed.createComponent(FlightCustomerRefundComponent);
    f.detectChanges();
    return { f, root: f.nativeElement as HTMLElement };
  };
  it('separates exact customer money, entitlement and unestablished bank credit', () => {
    const { root } = render();
    expect(root.textContent).toContain('Демо: возврат отправлен');
    expect(root.textContent).toContain('80 GBP');
    expect(root.textContent).toContain('Условия поставщика');
    expect(root.textContent).toContain('Поступление денег от поставщика не подтверждено');
    expect(root.textContent).toContain('Зачисление клиенту не подтверждено');
    expect(root.textContent).toContain('Исходный вымышленный способ оплаты');
    expect(root.querySelector('input')).toBeNull();
    expect(root.textContent).not.toContain('Повторить выплату');
  });
  it.each([
    ['Unknown', 'Результат отправки неизвестен'],
    ['ManualReviewRequired', 'Требуется ручная проверка'],
    ['NoRefundDue', 'По условиям отмены сумма возврата — 0'],
    ['NoCustomerPayment', 'Подтверждено: клиентский платёж не выполнялся'],
    ['ConfirmedNoEffect', 'Подтверждено: выплата не выполнена'],
    ['LegacyUnverified', 'Историческая запись не доказывает выплату клиенту'],
    ['Pending', 'Отправка ещё проверяется'],
  ] as const)('renders %s without a success claim', (state, text) => {
    snapshot.set({ ...sent, state, source: 'None' });
    const { root } = render();
    expect(root.textContent).toContain(text);
    expect(root.textContent).not.toContain('Демо: возврат отправлен');
  });
  it('renders exact server decimals and operator provenance without numeric conversion', () => {
    snapshot.set({ ...sent, source: 'OperatorVerified', amount: { amount: '80.000000000000000001', currency: 'GBP' } });
    const { root } = render();
    expect(root.textContent).toContain('80.000000000000000001 GBP');
    expect(root.textContent).toContain('Подтверждено оператором');
  });
  it('refreshes only the retained read and gives an order-based manual fallback', () => {
    snapshot.set({ ...sent, state: 'ManualReviewRequired', needsManualReview: true });
    view.pollingEnded.set(true);
    const { root } = render();
    const b = root.querySelector<HTMLButtonElement>('button');
    expect(b?.textContent).toContain('Обновить состояние');
    b?.click();
    expect(view.read).toHaveBeenCalled();
    expect(root.textContent).toContain('Обратитесь в поддержку с номером заказа');
    expect(root.textContent).toContain('Автоматическое обновление остановлено');
    expect(root.querySelector('a')).toBeNull();
  });
});
