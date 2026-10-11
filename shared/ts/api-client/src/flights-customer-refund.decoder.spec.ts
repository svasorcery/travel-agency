import { decodeCustomerRefundStatus } from './flights-customer-refund.decoder';

const id = '00000000-0000-0000-0000-000000000123';
const sent = {
  aggregateId: id,
  bookingVersion: 40,
  caseRevision: 3,
  state: 'Sent',
  amount: { amount: '80.125', currency: 'GBP' },
  destination: 'OriginalFictionalPaymentMethod',
  source: 'FictionalGateway',
  reason: 'None',
  supplierRefund: {
    amount: { amount: '80.125', currency: 'GBP' },
    destination: 'Balance',
    financialSource: 'SupplierApi',
  },
  supplierSettlement: 'NotEstablished',
  customerCredit: 'NotEstablished',
  needsManualReview: false,
  observedAt: '2030-06-01T12:00:09Z',
  supplierReport: null,
};
describe('customer refund authoritative private contract', () => {
  it('preserves exact decimal money and never infers supplier settlement or bank credit', () => {
    const value = decodeCustomerRefundStatus(sent, id);
    expect(value.amount?.amount).toBe('80.125');
    expect(value.customerCredit).toBe('NotEstablished');
  });
  it.each([
    { ...sent, aggregateId: '00000000-0000-0000-0000-000000000999' },
    { ...sent, amount: { amount: 80.125, currency: 'GBP' } },
    { ...sent, amount: { amount: '8e1', currency: 'GBP' } },
    { ...sent, state: 'Credited' },
    { ...sent, bookingVersion: 1.5 },
    { ...sent, paymentReference: 'sensitive' },
    { ...sent, observedAt: '2030-06-01T12:00:09' },
  ])('rejects unbound, malformed or private-field responses', (value) => {
    expect(() => decodeCustomerRefundStatus(value, id)).toThrow();
  });
});
