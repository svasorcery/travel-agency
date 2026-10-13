import {
  decodeSupportCase,
  decodeSupportCasePage,
  decodeSupportMessagePage,
  decodeSupportReceipt,
  SupportContractError,
} from './support.decoder';

const id = '11111111-1111-4111-8111-111111111111';
const owner = '22222222-2222-4222-8222-222222222222';
const action = '33333333-3333-4333-8333-333333333333';
const date = '2030-01-01T00:00:00Z';
export const supportCaseFixture = {
  caseId: id,
  ownerId: owner,
  title: 'Вопрос о билете',
  state: 'Open',
  revision: 3,
  category: 'Unclassified',
  priority: 'Normal',
  triageSource: 'Default',
  orderId: null,
  orderVerifiedAt: null,
  orderStreamVersion: null,
  createdAt: date,
  updatedAt: date,
  resolvedAt: null,
  purgeAt: null,
};

describe('Support response contract', () => {
  it('decodes a nonempty case page without treating array indexes as expected resource IDs', () => {
    const page = decodeSupportCasePage({
      items: [supportCaseFixture, { ...supportCaseFixture, caseId: action }],
      nextCursor: null,
    });
    expect(page.items.map((row) => row.caseId)).toEqual([id, action]);
  });
  it('requires explicit nullable fields, valid bounded revisions and coherent order proof', () => {
    expect(decodeSupportCase(supportCaseFixture, id).revision).toBe(3);
    expect(
      decodeSupportCase(
        {
          ...supportCaseFixture,
          createdAt: '2030-01-01T00:00:00+00:00',
          updatedAt: '2030-01-01T00:00:00.1234567+00:00',
        },
        id,
      ).revision,
    ).toBe(3);
    expect(() => decodeSupportCase({ ...supportCaseFixture, createdAt: '2030-01-01T03:00:00+03:00' }, id)).toThrow();
    for (const value of [
      { ...supportCaseFixture, revision: 2_147_483_648 },
      { ...supportCaseFixture, purgeAt: undefined },
      { ...supportCaseFixture, orderId: owner },
    ]) {
      expect(() => decodeSupportCase(value, id)).toThrow(SupportContractError);
    }
    expect(() => decodeSupportCase(supportCaseFixture, owner)).toThrow(SupportContractError);
  });
  it('rejects another case, duplicate messages and oversized pages', () => {
    const message = {
      caseId: id,
      messageId: action,
      actorKind: 'Customer',
      body: '<script>plain text</script>',
      writtenAtRevision: 1,
      createdAt: date,
    };
    expect(decodeSupportMessagePage({ caseId: id, items: [message], nextCursor: null }, id).items[0].body).toContain(
      '<script>',
    );
    expect(() => decodeSupportMessagePage({ caseId: owner, items: [], nextCursor: null }, id)).toThrow();
    expect(() => decodeSupportMessagePage({ caseId: id, items: [message, message], nextCursor: null }, id)).toThrow();
    expect(() =>
      decodeSupportMessagePage({ caseId: id, items: Array(21).fill(message), nextCursor: null }, id),
    ).toThrow();
  });
  it('binds receipts to the exact action and expected kind without private text', () => {
    const receipt = {
      caseId: id,
      actionId: action,
      kind: 'CustomerReply',
      appliedRevision: 2,
      appliedState: 'Open',
      appliedAt: date,
      messageId: action,
    };
    expect(decodeSupportReceipt(receipt, id, action, 'CustomerReply').appliedRevision).toBe(2);
    expect(() => decodeSupportReceipt(receipt, id, owner, 'CustomerReply')).toThrow();
    expect(() => decodeSupportReceipt(receipt, id, action, 'Resolve')).toThrow();
    expect(() => decodeSupportReceipt({ ...receipt, body: 'must not leak' }, id, action)).toThrow();
  });
});
