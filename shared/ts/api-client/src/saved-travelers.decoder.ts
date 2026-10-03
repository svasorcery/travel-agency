import type {
  SavedTraveler,
  SavedTravelerDetails,
  SavedTravelerFieldError,
  SavedTravelerPage,
  SavedTravelerReceipt,
} from './saved-travelers.types';

const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
export class SavedTravelerContractError extends Error {
  constructor() {
    super('Invalid saved traveler contract.');
    this.name = 'SavedTravelerContractError';
  }
}
export function isTravelerGuid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value) && value !== '00000000-0000-0000-0000-000000000000';
}
export function validTravelerOffset(value: number): boolean {
  return Number.isSafeInteger(value) && value >= 0 && value % 20 === 0 && value <= 2_147_483_640;
}
interface TravelerWireRecord {
  id?: unknown;
  revision?: unknown;
  details?: unknown;
  title?: unknown;
  givenName?: unknown;
  familyName?: unknown;
  dateOfBirth?: unknown;
  gender?: unknown;
  email?: unknown;
  phone?: unknown;
  items?: unknown;
  offset?: unknown;
  hasMore?: unknown;
}
function record(value: unknown): TravelerWireRecord {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new SavedTravelerContractError();
  return value as TravelerWireRecord;
}
export function validTravelerName(value: string): boolean {
  return (
    value.length >= 1 &&
    value.length <= 20 &&
    /^[A-Za-z\u00c0-\u00ff\u0100-\u017f '-]{1,20}$/.test(value.trim()) &&
    !/[ÆæĲĳŒœÞð×÷]/.test(value) &&
    /[A-Za-z\u00c0-\u017f]/.test(value)
  );
}
export function travelerDateError(
  value: string,
  today = new Date().toISOString().slice(0, 10),
): 'date' | 'future' | null {
  const date = new Date(`${value}T00:00:00Z`);
  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
    value < '0001-01-02' ||
    !Number.isFinite(date.getTime()) ||
    date.toISOString().slice(0, 10) !== value
  )
    return 'date';
  return value > today ? 'future' : null;
}
export function decodeSavedTravelerDetails(value: unknown): SavedTravelerDetails {
  const d = record(value);
  if (
    !['mr', 'ms', 'mrs', 'miss', 'dr'].includes(d.title as string) ||
    !['male', 'female'].includes(d.gender as string) ||
    typeof d.givenName !== 'string' ||
    !validTravelerName(d.givenName) ||
    typeof d.familyName !== 'string' ||
    !validTravelerName(d.familyName) ||
    typeof d.dateOfBirth !== 'string' ||
    travelerDateError(d.dateOfBirth) ||
    typeof d.email !== 'string' ||
    !d.email.trim() ||
    d.email.length > 254 ||
    typeof d.phone !== 'string' ||
    !/^\+[1-9][0-9]{7,14}$/.test(d.phone)
  )
    throw new SavedTravelerContractError();
  return {
    title: d.title as SavedTravelerDetails['title'],
    givenName: d.givenName,
    familyName: d.familyName,
    dateOfBirth: d.dateOfBirth,
    gender: d.gender as SavedTravelerDetails['gender'],
    email: d.email,
    phone: d.phone,
  };
}
export function decodeSavedTraveler(value: unknown, expectedId?: string): SavedTraveler {
  const r = record(value);
  if (
    !isTravelerGuid(r.id) ||
    !isTravelerGuid(r.revision) ||
    (expectedId !== undefined && r.id.toLowerCase() !== expectedId.toLowerCase())
  )
    throw new SavedTravelerContractError();
  return { id: r.id.toLowerCase(), revision: r.revision.toLowerCase(), details: decodeSavedTravelerDetails(r.details) };
}
export function decodeSavedTravelerPage(value: unknown, expectedOffset: number): SavedTravelerPage {
  const r = record(value);
  if (
    !validTravelerOffset(expectedOffset) ||
    r.offset !== expectedOffset ||
    !Array.isArray(r.items) ||
    r.items.length > 20 ||
    typeof r.hasMore !== 'boolean' ||
    (r.hasMore && r.items.length !== 20)
  )
    throw new SavedTravelerContractError();
  const items = r.items.map((item) => decodeSavedTraveler(item));
  if (new Set(items.map((item) => item.id)).size !== items.length) throw new SavedTravelerContractError();
  return { items, offset: expectedOffset, hasMore: r.hasMore };
}
export function decodeSavedTravelerReceipt(
  value: unknown,
  expectedId: string,
  status: number,
  expectedStatus: 200 | 201,
  etag: string | null,
): SavedTravelerReceipt {
  const r = record(value);
  if (
    status !== expectedStatus ||
    !isTravelerGuid(r.id) ||
    r.id.toLowerCase() !== expectedId.toLowerCase() ||
    !isTravelerGuid(r.revision) ||
    etag !== `"${r.revision.toLowerCase()}"`
  )
    throw new SavedTravelerContractError();
  return { id: r.id.toLowerCase(), revision: r.revision.toLowerCase() };
}
const FIELD_CODES: Record<string, readonly string[]> = {
  title: ['Flights.PassengerTitleInvalid'],
  givenName: ['Flights.PassengerGivenNameInvalid'],
  familyName: ['Flights.PassengerFamilyNameInvalid'],
  dateOfBirth: ['Flights.PassengerDateOfBirthInvalid', 'Flights.PassengerDateOfBirthFutureInvalid'],
  gender: ['Flights.PassengerGenderInvalid'],
  email: ['Flights.PassengerEmailInvalid'],
  phone: ['Flights.PassengerPhoneInvalid'],
};
export function safeTravelerFieldErrors(value: unknown): SavedTravelerFieldError[] {
  if (!Array.isArray(value) || value.length > 7) return [];
  const result: SavedTravelerFieldError[] = [];
  for (const candidate of value) {
    if (
      !candidate ||
      typeof candidate !== 'object' ||
      typeof candidate.field !== 'string' ||
      typeof candidate.code !== 'string' ||
      !Object.hasOwn(FIELD_CODES, candidate.field) ||
      !FIELD_CODES[candidate.field].includes(candidate.code) ||
      result.some((item) => item.field === candidate.field)
    )
      continue;
    result.push({ field: candidate.field as SavedTravelerFieldError['field'], code: candidate.code });
  }
  return result;
}
