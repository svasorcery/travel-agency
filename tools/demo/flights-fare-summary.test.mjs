import assert from 'node:assert/strict';
import { test } from 'node:test';
import { demoFareSummary, historicalDemoFareSummary } from './flights-fare-summary.mjs';

const quote = {
  aggregateId: '11111111-1111-4111-8111-111111111111',
  binding: { revision: '22222222-2222-4222-8222-222222222222', passengerCount: 9 },
  offer: {
    id: '33333333-3333-4333-8333-333333333333',
    expiresAt: '2037-01-01T00:00:00Z',
    itinerary: { slices: Array.from({ length: 4 }, () => ({ segments: [{ departAt: '2037-02-01T00:00:00Z' }] })) },
  },
};
test('fictional mixed graph retains exact scopes and zero versus unknown', () => {
  const summary = demoFareSummary(quote);
  assert.equal(summary.passengers.length, 36);
  assert.deepEqual(summary.segmentCounts, [1, 1, 1, 1]);
  assert.equal(summary.changeBeforeDeparture.penalty.amount, '0.00');
  assert.equal(summary.refundBeforeDeparture.permission, 'Unknown');
  assert.equal(summary.passengers[0].checkedBaggageQuantity, 0);
  assert.equal(summary.legs[1].changeBeforeDeparture.permission, 'Unknown');
});
test('rejected fake plan keeps deterministic output without invented fees', () => {
  const summary = demoFareSummary(quote, 'mixed', 'organized');
  assert.equal(summary.aiStatus, 'InvalidOutput');
  assert.equal(summary.presentation, 'Deterministic');
  assert.equal(summary.changeBeforeDeparture.penalty.amount, '0.00');
});
test('expired and history never regain current status or extend original expiry', () => {
  const expired = demoFareSummary(quote, 'expired');
  assert.equal(expired.state, 'Stale');
  const history = historicalDemoFareSummary(expired);
  assert.equal(history.reason, 'Historical');
  assert.equal(history.offerExpiresAt, expired.offerExpiresAt);
  assert.equal(history.aiAvailable, false);
});
