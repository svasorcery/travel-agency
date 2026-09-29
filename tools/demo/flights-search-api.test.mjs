import assert from 'node:assert/strict';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { connect } from 'node:net';
import { after, before, test } from 'node:test';
import { buildDemoSearchResponse, createDemoServer } from './flights-search-api.mjs';

const fixture = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-search.json', import.meta.url), 'utf8'));

test('one-way and round-trip demo responses match the canonical HTTP example', () => {
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), fixture.oneWay.response);
  assert.deepEqual(buildDemoSearchResponse(fixture.roundTrip.request), fixture.roundTrip.response);
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), buildDemoSearchResponse(fixture.oneWay.request));
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

test('HTTP stub rejects malformed search and never handles booking or NL routes', async () => {
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
  for (const route of ['/api/flights/orders/quote', '/api/flights/search/nl', '/events/flights/orders/abc']) {
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
