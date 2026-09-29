import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const examples = JSON.parse(readFileSync(new URL('../../tests/fixtures/flights-search.json', import.meta.url), 'utf8'));
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
    value.passengerCount === 1 &&
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

export function buildDemoSearchResponse(criteria) {
  if (!validCriteria(criteria)) throw new TypeError('Invalid demo search criteria');
  if (criteria.origin !== 'LED' || criteria.destination !== 'DME') {
    return structuredClone(examples.empty.response);
  }
  const roundTrip = criteria.returnDate !== null;
  const response = structuredClone(roundTrip ? examples.roundTrip.response : examples.oneWay.response);
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

function sendJson(response, status, body, contentType = 'application/json') {
  response.writeHead(status, { 'Content-Type': contentType, 'Cache-Control': 'no-store' });
  response.end(JSON.stringify(body));
}

async function readJson(request) {
  let size = 0;
  const chunks = [];
  for await (const chunk of request) {
    size += chunk.length;
    if (size > maxBodyBytes) throw new TypeError('Request body too large');
    chunks.push(chunk);
  }
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

export function createDemoServer() {
  return createServer(async (request, response) => {
    const url = new URL(request.url ?? '/', 'http://127.0.0.1');
    if (request.method === 'GET' && url.pathname === '/') {
      response.writeHead(200, { 'Content-Type': 'text/plain; charset=utf-8' });
      response.end('Flights demo ready');
      return;
    }
    if (request.method !== 'POST' || url.pathname !== '/api/flights/search') {
      sendJson(response, 404, { status: 404, title: 'Not Found' });
      return;
    }
    if (
      url.searchParams.get('currency') !== 'RUB' ||
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
      const criteria = await readJson(request);
      const result = buildDemoSearchResponse(criteria);
      response.writeHead(200, {
        'Content-Type': 'application/json',
        'Cache-Control': 'no-store',
        'X-Travel-Demo': 'fixtures',
      });
      response.end(JSON.stringify(result));
    } catch (error) {
      if (request.aborted || response.destroyed) return;
      if (error instanceof SyntaxError || error instanceof TypeError) {
        sendJson(
          response,
          400,
          { status: 400, title: 'Validation', detail: 'Search criteria are invalid.' },
          'application/problem+json',
        );
        return;
      }
      console.error('Flights demo search failed', error);
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
