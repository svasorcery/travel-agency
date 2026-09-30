import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { checkReadme, renderExamples, replaceExamples, validateCatalog } from './readme-examples.mjs';

const catalog = JSON.parse(readFileSync(new URL('../../docs/examples/flights-requests.json', import.meta.url), 'utf8'));

test('catalog has exact routes, required headers and dynamic dates', () => {
  assert.doesNotThrow(() => validateCatalog(catalog));
  const oldSearch = structuredClone(catalog);
  oldSearch[0].body.passengerCount = undefined;
  oldSearch[0].body.passengers = [{ type: 'adult' }];
  assert.throws(() => validateCatalog(oldSearch), /passengerCount|passengers/);

  const missingAuth = structuredClone(catalog);
  delete missingAuth[3].headers.Authorization;
  assert.throws(() => validateCatalog(missingAuth), /Authorization/);

  const invalidKey = structuredClone(catalog);
  invalidKey[3].headers['Idempotency-Key'] = 'demo-hold-001';
  assert.throws(() => validateCatalog(invalidKey), /Idempotency-Key/);
  const literalDate = structuredClone(catalog);
  literalDate[0].body.departureDate = '2026-08-01';
  assert.throws(() => validateCatalog(literalDate), /departureDate/);
});

test('renderer changes when method, header or body changes', () => {
  const original = renderExamples(catalog);
  assert.ok(original.includes('curl --no-buffer -sS -X GET http://localhost:5099/events/flights/orders/'));
  assert.ok(original.includes('curl -sS -X GET http://localhost:5099/api/flights/orders/{{aggregateId}}'));
  for (const mutate of [
    (data) => {
      data[0].method = 'PUT';
    },
    (data) => {
      data[3].headers['Idempotency-Key'] = 'different-key';
    },
    (data) => {
      data[0].body.origin = 'DME';
    },
  ]) {
    const changed = structuredClone(catalog);
    mutate(changed);
    assert.notEqual(renderExamples(changed), original);
  }
});

test('order GET example requires an owner bearer and no write key or body', () => {
  const order = catalog.find((entry) => entry.id === 'getOrder');
  assert.equal(order.method, 'GET');
  assert.deepEqual(order.headers, { Authorization: 'Bearer {{jwt}}' });
  assert.equal(order.body, null);
  const invalid = structuredClone(catalog);
  delete invalid.find((entry) => entry.id === 'getOrder').headers.Authorization;
  assert.throws(() => validateCatalog(invalid), /Authorization/);
});

test('README marker replacement is exact and fails on absent or duplicate markers', () => {
  const body = 'before\n<!-- BEGIN FLIGHTS REQUEST EXAMPLES -->\nold\n<!-- END FLIGHTS REQUEST EXAMPLES -->\nafter\n';
  const rendered = renderExamples(catalog);
  const updated = replaceExamples(body, rendered);
  assert.match(updated, /^before\n/);
  assert.match(updated, /\nafter\n$/);
  assert.equal(checkReadme(updated, rendered), true);
  assert.equal(checkReadme(body, rendered), false);
  assert.throws(() => replaceExamples('before\n', rendered), /marker/i);
  assert.throws(() => replaceExamples(body + body, rendered), /marker/i);
});

test('catalog rejects unresolved tokens and SSE parameter drift', () => {
  const token = structuredClone(catalog);
  token[2].body.providerOfferRef = '{{unknown}}';
  assert.throws(() => validateCatalog(token), /token/i);

  const path = structuredClone(catalog);
  path.find((entry) => entry.id === 'sse').pathParameters = {};
  assert.throws(() => validateCatalog(path), /orderId/);
});

test('catalog rejects malformed substitutions and unusable SSE authorization', () => {
  for (const authorization of ['', 'Basic ignored', 'Bearer {{jwt}']) {
    const invalid = structuredClone(catalog);
    invalid.find((entry) => entry.id === 'sse').headers.Authorization = authorization;
    assert.throws(() => validateCatalog(invalid), /Authorization|token/i);
  }

  const malformedOffer = structuredClone(catalog);
  malformedOffer[2].body.providerOfferRef = '{{providerOfferRef}';
  assert.throws(() => validateCatalog(malformedOffer), /token|providerOfferRef/i);

  const missingOffer = structuredClone(catalog);
  missingOffer[2].body.providerOfferRef = 'offer_demo';
  assert.throws(() => validateCatalog(missingOffer), /providerOfferRef/i);

  const missingAggregate = structuredClone(catalog);
  missingAggregate[3].body.aggregateId = '11111111-1111-1111-1111-111111111111';
  assert.throws(() => validateCatalog(missingAggregate), /aggregateId/i);
});
