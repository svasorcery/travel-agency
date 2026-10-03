import { createHash, randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const examples = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-search.json', import.meta.url), 'utf8'));
const booking = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-booking.json', import.meta.url), 'utf8'));
const maxBodyBytes = 16 * 1024;

function validDate(value) {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const parsed = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
}

function validCriteria(value) {
  return (
    value !== null &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    typeof value.origin === 'string' &&
    /^[A-Z]{3}$/.test(value.origin) &&
    typeof value.destination === 'string' &&
    /^[A-Z]{3}$/.test(value.destination) &&
    value.origin !== value.destination &&
    validDate(value.departureDate) &&
    (value.returnDate === null || (validDate(value.returnDate) && value.returnDate >= value.departureDate)) &&
    Number.isInteger(value.passengerCount) &&
    value.passengerCount >= 1 &&
    value.passengerCount <= 9 &&
    value.cabinClass === 'economy'
  );
}

function shiftTimestamp(value, days) {
  const originalDate = value.slice(0, 10);
  const shifted = new Date(Date.parse(`${originalDate}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10);
  return shifted + value.slice(10);
}

function dateDifference(date, anchor) {
  return (Date.parse(`${date}T00:00:00Z`) - Date.parse(`${anchor}T00:00:00Z`)) / 86_400_000;
}

function demoAggregateId(providerOfferRef) {
  const hex = createHash('sha256').update(`travel-flights-demo:${providerOfferRef}`).digest('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}

function demoExpiry(departureDate) {
  const precedingMinute = new Date(Date.parse(`${departureDate}T00:00:00Z`) - 60_000);
  return `${precedingMinute.toISOString().slice(0, 19)}+00:00`;
}

function validGuid(value) {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
  );
}

function validIdempotencyKey(value) {
  return (
    typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)
  );
}

const totals = {
  oneWay: {
    search: [10500, 20750, 31020, 41330, 51610, 61900, 72240, 82550, 92900],
    quote: [10800, 21420, 31990, 42580, 53160, 63750, 74330, 84950, 95580],
  },
  roundTrip: {
    search: [20000, 39750, 59100, 78200, 97400, 116300, 135200, 154100, 172900],
    quote: [20500, 40720, 60630, 80310, 100090, 119570, 139180, 158850, 178480],
  },
};
function problem(status, code, passengerErrors) {
  return { status, code, passengerErrors };
}
function validateParty(body, quote) {
  if (
    !body ||
    typeof body !== 'object' ||
    Array.isArray(body) ||
    !validGuid(body.aggregateId) ||
    !Array.isArray(body.passengers) ||
    body.passengers.length < 1 ||
    body.passengers.length > 9
  )
    return problem(400, 'Flights.CommandInvalid');
  if (
    body.quoteRevision == null ||
    body.quoteRevision === '' ||
    body.quoteRevision === '00000000-0000-0000-0000-000000000000'
  )
    return problem(400, 'Flights.QuoteBindingRequired');
  if (!validGuid(body.quoteRevision)) return problem(400, 'Flights.QuoteBindingInvalid');
  if (!quote) return problem(404, 'Flights.OfferNotFound');
  if (Date.parse(quote.offer.expiresAt) <= Date.now()) return problem(400, 'Flights.OfferExpired');
  if (body.quoteRevision.toLowerCase() !== quote.binding.revision.toLowerCase())
    return problem(409, 'Flights.QuoteRevisionMismatch');
  if (body.passengers.length !== quote.binding.passengerCount) return problem(409, 'Flights.PassengerCountMismatch');
  const ids = body.passengers.map((p) => p?.bookingPassengerId);
  if (ids.some((id) => !validGuid(id))) return problem(400, 'Flights.QuoteBindingInvalid');
  const members = new Set(ids.map((id) => id.toLowerCase()));
  if (members.size !== ids.length || quote.binding.slots.some((s) => !members.has(s.bookingPassengerId.toLowerCase())))
    return problem(409, 'Flights.PassengerSlotsMismatch');
  if (!quote.offer.holdEligible)
    return problem(
      400,
      quote.offer.holdIneligibilityReason === 'identity-documents-required'
        ? 'Flights.IdentityDocumentsRequired'
        : 'Flights.HoldNotSupported',
    );
  const errors = [],
    today = new Date().toISOString().slice(0, 10);
  for (const p of body.passengers) {
    const add = (field, code) =>
      errors.push({ bookingPassengerId: p.bookingPassengerId.toLowerCase(), field, code: `Flights.${code}` });
    if (!['mr', 'ms', 'mrs', 'miss', 'dr'].includes(p.title)) add('title', 'PassengerTitleInvalid');
    for (const field of ['givenName', 'familyName']) {
      const v = p[field];
      if (
        typeof v !== 'string' ||
        !/^[A-Za-z\u00c0-\u00ff\u0100-\u017f '-]{1,20}$/.test(v.trim()) ||
        /[\u00c6\u00e6\u0132\u0133\u0152\u0153\u00de\u00f0\u00d7\u00f7]/.test(v) ||
        !/[A-Za-z\u00c0-\u017f]/.test(v)
      )
        add(field, field === 'givenName' ? 'PassengerGivenNameInvalid' : 'PassengerFamilyNameInvalid');
    }
    if (!['male', 'female'].includes(p.gender)) add('gender', 'PassengerGenderInvalid');
    if (typeof p.email !== 'string' || p.email.length > 254 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(p.email))
      add('email', 'PassengerEmailInvalid');
    if (typeof p.phone !== 'string' || !/^\+[1-9][0-9]{7,14}$/.test(p.phone)) add('phone', 'PassengerPhoneInvalid');
    if (!validDate(p.dateOfBirth) || p.dateOfBirth < '0001-01-01') add('dateOfBirth', 'PassengerDateOfBirthInvalid');
    else if (p.dateOfBirth > today) add('dateOfBirth', 'PassengerDateOfBirthFutureInvalid');
    else {
      const y = Number(p.dateOfBirth.slice(0, 4)) + 18,
        leap = y % 4 === 0 && (y % 100 !== 0 || y % 400 === 0),
        birthday = `${String(y).padStart(4, '0')}-${p.dateOfBirth.slice(5) === '02-29' && !leap ? '02-28' : p.dateOfBirth.slice(5)}`;
      if (y > 9999 || birthday > quote.binding.firstDepartureLocalDate)
        add('dateOfBirth', 'PassengerAdultRequiredInvalid');
    }
  }
  return errors.length ? problem(400, 'Flights.PassengerInvalid', errors.slice(0, 63)) : null;
}
function buildDemoHoldResponse(body) {
  return {
    aggregateId: body.aggregateId,
    providerOrderId: `demo-order-${body.aggregateId.slice(0, 8)}`,
    heldUntil: new Date(Date.now() + 15 * 60_000).toISOString(),
  };
}

function buildDemoConfirmResponse(body) {
  if (body === null || typeof body !== 'object' || Array.isArray(body) || !validGuid(body.aggregateId)) return null;
  return { aggregateId: body.aggregateId, status: 'Confirmed', paymentRef: null };
}

export function buildDemoSearchResponse(criteria) {
  if (!validCriteria(criteria)) throw new TypeError('Invalid demo search criteria');
  if (criteria.origin !== 'LED' || criteria.destination !== 'DME') {
    const empty = structuredClone(examples.empty.response);
    if (criteria.passengerCount > 1)
      empty.skippedProviders = [{ provider: 'travelpayouts', reasonCode: 'passenger-count-unsupported' }];
    return empty;
  }
  const roundTrip = criteria.returnDate !== null;
  const response = structuredClone(roundTrip ? examples.rankedRoundTrip.response : examples.rankedOneWay.response);
  response.offers[0].providerOfferRef = roundTrip
    ? `off_fixture_rt_${criteria.departureDate}_${criteria.returnDate}_p${criteria.passengerCount}`
    : `off_fixture_ow_${criteria.departureDate}_p${criteria.passengerCount}`;
  const bookable = response.offers[0];
  bookable.passengerCount = criteria.passengerCount;
  bookable.holdEligible = true;
  bookable.holdIneligibilityReason = null;
  bookable.totalAmount = totals[roundTrip ? 'roundTrip' : 'oneWay'].search[criteria.passengerCount - 1];
  if (criteria.passengerCount > 1) {
    response.offers = response.offers.filter((o) => o.provider === 'duffel');
    response.ranking.entries = response.ranking.entries.filter((e) => e.offerId === bookable.id);
    response.skippedProviders = [{ provider: 'travelpayouts', reasonCode: 'passenger-count-unsupported' }];
  }
  response.ranking.entries.find((entry) => entry.offerId === bookable.id).sourceAmount = bookable.totalAmount;
  for (const offer of response.offers) {
    for (const [sliceIndex, slice] of offer.itinerary.slices.entries()) {
      const targetDate = sliceIndex === 1 ? criteria.returnDate : criteria.departureDate;
      const anchor = sliceIndex === 1 ? '2030-06-17' : '2030-06-10';
      const days = dateDifference(targetDate, anchor);
      for (const segment of slice.segments) {
        segment.departAt = shiftTimestamp(segment.departAt, days);
        segment.arriveAt = shiftTimestamp(segment.arriveAt, days);
      }
    }
  }
  return response;
}

function buildDemoQuoteResponse(body) {
  if (body === null || typeof body !== 'object' || Array.isArray(body) || body.provider !== 'duffel') {
    return null;
  }
  if (
    body.passengerCount !== undefined &&
    (!Number.isInteger(body.passengerCount) || body.passengerCount < 1 || body.passengerCount > 9)
  )
    return problem(400, 'Flights.CommandInvalid');
  const oneWay = /^off_fixture_ow_(\d{4}-\d{2}-\d{2})(?:_p([1-9]))?$/.exec(body.providerOfferRef);
  const roundTrip = /^off_fixture_rt_(\d{4}-\d{2}-\d{2})_(\d{4}-\d{2}-\d{2})(?:_p([1-9]))?$/.exec(
    body.providerOfferRef,
  );
  if (!oneWay && !roundTrip) return null;
  const departureDate = (oneWay ?? roundTrip)[1];
  const returnDate = roundTrip?.[2] ?? null;
  if (!validDate(departureDate) || (returnDate !== null && (!validDate(returnDate) || returnDate < departureDate))) {
    return null;
  }
  const count = Number(oneWay?.[2] ?? roundTrip?.[3] ?? 1);
  if ((body.passengerCount ?? 1) !== count) return problem(409, 'Flights.PassengerCountMismatch');
  const selected = roundTrip ? booking.roundTrip : booking.oneWay;
  const aggregateId = body.aggregateId ?? null;
  const expectedId = demoAggregateId(body.providerOfferRef);
  if (aggregateId !== null && aggregateId !== expectedId) return null;

  const response = structuredClone(
    aggregateId !== null && oneWay && count === 1 ? booking.reQuoteChanged.response : selected.response,
  );
  const search = buildDemoSearchResponse({
    ...examples.oneWay.request,
    departureDate,
    returnDate,
    passengerCount: count,
  });
  response.offer.itinerary = search.offers[0].itinerary;
  response.offer.providerOfferRef = body.providerOfferRef;
  response.offer.expiresAt = demoExpiry(departureDate);
  response.offer.fetchedAt = `${new Date(Date.parse(response.offer.expiresAt) - 20 * 60_000).toISOString().slice(0, 19)}+00:00`;
  response.aggregateId = expectedId;
  response.offer.passengerCount = count;
  response.offer.holdEligible = true;
  response.offer.holdIneligibilityReason = null;
  if (!(aggregateId !== null && oneWay && count === 1))
    response.offer.totalAmount = totals[roundTrip ? 'roundTrip' : 'oneWay'].quote[count - 1];
  response.binding = {
    revision: randomUUID(),
    passengerCount: count,
    firstDepartureLocalDate: departureDate,
    slots: Array.from({ length: count }, (_, i) => ({
      bookingPassengerId: demoAggregateId(`${body.providerOfferRef}:passenger:${i}`),
      kind: 'adult',
    })),
  };
  return response;
}

function orderResponse(aggregateId, order, status = order.status) {
  return {
    aggregateId,
    status,
    totalAmount: order.offer.totalAmount,
    passengerCount: order.offer.passengerCount,
    currency: order.offer.currency,
    itinerary: order.offer.itinerary,
    ticketNumbers: status === 'Ticketed' ? order.ticketNumbers : [],
    bookedAt: order.bookedAt,
    ticketedAt: status === 'Ticketed' ? order.ticketedAt : null,
    cancelledAt: status === 'Cancelled' || status === 'Refunded' ? (order.cancelledAt ?? null) : null,
    refundedAt: null,
  };
}

function integerQueryParameter(searchParams, name, defaultValue) {
  const value = searchParams.get(name);
  if (value === null) return defaultValue;
  if (!/^[+-]?\d+$/.test(value)) throw new TypeError('Invalid integer query parameter');
  const number = Number(value);
  if (!Number.isInteger(number) || number < -2_147_483_648 || number > 2_147_483_647) {
    throw new TypeError('Invalid integer query parameter');
  }
  return number;
}

function sendProblem(response, status, code, passengerErrors) {
  sendJson(
    response,
    status,
    {
      status,
      type: `https://travel.local/errors/${code}`,
      title: 'Demo command rejected',
      ...(passengerErrors ? { passengerErrors } : {}),
    },
    'application/problem+json',
  );
}

function sendJson(response, status, body, contentType = 'application/json') {
  response.writeHead(status, { 'Content-Type': contentType, 'Cache-Control': 'no-store' });
  response.end(JSON.stringify(body));
}

async function readRaw(request) {
  let size = 0;
  const chunks = [];
  for await (const chunk of request) {
    size += chunk.length;
    if (size <= maxBodyBytes) chunks.push(chunk);
  }
  if (size > maxBodyBytes) throw new RangeError('Request body too large');
  return Buffer.concat(chunks).toString('utf8');
}

export function createDemoServer(options = {}) {
  const quotedOffers = new Map();
  const orders = new Map();
  const operations = new Map();
  return createServer(async (request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    if (request.method === 'GET' && url.pathname === '/') {
      response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
      response.end('Flights demo ready');
      return;
    }
    const orderGet = request.method === 'GET' && /^\/api\/flights\/orders\/([0-9a-f-]+)$/i.exec(url.pathname);
    const listRoute = request.method === 'GET' && url.pathname === '/api/flights/orders';
    const searchRoute = request.method === 'POST' && url.pathname === '/api/flights/search';
    const quoteRoute = request.method === 'POST' && url.pathname === '/api/flights/orders/quote';
    const holdRoute = request.method === 'POST' && url.pathname === '/api/flights/orders/hold';
    const confirmRoute = request.method === 'POST' && url.pathname === '/api/flights/orders/confirm';
    const cancelRoute =
      request.method === 'POST' &&
      /^\/api\/flights\/orders\/([0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})\/cancel$/i.exec(url.pathname);
    if (!searchRoute && !quoteRoute && !holdRoute && !confirmRoute && !cancelRoute && !orderGet && !listRoute) {
      sendJson(response, 404, { status: 404, title: 'Not Found' });
      return;
    }

    if (request.headers.authorization !== undefined) {
      sendJson(response, 400, { status: 400, title: 'DemoAuthRejected' }, 'application/problem+json');
      return;
    }

    if (cancelRoute) {
      const key = request.headers['idempotency-key'];
      if (!validIdempotencyKey(key)) {
        sendProblem(response, 400, 'Flights.IdempotencyKey.Missing');
        return;
      }
      const id = cancelRoute[1].toLowerCase();
      try {
        const raw = await readRaw(request);
        const identity = `${url.pathname}:${key}`;
        const previous = operations.get(identity);
        if (previous && previous.raw !== raw) {
          sendProblem(response, 409, 'Flights.IdempotencyConflict');
          return;
        }
        if (previous) {
          if (previous.body === null) sendProblem(response, 409, 'Flights.IdempotencyInFlight');
          else {
            response.setHeader('Idempotency-Replay', 'true');
            sendJson(response, 200, previous.body);
          }
          return;
        }
        if (!validGuid(id) || url.search !== '' || raw !== '') {
          sendProblem(response, 400, 'Flights.CommandInvalid');
          return;
        }
        const order = orders.get(id);
        if (!order) {
          sendProblem(response, 404, 'Flights.OfferNotFound');
          return;
        }
        if (order.status === 'Ticketed') {
          sendProblem(response, 409, 'Flights.OrderNotCancellable');
          return;
        }
        const operation = { raw, body: null };
        operations.set(identity, operation);
        if (options.cancelDelayMs) await new Promise((resolve) => setTimeout(resolve, options.cancelDelayMs));
        if (order.status !== 'Cancelled' && order.status !== 'Refunded') {
          order.statusBeforeCancel = order.status;
          order.status = 'Cancelled';
          order.cancelledAt = new Date().toISOString();
          order.cancelProjectionReads = options.cancelProjectionReads ?? 0;
        }
        operation.body = orderResponse(id, order);
        sendJson(response, 200, operation.body);
      } catch {
        if (!request.aborted && !response.destroyed) sendProblem(response, 400, 'Flights.CommandInvalid');
      }
      return;
    }

    if (listRoute) {
      try {
        const limit = Math.max(1, Math.min(200, integerQueryParameter(url.searchParams, 'limit', 50)));
        const offset = Math.max(0, integerQueryParameter(url.searchParams, 'offset', 0));
        const ordered = [...orders.entries()].sort(
          ([firstId, first], [secondId, second]) =>
            second.bookedAt.localeCompare(first.bookedAt) || secondId.localeCompare(firstId),
        );
        const items = ordered.slice(offset, offset + limit).map(([id, order]) => orderResponse(id, order));
        response.writeHead(200, {
          'Content-Type': 'application/json',
          'Cache-Control': 'no-store',
          'X-Travel-Demo': 'fixtures',
        });
        response.end(JSON.stringify({ items, limit, offset }));
      } catch (error) {
        if (!(error instanceof TypeError)) throw error;
        sendJson(response, 400, { status: 400, title: 'Validation' }, 'application/problem+json');
      }
      return;
    }

    if (orderGet) {
      const aggregateId = orderGet[1].toLowerCase();
      const order = validGuid(aggregateId) && url.search === '' ? orders.get(aggregateId) : null;
      if (!order) {
        sendJson(response, 404, { status: 404, title: 'Not Found' }, 'application/problem+json');
        return;
      }
      if (order.status === 'Confirmed') {
        order.confirmationReads++;
        if (order.confirmationReads === 1) {
          sendJson(response, 404, { status: 404, title: 'Not Found' }, 'application/problem+json');
          return;
        }
        if (order.confirmationReads >= 4) {
          order.status = 'Ticketed';
          order.ticketedAt = new Date().toISOString();
          order.ticketNumbers = [`DEMO-TKT-${aggregateId.slice(0, 8)}`];
        }
      }
      const staleCancel = order.status === 'Cancelled' && order.cancelProjectionReads > 0;
      if (staleCancel) order.cancelProjectionReads--;
      const status = staleCancel
        ? order.statusBeforeCancel
        : order.status === 'Confirmed' && order.confirmationReads === 2
          ? 'Held'
          : order.status;
      const body = orderResponse(aggregateId, order, status);
      response.writeHead(200, {
        'Content-Type': 'application/json',
        'Cache-Control': 'no-store',
        'X-Travel-Demo': 'fixtures',
      });
      response.end(JSON.stringify(body));
      return;
    }

    if ((holdRoute || confirmRoute) && !validIdempotencyKey(request.headers['idempotency-key'])) {
      sendJson(response, 400, { status: 400, title: 'IdempotencyKeyRequired' }, 'application/problem+json');
      return;
    }

    if (
      (searchRoute && url.searchParams.get('currency') !== 'RUB') ||
      ((quoteRoute || holdRoute || confirmRoute) && url.search !== '') ||
      !/^application\/json(?:\s*;|$)/i.test(request.headers['content-type'] ?? '')
    ) {
      sendJson(
        response,
        400,
        { status: 400, title: 'Validation', detail: 'Search request format is invalid.' },
        'application/problem+json',
      );
      return;
    }
    try {
      const rawBody = await readRaw(request);
      const identity = `${url.pathname}:${request.headers['idempotency-key']}`;
      const previous = holdRoute || confirmRoute ? operations.get(identity) : null;
      if (previous) {
        if (previous.raw !== rawBody) sendProblem(response, 409, 'Flights.IdempotencyConflict');
        else {
          response.setHeader('Idempotency-Replay', 'true');
          sendJson(response, 200, previous.body);
        }
        return;
      }
      const body = JSON.parse(rawBody);
      if (confirmRoute && orders.has(body.aggregateId) && orders.get(body.aggregateId).status !== 'Held') {
        sendProblem(response, 409, 'Flights.InvalidState');
        return;
      }
      if (holdRoute) {
        const rejection = validateParty(body, quotedOffers.get(body?.aggregateId));
        if (rejection) {
          sendProblem(response, rejection.status, rejection.code, rejection.passengerErrors);
          return;
        }
        if (orders.has(body.aggregateId)) {
          sendProblem(response, 409, 'Flights.InvalidState');
          return;
        }
      }
      const result = searchRoute
        ? buildDemoSearchResponse(body)
        : quoteRoute
          ? buildDemoQuoteResponse(body)
          : holdRoute
            ? buildDemoHoldResponse(body)
            : buildDemoConfirmResponse(body);
      if (result?.code) {
        sendProblem(response, result.status, result.code);
        return;
      }
      if (
        result === null ||
        (holdRoute && !quotedOffers.has(body.aggregateId)) ||
        (confirmRoute && !orders.has(body.aggregateId))
      ) {
        const status = searchRoute || quoteRoute ? 404 : 400;
        sendJson(
          response,
          status,
          { status, title: searchRoute || quoteRoute ? 'Offer unavailable' : 'Validation' },
          'application/problem+json',
        );
        return;
      }
      if (quoteRoute) quotedOffers.set(result.aggregateId, result);
      if (holdRoute) {
        orders.set(body.aggregateId, {
          offer: quotedOffers.get(body.aggregateId).offer,
          bookedAt: new Date().toISOString(),
          status: 'Held',
          confirmationReads: 0,
          ticketNumbers: [],
          ticketedAt: null,
        });
      }
      if (holdRoute) operations.set(identity, { raw: rawBody, body: structuredClone(result) });
      if (confirmRoute) {
        const order = orders.get(body.aggregateId);
        order.status = 'Confirmed';
        order.confirmationReads = 0;
        operations.set(identity, { raw: rawBody, body: structuredClone(result) });
      }
      response.writeHead(200, {
        'Content-Type': 'application/json',
        'Cache-Control': 'no-store',
        'X-Travel-Demo': 'fixtures',
      });
      response.end(JSON.stringify(result));
    } catch (error) {
      if (request.aborted || response.destroyed) return;
      if (error instanceof RangeError) {
        sendProblem(response, 413, 'Flights.RequestTooLarge');
        return;
      }
      if (error instanceof SyntaxError || error instanceof TypeError) {
        sendJson(
          response,
          400,
          { status: 400, title: 'Validation', detail: 'Search criteria are invalid.' },
          'application/problem+json',
        );
        return;
      }
      console.error('Flights demo request failed');
      sendJson(
        response,
        500,
        { status: 500, title: 'Failure', detail: 'Demo search failed.' },
        'application/problem+json',
      );
    }
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const server = createDemoServer();
  server.listen(5100, '127.0.0.1', () => {
    process.stdout.write('Flights demo API listening on http://127.0.0.1:5100\n');
  });
  process.on('SIGINT', () => server.close());
  process.on('SIGTERM', () => server.close());
}
