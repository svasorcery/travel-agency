import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { once } from 'node:events';
import { readFileSync } from 'node:fs';
import { connect } from 'node:net';
import { after, before, test } from 'node:test';
import { buildDemoSearchResponse, createDemoServer } from './flights-search-api.mjs';

const fixture = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-search.json', import.meta.url), 'utf8'));
function searchExpected(value) {
  const result = structuredClone(value);
  result.offers[0].providerOfferRef += '_p1';
  if (result.offers[0].itinerary.isRoundTrip) {
    result.offers[0].totalAmount = 20000;
    result.ranking.entries[0].sourceAmount = 20000;
  }
  return result;
}

const booking = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-booking.json', import.meta.url), 'utf8'));

test('demo supplies fixed ranking facts, including honest unknown partner duration', () => {
  const response = buildDemoSearchResponse(fixture.oneWay.request);
  assert.equal(response.ranking.policy, 'price-first-v1');
  assert.equal(response.ranking.entries[0].rank, 1);
  assert.equal(response.ranking.entries[0].durationSeconds, 7200);
  assert.equal(response.ranking.entries[1].durationSeconds, null);
  assert.equal(response.ranking.entries[1].transfers, null);
  assert.deepEqual(response.ranking.entries[1].limitations, ['partial-itinerary']);
  assert.deepEqual(
    response.ranking.entries.map((entry) => entry.offerId),
    response.offers.map((offer) => offer.id),
  );
});

test('one-way and round-trip demo responses match the canonical HTTP example', () => {
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), searchExpected(fixture.rankedOneWay.response));
  assert.deepEqual(
    buildDemoSearchResponse(fixture.roundTrip.request),
    searchExpected(fixture.rankedRoundTrip.response),
  );
  assert.deepEqual(buildDemoSearchResponse(fixture.oneWay.request), buildDemoSearchResponse(fixture.oneWay.request));
});

test('demo booking references identify the selected dates and trip type', () => {
  const oneWay = buildDemoSearchResponse(fixture.oneWay.request).offers[0];
  const roundTrip = buildDemoSearchResponse(fixture.roundTrip.request).offers[0];
  const later = buildDemoSearchResponse({ ...fixture.oneWay.request, departureDate: '2031-07-12' }).offers[0];
  assert.equal(oneWay.providerOfferRef, 'off_fixture_ow_2030-06-10_p1');
  assert.equal(roundTrip.providerOfferRef, 'off_fixture_rt_2030-06-10_2030-06-17_p1');
  assert.equal(later.providerOfferRef, 'off_fixture_ow_2031-07-12_p1');
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

const travelerDetails = {
  title: 'mr',
  givenName: 'Fictional',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'male',
  email: 'fictional@example.test',
  phone: '+79001234567',
};
async function travelerRequest(id, method = 'GET', details, headers = {}, owner = 'demo-only') {
  return fetch(`${baseUrl}/api/flights/travelers${id ? `/${id}` : '?offset=0'}`, {
    method,
    headers: {
      'X-Travel-Demo-Owner': owner,
      ...(details === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...headers,
    },
    ...(details === undefined ? {} : { body: JSON.stringify(details) }),
  });
}
async function createTraveler(details = travelerDetails, owner = 'demo-only') {
  const id = randomUUID();
  const response = await travelerRequest(id, 'PUT', details, { 'If-None-Match': '*' }, owner);
  assert.equal(response.status, 201);
  const receipt = await response.json();
  assert.deepEqual(Object.keys(receipt).sort(), ['id', 'revision']);
  assert.equal(receipt.id, id);
  assert.equal(response.headers.get('etag'), `"${receipt.revision}"`);
  return receipt;
}

test('profile conditional CRUD has coherent receipts and excludes PII from receipts', async () => {
  const created = await createTraveler();
  const get = await travelerRequest(created.id);
  assert.equal(get.status, 200);
  assert.equal(get.headers.get('etag'), `"${created.revision}"`);
  assert.deepEqual(await get.json(), { ...created, details: travelerDetails });
  const updated = await travelerRequest(
    created.id,
    'PUT',
    { ...travelerDetails, givenName: 'Edited' },
    { 'If-Match': `"${created.revision}"` },
  );
  assert.equal(updated.status, 200);
  const receipt = await updated.json();
  assert.notEqual(receipt.revision, created.revision);
  assert.equal(updated.headers.get('etag'), `"${receipt.revision}"`);
  const removed = await travelerRequest(created.id, 'DELETE', undefined, { 'If-Match': `"${receipt.revision}"` });
  assert.equal(removed.status, 204);
  assert.equal(await removed.text(), '');
  assert.equal((await travelerRequest(created.id)).status, 404);
});

test('profile fake owners isolate list/get/create/update/delete without revealing foreign rows', async () => {
  const created = await createTraveler();
  for (const [method, details, headers] of [
    ['GET', undefined, {}],
    ['PUT', travelerDetails, { 'If-None-Match': '*' }],
    ['PUT', travelerDetails, { 'If-Match': `"${created.revision}"` }],
    ['DELETE', undefined, { 'If-Match': `"${created.revision}"` }],
  ])
    assert.equal((await travelerRequest(created.id, method, details, headers, 'demo-other')).status, 404);
  const foreignPage = await (await travelerRequest('', 'GET', undefined, {}, 'demo-other')).json();
  assert.ok(!foreignPage.items.some((item) => item.id === created.id));
  assert.equal((await travelerRequest(created.id, 'GET', undefined, {}, 'not-a-demo-owner')).status, 401);
  assert.equal((await fetch(`${baseUrl}/api/flights/travelers/${created.id}`)).status, 200);
  assert.equal(
    (await travelerRequest(created.id, 'GET', undefined, { Authorization: 'Bearer fictional' })).status,
    400,
  );
});

test('profile own collisions/stale versions are 412; missing rows are 404', async () => {
  const created = await createTraveler();
  assert.equal((await travelerRequest(created.id, 'PUT', travelerDetails, { 'If-None-Match': '*' })).status, 412);
  for (const method of ['PUT', 'DELETE']) {
    assert.equal(
      (
        await travelerRequest(created.id, method, method === 'PUT' ? travelerDetails : undefined, {
          'If-Match': `"${randomUUID()}"`,
        })
      ).status,
      412,
    );
    assert.equal(
      (
        await travelerRequest(randomUUID(), method, method === 'PUT' ? travelerDetails : undefined, {
          'If-Match': `"${randomUUID()}"`,
        })
      ).status,
      404,
    );
  }
});

test('profile conditional grammar rejects missing, weak, multiple, wildcard and mixed headers before effects', async () => {
  for (const headers of [
    {},
    { 'If-Match': '*' },
    { 'If-Match': `W/"${randomUUID()}"` },
    { 'If-Match': `"${randomUUID()}","${randomUUID()}"` },
    { 'If-None-Match': 'bad' },
    { 'If-Match': `"${randomUUID()}"`, 'If-None-Match': '*' },
  ]) {
    const id = randomUUID();
    const response = await travelerRequest(id, 'PUT', travelerDetails, headers);
    assert.equal(response.status, Object.keys(headers).length ? 400 : 428);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.equal((await travelerRequest(id)).status, 404);
  }
  assert.equal((await travelerRequest(randomUUID(), 'DELETE', undefined, { 'If-None-Match': '*' })).status, 400);
});

test('profile validation is strict, safe, raw-name bounded, and permits a child saved for later travel', async () => {
  await createTraveler({ ...travelerDetails, dateOfBirth: '2020-01-01' });
  for (const details of [
    { ...travelerDetails, owner: 'PII-SENTINEL' },
    { ...travelerDetails, givenName: ' '.repeat(50) + 'PII-SENTINEL' },
    { ...travelerDetails, title: 'other' },
    { ...travelerDetails, gender: '' },
    { ...travelerDetails, dateOfBirth: '9999-01-01' },
    { ...travelerDetails, dateOfBirth: '0001-01-01' },
    { ...travelerDetails, email: ' PII-SENTINEL@example.test' },
    { ...travelerDetails, phone: 'PII-SENTINEL' },
  ]) {
    const id = randomUUID();
    const response = await travelerRequest(id, 'PUT', details, { 'If-None-Match': '*' });
    assert.equal(response.status, 400);
    const body = await response.text();
    assert.ok(!body.includes('PII-SENTINEL'));
    assert.equal((await travelerRequest(id)).status, 404);
  }
});

test('profile DELETE refuses bodies, including oversize, without deleting an existing row', async () => {
  const created = await createTraveler();
  for (const [body, expected] of [
    ['x', 400],
    ['x'.repeat(16 * 1024 + 1), 413],
  ]) {
    const response = await fetch(`${baseUrl}/api/flights/travelers/${created.id}`, {
      method: 'DELETE',
      body,
      headers: { 'If-Match': `"${created.revision}"` },
    });
    assert.equal(response.status, expected);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.equal((await travelerRequest(created.id)).status, 200);
  }
});

test('profile fixed pages are ordered and reject arbitrary limits/offsets', async () => {
  const local = createDemoServer({ now: () => new Date('2026-10-03T00:00:00Z') });
  await new Promise((resolve) => local.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${local.address().port}/api/flights/travelers`;
  try {
    const ids = Array.from({ length: 22 }, () => randomUUID());
    for (const id of ids)
      assert.equal(
        (
          await fetch(`${url}/${id}`, {
            method: 'PUT',
            body: JSON.stringify(travelerDetails),
            headers: { 'Content-Type': 'application/json', 'If-None-Match': '*' },
          })
        ).status,
        201,
      );
    const first = await (await fetch(`${url}?offset=0`)).json();
    assert.equal(first.items.length, 20);
    assert.equal(first.hasMore, true);
    assert.deepEqual(
      first.items.map((i) => i.id),
      ids.sort().reverse().slice(0, 20),
    );
    const second = await (await fetch(`${url}?offset=20`)).json();
    assert.equal(second.items.length, 2);
    assert.equal(second.hasMore, false);
    for (const query of [
      'offset=1',
      'offset=-20',
      'offset=20.0',
      'offset=2147483641',
      'offset=0&offset=20',
      'limit=20',
      'search=fictional',
    ])
      assert.equal((await fetch(`${url}?${query}`)).status, 400);
  } finally {
    await new Promise((resolve) => local.close(resolve));
  }
});

test('profile known and chunked oversized PUT have zero effects', async () => {
  for (const chunked of [false, true]) {
    const id = randomUUID();
    const body = JSON.stringify({ ...travelerDetails, givenName: 'x'.repeat(16 * 1024) });
    const response = await fetch(`${baseUrl}/api/flights/travelers/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'If-None-Match': '*' },
      body: chunked
        ? new ReadableStream({
            start(c) {
              c.enqueue(new TextEncoder().encode(body.slice(0, 100)));
              c.enqueue(new TextEncoder().encode(body.slice(100)));
              c.close();
            },
          })
        : body,
      ...(chunked ? { duplex: 'half' } : {}),
    });
    assert.equal(response.status, 413);
    assert.equal((await travelerRequest(id)).status, 404);
  }
});

test('fictional profile key unavailability gives no partial page, preserves records, and never gates DELETE', async () => {
  const options = { travelerKeysUnavailable: false };
  const local = createDemoServer(options);
  await new Promise((resolve) => local.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${local.address().port}/api/flights/travelers`;
  const id = randomUUID();
  const headers = { 'Content-Type': 'application/json', 'If-None-Match': '*' };
  try {
    const receipt = await (
      await fetch(`${url}/${id}`, { method: 'PUT', headers, body: JSON.stringify(travelerDetails) })
    ).json();
    options.travelerKeysUnavailable = true;
    for (const path of ['', `/${id}`]) {
      const response = await fetch(url + path);
      assert.equal(response.status, 503);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.ok(!(await response.text()).includes(travelerDetails.givenName));
    }
    const unknown = randomUUID();
    assert.equal(
      (await fetch(`${url}/${unknown}`, { method: 'PUT', headers, body: JSON.stringify(travelerDetails) })).status,
      503,
    );
    options.travelerKeysUnavailable = false;
    assert.equal((await fetch(`${url}/${unknown}`)).status, 404);
    assert.deepEqual((await (await fetch(`${url}/${id}`)).json()).revision, receipt.revision);
    options.travelerKeysUnavailable = true;
    assert.equal(
      (await fetch(`${url}/${id}`, { method: 'DELETE', headers: { 'If-Match': `"${receipt.revision}"` } })).status,
      204,
    );
  } finally {
    await new Promise((resolve) => local.close(resolve));
  }
});

test('profile chunked DELETE oversized body has zero effects and all prefix refusals are no-store', async () => {
  const receipt = await createTraveler();
  const response = await fetch(`${baseUrl}/api/flights/travelers/${receipt.id}`, {
    method: 'DELETE',
    headers: { 'If-Match': `"${receipt.revision}"` },
    duplex: 'half',
    body: new ReadableStream({
      start(controller) {
        controller.enqueue(new Uint8Array(16 * 1024 + 1));
        controller.close();
      },
    }),
  });
  assert.equal(response.status, 413);
  assert.equal((await travelerRequest(receipt.id)).status, 200);
  for (const suffix of ['/invalid', '/bad/extra', `/${randomUUID()}`]) {
    const refused = await fetch(`${baseUrl}/API/FLIGHTS/TRAVELERS${suffix}`);
    assert.equal(refused.status, 404);
    assert.equal(refused.headers.get('cache-control'), 'no-store');
  }
  const again = await fetch(`${baseUrl}/api/flights/travelers/${receipt.id}/`);
  assert.equal(again.status, 200);
});

test('HTTP stub returns the canonical contract and proves the demo source', async () => {
  const response = await fetch(`${baseUrl}/api/flights/search?currency=RUB`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Accept-Language': 'ru' },
    body: JSON.stringify(fixture.oneWay.request),
  });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('x-travel-demo'), 'fixtures');
  assert.deepEqual(await response.json(), searchExpected(fixture.rankedOneWay.response));
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
    const result = await response.json();
    const expected = structuredClone(booking[name].response);
    expected.binding = result.binding;
    assert.deepEqual(result, expected);
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
  const result = await response.json();
  const expected = structuredClone(booking.oneWay.response);
  expected.binding = result.binding;
  assert.deepEqual(result, expected);
});

test('re-quote keeps fictional price facts and never accepts a partner or unknown reference', async () => {
  const requote = await fetch(`${baseUrl}/api/flights/orders/quote`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(booking.reQuoteChanged.request),
  });
  assert.equal(requote.status, 200);
  const result = await requote.json();
  const expected = structuredClone(booking.reQuoteChanged.response);
  expected.binding = result.binding;
  assert.deepEqual(result, expected);

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
  const quoted = await (await post(baseUrl, 'orders/quote', booking.oneWay.request)).json();
  const aggregateId = quoted.aggregateId;
  const passenger = {
    givenName: 'Demo',
    familyName: 'Traveler',
    dateOfBirth: '1990-04-12',
    gender: 'male',
    title: 'mr',
    email: 'demo@example.test',
    phone: '+79161234567',
  };
  const holdKey = '4e5ca40a-3d3f-42c0-99e8-1a63a1d758ac';
  const confirmKey = '5f6db15b-4e13-43d1-8af9-2a74b2e869bd';
  const heldResponse = await fetch(`${baseUrl}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': holdKey },
    body: JSON.stringify({
      aggregateId,
      quoteRevision: quoted.binding.revision,
      passengers: [{ ...passenger, bookingPassengerId: quoted.binding.slots[0].bookingPassengerId }],
    }),
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
    gender: 'male',
    title: 'mr',
    email: 'demo@example.test',
    phone: '+79161234567',
  };
  const hold = await fetch(`${baseUrl}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': randomUUID() },
    body: JSON.stringify({
      aggregateId,
      quoteRevision: quoted.binding.revision,
      passengers: [{ ...passenger, bookingPassengerId: quoted.binding.slots[0].bookingPassengerId }],
    }),
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
      'Idempotency-Key': randomUUID(),
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
  const second = await quote();
  assert.notEqual(second.binding.revision, first.binding.revision);
  second.binding.revision = first.binding.revision;
  assert.deepEqual(second, first);
  assert.ok(Date.parse(first.offer.fetchedAt) < Date.parse(first.offer.expiresAt));
  assert.ok(Date.parse(first.offer.expiresAt) < Date.parse(first.offer.itinerary.slices[0].segments[0].departAt));
});

test('HTTP stub rejects malformed search and leaves unrelated booking, cancel and NL routes unimplemented', async () => {
  const bad = await fetch(`${baseUrl}/api/flights/search?currency=RUB`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...fixture.oneWay.request, passengerCount: 10 }),
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
  gender: 'male',
  title: 'mr',
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
  const quoted = await quote.json();
  const { aggregateId } = quoted;
  const hold = await fetch(`${url}/api/flights/orders/hold`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': randomUUID() },
    body: JSON.stringify({
      aggregateId,
      quoteRevision: quoted.binding.revision,
      passengers: [{ ...listPassenger, bookingPassengerId: quoted.binding.slots[0].bookingPassengerId }],
    }),
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
          'passengerCount',
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

test('group demo uses nonlinear totals and no synthetic partner offer', () => {
  for (const [passengerCount, amount] of [
    [2, 20750],
    [9, 92900],
  ]) {
    const r = buildDemoSearchResponse({ ...fixture.oneWay.request, passengerCount });
    assert.equal(r.offers.length, 1);
    assert.equal(r.offers[0].totalAmount, amount);
    assert.equal(r.offers[0].passengerCount, passengerCount);
    assert.equal(r.offers[0].holdEligible, true);
    assert.match(r.offers[0].providerOfferRef, new RegExp(`_p${passengerCount}$`));
    assert.deepEqual(r.skippedProviders, [{ provider: 'travelpayouts', reasonCode: 'passenger-count-unsupported' }]);
    assert.deepEqual(
      r.ranking.entries.map((e) => e.offerId),
      r.offers.map((e) => e.id),
    );
  }
});
async function post(url, path, body, key = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaab') {
  return fetch(`${url}/api/flights/${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key },
    body: typeof body === 'string' ? body : JSON.stringify(body),
  });
}
function partyFor(binding) {
  return binding.slots.map((s, i) => ({
    ...listPassenger,
    bookingPassengerId: s.bookingPassengerId,
    title: i % 2 ? 'ms' : 'mr',
    gender: i % 2 ? 'female' : 'male',
    givenName: `Demo${String.fromCharCode(65 + i)}`,
  }));
}
async function groupQuote(url, count = 2, roundTrip = false) {
  const ref = roundTrip ? `off_fixture_rt_2032-06-10_2032-06-17_p${count}` : `off_fixture_ow_2032-06-10_p${count}`;
  const r = await post(url, 'orders/quote', { provider: 'duffel', providerOfferRef: ref, passengerCount: count });
  assert.equal(r.status, 200);
  return r.json();
}
test('group quote binds slots and fresh revision; hold replays only identical bytes', async () => {
  await withDemoServer(async (url) => {
    const q = await groupQuote(url);
    assert.equal(q.offer.totalAmount, 21420);
    assert.equal(q.binding.slots.length, 2);
    const refreshed = await (
      await post(url, 'orders/quote', {
        provider: 'duffel',
        providerOfferRef: q.offer.providerOfferRef,
        aggregateId: q.aggregateId,
        passengerCount: 2,
      })
    ).json();
    assert.notEqual(refreshed.binding.revision, q.binding.revision);
    assert.deepEqual(refreshed.binding.slots, q.binding.slots);
    const raw = JSON.stringify({
      aggregateId: q.aggregateId,
      quoteRevision: refreshed.binding.revision,
      passengers: partyFor(refreshed.binding),
    });
    const held = await post(url, 'orders/hold', raw);
    assert.equal(held.status, 200);
    const result = await held.json();
    const replay = await post(url, 'orders/hold', raw);
    assert.equal(replay.headers.get('idempotency-replay'), 'true');
    assert.deepEqual(await replay.json(), result);
    const conflict = await post(url, 'orders/hold', raw + ' ');
    assert.equal(conflict.status, 409);
    assert.match((await conflict.json()).type, /IdempotencyConflict$/);
    const malformedConflict = await post(url, 'orders/hold', '{');
    assert.equal(malformedConflict.status, 409);
    assert.match((await malformedConflict.json()).type, /IdempotencyConflict$/);
    const observed = await (await fetch(`${url}/api/flights/orders/${q.aggregateId}`)).json();
    assert.equal(observed.passengerCount, 2);
    assert.deepEqual(observed.ticketNumbers, []);
  });
});
test('missing stale foreign duplicate and wrong count reject before effects', async () => {
  await withDemoServer(async (url) => {
    const q = await groupQuote(url),
      valid = { aggregateId: q.aggregateId, quoteRevision: q.binding.revision, passengers: partyFor(q.binding) };
    for (const [body, status, code] of [
      [{ ...valid, quoteRevision: undefined }, 400, 'QuoteBindingRequired'],
      [{ ...valid, quoteRevision: '11111111-1111-4111-8111-111111111111' }, 409, 'QuoteRevisionMismatch'],
      [{ ...valid, passengers: valid.passengers.slice(0, 1) }, 409, 'PassengerCountMismatch'],
      [
        {
          ...valid,
          passengers: [
            valid.passengers[0],
            { ...valid.passengers[1], bookingPassengerId: '22222222-2222-4222-8222-222222222222' },
          ],
        },
        409,
        'PassengerSlotsMismatch',
      ],
      [{ ...valid, passengers: [valid.passengers[0], valid.passengers[0]] }, 409, 'PassengerSlotsMismatch'],
    ]) {
      const r = await post(url, 'orders/hold', body);
      assert.equal(r.status, status);
      assert.match((await r.json()).type, new RegExp(`Flights\\.${code}$`));
      assert.deepEqual((await (await fetch(`${url}/api/flights/orders`)).json()).items, []);
    }
  });
});
test('nine bounded adults fit cap; invalid details safe; oversize zero effects', async () => {
  await withDemoServer(async (url) => {
    const q = await groupQuote(url, 9, true),
      passengers = partyFor(q.binding).map((p) => ({
        ...p,
        givenName: 'A'.repeat(20),
        familyName: 'B'.repeat(20),
        email: `${'a'.repeat(64)}@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(61)}`,
        phone: '+123456789012345',
      }));
    const body = { aggregateId: q.aggregateId, quoteRevision: q.binding.revision, passengers };
    assert.ok(Buffer.byteLength(JSON.stringify(body)) < 16384);
    const bad = await post(url, 'orders/hold', {
      ...body,
      passengers: passengers.map((p, i) =>
        i ? p : { ...p, dateOfBirth: '2020-01-01', title: 'unknown', gender: 'unspecified', email: 'private-sentinel' },
      ),
    });
    assert.equal(bad.status, 400);
    const problem = await bad.json();
    assert.match(problem.type, /PassengerInvalid$/);
    assert.ok(problem.passengerErrors.length <= 63);
    assert.ok(problem.passengerErrors.some((e) => e.code === 'Flights.PassengerAdultRequiredInvalid'));
    assert.equal(JSON.stringify(problem).includes('private-sentinel'), false);
    const large = await post(url, 'orders/hold', JSON.stringify(body) + ' '.repeat(16384));
    assert.equal(large.status, 413);
    assert.match((await large.json()).type, /RequestTooLarge$/);
    assert.deepEqual((await (await fetch(`${url}/api/flights/orders`)).json()).items, []);
    assert.equal((await post(url, 'orders/hold', body)).status, 200);
  });
});

test('quote rejects out-of-range count while an empty group search still reports the capability skip', async () => {
  await withDemoServer(async (url) => {
    for (const passengerCount of [0, 10, 1.5, null, '2']) {
      const r = await post(url, 'orders/quote', {
        provider: 'duffel',
        providerOfferRef: 'off_fixture_ow_2032-06-10_p2',
        passengerCount,
      });
      assert.equal(r.status, 400);
      assert.match((await r.json()).type, /CommandInvalid$/);
    }
    const r = buildDemoSearchResponse({ ...fixture.oneWay.request, destination: 'VKO', passengerCount: 2 });
    assert.equal(r.offers.length, 0);
    assert.deepEqual(r.skippedProviders, [{ provider: 'travelpayouts', reasonCode: 'passenger-count-unsupported' }]);
  });
});
test('streamed oversize party has zero effects; explicit genders titles bounds and leap birthday are checked', async (t) => {
  t.mock.timers.enable({ apis: ['Date'], now: Date.parse('2026-01-01T00:00:00Z') });
  await withDemoServer(async (url) => {
    const q = await groupQuote(url),
      base = { aggregateId: q.aggregateId, quoteRevision: q.binding.revision, passengers: partyFor(q.binding) };
    const stream = new ReadableStream({
      start(c) {
        c.enqueue(new TextEncoder().encode(JSON.stringify(base)));
        c.enqueue(new TextEncoder().encode(' '.repeat(16384)));
        c.close();
      },
    });
    const oversize = await fetch(`${url}/api/flights/orders/hold`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': randomUUID() },
      body: stream,
      duplex: 'half',
    });
    assert.equal(oversize.status, 413);
    assert.deepEqual((await (await fetch(`${url}/api/flights/orders`)).json()).items, []);
    for (const [field, value, code] of [
      ['phone', '', 'PassengerPhoneInvalid'],
      ['givenName', 'A'.repeat(21), 'PassengerGivenNameInvalid'],
      ['familyName', 'Demo1', 'PassengerFamilyNameInvalid'],
      ['email', 'a'.repeat(255), 'PassengerEmailInvalid'],
      ['title', 'unknown', 'PassengerTitleInvalid'],
      ['gender', 'unspecified', 'PassengerGenderInvalid'],
      ['dateOfBirth', '2000-02-30', 'PassengerDateOfBirthInvalid'],
      ['dateOfBirth', '9999-01-01', 'PassengerDateOfBirthFutureInvalid'],
    ]) {
      const r = await post(url, 'orders/hold', {
        ...base,
        passengers: base.passengers.map((p, i) => (i ? p : { ...p, [field]: value })),
      });
      assert.equal(r.status, 400);
      const e = await r.json();
      assert.ok(e.passengerErrors.some((p) => p.field === field && p.code === `Flights.${code}`));
    }
    const birthday = await post(url, 'orders/quote', {
      provider: 'duffel',
      providerOfferRef: 'off_fixture_ow_2026-02-28_p1',
      passengerCount: 1,
    });
    const leap = await birthday.json();
    const held = await post(url, 'orders/hold', {
      aggregateId: leap.aggregateId,
      quoteRevision: leap.binding.revision,
      passengers: partyFor(leap.binding).map((p) => ({
        ...p,
        dateOfBirth: '2008-02-29',
        title: 'dr',
        gender: 'female',
      })),
    });
    assert.equal(held.status, 200);
  });
});

test('ranking evidence matches explicit group and singleton return totals', () => {
  for (const passengerCount of [1, 2, 9])
    for (const base of [fixture.oneWay.request, fixture.roundTrip.request]) {
      const r = buildDemoSearchResponse({ ...base, passengerCount });
      for (const entry of r.ranking.entries) {
        const offer = r.offers.find((o) => o.id === entry.offerId);
        assert.equal(entry.sourceAmount, offer.totalAmount);
      }
    }
});

const openJaw = {
  legs: [
    { origin: 'LED', destination: 'DME', departureDate: '2030-08-01' },
    { origin: 'VKO', destination: 'LED', departureDate: '2030-08-08' },
  ],
  passengerCount: 2,
  cabinClass: 'economy',
};
const fourLeg = {
  ...openJaw,
  legs: [
    openJaw.legs[0],
    { origin: 'VKO', destination: 'KZN', departureDate: '2030-08-03' },
    { origin: 'KZN', destination: 'SVO', departureDate: '2030-08-05' },
    { origin: 'SVO', destination: 'LED', departureDate: '2030-08-08' },
  ],
};
test('v2 exact ordered open-jaw and four-leg offers retain one nonlinear whole-party price and capability skip', () => {
  for (const criteria of [openJaw, fourLeg]) {
    const response = buildDemoSearchResponse(criteria);
    assert.equal(response.offers.length, 1);
    const offer = response.offers[0];
    assert.equal(offer.itinerary.journeyKind, 'multi-leg');
    assert.equal(offer.itinerary.isRoundTrip, false);
    assert.equal(offer.passengerCount, 2);
    assert.deepEqual(
      offer.itinerary.slices.map((s) => [s.origin, s.destination, s.segments[0].departAt.slice(0, 10)]),
      criteria.legs.map((l) => [l.origin, l.destination, l.departureDate]),
    );
    assert.equal(offer.itinerary.totalDuration, criteria.legs.length === 2 ? '04:00:00' : '08:00:00');
    assert.notEqual(
      offer.totalAmount,
      buildDemoSearchResponse({ ...criteria, passengerCount: 1 }).offers[0].totalAmount * 2,
    );
    assert.deepEqual(response.skippedProviders, [{ provider: 'travelpayouts', reasonCode: 'journey-unsupported' }]);
    assert.equal(response.ranking.entries[0].sourceAmount, offer.totalAmount);
    assert.equal(response.ranking.entries[0].durationSeconds, criteria.legs.length * 7200);
    assert.equal(response.ranking.entries[0].transfers, 0);
  }
});
test('v2 geometry derives one-way and mirrored return rather than using only leg count', () => {
  for (const [legs, kind, roundTrip] of [
    [[openJaw.legs[0]], 'one-way', false],
    [[openJaw.legs[0], { origin: 'DME', destination: 'LED', departureDate: '2030-08-08' }], 'round-trip', true],
  ]) {
    const response = buildDemoSearchResponse({ ...openJaw, legs, passengerCount: 1 });
    assert.equal(response.offers[0].itinerary.journeyKind, kind);
    assert.equal(response.offers[0].itinerary.isRoundTrip, roundTrip);
    assert.equal(response.offers.length, 1);
    assert.equal(response.skippedProviders[0].reasonCode, 'journey-unsupported');
  }
  const changedFourth = structuredClone(fourLeg);
  changedFourth.legs[3].destination = 'DME';
  assert.notEqual(
    buildDemoSearchResponse(fourLeg).offers[0].providerOfferRef,
    buildDemoSearchResponse(changedFourth).offers[0].providerOfferRef,
  );
});
test('v2 HTTP rejects malformed exact airport/date/count bodies, mixed contracts and wrong query without effects', async () => {
  await withDemoServer(async (url) => {
    for (const body of [
      '{',
      { ...openJaw, legs: [] },
      { ...openJaw, legs: Array(5).fill(openJaw.legs[0]) },
      { ...openJaw, legs: [{ ...openJaw.legs[0], origin: 'MOW' }] },
      { ...openJaw, legs: [{ ...openJaw.legs[0], origin: 'led' }] },
      { ...openJaw, legs: [{ ...openJaw.legs[0], destination: 'LED' }] },
      { ...openJaw, legs: [{ ...openJaw.legs[0], departureDate: '2030-02-30' }] },
      { ...openJaw, legs: [...openJaw.legs].reverse() },
      ...[0, 10, 1.5, '2', null].map((passengerCount) => ({ ...openJaw, passengerCount })),
      { ...openJaw, cabinClass: 'business' },
      { ...openJaw, origin: 'LED' },
      { ...openJaw, legs: [{ ...openJaw.legs[0], returnDate: null }] },
    ])
      assert.equal((await post(url, 'search/v2?currency=RUB', body)).status, 400);
    for (const query of ['currency=USD', 'currency=RUB&currency=RUB', 'currency=RUB&extra=1', ''])
      assert.equal((await post(url, `search/v2?${query}`, openJaw)).status, 400);
    assert.equal((await post(url, 'search?currency=RUB', openJaw)).status, 400);
    assert.equal((await post(url, 'search/v2?currency=RUB', fixture.oneWay.request)).status, 400);
    const response = await fetch(`${url}/api/flights/search/v2?currency=RUB`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'fictional' },
      body: JSON.stringify(openJaw),
    });
    assert.equal(response.status, 400);
    assert.equal((await (await fetch(`${url}/api/flights/orders`)).json()).items.length, 0);
  });
});
test('v2 HTTP refuses non-string airport arrays instead of reporting genuine empty inventory', async () => {
  await withDemoServer(async (url) => {
    for (const field of ['origin', 'destination']) {
      const body = structuredClone(openJaw);
      body.legs[0][field] = [body.legs[0][field]];
      const response = await post(url, 'search/v2?currency=RUB', body);
      assert.equal(response.status, 400);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.equal((await (await fetch(`${url}/api/flights/orders`)).json()).items.length, 0);
    }
  });
});
test('v2 HTTP refuses duplicate root and leg fields including escaped names and binder-case aliases', async () => {
  await withDemoServer(async (url) => {
    const raw = JSON.stringify(openJaw);
    for (const body of [
      raw.replace('"passengerCount":2', '"passengerCount":1,"passengerCount":2'),
      raw.replace('"origin":"LED"', '"origin":"JFK","origin":"LED"'),
      raw.replace('"legs":', '"legs":[],"legs":'),
      raw.replace('"passengerCount":2', '"passengerCount":1,"passenger\\u0043ount":2'),
      raw.replace('"origin":"LED"', '"ori\\u0067in":"JFK","origin":"LED"'),
      raw.replace('"destination":"DME"', '"destination":"JFK","destinatio\\u006e":"DME"'),
      raw.replace('"passengerCount":2', '"PassengerCount":1,"passengerCount":2'),
      raw.replace('"origin":"LED"', '"ORIGIN":"JFK","origin":"LED"'),
    ]) {
      const response = await post(url, 'search/v2?currency=RUB', body);
      assert.equal(response.status, 400);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.ok(!(await response.text()).includes('JFK'));
      assert.equal((await (await fetch(`${url}/api/flights/orders`)).json()).items.length, 0);
    }
    // JSON escapes are legal when the decoded name occurs only once; repeated names in different legs are legal.
    const escaped = raw.replace('"legs":', '"l\\u0065gs":').replace('"origin":"LED"', '"ori\\u0067in":"LED"');
    const valid = await post(url, 'search/v2?currency=RUB', escaped);
    assert.equal(valid.status, 200);
    assert.deepEqual(
      (await valid.json()).offers[0].itinerary.slices.map((s) => [s.origin, s.destination]),
      openJaw.legs.map((l) => [l.origin, l.destination]),
    );
    // Legacy parsing remains compatible; v2's stricter duplicate refusal does not change v1.
    const legacy = JSON.stringify(fixture.oneWay.request).replace('"origin":"LED"', '"origin":"JFK","origin":"LED"');
    assert.equal((await post(url, 'search?currency=RUB', legacy)).status, 200);
  });
});
test('v2 HTTP refuses DateOnly.MinValue in any requested leg without returning an offer', async () => {
  await withDemoServer(async (url) => {
    for (const legs of [
      [{ ...openJaw.legs[0], departureDate: '0001-01-01' }],
      openJaw.legs.map((l) => ({ ...l, departureDate: '0001-01-01' })),
    ]) {
      const response = await post(url, 'search/v2?currency=RUB', { ...openJaw, legs });
      assert.equal(response.status, 400);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.equal((await (await fetch(`${url}/api/flights/orders`)).json()).items.length, 0);
    }
  });
});
test('v2 unknown airport yields no silent partial itinerary while same local dates remain ordered', () => {
  const unknown = structuredClone(fourLeg);
  unknown.legs[3].destination = 'JFK';
  const response = buildDemoSearchResponse(unknown);
  assert.equal(response.offers.length, 0);
  assert.deepEqual(response.partialFailures, []);
  assert.equal(response.skippedProviders[0].reasonCode, 'journey-unsupported');
  const sameDay = buildDemoSearchResponse({
    ...fourLeg,
    legs: fourLeg.legs.map((l) => ({ ...l, departureDate: '2030-08-01' })),
  });
  const segments = sameDay.offers[0].itinerary.slices.map((s) => s.segments[0]);
  for (let i = 1; i < segments.length; i++)
    assert.ok(Date.parse(segments[i].departAt) >= Date.parse(segments[i - 1].arriveAt));
});
test('v2 mirrored return, open-jaw, three and four legs quote, hold, confirm, detail and list form one intact order', async () => {
  const mirrored = { ...openJaw, legs: [openJaw.legs[0], { ...openJaw.legs[1], origin: 'DME' }] };
  const threeLeg = { ...fourLeg, legs: fourLeg.legs.slice(0, 3) };
  for (const criteria of [mirrored, openJaw, threeLeg, fourLeg])
    await withDemoServer(async (url) => {
      const searched = await post(url, 'search/v2?currency=RUB', criteria);
      assert.equal(searched.status, 200);
      assert.equal(searched.headers.get('x-travel-demo'), 'fixtures');
      const offer = (await searched.json()).offers[0];
      const quoted = await post(url, 'orders/quote', {
        provider: 'duffel',
        providerOfferRef: offer.providerOfferRef,
        passengerCount: 2,
      });
      assert.equal(quoted.status, 200);
      const quote = await quoted.json();
      assert.deepEqual(quote.offer.itinerary, offer.itinerary);
      assert.equal(quote.binding.firstDepartureLocalDate, criteria.legs[0].departureDate);
      assert.equal(quote.binding.slots.length, 2);
      const refresh = await post(url, 'orders/quote', {
        provider: 'duffel',
        providerOfferRef: offer.providerOfferRef,
        aggregateId: quote.aggregateId,
        passengerCount: 2,
      });
      const fresh = await refresh.json();
      assert.notEqual(fresh.binding.revision, quote.binding.revision);
      assert.deepEqual(fresh.binding.slots, quote.binding.slots);
      const body = {
        aggregateId: fresh.aggregateId,
        quoteRevision: fresh.binding.revision,
        passengers: partyFor(fresh.binding),
      };
      const held = await post(url, 'orders/hold', body);
      assert.equal(held.status, 200);
      assert.equal((await held.json()).aggregateId, fresh.aggregateId);
      const list = await (await fetch(`${url}/api/flights/orders`)).json();
      assert.equal(list.items.length, 1);
      assert.equal(list.items[0].totalAmount, fresh.offer.totalAmount);
      assert.equal(list.items[0].passengerCount, 2);
      assert.deepEqual(list.items[0].itinerary, offer.itinerary);
      assert.deepEqual(await (await fetch(`${url}/api/flights/orders/${fresh.aggregateId}`)).json(), list.items[0]);
      assert.equal((await post(url, 'orders/confirm', { aggregateId: fresh.aggregateId })).status, 200);
      for (let i = 0; i < 4; i++) await fetch(`${url}/api/flights/orders/${fresh.aggregateId}`);
      const ticketed = await (await fetch(`${url}/api/flights/orders`)).json();
      assert.equal(ticketed.items.length, 1);
      assert.equal(ticketed.items[0].status, 'Ticketed');
      assert.deepEqual(ticketed.items[0].itinerary, offer.itinerary);
      assert.ok(!JSON.stringify(ticketed).includes(listPassenger.givenName));
    });
});
