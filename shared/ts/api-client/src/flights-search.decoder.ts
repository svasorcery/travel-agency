import type { FlightItinerary, FlightSearchResponse } from './flights-search.types';

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
    if (item['deeplinkUrl'] !== null || item['partnerName'] !== null) {
      throw new FlightSearchContractError(`${field}.variant`);
    }
  } else {
    if (item['providerOfferRef'] !== null || item['expiresAt'] !== null) {
      throw new FlightSearchContractError(`${field}.variant`);
    }
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
  return value as FlightSearchResponse;
}
