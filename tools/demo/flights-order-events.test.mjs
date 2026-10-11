import assert from 'node:assert/strict';
import { once } from 'node:events';
import test from 'node:test';
import { fictionalOrderLiveId } from './flights-order-events.mjs';
import { createDemoServer } from './flights-search-api.mjs';

for (const preset of ['order-live-ticket', 'order-live-report', 'order-live-refund']) {
  test(`${preset}: stream announces a retained change; reads do not execute mutations`, async () => {
    const server = createDemoServer({ orderEventsPreset: preset, orderEventsDelayMs: 30 });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const origin = `http://127.0.0.1:${server.address().port}`;
    const id = fictionalOrderLiveId(preset);
    const controller = new AbortController();
    let writes = 0;
    server.on('request', (request) => {
      if (request.method !== 'GET') writes++;
    });
    try {
      const before = await (await fetch(`${origin}/api/flights/orders/${id}/customer-refund`)).json();
      const response = await fetch(`${origin}/events/flights/orders/${id}`, { signal: controller.signal });
      assert.equal(response.status, 200);
      assert.equal(response.headers.get('Content-Type'), 'text/event-stream');
      const reader = response.body.getReader();
      let raw = '';
      while (!raw.includes('event:')) raw += new TextDecoder().decode((await reader.read()).value);
      const data = JSON.parse(
        raw
          .split('\n')
          .find((line) => line.startsWith('data: '))
          .slice(6),
      );
      assert.equal(data.orderId, id);
      assert.equal(data.streamVersion, 4);
      assert.deepEqual(data.payload, {});
      const order = await (await fetch(`${origin}/api/flights/orders/${id}`)).json();
      const after = await (await fetch(`${origin}/api/flights/orders/${id}/customer-refund`)).json();
      assert.equal(order.projectedStreamVersion, 4);
      assert.equal(after.bookingVersion, 4);
      if (preset === 'order-live-ticket') assert.equal(order.status, 'Ticketed');
      if (preset === 'order-live-report') {
        assert.equal(after.supplierReport.state, 'Unverified');
        assert.equal(after.amount, null);
      }
      if (preset === 'order-live-refund') {
        assert.equal(before.state, 'Unknown');
        assert.equal(after.state, 'Sent');
        assert.equal(after.customerCredit, 'NotEstablished');
      }
      for (let i = 0; i < 3; i++) {
        const again = await (await fetch(`${origin}/api/flights/orders/${id}/customer-refund`)).json();
        assert.equal(again.bookingVersion, 4);
      }
      controller.abort();
      assert.equal(writes, 0);
    } finally {
      controller.abort();
      await new Promise((resolve) => server.close(resolve));
    }
  });
}

test('fictional stream refuses Authorization, query secrets and foreign/missing resources', async () => {
  const server = createDemoServer({ orderEventsPreset: 'order-live-ticket' });
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const origin = `http://127.0.0.1:${server.address().port}`;
  try {
    assert.equal(
      (
        await fetch(`${origin}/events/flights/orders/${fictionalOrderLiveId('order-live-ticket')}`, {
          headers: { Authorization: 'fictional-token' },
        })
      ).status,
      400,
    );
    assert.equal(
      (await fetch(`${origin}/events/flights/orders/${fictionalOrderLiveId('order-live-ticket')}?token=fictional`))
        .status,
      400,
    );
    assert.equal((await fetch(`${origin}/events/flights/orders/11111111-1111-1111-1111-111111111111`)).status, 404);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
});
