export const customerRefundStates = [
  'NotApplicable',
  'Assessing',
  'Ready',
  'DispatchClaimed',
  'Pending',
  'Unknown',
  'ManualReviewRequired',
  'Sent',
  'ConfirmedNoEffect',
  'NoRefundDue',
  'NoCustomerPayment',
  'LegacyUnverified',
] as const;
export type CustomerRefundState = (typeof customerRefundStates)[number];
export type CustomerRefundMoney = { amount: string; currency: string };
export type CustomerRefundStatus = {
  aggregateId: string;
  bookingVersion: number;
  caseRevision: number | null;
  state: CustomerRefundState;
  amount: CustomerRefundMoney | null;
  destination: 'OriginalFictionalPaymentMethod' | null;
  source: 'None' | 'FictionalGateway' | 'OperatorVerified';
  reason: string;
  supplierRefund: {
    amount: CustomerRefundMoney;
    destination: string;
    financialSource: 'SupplierApi' | 'OperatorVerified';
  } | null;
  supplierSettlement: 'NotEstablished';
  customerCredit: 'NotEstablished';
  needsManualReview: boolean;
  observedAt: string;
  supplierReport: {
    state: 'Unverified' | 'ConfirmedCancellation' | 'ConfirmedNotCancelled';
    source: 'SignedWebhook' | 'OperatorVerified';
    observedAt: string;
    cancelledAt: string | null;
  } | null;
};
