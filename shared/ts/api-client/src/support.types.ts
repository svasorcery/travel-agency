export const supportStates = ['Open', 'WaitingForCustomer', 'Resolved'] as const;
export const supportCategories = [
  'Unclassified',
  'Payment',
  'Ticketing',
  'Cancellation',
  'Refund',
  'FareConditions',
  'Ancillaries',
  'Technical',
  'Other',
] as const;
export const supportPriorities = ['Normal', 'High'] as const;
export const supportActionKinds = [
  'Create',
  'CustomerReply',
  'ReplyAndReopen',
  'OperatorReply',
  'Resolve',
  'SetTriage',
] as const;
export type SupportMode = 'customer' | 'operator';
export type SupportState = (typeof supportStates)[number];
export type SupportCategory = (typeof supportCategories)[number];
export type SupportPriority = (typeof supportPriorities)[number];
export type SupportActionKind = (typeof supportActionKinds)[number];
export interface SupportCase {
  caseId: string;
  ownerId: string;
  title: string;
  state: SupportState;
  revision: number;
  category: SupportCategory;
  priority: SupportPriority;
  triageSource: 'Default' | 'Operator';
  orderId: string | null;
  orderVerifiedAt: string | null;
  orderStreamVersion: number | null;
  createdAt: string;
  updatedAt: string;
  resolvedAt: string | null;
  purgeAt: string | null;
}
export interface SupportCasePage {
  items: SupportCase[];
  nextCursor: string | null;
}
export interface SupportMessage {
  caseId: string;
  messageId: string;
  actorKind: 'Customer' | 'Operator';
  body: string;
  writtenAtRevision: number;
  createdAt: string;
}
export interface SupportMessagePage {
  caseId: string;
  items: SupportMessage[];
  nextCursor: string | null;
}
export interface SupportReceipt {
  caseId: string;
  actionId: string;
  kind: SupportActionKind;
  appliedRevision: number;
  appliedState: SupportState;
  appliedAt: string;
  messageId: string | null;
}
export interface SupportFilters {
  state?: SupportState;
  category?: SupportCategory;
  priority?: SupportPriority;
}
export interface CreateSupportCase {
  messageId: string;
  title: string;
  body: string;
  orderId?: string | null;
}
export interface SupportReply {
  body: string;
  expectedRevision: number;
  kind: 'Reply' | 'ReplyAndReopen';
}
export type SupportControl =
  | { kind: 'Resolve'; expectedRevision: number }
  | { kind: 'SetTriage'; expectedRevision: number; category: SupportCategory; priority: SupportPriority };
