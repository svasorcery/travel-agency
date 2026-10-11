import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { createFictionalCustomerRefund } from './flights-customer-refunds.mjs';
import { createDemoServer } from './flights-search-api.mjs';

const fixture = JSON.parse(
  readFileSync(new URL('../../tests/fixtures/flights-customer-refunds.json', import.meta.url), 'utf8'),
);
const id = '00000000-0000-0000-0000-000000000123';
for (const preset of fixture.presets) {
  test(preset.name + ' admits once and owner reads never execute a payout', () => {
    const run = createFictionalCustomerRefund(preset.name);
    const order = {
      version: 10,
      status: 'Cancelled',
      paymentBasis: { amount: fixture.captured, source: 'TestWalletObserved' },
      offer: { passengerCount: 9, itinerary: { slices: Array.from({ length: 4 }, () => ({})) } },
    };
    const op = {
      phase: 'Succeeded',
      source: 'SupplierApi',
      terms: {
        refundAmount: preset.amount,
        refundCurrency: 'GBP',
        refundDestination: 'Balance',
        financialSource: 'SupplierApi',
      },
    };
    run.complete(order, op);
    const retained = run.status(id, order, op);
    assert.equal(retained.state, preset.state);
    assert.equal(retained.amount.amount, preset.amount);
    assert.equal(run.calls, preset.calls);
    const caseId = order.customerRefund.id;
    for (let i = 0; i < 4; i++) {
      run.complete(order, op);
      assert.deepEqual(run.status(id, order, op), retained);
    }
    assert.equal(order.customerRefund.id, caseId);
    assert.equal(run.calls, preset.calls);
    assert.equal(retained.supplierSettlement, 'NotEstablished');
    assert.equal(retained.customerCredit, 'NotEstablished');
    assert.doesNotMatch(JSON.stringify(retained), /paymentReference|refundId|receiptReference|Authorization|@example/);
  });
  test(preset.name + ' is fixed at startup and purchase-consent workflow retains exact GBP money', async () => {
    const server = createDemoServer({ customerRefundPreset: preset.name });
    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    const base = 'http://127.0.0.1:' + server.address().port;
    const post = (path, body, key) =>
      fetch(base + '/api/flights/' + path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...(key ? { 'Idempotency-Key': key } : {}) },
        body: JSON.stringify(body),
      });
    try {
      const request = { provider: 'duffel', providerOfferRef: 'off_fixture_ow_2037-07-20_p2', passengerCount: 2 };
      let q = await (await post('orders/quote', request)).json();
      assert.equal(q.offer.currency, 'GBP');
      const c = await (
        await post('orders/ancillaries', {
          aggregateId: q.aggregateId,
          quoteRevision: q.binding.revision,
          includeSeats: true,
        })
      ).json();
      const bag = c.services.find((s) => s.kind === 'checked-baggage');
      const seat = c.services.find((s) => s.kind === 'seat' && s.unitPrice.amount === '0.00');
      q = await (
        await post('orders/quote', {
          ...request,
          aggregateId: q.aggregateId,
          selections: [
            { selectionKey: bag.selectionKey, quantity: 2 },
            { selectionKey: seat.selectionKey, quantity: 1 },
          ],
        })
      ).json();
      assert.deepEqual(q.purchase.total, fixture.captured);
      assert.equal(q.purchase.services.length, 2);
      const passengers = q.binding.slots.map((s) => ({
        bookingPassengerId: s.bookingPassengerId,
        title: 'mr',
        gender: 'male',
        givenName: 'Fictional',
        familyName: 'Traveler',
        dateOfBirth: '1990-01-01',
        email: 'fictional@example.test',
        phone: '+441234567890',
      }));
      assert.equal(
        (
          await post(
            'orders/hold',
            { aggregateId: q.aggregateId, quoteRevision: q.binding.revision, passengers, acceptAncillaries: true },
            randomUUID(),
          )
        ).status,
        200,
      );
      assert.equal((await post('orders/confirm', { aggregateId: q.aggregateId }, randomUUID())).status, 200);
      const resource = 'orders/' + q.aggregateId;
      let view = await (await fetch(base + '/api/flights/' + resource + '/cancellation')).json();
      const prepare = {
        aggregateId: q.aggregateId,
        operationId: randomUUID(),
        expectedBookingVersion: view.bookingVersion,
      };
      view = await (await post('cancellations/prepare', prepare)).json();
      assert.equal(view.operation.terms.refundAmount, preset.amount);
      assert.equal(view.operation.terms.refundCurrency, 'GBP');
      const t = view.operation.terms;
      const consent = {
        aggregateId: q.aggregateId,
        operationId: view.operation.operationId,
        expectedOperationRevision: view.operation.revision,
        termsRevision: t.revision,
        termsHash: t.hash,
        noticeVersion: t.noticeVersion,
        accepted: true,
      };
      assert.equal((await post('cancellations/consent', consent)).status, 202);
      const get = await fetch(base + '/api/flights/' + resource + '/customer-refund');
      assert.equal(get.status, 200);
      assert.equal(get.headers.get('cache-control'), 'no-store');
      const retained = await get.json();
      assert.equal(retained.state, preset.state);
      assert.equal(retained.amount.amount, preset.amount);
      assert.equal((await post('cancellations/consent', consent)).status, 200);
      assert.deepEqual(await (await fetch(base + '/api/flights/' + resource + '/customer-refund')).json(), retained);
      assert.equal((await fetch(base + '/api/flights/' + resource + '/customer-refund/review')).status, 404);
      assert.equal((await post(resource + '/customer-refund', {})).status, 404);
      assert.equal((await fetch(base + '/api/flights/' + resource + '/customer-refund?resolve=sent')).status, 400);
    } finally {
      await new Promise((r) => server.close(r));
    }
  });
}
test('unknown preset is rejected and absent capture cannot be replaced by order total', () => {
  assert.throws(() => createFictionalCustomerRefund('live'), TypeError);
  const run = createFictionalCustomerRefund('refund-sent'),
    order = { version: 5, status: 'Cancelled', offer: { totalAmount: 100, currency: 'GBP' } };
  run.complete(order, {
    phase: 'Succeeded',
    source: 'SupplierApi',
    terms: { refundAmount: '80', refundCurrency: 'GBP', refundDestination: 'Balance', financialSource: 'SupplierApi' },
  });
  assert.equal(run.calls, 0);
  assert.equal(run.status(id, order).state, 'ManualReviewRequired');
});
