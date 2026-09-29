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

test('HTTP stub rejects malformed search and never handles hold, confirm or NL routes', async () => {
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
  for (const route of [
    '/api/flights/orders/hold',
    '/api/flights/orders/confirm',
    '/api/flights/search/nl',
    '/events/flights/orders/abc',
  ]) {
    const response = await fetch(`${baseUrl}${route}`, { method: 'POST' });
    assert.equal(response.status, 404);
  }
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
