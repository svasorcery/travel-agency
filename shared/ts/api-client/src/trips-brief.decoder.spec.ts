import { describe, expect, it } from 'vitest';
import {
  decodeTripBriefCandidate,
  decodeTripBriefCapability,
  parseTripBriefJson,
  tripBriefPolicyReviewed,
  tripBriefRequest,
} from './trips-brief.decoder';

const id = '11111111-1111-4111-8111-111111111111';
const policy = 'a'.repeat(64);
export const briefFixture = () => ({
  candidateVersion: 1,
  requestId: id,
  processingPolicyVersion: policy,
  referenceInstant: '2026-10-27T00:00:00Z',
  referenceZone: 'Europe/Moscow',
  referenceLocalDate: '2026-10-27',
  expiresAt: '2026-10-27T00:10:00Z',
  fields: Object.fromEntries(
    ['destinationId', 'startDate', 'endDate', 'durationDays', 'adults', 'interests', 'pace'].map((k) => [
      k,
      { state: 'missing', value: null, evidence: [], alternatives: [] },
    ]),
  ),
  issues: [],
});
describe('private brief strict boundary', () => {
  it('decodes a complete seven-slot result without treating missing as default', () => {
    expect(
      decodeTripBriefCandidate(briefFixture(), {
        requestId: id,
        text: 'Москва',
        processingPolicyVersion: policy,
        referenceZone: 'Europe/Moscow',
      }).fields.adults.value,
    ).toBeNull();
  });
  it('rejects unknown capability members and inconsistent nulls', () => {
    expect(() =>
      decodeTripBriefCapability({
        candidateVersion: 1,
        status: 'unconfigured',
        processorId: null,
        modelId: null,
        processingPolicyVersion: null,
        extra: true,
      }),
    ).toThrow();
    expect(() =>
      decodeTripBriefCapability({
        candidateVersion: 1,
        status: 'configured',
        processorId: null,
        modelId: 'x',
        processingPolicyVersion: policy,
      }),
    ).toThrow();
  });
  it('rejects non-null missing values, wrong echoes, unknown fields and expiry above ten minutes', () => {
    for (const change of [
      (x: ReturnType<typeof briefFixture>) => ((x.fields.adults as Record<string, unknown>)['value'] = 1),
      (x: ReturnType<typeof briefFixture>) => (x.requestId = '22222222-2222-4222-8222-222222222222'),
      (x: ReturnType<typeof briefFixture>) => ((x.fields as Record<string, unknown>)['money'] = {}),
      (x: ReturnType<typeof briefFixture>) => (x.expiresAt = '2026-10-27T00:10:01Z'),
    ]) {
      const x = briefFixture();
      change(x);
      expect(() =>
        decodeTripBriefCandidate(x, {
          requestId: id,
          text: 'Москва',
          processingPolicyVersion: policy,
          referenceZone: 'Europe/Moscow',
        }),
      ).toThrow();
    }
  });
  it('rejects evidence splitting UTF16 surrogate pairs and bounded alternatives', () => {
    const x = briefFixture();
    x.fields['adults'] = {
      state: 'extracted',
      value: 2,
      evidence: [{ start: 1, end: 2 }],
      alternatives: [],
    } as unknown as (typeof x.fields)[string];
    expect(() =>
      decodeTripBriefCandidate(x, {
        requestId: id,
        text: '😀 2 взрослых',
        processingPolicyVersion: policy,
        referenceZone: 'Europe/Moscow',
      }),
    ).toThrow();
  });
  it('validates scalar, byte, unicode, exact request keys without truncation', () => {
    const request = {
      requestId: id,
      text: 'Москва',
      locale: 'ru',
      referenceZone: null,
      processingPolicyVersion: policy,
    };
    expect(tripBriefRequest(request).text).toBe('Москва');
    for (const text of ['', '😀'.repeat(1001), '\ud800'])
      expect(() => tripBriefRequest({ ...request, text })).toThrow();
    expect(() => tripBriefRequest({ ...request, tripId: id })).toThrow();
  });
});

describe('brief complete untrusted envelope', () => {
  it('rejects escaped duplicate members rather than parse-last-wins', () => {
    expect(() => parseTripBriefJson('{"candidateVersion":1,"candidateVersion":1}', 2048)).toThrow();
    expect(() => parseTripBriefJson('{"nested":{"value":1,"value":2}}', 2048)).toThrow();
  });
  it('rejects unknown issue codes and out-of-text evidence without a partial candidate', () => {
    for (const issue of [
      { code: 'modelInvented', slots: [], evidence: [{ start: 0, end: 1 }] },
      { code: 'unsupportedMoney', slots: [], evidence: [{ start: 0, end: 99 }] },
      { code: 'unsupportedMoney', slots: [], evidence: [] },
    ]) {
      const x = { ...briefFixture(), issues: [issue] };
      expect(() =>
        decodeTripBriefCandidate(x, {
          requestId: id,
          text: 'Москва',
          processingPolicyVersion: policy,
          referenceZone: 'Europe/Moscow',
        }),
      ).toThrow();
    }
  });
  it('checks configured processor against reviewed notice instead of trusting an arbitrary digest', async () => {
    const processorId = 'unknown',
      modelId = 'fictional-model';
    const digest = await crypto.subtle.digest(
      'SHA-256',
      new TextEncoder().encode(JSON.stringify([processorId, modelId, 'trips-brief-ru-v1'])),
    );
    const hash = [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('');
    expect(
      await tripBriefPolicyReviewed({
        candidateVersion: 1,
        status: 'configured',
        processorId,
        modelId,
        processingPolicyVersion: hash,
      }),
    ).toBe(false);
  });
});

it('rejects nonmissing claims without evidence and extracted alternatives', () => {
  for (const field of [
    { state: 'ambiguous', value: null, evidence: [], alternatives: [] },
    { state: 'extracted', value: 2, evidence: [{ start: 0, end: 1 }], alternatives: [3] },
  ]) {
    const value = { ...briefFixture(), fields: { ...briefFixture().fields, adults: field } };
    expect(() =>
      decodeTripBriefCandidate(value, {
        requestId: id,
        text: 'Москва',
        processingPolicyVersion: policy,
        referenceZone: 'Europe/Moscow',
      }),
    ).toThrow();
  }
});

it('rejects rolled UTC admission instants instead of Date.parse normalization', () => {
  const value = { ...briefFixture(), referenceInstant: '2026-10-26T24:00:00Z' };
  expect(() =>
    decodeTripBriefCandidate(value, {
      requestId: id,
      text: 'Москва',
      processingPolicyVersion: policy,
      referenceZone: 'Europe/Moscow',
    }),
  ).toThrow();
});
