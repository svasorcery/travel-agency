import { SupportContractError } from './support.decoder';
import { decodeSupportClassificationSuggestion } from './support-classification.decoder';

const id = '11111111-1111-4111-8111-111111111111';
const requestId = '22222222-2222-4222-8222-222222222222';
const now = Date.parse('2030-01-01T00:00:00Z');
function candidate() {
  return {
    caseId: id,
    requestId,
    capturedRevision: 3,
    generatedAt: '2030-01-01T00:00:00Z',
    expiresAt: '2030-01-01T00:02:00Z',
    status: 'Candidate',
    reason: 'Ready',
    category: 'Refund',
    queue: 'Refund',
    servingProfileId: 'a'.repeat(64),
    questionVersion: 'support-category-v1',
    urgency: {
      kind: 'Unknown',
      nextScheduledDepartureAt: null,
      observedAt: null,
      streamVersion: null,
      ruleVersion: 'support-urgency-v1',
    },
  };
}
function decode(value: unknown) {
  return decodeSupportClassificationSuggestion(value, id, requestId, 3, now);
}
describe('private Support classification wire contract', () => {
  it('decodes a case/request/revision bound candidate without treating it as saved triage', () => {
    expect(decode(candidate())).toEqual(candidate());
  });
  it.each([
    { caseId: requestId },
    { requestId: id },
    { capturedRevision: 4 },
    { capturedRevision: 2147483648 },
    { category: 'Unclassified' },
    { category: 'External' },
    { queue: 'Payment' },
    { servingProfileId: null },
    { servingProfileId: '<private>' },
    { questionVersion: 'foreign' },
    { status: 'Automatic' },
    { reason: 'Unavailable' },
    { expiresAt: '2030-01-01T00:02:01Z' },
    { expiresAt: '2030-01-01T00:00:00Z' },
    { generatedAt: '2030-02-30T00:00:00Z' },
    { generatedAt: 'invalid' },
    { debug: 'raw text' },
  ])('rejects malformed or mismatched candidate %j', (change) => {
    expect(() => decode({ ...candidate(), ...change })).toThrow(SupportContractError);
  });
  it('rejects missing fields, expired responses, future generation and non-v4 requests', () => {
    const { queue: _, ...missing } = candidate();
    expect(() => decode(missing)).toThrow(SupportContractError);
    expect(() => decodeSupportClassificationSuggestion(candidate(), id, requestId, 3, now + 120000)).toThrow(
      SupportContractError,
    );
    expect(() => decodeSupportClassificationSuggestion(candidate(), id, requestId, 3, now - 1)).toThrow(
      SupportContractError,
    );
    expect(() => decode({ ...candidate(), requestId: requestId.replace('-4222-', '-1222-') })).toThrow(
      SupportContractError,
    );
  });
  it('allows empty Disabled and manual results while keeping independent fresh High advice', () => {
    const manual = {
      ...candidate(),
      status: 'ManualRequired',
      reason: 'LowConfidence',
      category: null,
      queue: null,
      servingProfileId: null,
      questionVersion: null,
      urgency: {
        kind: 'High',
        nextScheduledDepartureAt: '2030-01-01T04:00:00Z',
        observedAt: '2029-12-31T23:59:40Z',
        streamVersion: 4,
        ruleVersion: 'support-urgency-v1',
      },
    };
    expect(decode(manual)).toEqual(manual);
    expect(() => decode({ ...manual, status: 'Disabled', reason: 'Disabled' })).toThrow(SupportContractError);
    expect(() => decode({ ...candidate(), status: 'ManualRequired', reason: 'Ready' })).toThrow(SupportContractError);
    expect(decode({ ...manual, status: 'Disabled', reason: 'Disabled', urgency: candidate().urgency })).toBeDefined();
  });
  it.each([
    { kind: 'Unknown', observedAt: '2030-01-01T00:00:00Z' },
    { kind: 'High' },
    {
      kind: 'High',
      observedAt: '2030-01-01T00:00:01Z',
      streamVersion: 1,
      nextScheduledDepartureAt: '2030-01-01T01:00:00Z',
    },
    {
      kind: 'High',
      observedAt: '2029-12-31T23:59:29Z',
      streamVersion: 1,
      nextScheduledDepartureAt: '2030-01-01T01:00:00Z',
    },
    {
      kind: 'High',
      observedAt: '2030-01-01T00:00:00Z',
      streamVersion: 1,
      nextScheduledDepartureAt: '2029-12-31T23:59:59Z',
    },
    {
      kind: 'High',
      observedAt: '2030-01-01T00:00:00Z',
      streamVersion: 1,
      nextScheduledDepartureAt: '2030-01-02T00:00:01Z',
    },
    { kind: 'Normal' },
    { ruleVersion: 'foreign' },
    { leakedText: 'private' },
  ])('rejects untrusted urgency %j', (change) => {
    expect(() => decode({ ...candidate(), urgency: { ...candidate().urgency, ...change } })).toThrow(
      SupportContractError,
    );
  });
  it('allows KeepCurrent from fresh closed-order evidence without inventing a departure', () => {
    expect(
      decode({
        ...candidate(),
        urgency: { ...candidate().urgency, kind: 'KeepCurrent', observedAt: '2030-01-01T00:00:00Z', streamVersion: 1 },
      }),
    ).toBeDefined();
  });
});

it('accepts source stream versions beyond Int32 while rejecting unsafe JavaScript integers', () => {
  const urgency = {
    ...candidate().urgency,
    kind: 'KeepCurrent',
    observedAt: '2030-01-01T00:00:00Z',
    streamVersion: 2147483648,
  };
  expect(decode({ ...candidate(), urgency }).urgency.streamVersion).toBe(2147483648);
  expect(() => decode({ ...candidate(), urgency: { ...urgency, streamVersion: Number.MAX_SAFE_INTEGER + 1 } })).toThrow(
    SupportContractError,
  );
});
