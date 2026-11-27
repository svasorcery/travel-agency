import {
  parseTripsJson,
  TripsContractError,
  tripBound,
  tripBrief,
  tripDate,
  tripDates,
  tripGuid,
  tripMinor,
  tripMoney,
  tripRecord,
  tripText,
} from './trips.decoder';
import { type TripBrief, tripCurrencies, tripInterests } from './trips.types';
import { tripItineraryReleases } from './trips-itinerary.releases';

function fail(): never {
  throw new TripsContractError();
}
function oneOf<T extends string>(value: unknown, values: readonly T[]): T {
  return typeof value === 'string' && values.includes(value as T) ? (value as T) : fail();
}
function list<T>(value: unknown, limit: number, decode: (item: unknown) => T): T[] {
  if (!Array.isArray(value) || value.length > limit) return fail();
  return value.map(decode);
}
function number(value: unknown, max: number, min = 0): number {
  return typeof value === 'number' && Number.isInteger(value) && value <= max && value >= min ? value : fail();
}
function flag(value: unknown): boolean {
  return typeof value === 'boolean' ? value : fail();
}
function instant(value: unknown, utc = false): string {
  const text = tripText(value, 40);
  if (
    !/^\d{4}-\d\d-\d\dT(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-]\d\d:\d\d)$/.test(text) ||
    !Number.isFinite(Date.parse(text)) ||
    (utc && !/(?:Z|\+00:00)$/.test(text))
  )
    return fail();
  tripDate(text.slice(0, 10));
  return text;
}
function time(value: unknown): string {
  return typeof value === 'string' && /^(?:[01]\d|2[0-3]):[0-5]\d$/.test(value) ? value : fail();
}
export function decodeTripOptionAvailability(value: unknown) {
  const v = tripRecord(value, ['profileId', 'asOfUtc', 'startDate', 'endDate', 'dataMode']);
  const dates = tripDates(tripDate(v['startDate']), tripDate(v['endDate']));
  if (dates.length < 2 || dates.length > 3) return fail();
  return {
    profileId: oneOf(v['profileId'], ['spb-option'] as const),
    dataMode: oneOf(v['dataMode'], ['fictional'] as const),
    asOfUtc: instant(v['asOfUtc'], true),
    startDate: dates[0],
    endDate: dates[dates.length - 1],
  };
}
export type TripOptionAvailability = ReturnType<typeof decodeTripOptionAvailability>;
const unsupportedReasons = [
  'directionUnsupported',
  'dateRangeUnsupported',
  'groupUnsupported',
  'horizonUnsupported',
] as const;
const noMatchReasons = [
  'flightUnavailable',
  'hotelUnavailable',
  'timeIncompatible',
  'budgetExceeded',
  'noWalks',
] as const;
const selectionReasons = [
  'onlyCompatibleOption',
  'moreInterests',
  'moreStops',
  'completeFlightAndStay',
  'lowerComparablePrice',
  'equalByStableIds',
] as const;

type OptionFormat = 'preview' | 'historicalV1';
function leg(value: unknown, format: OptionFormat) {
  const v = tripRecord(value, ['fromAirport', 'toAirport', 'departsAt', 'arrivesAt']);
  const airport = (a: unknown) => (typeof a === 'string' && /^[A-Z]{3}$/.test(a) ? a : fail());
  const departsAt = instant(v['departsAt']),
    arrivesAt = instant(v['arrivesAt']);
  if (format === 'preview' && Date.parse(departsAt) >= Date.parse(arrivesAt)) return fail();
  return { fromAirport: airport(v['fromAirport']), toAirport: airport(v['toAirport']), departsAt, arrivesAt };
}
function stop(value: unknown) {
  const v = tripRecord(value, [
    'id',
    'title',
    'start',
    'end',
    'dwellMinutes',
    'incomingEdgeId',
    'walkFromPreviousMinutes',
    'accessHoursKnown',
  ]);
  return {
    id: tripText(v['id'], 100),
    title: tripText(v['title'], 200),
    start: time(v['start']),
    end: time(v['end']),
    dwellMinutes: number(v['dwellMinutes'], 1440, 1),
    incomingEdgeId: v['incomingEdgeId'] === null ? null : tripText(v['incomingEdgeId'], 200),
    walkFromPreviousMinutes: number(v['walkFromPreviousMinutes'], 1440),
    accessHoursKnown: flag(v['accessHoursKnown']),
  };
}
function day(value: unknown) {
  const v = tripRecord(value, ['date', 'windowStart', 'windowEnd', 'stops', 'programmeMinutes', 'emptyReason']);
  const stops = list(v['stops'], 16, stop);
  const windowStart = v['windowStart'] === null ? null : time(v['windowStart']);
  const windowEnd = v['windowEnd'] === null ? null : time(v['windowEnd']);
  const emptyReason =
    v['emptyReason'] === null ? null : oneOf(v['emptyReason'], ['noWalkingWindow', 'noEligibleStops'] as const);
  if (
    (windowStart === null) !== (windowEnd === null) ||
    (stops.length === 0) !== (emptyReason !== null) ||
    (stops.length > 0 && windowStart === null)
  )
    return fail();
  return {
    date: tripDate(v['date']),
    windowStart,
    windowEnd,
    stops,
    programmeMinutes: number(v['programmeMinutes'], 1440),
    emptyReason,
  };
}
function priceLine(value: unknown, format: OptionFormat) {
  const v = tripRecord(value, ['component', 'kind', 'amount', 'currency']);
  return {
    component: oneOf(v['component'], ['flightBase', 'hotelBase', 'includedTax', 'localFee'] as const),
    kind: oneOf(v['kind'], ['travelerMandatory', 'included', 'unknownMandatory', 'supplierNet'] as const),
    amount: v['amount'] === null ? null : tripMoney(v['amount'], format === 'preview'),
    currency: v['currency'] === null ? null : oneOf(v['currency'], tripCurrencies),
  };
}
function tripOption(value: unknown, brief: TripBrief, format: OptionFormat) {
  const v = tripRecord(value, [
    'flight',
    'hotel',
    'days',
    'price',
    'coveredInterests',
    'unmetInterests',
    'selectionReason',
    'arrivalTransferMinutes',
    'departureTransferMinutes',
    'hotelToWalkMinutes',
    'walkToHotelMinutes',
  ]);
  const f = tripRecord(v['flight'], ['id', 'adults', 'outbound', 'inbound']);
  const h = tripRecord(v['hotel'], ['id', 'name', 'adults', 'nights', 'checkInAt', 'checkOutAt']);
  const p = tripRecord(v['price'], [
    'currency',
    'knownSubtotal',
    'flightAndStayComplete',
    'budgetStatus',
    'components',
    'unpricedExpenses',
  ]);
  const dates = tripDates(brief.startDate, brief.endDate);
  const days = list(v['days'], 3, day);
  const knownSubtotal = tripMoney(p['knownSubtotal'], true);
  if (
    days.length !== dates.length ||
    days.some((d, i) => d.date !== dates[i]) ||
    h['adults'] !== brief.adults ||
    f['adults'] !== brief.adults ||
    h['nights'] !== days.length - 1 ||
    p['currency'] !== brief.currency ||
    (format === 'historicalV1' && p['knownSubtotal'] !== knownSubtotal)
  )
    return fail();
  const interests = (a: unknown) => list(a, tripInterests.length, (i) => oneOf(i, tripInterests));
  return {
    flight: {
      id: tripText(f['id'], 64),
      adults: number(f['adults'], 2, 1),
      outbound: leg(f['outbound'], format),
      inbound: leg(f['inbound'], format),
    },
    hotel: {
      id: tripText(h['id'], 64),
      name: tripText(h['name'], 200),
      adults: number(h['adults'], 2, 1),
      nights: number(h['nights'], 2, 1),
      checkInAt: instant(h['checkInAt']),
      checkOutAt: instant(h['checkOutAt']),
    },
    days,
    coveredInterests: interests(v['coveredInterests']),
    unmetInterests: interests(v['unmetInterests']),
    selectionReason: oneOf(v['selectionReason'], selectionReasons),
    arrivalTransferMinutes: number(v['arrivalTransferMinutes'], 1440),
    departureTransferMinutes: number(v['departureTransferMinutes'], 1440),
    hotelToWalkMinutes: number(v['hotelToWalkMinutes'], 1440),
    walkToHotelMinutes: number(v['walkToHotelMinutes'], 1440),
    price: {
      currency: oneOf(p['currency'], tripCurrencies),
      knownSubtotal,
      flightAndStayComplete: flag(p['flightAndStayComplete']),
      budgetStatus: oneOf(p['budgetStatus'], ['notSpecified', 'indeterminate'] as const),
      components: list(p['components'], 32, (line) => priceLine(line, format)),
      unpricedExpenses: list(p['unpricedExpenses'], 8, (e) => oneOf(e, ['meals', 'transfers'] as const)),
    },
  };
}
function previewFacts(value: unknown, brief: TripBrief, format: OptionFormat) {
  const v = tripRecord(value, [
    'outcome',
    'reasons',
    'catalogueVersion',
    'policyVersion',
    'asOfUtc',
    'datasetId',
    'datasetVersion',
    'datasetHash',
    'attribution',
    'dataUrl',
    'option',
  ]);
  const datasetVersion = tripText(v['datasetVersion'], 32);
  const datasetId = oneOf(v['datasetId'], ['saint-petersburg-outdoor'] as const);
  const datasetHash = v['datasetHash'];
  const dataUrl = `/trips-data/${datasetId}/${datasetVersion}/saint-petersburg-v${datasetVersion}.manifest.json`;
  if (
    !/^[1-9]\d*$/.test(datasetVersion) ||
    typeof datasetHash !== 'string' ||
    !/^[0-9a-f]{64}$/.test(datasetHash) ||
    v['dataUrl'] !== dataUrl
  )
    return fail();
  const meta = {
    asOfUtc: instant(v['asOfUtc'], true),
    catalogueVersion: oneOf(v['catalogueVersion'], ['A', 'B'] as const),
    policyVersion: tripText(v['policyVersion'], 100),
    attribution: tripText(v['attribution'], 1024),
    dataUrl,
    datasetId,
    datasetVersion,
    datasetHash,
  };
  const outcome = oneOf(v['outcome'], ['option', 'noMatch', 'unsupported'] as const);
  if (outcome === 'option') {
    if (!Array.isArray(v['reasons']) || v['reasons'].length > 0) return fail();
    return { ...meta, outcome, option: tripOption(v['option'], brief, format), reasons: [] };
  }
  const reasons = list(v['reasons'], 8, (r) =>
    outcome === 'unsupported' ? oneOf(r, unsupportedReasons) : oneOf(r, noMatchReasons),
  );
  if (v['option'] !== null || !reasons.length) return fail();
  return { ...meta, outcome, option: null, reasons };
}
export function decodeTripOptionPreview(value: unknown, brief: TripBrief, discovery: TripOptionAvailability) {
  const preview = previewFacts(value, brief, 'preview');
  const release = tripItineraryReleases['saint-petersburg'];
  if (
    Date.parse(preview.asOfUtc) !== Date.parse(discovery.asOfUtc) ||
    preview.datasetId !== release.datasetId ||
    preview.datasetVersion !== release.version ||
    preview.datasetHash !== release.hash ||
    preview.dataUrl !== release.offer
  )
    return fail();
  return preview;
}
export type TripOptionPreview = ReturnType<typeof decodeTripOptionPreview>;

export interface TripOptionSelection {
  tripId: string;
  expiresAtUtc: string;
  selectionToken: string;
}
export function decodeOptionEnvelope(raw: string, brief: TripBrief, discovery: TripOptionAvailability) {
  const value = parseTripsJson(raw, 128 * 1024);
  if (!value || typeof value !== 'object' || !Object.hasOwn(value, 'preview')) {
    if (new TextEncoder().encode(raw).length > 32 * 1024) return fail();
    return { preview: decodeTripOptionPreview(value, brief, discovery), selection: null };
  }
  const envelope = tripRecord(value, ['preview', 'selection']);
  tripBound(envelope['preview'], 32 * 1024);
  const preview = decodeTripOptionPreview(envelope['preview'], brief, discovery);
  if (envelope['selection'] === null) return { preview, selection: null };
  const s = tripRecord(envelope['selection'], ['tripId', 'expiresAtUtc', 'selectionToken']);
  const token = s['selectionToken'];
  if (
    !preview.option ||
    typeof token !== 'string' ||
    !token.length ||
    token.length > 64 * 1024 ||
    /[^\x21-\x7e]/.test(token)
  )
    return fail();
  return {
    preview,
    selection: { tripId: tripGuid(s['tripId']), expiresAtUtc: instant(s['expiresAtUtc'], true), selectionToken: token },
  };
}
export function decodeSavedTripOption(value: unknown) {
  const wrapper = tripRecord(value, ['savedOption']);
  if (wrapper['savedOption'] === null) return null;
  const s = tripRecord(wrapper['savedOption'], ['version', 'brief', 'preview', 'savedAtUtc']);
  tripBound(s, 40 * 1024);
  if (s['version'] !== 1) return fail();
  const brief = tripBrief(s['brief']);
  if (brief.originId !== 'moscow' || brief.destinationId !== 'saint-petersburg') return fail();
  const preview = previewFacts(s['preview'], brief, 'historicalV1');
  const savedAtUtc = instant(s['savedAtUtc'], true);
  if (
    historicalTicks(savedAtUtc) === firstHistoricalTick ||
    !historicalId(preview.policyVersion) ||
    !preview.option ||
    !validHistoricalOption(preview.option, brief)
  )
    return fail();
  return { version: 1 as const, brief, preview, savedAtUtc };
}
export type SavedTripOption = NonNullable<ReturnType<typeof decodeSavedTripOption>>;

type HistoricalOption = NonNullable<TripOptionPreview['option']>;
const historicalId = (value: string) => /^[A-Za-z0-9_-]+$/.test(value);
const firstHistoricalTick = BigInt(Date.parse('0001-01-01T00:00:00Z')) * 10000n;
const lastHistoricalTick = BigInt(Date.parse('9999-12-31T23:59:59.999Z')) * 10000n + 9999n;

// DateTimeOffset preserves seven fractional digits and allows offsets through ±14:00.
function historicalTicks(iso: string): bigint {
  if (!iso.endsWith('Z') && iso.slice(-5) > '14:00') return fail();
  const fraction = iso.match(/\.(\d+)/)?.[1] ?? '';
  const ticks = BigInt(Date.parse(iso)) * 10000n + BigInt(fraction.padEnd(7, '0').slice(3));
  return ticks >= firstHistoricalTick && ticks <= lastHistoricalTick ? ticks : fail();
}

function validHistoricalOption(option: HistoricalOption, brief: TripBrief): boolean {
  const { flight, hotel } = option;
  const { outbound, inbound } = flight;
  const displayed = [...option.coveredInterests, ...option.unmetInterests];
  return (
    historicalId(flight.id) &&
    historicalId(hotel.id) &&
    outbound.fromAirport === 'SVO' &&
    outbound.toAirport === 'LED' &&
    inbound.fromAirport === 'LED' &&
    inbound.toAirport === 'SVO' &&
    [outbound.departsAt, outbound.arrivesAt, hotel.checkInAt].every((iso) => iso.slice(0, 10) === brief.startDate) &&
    [inbound.departsAt, inbound.arrivesAt, hotel.checkOutAt].every((iso) => iso.slice(0, 10) === brief.endDate) &&
    historicalTicks(outbound.departsAt) < historicalTicks(outbound.arrivesAt) &&
    historicalTicks(outbound.arrivesAt) <= historicalTicks(hotel.checkInAt) &&
    historicalTicks(hotel.checkInAt) < historicalTicks(hotel.checkOutAt) &&
    historicalTicks(hotel.checkOutAt) <= historicalTicks(inbound.departsAt) &&
    historicalTicks(inbound.departsAt) < historicalTicks(inbound.arrivesAt) &&
    displayed.length === brief.interests.length &&
    brief.interests.every((interest) => displayed.includes(interest)) &&
    validHistoricalPrice(option.price, brief) &&
    validHistoricalDays(option.days)
  );
}

function validHistoricalPrice(price: HistoricalOption['price'], brief: TripBrief): boolean {
  let subtotal = 0n;
  const comparableBases = new Set<string>();
  let missingMandatory = false;
  for (const line of price.components) {
    if (
      (line.currency === null) !== (line.amount === null) ||
      (line.kind === 'unknownMandatory' && line.amount !== null)
    )
      return false;
    if (line.kind === 'travelerMandatory' && line.currency === brief.currency && line.amount !== null) {
      subtotal += tripMinor(line.amount);
      comparableBases.add(line.component);
    } else if (line.kind === 'travelerMandatory' || line.kind === 'unknownMandatory') {
      missingMandatory = true;
    }
  }
  const complete = !missingMandatory && comparableBases.has('flightBase') && comparableBases.has('hotelBase');
  return (
    price.components.length > 0 &&
    price.flightAndStayComplete === complete &&
    subtotal === tripMinor(price.knownSubtotal) &&
    price.budgetStatus === (brief.totalBudget === null ? 'notSpecified' : 'indeterminate') &&
    new Set(price.unpricedExpenses).size === price.unpricedExpenses.length
  );
}

function validHistoricalDays(days: HistoricalOption['days']): boolean {
  const visited = new Set<string>();
  const minute = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3));
  for (const day of days) {
    if (
      day.stops.length > 4 ||
      (day.windowStart !== null && day.windowEnd !== null && day.windowStart >= day.windowEnd)
    )
      return false;
    if (!day.stops.length) {
      if (
        day.programmeMinutes !== 0 ||
        day.emptyReason !== (day.windowStart === null ? 'noWalkingWindow' : 'noEligibleStops')
      )
        return false;
      continue;
    }
    if (!day.windowStart || !day.windowEnd) return false;
    let cursor = minute(day.windowStart);
    for (const [index, stop] of day.stops.entries()) {
      const start = minute(stop.start),
        end = minute(stop.end);
      if (
        !historicalId(stop.id) ||
        visited.has(stop.id) ||
        (stop.incomingEdgeId !== null && !historicalId(stop.incomingEdgeId)) ||
        (index === 0) !== (stop.incomingEdgeId === null) ||
        (index === 0 && stop.walkFromPreviousMinutes !== 0) ||
        start < cursor + stop.walkFromPreviousMinutes ||
        end - start !== stop.dwellMinutes ||
        stop.end > day.windowEnd
      )
        return false;
      visited.add(stop.id);
      cursor = end;
    }
    if (day.programmeMinutes !== cursor - minute(day.windowStart)) return false;
  }
  return visited.size > 0;
}
