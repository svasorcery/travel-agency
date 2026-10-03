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

function validLegCriteria(value) {
  return (
    value !== null &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    Object.keys(value).length === 3 &&
    ['legs', 'passengerCount', 'cabinClass'].every((key) => Object.hasOwn(value, key)) &&
    Number.isInteger(value.passengerCount) &&
    value.passengerCount >= 1 &&
    value.passengerCount <= 9 &&
    value.cabinClass === 'economy' &&
    Array.isArray(value.legs) &&
    value.legs.length >= 1 &&
    value.legs.length <= 4 &&
    value.legs.every(
      (leg, index) =>
        leg !== null &&
        typeof leg === 'object' &&
        !Array.isArray(leg) &&
        Object.keys(leg).length === 3 &&
        ['origin', 'destination', 'departureDate'].every((key) => Object.hasOwn(leg, key)) &&
        typeof leg.origin === 'string' &&
        typeof leg.destination === 'string' &&
        /^[A-Z]{3}$/.test(leg.origin) &&
        /^[A-Z]{3}$/.test(leg.destination) &&
        leg.origin !== leg.destination &&
        !['MOW', 'LON', 'NYC'].includes(leg.origin) &&
        !['MOW', 'LON', 'NYC'].includes(leg.destination) &&
        validDate(leg.departureDate) &&
        leg.departureDate > '0001-01-01' &&
        (index === 0 || leg.departureDate >= value.legs[index - 1].departureDate),
    )
  );
}

// Syntax is validated by JSON.parse first. Walk bounded JSON tokens iteratively so string values
// are never mistaken for keys, escaped names are decoded, and each object has its own key set.
// Case-folded duplicates also refuse ambiguous aliases of the backend's case-insensitive binder.
function refuseDuplicateJsonMembers(raw) {
  const containers = [];
  for (let cursor = 0; cursor < raw.length; cursor++) {
    const token = raw[cursor];
    if (token === '{') containers.push({ keys: new Set(), expectsKey: true });
    else if (token === '[') containers.push(null);
    else if (token === '}' || token === ']') containers.pop();
    else if (token === ',') {
      const object = containers.at(-1);
      if (object) object.expectsKey = true;
    } else if (token === '"') {
      const start = cursor;
      cursor++;
      while (raw[cursor] !== '"') {
        if (raw[cursor] === '\\') cursor += 2;
        else cursor++;
      }
      const object = containers.at(-1);
      if (object?.expectsKey) {
        const key = JSON.parse(raw.slice(start, cursor + 1)).toLowerCase();
        if (object.keys.has(key)) throw new TypeError('Duplicate demo JSON member');
        object.keys.add(key);
        object.expectsKey = false;
      }
    }
  }
}

// Each reference contains the complete ordered fictional itinerary. There is no split-ticket path.
function legReference(criteria) {
  return `off_fixture_v2_${criteria.legs.map((l) => `${l.origin}-${l.destination}-${l.departureDate}`).join('_')}_p${criteria.passengerCount}`;
}
function criteriaFromLegReference(reference) {
  if (typeof reference !== 'string') return null;
  const match =
    /^off_fixture_v2_((?:[A-Z]{3}-[A-Z]{3}-\d{4}-\d{2}-\d{2}_){0,3}[A-Z]{3}-[A-Z]{3}-\d{4}-\d{2}-\d{2})_p([1-9])$/.exec(
      reference,
    );
  if (!match) return null;
  const criteria = {
    legs: match[1]
      .split('_')
      .map((l) => ({ origin: l.slice(0, 3), destination: l.slice(4, 7), departureDate: l.slice(8) })),
    passengerCount: Number(match[2]),
    cabinClass: 'economy',
  };
  return validLegCriteria(criteria) ? criteria : null;
}
const fictionalAirports = new Set(['LED', 'DME', 'VKO', 'SVO', 'KZN']);
const multiLegTotals = {
  3: {
    search: [29500, 58250, 86800, 115100, 143300, 171300, 199200, 227000, 254700],
    quote: [30100, 59620, 88890, 117880, 146760, 175450, 204010, 232490, 260860],
  },
  4: {
    search: [39200, 77300, 115100, 152700, 190100, 227300, 264300, 301100, 337700],
    quote: [40000, 78920, 117550, 155970, 194180, 232160, 269970, 307520, 344910],
  },
};
function legTotal(criteria, phase) {
  const prices =
    criteria.legs.length > 2
      ? multiLegTotals[criteria.legs.length]
      : totals[criteria.legs.length === 2 ? 'roundTrip' : 'oneWay'];
  return prices[phase][criteria.passengerCount - 1];
}
function buildLegSearchResponse(criteria) {
  if (!validLegCriteria(criteria)) throw new TypeError('Invalid demo leg search criteria');
  const skippedProviders = [{ provider: 'travelpayouts', reasonCode: 'journey-unsupported' }];
  if (criteria.legs.some((l) => !fictionalAirports.has(l.origin) || !fictionalAirports.has(l.destination)))
    return { ...structuredClone(examples.empty.response), skippedProviders };
  const response = structuredClone(examples.rankedOneWay.response);
  const offer = response.offers[0];
  response.offers = [offer];
  const roundTrip =
    criteria.legs.length === 2 &&
    criteria.legs[0].origin === criteria.legs[1].destination &&
    criteria.legs[0].destination === criteria.legs[1].origin;
  offer.providerOfferRef = legReference(criteria);
  offer.passengerCount = criteria.passengerCount;
  offer.totalAmount = legTotal(criteria, 'search');
  offer.holdEligible = true;
  offer.holdIneligibilityReason = null;
  offer.itinerary = {
    journeyKind: criteria.legs.length === 1 ? 'one-way' : roundTrip ? 'round-trip' : 'multi-leg',
    isRoundTrip: roundTrip,
    totalDuration: `${String(criteria.legs.length * 2).padStart(2, '0')}:00:00`,
    slices: criteria.legs.map((leg, index) => ({
      origin: leg.origin,
      destination: leg.destination,
      duration: '02:00:00',
      segments: [
        {
          origin: leg.origin,
          destination: leg.destination,
          departAt: `${leg.departureDate}T${String(10 + index * 3).padStart(2, '0')}:00:00+03:00`,
          arriveAt: `${leg.departureDate}T${String(12 + index * 3).padStart(2, '0')}:00:00+03:00`,
          carrierCode: 'SU',
          flightNumber: `SU${101 + index}`,
          cabinClass: 'economy',
        },
      ],
    })),
  };
  response.skippedProviders = skippedProviders;
  response.ranking.entries = [response.ranking.entries[0]];
  Object.assign(response.ranking.entries[0], {
    sourceAmount: offer.totalAmount,
    durationSeconds: criteria.legs.length * 7200,
    transfers: 0,
  });
  return response;
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
  if (criteria && Object.hasOwn(criteria, 'legs')) return buildLegSearchResponse(criteria);
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
  const legCriteria = criteriaFromLegReference(body.providerOfferRef);
  if (legCriteria) {
    if ((body.passengerCount ?? 1) !== legCriteria.passengerCount)
      return problem(409, 'Flights.PassengerCountMismatch');
    const expectedId = demoAggregateId(body.providerOfferRef);
    if (body.aggregateId != null && body.aggregateId !== expectedId) return null;
    const offer = buildLegSearchResponse(legCriteria).offers[0];
    if (!offer) return null;
    offer.totalAmount = legTotal(legCriteria, 'quote');
    offer.expiresAt = demoExpiry(legCriteria.legs[0].departureDate);
    offer.fetchedAt = `${new Date(Date.parse(offer.expiresAt) - 20 * 60_000).toISOString().slice(0, 19)}+00:00`;
    return {
      ...structuredClone(booking.oneWay.response),
      aggregateId: expectedId,
      offer,
      binding: {
        revision: randomUUID(),
        passengerCount: legCriteria.passengerCount,
        firstDepartureLocalDate: legCriteria.legs[0].departureDate,
        slots: Array.from({ length: legCriteria.passengerCount }, (_, i) => ({
          bookingPassengerId: demoAggregateId(`${body.providerOfferRef}:passenger:${i}`),
          kind: 'adult',
        })),
      },
    };
  }
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

const travelerFields = ['title', 'givenName', 'familyName', 'dateOfBirth', 'gender', 'email', 'phone'];
function travelerValidation(details, today) {
  if (
    !details ||
    typeof details !== 'object' ||
    Array.isArray(details) ||
    Object.keys(details).length !== 7 ||
    Object.keys(details).some((key) => !travelerFields.includes(key))
  )
    return [];
  const errors = [];
  const add = (field, code) => errors.push({ field, code: `Flights.Passenger${code}Invalid` });
  if (!['mr', 'ms', 'mrs', 'miss', 'dr'].includes(details.title)) add('title', 'Title');
  for (const field of ['givenName', 'familyName']) {
    const value = details[field];
    if (
      typeof value !== 'string' ||
      value.length < 1 ||
      value.length > 20 ||
      !/^[A-Za-z\u00c0-\u017f '-]+$/.test(value) ||
      /[ÆæĲĳŒœÞð×÷]/.test(value) ||
      !/[A-Za-z\u00c0-\u017f]/.test(value)
    )
      add(field, field === 'givenName' ? 'GivenName' : 'FamilyName');
  }
  if (!['male', 'female'].includes(details.gender)) add('gender', 'Gender');
  if (!validDate(details.dateOfBirth) || details.dateOfBirth <= '0001-01-01') add('dateOfBirth', 'DateOfBirth');
  else if (details.dateOfBirth > today) add('dateOfBirth', 'DateOfBirthFuture');
  if (
    typeof details.email !== 'string' ||
    details.email.length > 254 ||
    !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(details.email)
  )
    add('email', 'Email');
  if (typeof details.phone !== 'string' || !/^\+[1-9][0-9]{7,14}$/.test(details.phone)) add('phone', 'Phone');
  return errors.length ? errors : null;
}
function travelerPrecondition(request, method) {
  const match = request.headers['if-match'];
  const none = request.headers['if-none-match'];
  if (match === undefined && none === undefined) return { status: 428, code: 'Flights.TravelerPreconditionRequired' };
  if (
    (match !== undefined && none !== undefined) ||
    (method === 'DELETE' && none !== undefined) ||
    (none !== undefined && none !== '*') ||
    (match !== undefined &&
      (typeof match !== 'string' ||
        !/^"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}"$/i.test(match) ||
        !validGuid(match.slice(1, -1))))
  )
    return { status: 400, code: 'Flights.TravelerPreconditionInvalid' };
  return { create: none === '*', revision: match?.slice(1, -1).toLowerCase() };
}
async function handleTraveler(request, response, url, travelers, options) {
  const reject = (status, code, fieldErrors) =>
    sendJson(
      response,
      status,
      {
        status,
        type: `https://travel.local/errors/${code}`,
        title: 'Demo traveler request rejected',
        ...(fieldErrors?.length ? { fieldErrors } : {}),
      },
      'application/problem+json',
    );
  response.setHeader('Cache-Control', 'no-store');
  response.setHeader('X-Travel-Demo', 'fixtures');
  if (request.headers.authorization !== undefined) return reject(400, 'Flights.DemoAuthRejected');
  const owner = request.headers['x-travel-demo-owner'] ?? 'demo-only';
  if (owner !== 'demo-only' && owner !== 'demo-other') return reject(401, 'Flights.TravelerIdentityInvalid');
  const route = /^\/api\/flights\/travelers(?:\/([^/]+))?\/?$/i.exec(url.pathname);
  if (!route) return reject(404, 'Flights.TravelerNotFound');
  const id = route[1]?.toLowerCase();
  if (id !== undefined && !validGuid(id)) return reject(404, 'Flights.TravelerNotFound');
  const method = request.method;
  if (method === 'GET' && id === undefined) {
    const values = url.searchParams.getAll('offset');
    const raw = values[0] ?? '0';
    const offset = Number(raw);
    if (
      [...url.searchParams.keys()].some((key) => key !== 'offset') ||
      values.length > 1 ||
      !/^(0|[1-9][0-9]*)$/.test(raw) ||
      !Number.isSafeInteger(offset) ||
      offset > 2147483640 ||
      offset % 20 !== 0
    )
      return reject(400, 'Flights.TravelerPageInvalid');
    if (options.travelerKeysUnavailable) return reject(503, 'Flights.PiiProtectionUnavailable');
    const read = [...travelers.values()]
      .filter((r) => r.owner === owner)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))
      .slice(offset, offset + 21);
    return sendJson(response, 200, {
      items: read.slice(0, 20).map(({ id, revision, details }) => ({ id, revision, details })),
      offset,
      hasMore: read.length > 20,
    });
  }
  if (id === undefined || url.search !== '') return reject(404, 'Flights.TravelerNotFound');
  if (method === 'GET') {
    const existing = travelers.get(id);
    if (!existing || existing.owner !== owner) return reject(404, 'Flights.TravelerNotFound');
    if (options.travelerKeysUnavailable) return reject(503, 'Flights.PiiProtectionUnavailable');
    response.setHeader('ETag', `"${existing.revision}"`);
    return sendJson(response, 200, { id, revision: existing.revision, details: existing.details });
  }
  if (method !== 'PUT' && method !== 'DELETE') return reject(404, 'Flights.TravelerNotFound');
  try {
    // Drain within a fixed in-memory cap before interpreting the command or mutating any row.
    const raw = await readRaw(request);
    if (method === 'DELETE' && raw !== '') return reject(400, 'Flights.TravelerInvalid');
    const condition = travelerPrecondition(request, method);
    if (condition.status) return reject(condition.status, condition.code);
    let details;
    if (method === 'PUT') {
      if (!/^application\/json(?:\s*;|$)/i.test(request.headers['content-type'] ?? ''))
        return reject(400, 'Flights.TravelerInvalid');
      details = JSON.parse(raw);
      const errors = travelerValidation(details, (options.now?.() ?? new Date()).toISOString().slice(0, 10));
      if (errors !== null) return reject(400, 'Flights.TravelerInvalid', errors);
    }
    const existing = travelers.get(id);
    if (existing?.owner !== undefined && existing.owner !== owner) return reject(404, 'Flights.TravelerNotFound');
    if (condition.create && existing) return reject(412, 'Flights.TravelerPreconditionFailed');
    if (!condition.create && !existing) return reject(404, 'Flights.TravelerNotFound');
    if (!condition.create && existing.revision !== condition.revision)
      return reject(412, 'Flights.TravelerPreconditionFailed');
    if (method === 'DELETE') {
      travelers.delete(id);
      response.writeHead(204);
      response.end();
      return;
    }
    if (options.travelerKeysUnavailable) return reject(503, 'Flights.PiiProtectionUnavailable');
    const revision = randomUUID();
    // Fictional plain in-memory details only. This stub does not establish cryptographic acceptance.
    travelers.set(id, {
      id,
      owner,
      revision,
      details: structuredClone(details),
      createdAt: existing?.createdAt ?? (options.now?.() ?? new Date()).toISOString(),
    });
    response.setHeader('ETag', `"${revision}"`);
    return sendJson(response, condition.create ? 201 : 200, { id, revision });
  } catch (error) {
    if (request.aborted || response.destroyed) return;
    return reject(
      error instanceof RangeError ? 413 : 400,
      error instanceof RangeError ? 'Flights.RequestTooLarge' : 'Flights.TravelerInvalid',
    );
  }
}

export function createDemoServer(options = {}) {
  const quotedOffers = new Map();
  const orders = new Map();
  const operations = new Map();
  const travelers = new Map();
  return createServer(async (request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    if (/^\/api\/flights\/travelers(?:\/|$)/i.test(url.pathname)) {
      await handleTraveler(request, response, url, travelers, options);
      return;
    }
    if (request.method === 'GET' && url.pathname === '/') {
      response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
      response.end('Flights demo ready');
      return;
    }
    const orderGet = request.method === 'GET' && /^\/api\/flights\/orders\/([0-9a-f-]+)$/i.exec(url.pathname);
    const listRoute = request.method === 'GET' && url.pathname === '/api/flights/orders';
    const legSearchRoute = request.method === 'POST' && url.pathname === '/api/flights/search/v2';
    const searchRoute = request.method === 'POST' && (url.pathname === '/api/flights/search' || legSearchRoute);
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
      (legSearchRoute && url.search !== '?currency=RUB') ||
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
      if (legSearchRoute) refuseDuplicateJsonMembers(rawBody);
      if (searchRoute && (legSearchRoute ? !validLegCriteria(body) : !validCriteria(body)))
        throw new TypeError('Invalid demo search version');
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
