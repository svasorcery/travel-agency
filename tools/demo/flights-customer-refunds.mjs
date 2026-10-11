import { randomUUID } from 'node:crypto';
export const customerRefundPresets = ['refund-sent', 'refund-zero', 'refund-result-lost'];
const decimal = (s) => {
  if (typeof s !== 'string' || !/^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/.test(s)) return null;
  const [whole, fraction = ''] = s.split('.');
  return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
};
// Offline presentation fixtures only. Runtime admission/deadline/outbox are verified in .NET.
export function createFictionalCustomerRefund(preset = null) {
  if (preset !== null && !customerRefundPresets.includes(preset))
    throw new TypeError('Unknown fictional refund preset');
  let calls = 0;
  const status = (aggregateId, order, op) => {
    const c = order.customerRefund;
    const terms = op?.phase === 'Succeeded' ? op.terms : null;
    return {
      aggregateId,
      bookingVersion: order.version,
      caseRevision: c?.revision ?? null,
      state:
        c?.state ??
        (order.status === 'Refunded'
          ? 'LegacyUnverified'
          : order.status === 'Cancelled'
            ? 'ManualReviewRequired'
            : 'NotApplicable'),
      amount: c?.amount ?? null,
      destination: c?.destination ?? null,
      source: c?.source ?? 'None',
      reason: c?.reason ?? (order.status === 'Cancelled' ? 'CaptureUnverified' : 'None'),
      supplierRefund: terms
        ? {
            amount: { amount: terms.refundAmount, currency: terms.refundCurrency },
            destination: terms.refundDestination,
            financialSource: terms.financialSource,
          }
        : null,
      supplierSettlement: 'NotEstablished',
      customerCredit: 'NotEstablished',
      needsManualReview: c?.state === 'ManualReviewRequired' || (!c && order.status === 'Cancelled'),
      observedAt: c?.observedAt ?? order.cancelledAt ?? order.bookedAt ?? '2030-06-01T12:00:00Z',
      supplierReport: null,
    };
  };
  const complete = (order, op) => {
    if (order.customerRefund) return;
    const terms = op?.terms,
      basis = order.paymentBasis;
    const captured = decimal(basis?.amount?.amount),
      amount = decimal(terms?.refundAmount);
    const valid =
      preset !== null &&
      order.status === 'Cancelled' &&
      op?.phase === 'Succeeded' &&
      op.source === 'SupplierApi' &&
      basis?.source === 'TestWalletObserved' &&
      captured !== null &&
      captured > 0n &&
      amount !== null &&
      amount <= captured &&
      basis.amount.currency === terms.refundCurrency &&
      terms.refundDestination === 'Balance' &&
      terms.financialSource === 'SupplierApi';
    const c = {
      id: randomUUID(),
      revision: 1,
      state: 'ManualReviewRequired',
      amount: null,
      destination: null,
      source: 'None',
      reason: 'CaptureUnverified',
      observedAt: new Date().toISOString(),
    };
    order.customerRefund = c;
    order.version++;
    if (!valid) return;
    c.amount = structuredClone({ amount: terms.refundAmount, currency: terms.refundCurrency });
    c.destination = 'OriginalFictionalPaymentMethod';
    if (amount === 0n) {
      c.state = 'NoRefundDue';
      c.reason = 'ZeroEntitlement';
      return;
    }
    // Retain before simulating the one fictional gateway call; reads/replays never call again.
    c.revision++;
    order.version++;
    calls++;
    if (preset === 'refund-result-lost') {
      // This preset presents the final retained manual state, without a three-minute browser wait.
      // It does not prove the runtime deadline scheduler.
      c.state = 'ManualReviewRequired';
      c.reason = 'OutcomeDeadline';
      c.revision++;
      order.version++;
    } else {
      c.state = 'Sent';
      c.source = 'FictionalGateway';
      c.reason = 'None';
      c.revision++;
      order.version++;
    }
  };
  return {
    get calls() {
      return calls;
    },
    complete,
    status,
  };
}
