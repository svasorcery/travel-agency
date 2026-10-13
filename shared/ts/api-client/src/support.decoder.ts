import {
  type SupportActionKind,
  type SupportCase,
  type SupportCasePage,
  type SupportMessage,
  type SupportMessagePage,
  type SupportReceipt,
  supportActionKinds,
  supportCategories,
  supportPriorities,
  supportStates,
} from './support.types';
export class SupportContractError extends Error {
  constructor() {
    super('Invalid Support contract.');
    this.name = 'SupportContractError';
  }
}
export function supportGuid(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) ||
    value === '00000000-0000-0000-0000-000000000000'
  )
    throw new SupportContractError();
  return value.toLowerCase();
}
function record(value: unknown, keys: string[]): Record<string, unknown> {
  if (
    !value ||
    typeof value !== 'object' ||
    Array.isArray(value) ||
    Object.keys(value).some((key) => !keys.includes(key)) ||
    keys.some((key) => !Object.hasOwn(value, key))
  )
    throw new SupportContractError();
  return value as Record<string, unknown>;
}
function member<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value !== 'string' || !values.includes(value as T)) throw new SupportContractError();
  return value as T;
}
export function supportRevision(value: unknown): number {
  if (typeof value !== 'number' || !Number.isInteger(value) || value < 1 || value > 2_147_483_647)
    throw new SupportContractError();
  return value;
}
export function validSupportText(value: unknown, limit: number): value is string {
  return (
    typeof value === 'string' &&
    value.trim().length > 0 &&
    [...value].length <= limit &&
    !/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(value) &&
    new TextEncoder().encode(value).length <= 16_384
  );
}
function text(value: unknown, limit: number): string {
  if (!validSupportText(value, limit)) throw new SupportContractError();
  return value;
}
function time(value: unknown): string {
  if (
    typeof value !== 'string' ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) ||
    !Number.isFinite(Date.parse(value)) ||
    new Date(Date.parse(value)).toISOString().slice(0, 10) !== value.slice(0, 10)
  )
    throw new SupportContractError();
  return value;
}
function nullable<T>(value: unknown, decoder: (value: unknown) => T): T | null {
  return value === null ? null : decoder(value);
}
export function supportCursor(value: unknown): string | null {
  if (value === null) return null;
  if (typeof value !== 'string' || !/^[A-Za-z0-9_=-]{1,2048}$/.test(value)) throw new SupportContractError();
  return value;
}
export function decodeSupportCase(value: unknown, expectedId?: string): SupportCase {
  const r = record(value, [
    'caseId',
    'ownerId',
    'title',
    'state',
    'revision',
    'category',
    'priority',
    'triageSource',
    'orderId',
    'orderVerifiedAt',
    'orderStreamVersion',
    'createdAt',
    'updatedAt',
    'resolvedAt',
    'purgeAt',
  ]);
  const result: SupportCase = {
    caseId: supportGuid(r['caseId']),
    ownerId: supportGuid(r['ownerId']),
    title: text(r['title'], 120),
    state: member(r['state'], supportStates),
    revision: supportRevision(r['revision']),
    category: member(r['category'], supportCategories),
    priority: member(r['priority'], supportPriorities),
    triageSource: member(r['triageSource'], ['Default', 'Operator']),
    orderId: nullable(r['orderId'], supportGuid),
    orderVerifiedAt: nullable(r['orderVerifiedAt'], time),
    orderStreamVersion: nullable(r['orderStreamVersion'], supportRevision),
    createdAt: time(r['createdAt']),
    updatedAt: time(r['updatedAt']),
    resolvedAt: nullable(r['resolvedAt'], time),
    purgeAt: nullable(r['purgeAt'], time),
  };
  if (
    (expectedId !== undefined && result.caseId !== supportGuid(expectedId)) ||
    (result.orderId === null
      ? result.orderVerifiedAt !== null || result.orderStreamVersion !== null
      : result.orderVerifiedAt === null || result.orderStreamVersion === null) ||
    (result.state === 'Resolved'
      ? result.resolvedAt === null || result.purgeAt === null
      : result.resolvedAt !== null || result.purgeAt !== null)
  )
    throw new SupportContractError();
  return result;
}
function items<T>(value: unknown, decode: (value: unknown) => T, id: (item: T) => string): T[] {
  if (!Array.isArray(value) || value.length > 20) throw new SupportContractError();
  const result = value.map(decode);
  if (new Set(result.map(id)).size !== result.length) throw new SupportContractError();
  return result;
}
export function decodeSupportCasePage(value: unknown): SupportCasePage {
  const r = record(value, ['items', 'nextCursor']);
  return {
    items: items(
      r['items'],
      (value) => decodeSupportCase(value),
      (item) => item.caseId,
    ),
    nextCursor: supportCursor(r['nextCursor']),
  };
}
export function decodeSupportMessage(value: unknown, expectedId: string): SupportMessage {
  const r = record(value, ['caseId', 'messageId', 'actorKind', 'body', 'writtenAtRevision', 'createdAt']);
  const caseId = supportGuid(r['caseId']);
  if (caseId !== supportGuid(expectedId)) throw new SupportContractError();
  return {
    caseId,
    messageId: supportGuid(r['messageId']),
    actorKind: member(r['actorKind'], ['Customer', 'Operator']),
    body: text(r['body'], 4000),
    writtenAtRevision: supportRevision(r['writtenAtRevision']),
    createdAt: time(r['createdAt']),
  };
}
export function decodeSupportMessagePage(value: unknown, expectedId: string): SupportMessagePage {
  const r = record(value, ['caseId', 'items', 'nextCursor']);
  const caseId = supportGuid(r['caseId']);
  if (caseId !== supportGuid(expectedId)) throw new SupportContractError();
  return {
    caseId,
    items: items(
      r['items'],
      (value) => decodeSupportMessage(value, caseId),
      (item) => item.messageId,
    ),
    nextCursor: supportCursor(r['nextCursor']),
  };
}
export function decodeSupportReceipt(
  value: unknown,
  caseId: string,
  actionId: string,
  kind?: SupportActionKind,
): SupportReceipt {
  const r = record(value, ['caseId', 'actionId', 'kind', 'appliedRevision', 'appliedState', 'appliedAt', 'messageId']);
  const result: SupportReceipt = {
    caseId: supportGuid(r['caseId']),
    actionId: supportGuid(r['actionId']),
    kind: member(r['kind'], supportActionKinds),
    appliedRevision: supportRevision(r['appliedRevision']),
    appliedState: member(r['appliedState'], supportStates),
    appliedAt: time(r['appliedAt']),
    messageId: nullable(r['messageId'], supportGuid),
  };
  const control = result.kind === 'Resolve' || result.kind === 'SetTriage';
  const expectedState =
    result.kind === 'Resolve'
      ? 'Resolved'
      : result.kind === 'OperatorReply'
        ? 'WaitingForCustomer'
        : result.kind === 'SetTriage'
          ? null
          : 'Open';
  if (
    result.caseId !== supportGuid(caseId) ||
    result.actionId !== supportGuid(actionId) ||
    (kind !== undefined && result.kind !== kind) ||
    (control ? result.messageId !== null : result.messageId !== result.actionId) ||
    (expectedState !== null && result.appliedState !== expectedState)
  )
    throw new SupportContractError();
  return result;
}
