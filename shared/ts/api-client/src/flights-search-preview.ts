import type { FlightPassengerCount } from './flights-search.types';

export { default as flightPreviewLocations } from './flights-preview-locations.json';

export interface FlightSearchPreviewRequest {
  query: string;
  referenceDate: string;
}

export interface FlightSearchPreviewLocation {
  code: string;
  name: string;
}

export interface FlightSearchPreviewDate {
  value: string;
  source: 'explicit' | 'suggested';
}

export type FlightSearchPreviewField =
  | 'tripType'
  | 'origin'
  | 'destination'
  | 'departureDate'
  | 'returnDate'
  | 'passengerCount';

export interface FlightSearchPreviewIssue {
  field: FlightSearchPreviewField | 'request';
  reason: 'missing' | 'ambiguous' | 'unsupported' | 'invalid';
}

export interface FlightSearchPreview {
  tripType: 'oneWay' | 'roundTrip' | null;
  origin: FlightSearchPreviewLocation | null;
  destination: FlightSearchPreviewLocation | null;
  departureDate: FlightSearchPreviewDate | null;
  returnDate: FlightSearchPreviewDate | null;
  passengerCount: FlightPassengerCount | null;
  issues: readonly FlightSearchPreviewIssue[];
}

const fields: readonly FlightSearchPreviewIssue['field'][] = [
  'tripType',
  'origin',
  'destination',
  'departureDate',
  'returnDate',
  'passengerCount',
  'request',
];
const reasons: readonly FlightSearchPreviewIssue['reason'][] = ['missing', 'ambiguous', 'unsupported', 'invalid'];

function invalid(): never {
  throw new Error('Invalid flight search preview response.');
}

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (
    typeof value !== 'object' ||
    value === null ||
    Array.isArray(value) ||
    Object.keys(value).length !== keys.length ||
    keys.some((key) => !Object.hasOwn(value, key))
  )
    invalid();
  return value as Record<string, unknown>;
}

function member<T extends string>(value: unknown, options: readonly T[]): T {
  const choice = options.find((option) => option === value);
  return choice ?? invalid();
}

function decodeLocation(value: unknown): FlightSearchPreviewLocation | null {
  if (value === null) return null;
  const { code, name } = record(value, ['code', 'name']);
  if (typeof code !== 'string' || !/^[A-Z]{3}$/.test(code) || typeof name !== 'string' || !name.trim()) invalid();
  return { code, name };
}

function decodeDate(value: unknown): FlightSearchPreviewDate | null {
  if (value === null) return null;
  const { value: date, source } = record(value, ['value', 'source']);
  if (typeof date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(date) || date.startsWith('0000-')) invalid();
  const calendarDate = new Date(`${date}T00:00:00Z`);
  if (!Number.isFinite(calendarDate.getTime()) || calendarDate.toISOString().slice(0, 10) !== date) invalid();
  return { value: date, source: member(source, ['explicit', 'suggested']) };
}

export function decodeFlightSearchPreview(value: unknown): FlightSearchPreview {
  const draft = record(value, [
    'tripType',
    'origin',
    'destination',
    'departureDate',
    'returnDate',
    'passengerCount',
    'issues',
  ]);
  const passengerCount = draft['passengerCount'];
  if (
    passengerCount !== null &&
    (typeof passengerCount !== 'number' ||
      !Number.isInteger(passengerCount) ||
      passengerCount < 1 ||
      passengerCount > 9)
  )
    invalid();
  const issues = draft['issues'];
  if (!Array.isArray(issues)) invalid();
  return {
    tripType: draft['tripType'] === null ? null : member(draft['tripType'], ['oneWay', 'roundTrip'] as const),
    origin: decodeLocation(draft['origin']),
    destination: decodeLocation(draft['destination']),
    departureDate: decodeDate(draft['departureDate']),
    returnDate: decodeDate(draft['returnDate']),
    passengerCount: passengerCount as FlightPassengerCount | null,
    issues: issues.map((issue) => {
      const { field, reason } = record(issue, ['field', 'reason']);
      return { field: member(field, fields), reason: member(reason, reasons) };
    }),
  };
}
