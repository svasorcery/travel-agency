import assert from 'node:assert/strict';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { connect } from 'node:net';
import { after, before, test } from 'node:test';
import { buildDemoSearchResponse, createDemoServer } from './flights-search-api.mjs';

const fixture = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-search.json', import.meta.url), 'utf8'));
const booking = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-booking.json', import.meta.url), 'utf8'));

test('one-way and round-trip demo responses match the canonical HTTP example', () => {
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), fixture.oneWay.response);
  assert.deepEqual(buildDemoSearchResponse(fixture.roundTrip.request), fixture.roundTrip.response);
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), buildDemoSearchResponse(fixture.oneWay.request));
});

test('demo booking references identify the selected dates and trip type', () => {
  const oneWay = buildDemoSearchResponse(fixture.oneWay.request).offers[0];
  const roundTrip = buildDemoSearchResponse(fixture.roundTrip.request).offers[0];
  const later = buildDemoSearchResponse({ ...fixture.oneWay.request, departureDate: '2031-07-12' }).offers[0];
  assert.equal(oneWay.providerOfferRef, 'off_fixture_ow_2030-06-10');
  assert.equal(roundTrip.providerOfferRef, 'off_fixture_rt_2030-06-10_2030-06-17');
  assert.equal(later.providerOfferRef, 'off_fixture_ow_2031-07-12');
});

test('selected dates move the shown segments without inventing a partner return', () => {
  const result = buildDemoSearchResponse({
    ...fixture.roundTrip.request,
    departureDate: '2031-07-12',
    returnDate: '2031-07-19',
  });
  assert.equal(result.offers[0].itinerary.slices[0].segments[0].departAt, '2031-07-12T10:00:00+03:00');
  assert.equal(result.offers[0].itinerary.slices[1].segments[0].departAt, '2031-07-19T17:00:00+03:00');
  assert.equal(result.offers[1].itinerary.slices.length, 1);
});

test('an unknown valid route is an explicitly empty demo result', () => {
  assert.deepEqual(buildDemoSearchResponse({ ...fixture.oneWay.request, destination: 'VKO' }), fixture.empty.response);
});

let server;
let baseUrl;
before(async () => {
  server = createDemoServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});
after(async () => {
  await new Promise((resolve) => server.close(resolve));
});

test('HTTP stub returns the canonical contract and proves the demo source', async () => {
  const response = await fetch(`${baseUrl}/api/flights/search?currency=RUB`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept-Language': 'ru' },
    body: JSON.stringify(fixture.oneWay.request),
  });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('x-travel-demo'), 'fixtures');
  assert.deepEqual(await response.json(), fixture.oneWay.response);
});

test('HTTP quote uses the selected fake reference and shared booking response', async () => {
  for (const name of ['oneWay', 'roundTrip']) {
    const response = await fetch(`${baseUrl}/api/flights/orders/quote`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(booking[name].request),
    });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('x-travel-demo'), 'fixtures');
    assert.deepEqual(await response.json(), booking[name].response);
  }
});

test('new quote accepts the backend optional aggregateId field when omitted', async () => {
  const { aggregateId: _ignored, ...body } = booking.oneWay.request;
  const response = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), booking.oneWay.response);
});

test('re-quote is deterministic and never accepts a partner or unknown reference', async () => {
  const requote = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(booking.reQuoteChanged.request),
  });
  assert.equal(requote.status, 200);
  assert.deepEqual(await requote.json(), booking.reQuoteChanged.response);

  for (const invalid of [
    { providerOfferRef: null, provider: 'travelpayouts', aggregateId: null },
    { providerOfferRef: 'off_fixture_unknown', provider: 'duffel', aggregateId: null },
  ]) {
    const response = await fetch(`${baseUrl}/api/flights/orders/quote`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(invalid),
    });
    assert.equal(response.status, 404);
  }
});

test('a demo quote identity is tied to its exact date-specific reference', async () => {
  const laterRef = 'off_fixture_ow_2031-07-12';
  const later = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ providerOfferRef: laterRef, provider: 'duffel', aggregateId: null }),
  });
  assert.equal(later.status, 200);
  const laterBody = await later.json();
  assert.notEqual(laterBody.aggregateId, booking.oneWay.response.aggregateId);
  assert.equal(laterBody.offer.itinerary.slices[0].segments[0].departAt, '2031-07-12T10:00:00+03:00');

  const crossed = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      providerOfferRef: laterRef,
      provider: 'duffel',
      aggregateId: booking.oneWay.response.aggregateId,
    }),
  });
  assert.equal(crossed.status, 404);
});

test('isolated demo hold and confirm are fictional, require idempotency keys, and never echo passenger data', async () => {
  const aggregateId = booking.oneWay.response.aggregateId;
  const passenger = {
    givenName: 'Demo',
    familyName: 'Traveler',
    dateOfBirth: '1990-04-12',
    gender: 'unspecified',
    email: 'demo@example.test',
    phone: '+79161234567',
  };
  const holdKey = '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac';
  const confirmKey = '5f6db15b-4e13-43d1-8af9-2a74b2e869bd';
  const heldResponse = await fetch(`${baseUrl}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': holdKey },
    body: JSON.stringify({ aggregateId, passengers: [passenger] }),
  });
  assert.equal(heldResponse.status, 200);
  assert.equal(heldResponse.headers.get('x-travel-demo'), 'fixtures');
  const held = await heldResponse.json();
  assert.equal(held.aggregateId, aggregateId);
  assert.match(held.providerOrderId, /^demo-order-/);
  assert.ok(Number.isFinite(Date.parse(held.heldUntil)));
  assert.equal(JSON.stringify(held).includes(passenger.email), false);

  const confirmedResponse = await fetch(`${baseUrl}/api/flights/orders/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': confirmKey },
    body: JSON.stringify({ aggregateId }),
  });
  assert.equal(confirmedResponse.status, 200);
  assert.equal(confirmedResponse.headers.get('x-travel-demo'), 'fixtures');
  assert.deepEqual(await confirmedResponse.json(), { aggregateId, status: 'Confirmed', paymentRef: null });
});

test('fictional order GET converges from delayed projection to Ticketed without passenger data', async () => {
  const quote = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider: 'duffel', providerOfferRef: 'off_fixture_ow_2032-08-14' }),
  });
  assert.equal(quote.status, 200);
  const quoted = await quote.json();
  const aggregateId = quoted.aggregateId;
  const passenger = {
    givenName: 'Demo',
    familyName: 'Traveler',
    dateOfBirth: '1990-04-12',
    gender: 'unspecified',
    email: 'demo@example.test',
    phone: '+79161234567',
  };
  const hold = await fetch(`${baseUrl}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac' },
    body: JSON.stringify({ aggregateId, passengers: [passenger] }),
  });
  assert.equal(hold.status, 200);
  const held = await fetch(`${baseUrl}/api/flights/orders/${aggregateId}`);
  assert.equal(held.status, 200);
  assert.equal((await held.json()).status, 'Held');
  const uppercaseLink = await fetch(`${baseUrl}/api/flights/orders/${aggregateId.toUpperCase()}`);
  assert.equal(uppercaseLink.status, 200);
  assert.equal((await uppercaseLink.json()).aggregateId, aggregateId);

  const confirm = await fetch(`${baseUrl}/api/flights/orders/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': '5f6db15b-4e13-43d1-8af9-2a74b2e869be' },
    body: JSON.stringify({ aggregateId }),
  });
  assert.equal(confirm.status, 200);
  const statuses = [];
  for (let attempt = 0; attempt < 4; attempt++) {
    const response = await fetch(`${baseUrl}/api/flights/orders/${aggregateId}`);
    if (response.status === 404) statuses.push('404');
    else {
      assert.equal(response.headers.get('x-travel-demo'), 'fixtures');
      const result = await response.json();
      statuses.push(result.status);
      assert.equal(result.aggregateId, aggregateId);
      assert.equal(result.itinerary.slices.length, 1);
      assert.equal(JSON.stringify(result).includes(passenger.email), false);
      assert.equal('heldUntil' in result, false);
      if (result.status === 'Ticketed') assert.deepEqual(result.ticketNumbers, [`DEMO-TKT-${aggregateId.slice(0, 8)}`]);
    }
  }
  assert.deepEqual(statuses, ['404', 'Held', 'Confirmed', 'Ticketed']);
  assert.equal((await fetch(`${baseUrl}/api/flights/orders/11111111-1111-1111-1111-111111111111`)).status, 404);
});

test('isolated demo booking refuses credentials and missing idempotency headers', async () => {
  const aggregateId = booking.oneWay.response.aggregateId;
  const body = JSON.stringify({ aggregateId, passengers: [] });
  const withBearer = await fetch(`${baseUrl}/api/flights/orders/hold`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac',
      Authorization: 'Bearer should-not-reach-demo',
    },
    body,
  });
  assert.equal(withBearer.status, 400);

  const missingKey = await fetch(`${baseUrl}/api/flights/orders/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ aggregateId }),
  });
  assert.equal(missingKey.status, 400);
});

test('demo quote expiry is deterministic and precedes the selected departure', async () => {
  const request = {
    providerOfferRef: 'off_fixture_ow_2026-10-29',
    provider: 'duffel',
    aggregateId: null,
  };
  async function quote() {
    const response = await fetch(`${baseUrl}/api/flights/orders/quote`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    assert.equal(response.status, 200);
    return response.json();
  }
  const first = await quote();
  assert.deepEqual(await quote(), first);
  assert.ok(Date.parse(first.offer.fetchedAt) < Date.parse(first.offer.expiresAt));
  assert.ok(Date.parse(first.offer.expiresAt) < Date.parse(first.offer.itinerary.slices[0].segments[0].departAt));
});

test('HTTP stub rejects malformed search and leaves unrelated booking, cancel and NL routes unimplemented', async () => {
  const bad = await fetch(`${baseUrl}/api/flights/search?currency=RUB`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...fixture.oneWay.request, passengerCount: 2 }),
  });
  assert.equal(bad.status, 400);
  assert.equal((await bad.json()).status, 400);
  const wrongType = await fetch(`${baseUrl}/api/flights/search?currency=RUB`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/jsonp' },
    body: JSON.stringify(fixture.oneWay.request),
  });
  assert.equal(wrongType.status, 400);
  for (const route of ['/api/flights/orders/status', '/api/flights/orders/abc/cancel', '/api/flights/search/nl']) {
    const response = await fetch(`${baseUrl}${route}`, { method: 'POST' });
    assert.equal(response.status, 404);
  }
  const events = await fetch(`${baseUrl}/events/flights/orders/abc`);
  assert.equal(events.status, 404);
});

test('aborting a POST body does not stop subsequent demo searches', async () => {
  const received = once(server, 'request');
  const socket = connect(server.address().port, '127.0.0.1');
  socket.on('error', () => {});
  await once(socket, 'connect');
  socket.write(
    'POST /api/flights/search?currency=RUB HTTP/1.1\r\n' +
      'Host: 127.0.0.1\r\n' +
      'Content-Type: application/json\r\n' +
      'Content-Length: 200\r\n\r\n' +
      '{"origin":"LED"',
  );
  const [incoming] = await received;
  const closed = new Promise((resolve) => incoming.once('close', resolve));
  socket.destroy();
  await closed;
  await new Promise((resolve) => setImmediate(resolve));

  const response = await fetch(`${baseUrl}/`);
  assert.equal(response.status, 200);
});

async function withDemoServer(run) {
  const isolated = createDemoServer();
  await new Promise((resolve) => isolated.listen(0, '127.0.0.1', resolve));
  try {
    return await run(`http://127.0.0.1:${isolated.address().port}`);
  } finally {
    await new Promise((resolve, reject) => isolated.close((error) => (error ? reject(error) : resolve())));
  }
}

const listPassenger = {
  givenName: 'Fictional',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'unspecified',
  email: 'fictional@example.test',
  phone: '+79001234567',
};

async function holdDemoOrder(url, departureDate) {
  const quote = await fetch(`${url}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider: 'duffel', providerOfferRef: `off_fixture_ow_${departureDate}` }),
  });
  assert.equal(quote.status, 200);
  const { aggregateId } = await quote.json();
  const hold = await fetch(`${url}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac' },
    body: JSON.stringify({ aggregateId, passengers: [listPassenger] }),
  });
  assert.equal(hold.status, 200);
  return aggregateId;
}

test('demo cancellation is bodyless, replays a known result and never promises a refund', async () => {
  await withDemoServer(async (url) => {
    const id = await holdDemoOrder(url, '2032-04-01');
    const key = 'aaaaaaab-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
    const send = (body, operationKey = key) =>
      fetch(`${url}/api/flights/orders/${id}/cancel`, {
        method: 'POST',
        headers: { 'Idempotency-Key': operationKey },
        ...(body === undefined ? {} : { body }),
      });
    const first = await send();
    assert.equal(first.status, 200);
    const cancelled = await first.json();
    assert.equal(cancelled.status, 'Cancelled');
    assert.ok(cancelled.cancelledAt);
    assert.equal(cancelled.refundedAt, null);
    const retry = await send();
    assert.equal(retry.headers.get('idempotency-replay'), 'true');
    assert.deepEqual(await retry.json(), cancelled);
    const conflict = await send('{}');
    assert.equal(conflict.status, 409);
    assert.match((await conflict.json()).type, /IdempotencyConflict$/);
    const noop = await send(undefined, 'aaaaaaac-aaaa-4aaa-8aaa-aaaaaaaaaaaa');
    assert.deepEqual(await noop.json(), cancelled);
    const confirm = await fetch(`${url}/api/flights/orders/confirm`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'aaaaaaad-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
      body: JSON.stringify({ aggregateId: id }),
    });
    assert.equal(confirm.status, 409);
    const list = await (await fetch(`${url}/api/flights/orders`)).json();
    assert.equal(list.items[0].status, 'Cancelled');
  });
});

test('confirmed demo cancellation remains distinct from a ticketed rejection', async () => {
  await withDemoServer(async (url) => {
    const id = await holdDemoOrder(url, '2032-04-02');
    const body = JSON.stringify({ aggregateId: id });
    const confirm = () =>
      fetch(`${url}/api/flights/orders/confirm`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'aaaaaaae-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
        body,
      });
    assert.equal((await confirm()).status, 200);
    for (let i = 0; i < 4; i++) await fetch(`${url}/api/flights/orders/${id}`);
    const replay = await confirm();
    assert.equal(replay.headers.get('idempotency-replay'), 'true');
    const cancel = await fetch(`${url}/api/flights/orders/${id}/cancel`, {
      method: 'POST',
      headers: { 'Idempotency-Key': 'aaaaaaaf-aaaa-4aaa-8aaa-aaaaaaaaaaaa' },
    });
    assert.equal(cancel.status, 409);
    assert.match((await cancel.json()).type, /OrderNotCancellable$/);
  });
});

test('demo order list starts empty with server defaults and fixture no-store headers', async () => {
  await withDemoServer(async (url) => {
    const response = await fetch(`${url}/api/flights/orders`);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.equal(response.headers.get('x-travel-demo'), 'fixtures');
    assert.deepEqual(await response.json(), { items: [], limit: 50, offset: 0 });
  });
});

test('demo order list pages 25 fictional holds with a stable descending ID tie-breaker', async (t) => {
  t.mock.timers.enable({ apis: ['Date'], now: Date.parse('2030-05-01T10:00:00Z') });
  await withDemoServer(async (url) => {
    const ids = [];
    for (let day = 1; day <= 25; day++) ids.push(await holdDemoOrder(url, `2030-06-${String(day).padStart(2, '0')}`));
    const expectedIds = ids.toSorted().reverse();
    const firstResponse = await fetch(`${url}/api/flights/orders?limit=21&offset=0`);
    assert.equal(firstResponse.status, 200);
    const first = await firstResponse.json();
    assert.equal(first.limit, 21);
    assert.equal(first.offset, 0);
    assert.deepEqual(
      first.items.map((order) => order.aggregateId),
      expectedIds.slice(0, 21),
    );
    const nextResponse = await fetch(`${url}/api/flights/orders?limit=21&offset=20`);
    assert.equal(nextResponse.status, 200);
    const next = await nextResponse.json();
    assert.equal(next.limit, 21);
    assert.equal(next.offset, 20);
    assert.deepEqual(
      next.items.map((order) => order.aggregateId),
      expectedIds.slice(20),
    );
    assert.deepEqual(
      [...first.items.slice(0, 20), ...next.items].map((order) => order.aggregateId),
      expectedIds,
    );
    assert.deepEqual((await (await fetch(`${url}/api/flights/orders?limit=21&offset=25`)).json()).items, []);
    for (const order of first.items) {
      assert.equal(order.bookedAt, '2030-05-01T10:00:00.000Z');
      assert.equal(order.status, 'Held');
      assert.deepEqual(
        Object.keys(order).sort(),
        [
          'aggregateId',
          'bookedAt',
          'cancelledAt',
          'currency',
          'itinerary',
          'refundedAt',
          'status',
          'ticketNumbers',
          'ticketedAt',
          'totalAmount',
        ].sort(),
      );
      const detail = await fetch(`${url}/api/flights/orders/${order.aggregateId}`);
      assert.deepEqual(await detail.json(), order);
    }
    const serialized = JSON.stringify(first);
    for (const value of Object.values(listPassenger)) assert.equal(serialized.includes(value), false);
  });
});

test('demo order list sorts a newer hold before an older hold', async (t) => {
  t.mock.timers.enable({ apis: ['Date'], now: Date.parse('2030-05-01T10:00:00Z') });
  await withDemoServer(async (url) => {
    const olderId = await holdDemoOrder(url, '2030-06-10');
    t.mock.timers.setTime(Date.parse('2030-05-01T10:00:01Z'));
    const newerId = await holdDemoOrder(url, '2030-06-11');
    const response = await fetch(`${url}/api/flights/orders?limit=1`);
    assert.equal(response.status, 200);
    const list = await response.json();
    assert.equal(list.items[0].aggregateId, newerId);
    assert.notEqual(list.items[0].aggregateId, olderId);
  });
});

test('demo order list clamps paging bounds and rejects non-integer query values', async () => {
  await withDemoServer(async (url) => {
    await holdDemoOrder(url, '2030-06-10');
    for (const [query, limit, offset, count] of [
      ['limit=0&offset=-1', 1, 0, 1],
      ['limit=-20', 1, 0, 1],
      ['limit=201', 200, 0, 1],
      ['offset=50', 50, 50, 0],
    ]) {
      const response = await fetch(`${url}/api/flights/orders?${query}`);
      assert.equal(response.status, 200);
      const result = await response.json();
      assert.equal(result.limit, limit);
      assert.equal(result.offset, offset);
      assert.equal(result.items.length, count);
    }
    for (const query of ['limit=nope', 'offset=1.5', 'limit=2147483648']) {
      assert.equal((await fetch(`${url}/api/flights/orders?${query}`)).status, 400);
    }
  });
});

test('demo order list rejects every Authorization header', async () => {
  await withDemoServer(async (url) => {
    for (const authorization of ['Bearer should-not-reach-demo', 'Basic ignored', '']) {
      const response = await fetch(`${url}/api/flights/orders`, { headers: { Authorization: authorization } });
      assert.equal(response.status, 400);
      assert.equal((await response.json()).title, 'DemoAuthRejected');
    }
  });
});

test('demo list reads leave the single-order fictional ticketing progression unchanged', async () => {
  await withDemoServer(async (url) => {
    const aggregateId = await holdDemoOrder(url, '2032-08-14');
    const confirm = await fetch(`${url}/api/flights/orders/confirm`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': '5f6db15b-4e13-43d1-8af9-2a74b2e869bd' },
      body: JSON.stringify({ aggregateId }),
    });
    assert.equal(confirm.status, 200);
    const statuses = [];
    for (let attempt = 0; attempt < 4; attempt++) {
      for (let read = 0; read < 3; read++) {
        const response = await fetch(`${url}/api/flights/orders`);
        assert.equal(response.status, 200);
        const list = await response.json();
        assert.equal(list.items[0].status, 'Confirmed');
        assert.deepEqual(list.items[0].ticketNumbers, []);
        assert.equal(list.items[0].ticketedAt, null);
      }
      const detail = await fetch(`${url}/api/flights/orders/${aggregateId}`);
      statuses.push(detail.status === 404 ? '404' : (await detail.json()).status);
    }
    assert.deepEqual(statuses, ['404', 'Held', 'Confirmed', 'Ticketed']);
    const ticketed = await (await fetch(`${url}/api/flights/orders`)).json();
    assert.equal(ticketed.items[0].status, 'Ticketed');
    assert.deepEqual(ticketed.items[0].ticketNumbers, [`DEMO-TKT-${aggregateId.slice(0, 8)}`]);
  });
});

test('restarting the demo server clears the fictional order list', async () => {
  await withDemoServer(async (url) => {
    await holdDemoOrder(url, '2030-06-10');
    const response = await fetch(`${url}/api/flights/orders`);
    assert.equal(response.status, 200);
    assert.equal((await response.json()).items.length, 1);
  });
  await withDemoServer(async (url) => {
    const response = await fetch(`${url}/api/flights/orders`);
    assert.equal(response.status, 200);
    assert.deepEqual((await response.json()).items, []);
  });
});
