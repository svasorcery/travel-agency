import { cancellationGuid } from './flights-cancellation.decoder';
import {
  type CustomerRefundMoney,
  type CustomerRefundStatus,
  customerRefundStates,
} from './flights-customer-refund.types';

export class FlightCustomerRefundContractError extends Error {
  constructor() {
    super('Invalid Flights customer refund response.');
    this.name = 'FlightCustomerRefundContractError';
  }
}
const fail = (): never => {
  throw new FlightCustomerRefundContractError();
};
function object(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return fail();
  const r = value as Record<string, unknown>;
  if (Object.keys(r).some((key) => !keys.includes(key)) || keys.some((key) => !(key in r))) return fail();
  return r;
}
function choice<T extends string>(value: unknown, options: readonly T[]): T {
  if (typeof value !== 'string' || !options.includes(value as T)) return fail();
  return value as T;
}
function revision(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value <= 0) return fail();
  return value;
}
function date(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(value) ||
    !Number.isFinite(Date.parse(value))
  )
    return fail();
  return value;
}
function money(value: unknown): CustomerRefundMoney {
  const r = object(value, ['amount', 'currency']);
  if (
    typeof r['amount'] !== 'string' ||
    r['amount'].length > 58 ||
    !/^(?:0|[1-9]\d*)(?:\.\d{1,28})?$/.test(r['amount']) ||
    typeof r['currency'] !== 'string' ||
    !/^[A-Z]{3}$/.test(r['currency'])
  )
    return fail();
  return { amount: r['amount'], currency: r['currency'] };
}
export function decodeCustomerRefundStatus(value: unknown, aggregateId: string): CustomerRefundStatus {
  const r = object(value, [
    'aggregateId',
    'bookingVersion',
    'caseRevision',
    'state',
    'amount',
    'destination',
    'source',
    'reason',
    'supplierRefund',
    'supplierSettlement',
    'customerCredit',
    'needsManualReview',
    'observedAt',
    'supplierReport',
  ]);
  const id = cancellationGuid(r['aggregateId']);
  if (id !== cancellationGuid(aggregateId)) return fail();
  const state = choice(r['state'], customerRefundStates);
  const amount = r['amount'] === null ? null : money(r['amount']);
  const source = choice(r['source'], ['None', 'FictionalGateway', 'OperatorVerified']);
  const destination = r['destination'] === null ? null : choice(r['destination'], ['OriginalFictionalPaymentMethod']);
  if (
    (state === 'Sent' || state === 'Pending' || state === 'ConfirmedNoEffect') &&
    (amount === null || destination === null || source === 'None')
  )
    return fail();
  if (
    typeof r['reason'] !== 'string' ||
    !/^[A-Za-z][A-Za-z0-9]{0,63}$/.test(r['reason']) ||
    typeof r['needsManualReview'] !== 'boolean'
  )
    return fail();
  let supplierRefund: CustomerRefundStatus['supplierRefund'] = null;
  if (r['supplierRefund'] !== null) {
    const s = object(r['supplierRefund'], ['amount', 'destination', 'financialSource']);
    supplierRefund = {
      amount: money(s['amount']),
      destination: choice(s['destination'], [
        'Balance',
        'Card',
        'ArcBspCash',
        'AwaitingPayment',
        'OriginalFormOfPayment',
      ]),
      financialSource: choice(s['financialSource'], ['SupplierApi', 'OperatorVerified']),
    };
  }
  let supplierReport: CustomerRefundStatus['supplierReport'] = null;
  if (r['supplierReport'] !== null) {
    const s = object(r['supplierReport'], ['state', 'source', 'observedAt', 'cancelledAt']);
    supplierReport = {
      state: choice(s['state'], ['Unverified', 'ConfirmedCancellation', 'ConfirmedNotCancelled']),
      source: choice(s['source'], ['SignedWebhook', 'OperatorVerified']),
      observedAt: date(s['observedAt']),
      cancelledAt: s['cancelledAt'] === null ? null : date(s['cancelledAt']),
    };
  }
  return {
    aggregateId: id,
    bookingVersion: revision(r['bookingVersion']),
    caseRevision: r['caseRevision'] === null ? null : revision(r['caseRevision']),
    state,
    amount,
    destination,
    source,
    reason: r['reason'],
    supplierRefund,
    supplierSettlement: choice(r['supplierSettlement'], ['NotEstablished']),
    customerCredit: choice(r['customerCredit'], ['NotEstablished']),
    needsManualReview: r['needsManualReview'],
    observedAt: date(r['observedAt']),
    supplierReport,
  };
}
