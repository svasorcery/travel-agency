import type { FlightItinerary, FlightOffer, FlightSearchResponse } from './flights-search.types';

export class FlightSearchContractError extends Error {
  constructor(field: string) {
    super(`Invalid Flights search response at ${field}`);
    this.name = 'FlightSearchContractError';
  }
}

function record(value: unknown, field: string): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new FlightSearchContractError(field);
  }
  return value as Record<string, unknown>;
}

function array(value: unknown, field: string): unknown[] {
  if (!Array.isArray(value)) throw new FlightSearchContractError(field);
  return value;
}

function nonempty(value: unknown, field: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new FlightSearchContractError(field);
  }
  return value;
}

function timestamp(value: unknown, field: string): void {
  const text = nonempty(value, field);
  const calendarDate = text.slice(0, 10);
  const midnight = new Date(`${calendarDate}T00:00:00Z`);
  if (
    !/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(
      text,
    ) ||
    !Number.isFinite(Date.parse(text)) ||
    !Number.isFinite(midnight.getTime()) ||
    midnight.toISOString().slice(0, 10) !== calendarDate
  ) {
    throw new FlightSearchContractError(field);
  }
}

function duration(value: unknown, field: string): void {
  const text = nonempty(value, field);
  if (!/^(?:(?:0|[1-9]\d*)\.)?(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?$/.test(text)) {
    throw new FlightSearchContractError(field);
  }
}

function itinerary(value: unknown, field: string): void {
  const result = record(value, field);
  const slices = array(result['slices'], `${field}.slices`);
  if (slices.length < 1 || slices.length > 2 || result['isRoundTrip'] !== (slices.length === 2)) {
    throw new FlightSearchContractError(field);
  }
  duration(result['totalDuration'], `${field}.totalDuration`);
  slices.forEach((slice, sliceIndex) => {
    const sliceField = `${field}.slices[${sliceIndex}]`;
    const entry = record(slice, sliceField);
    nonempty(entry['origin'], `${sliceField}.origin`);
    nonempty(entry['destination'], `${sliceField}.destination`);
    duration(entry['duration'], `${sliceField}.duration`);
    const segments = array(entry['segments'], `${sliceField}.segments`);
    if (segments.length === 0) throw new FlightSearchContractError(`${sliceField}.segments`);
    segments.forEach((segment, segmentIndex) => {
      const segmentField = `${sliceField}.segments[${segmentIndex}]`;
      const part = record(segment, segmentField);
      for (const name of ['origin', 'destination', 'carrierCode', 'flightNumber', 'cabinClass']) {
        nonempty(part[name], `${segmentField}.${name}`);
      }
      timestamp(part['departAt'], `${segmentField}.departAt`);
      timestamp(part['arriveAt'], `${segmentField}.arriveAt`);
    });
  });
}

export function decodeFlightItinerary(value: unknown): FlightItinerary {
  itinerary(value, 'itinerary');
  return value as FlightItinerary;
}

function offer(value: unknown, field: string): void {
  const item = record(value, field);
  nonempty(item['id'], `${field}.id`);
  nonempty(item['provider'], `${field}.provider`);
  if (typeof item['totalAmount'] !== 'number' || !Number.isFinite(item['totalAmount']) || item['totalAmount'] < 0) {
    throw new FlightSearchContractError(`${field}.totalAmount`);
  }
  if (typeof item['currency'] !== 'string' || !/^[A-Z]{3}$/.test(item['currency'])) {
    throw new FlightSearchContractError(`${field}.currency`);
  }
  itinerary(item['itinerary'], `${field}.itinerary`);
  timestamp(item['fetchedAt'], `${field}.fetchedAt`);

  const bookable = typeof item['providerOfferRef'] === 'string' && item['providerOfferRef'].trim().length > 0;
  const partner = typeof item['deeplinkUrl'] === 'string' && item['deeplinkUrl'].length > 0;
  if (bookable === partner) throw new FlightSearchContractError(`${field}.variant`);
  if (bookable) {
    timestamp(item['expiresAt'], `${field}.expiresAt`);
    if (
      !Number.isInteger(item['passengerCount']) ||
      (item['passengerCount'] as number) < 1 ||
      (item['passengerCount'] as number) > 9
    )
      throw new FlightSearchContractError(`${field}.passengerCount`);
    if (
      typeof item['holdEligible'] !== 'boolean' ||
      (item['holdEligible']
        ? item['holdIneligibilityReason'] !== null
        : !['hold-not-supported', 'identity-documents-required', 'capability-unknown'].includes(
            item['holdIneligibilityReason'] as string,
          ))
    )
      throw new FlightSearchContractError(`${field}.holdEligible`);
    if (item['deeplinkUrl'] !== null || item['partnerName'] !== null) {
      throw new FlightSearchContractError(`${field}.variant`);
    }
  } else {
    if (item['providerOfferRef'] !== null || item['expiresAt'] !== null) {
      throw new FlightSearchContractError(`${field}.variant`);
    }
    if (item['passengerCount'] !== null || item['holdEligible'] !== null || item['holdIneligibilityReason'] !== null)
      throw new FlightSearchContractError(`${field}.partnerCapability`);
    nonempty(item['partnerName'], `${field}.partnerName`);
    try {
      const url = new URL(item['deeplinkUrl'] as string);
      if (!['http:', 'https:'].includes(url.protocol)) throw new Error('Invalid scheme');
    } catch {
      throw new FlightSearchContractError(`${field}.deeplinkUrl`);
    }
  }
}

export function decodeFlightSearchResponse(value: unknown): FlightSearchResponse {
  const response = record(value, 'response');
  array(response['offers'], 'offers').forEach((value, index) => {
    offer(value, `offers[${index}]`);
  });
  array(response['partialFailures'], 'partialFailures').forEach((value, index) => {
    const field = `partialFailures[${index}]`;
    const failure = record(value, field);
    nonempty(failure['provider'], `${field}.provider`);
    nonempty(failure['errorCode'], `${field}.errorCode`);
    if (
      typeof failure['elapsedMs'] !== 'number' ||
      !Number.isSafeInteger(failure['elapsedMs']) ||
      failure['elapsedMs'] < 0
    ) {
      throw new FlightSearchContractError(`${field}.elapsedMs`);
    }
  });
  array(response['skippedProviders'], 'skippedProviders').forEach((value, index) => {
    const skip = record(value, `skippedProviders[${index}]`);
    nonempty(skip['provider'], 'skippedProviders.provider');
    if (skip['reasonCode'] !== 'passenger-count-unsupported' || skip['elapsedMs'] !== undefined)
      throw new FlightSearchContractError('skippedProviders.reasonCode');
  });
  if (response['ranking'] !== undefined && response['ranking'] !== null) {
    ranking(response['ranking'], response['offers'] as FlightOffer[]);
  }
  return value as FlightSearchResponse;
}

function durationSeconds(value: string): number {
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})/.exec(value);
  if (!match) throw new FlightSearchContractError('ranking.durationSeconds');
  return Number(match[1] ?? 0) * 86400 + Number(match[2]) * 3600 + Number(match[3]) * 60 + Number(match[4]);
}

function ranking(value: unknown, offers: FlightOffer[]): void {
  const result = record(value, 'ranking');
  const target = nonempty(result['requestedCurrency'], 'ranking.requestedCurrency');
  const entries = array(result['entries'], 'ranking.entries');
  if (
    result['policy'] !== 'price-first-v1' ||
    !/^[A-Z]{3}$/.test(target) ||
    entries.length !== offers.length ||
    offers.length > 200
  ) {
    throw new FlightSearchContractError('ranking');
  }
  const ids = new Set<string>();
  let previousCurrency = '';
  let rank = 0;
  let previousFactors: number[] = [];
  entries.forEach((item, index) => {
    const entry = record(item, `ranking.entries[${index}]`);
    const offer = offers[index];
    const currency = offer.currency;
    if (currency !== previousCurrency) {
      if (previousCurrency && (currency === target || (previousCurrency !== target && currency < previousCurrency))) {
        throw new FlightSearchContractError('ranking.groups');
      }
      previousCurrency = currency;
      rank = 0;
      previousFactors = [];
    }
    rank++;
    const sourceAmount = entry['sourceAmount'];
    const sourceCurrency = entry['sourceCurrency'];
    const state = entry['priceState'];
    if (
      entry['offerId'] !== offer.id ||
      ids.has(offer.id) ||
      entry['currency'] !== currency ||
      entry['rank'] !== rank ||
      typeof sourceAmount !== 'number' ||
      !Number.isFinite(sourceAmount) ||
      sourceAmount < 0 ||
      typeof sourceCurrency !== 'string' ||
      !/^[A-Z]{3}$/.test(sourceCurrency)
    ) {
      throw new FlightSearchContractError('ranking.entry');
    }
    ids.add(offer.id);
    const validPrice =
      state === 'native'
        ? sourceCurrency === target && currency === target && sourceAmount === offer.totalAmount
        : state === 'converted'
          ? sourceCurrency !== target && currency === target
          : state === 'fx-unavailable' &&
            sourceCurrency !== target &&
            currency === sourceCurrency &&
            sourceAmount === offer.totalAmount;
    if (!validPrice) throw new FlightSearchContractError('ranking.price');
    const partner = offer.providerOfferRef === null;
    const expectedDuration = partner ? null : durationSeconds(offer.itinerary.totalDuration);
    const expectedTransfers = partner
      ? null
      : offer.itinerary.slices.reduce((sum, slice) => sum + slice.segments.length - 1, 0);
    if (
      entry['durationSeconds'] !== expectedDuration ||
      entry['transfers'] !== expectedTransfers ||
      (expectedDuration !== null && !Number.isSafeInteger(expectedDuration))
    ) {
      throw new FlightSearchContractError('ranking.factors');
    }
    const expectedLimitations = [
      ...(partner ? ['partial-itinerary'] : []),
      ...(state === 'fx-unavailable' ? ['fx-unavailable'] : []),
    ];
    const limitations = array(entry['limitations'], 'ranking.limitations');
    if (limitations.length !== expectedLimitations.length || limitations.some((v, i) => v !== expectedLimitations[i])) {
      throw new FlightSearchContractError('ranking.limitations');
    }
    const factors = [offer.totalAmount, expectedDuration ?? Infinity, expectedTransfers ?? Infinity];
    for (let i = 0; i < previousFactors.length; i++) {
      if (factors[i] < previousFactors[i]) throw new FlightSearchContractError('ranking.order');
      if (factors[i] > previousFactors[i]) break;
    }
    previousFactors = factors;
  });
}
