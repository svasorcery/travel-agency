import { describe, expect, it } from 'vitest';
import {
  decodeSavedTraveler,
  decodeSavedTravelerPage,
  decodeSavedTravelerReceipt,
  SavedTravelerContractError,
} from './saved-travelers.decoder';

const id = '11111111-1111-4111-8111-111111111111';
const revision = '22222222-2222-4222-8222-222222222222';
const details = {
  title: 'mr',
  givenName: 'Demo',
  familyName: 'Traveler',
  dateOfBirth: '2015-02-28',
  gender: 'male',
  email: 'demo@example.test',
  phone: '+79161234567',
};
describe('saved traveler response boundary', () => {
  it('accepts a future adult and returns an independent seven-field copy', () => {
    const source = { id, revision, details: { ...details } };
    const result = decodeSavedTraveler(source, id);
    source.details.givenName = 'Changed';
    expect(result.details.givenName).toBe('Demo');
  });
  it.each([
    { id: '00000000-0000-0000-0000-000000000000' },
    { revision: 'bad' },
    { details: { ...details, title: 'sir' } },
    { details: { ...details, givenName: `${' '.repeat(21)}Demo` } },
    { details: { ...details, givenName: 'Demo1' } },
    { details: { ...details, dateOfBirth: '0001-01-01' } },
    { details: { ...details, dateOfBirth: '2015-02-30' } },
    { details: { ...details, dateOfBirth: '9999-01-01' } },
    { details: { ...details, gender: 'other' } },
    { details: { ...details, email: 'x'.repeat(255) } },
    { details: { ...details, phone: '+１２３４５６７８９' } },
  ])('rejects malformed profile without exposing values', (patch) => {
    expect(() => decodeSavedTraveler({ id, revision, details, ...patch }, id)).toThrow(SavedTravelerContractError);
  });
  it('binds a detail read to its selected ID', () => {
    expect(() => decodeSavedTraveler({ id, revision, details }, revision)).toThrow(SavedTravelerContractError);
  });
  it('bounds pages, validates offset and hasMore, and rejects repeated identities', () => {
    expect(decodeSavedTravelerPage({ items: [], offset: 20, hasMore: false }, 20)).toEqual({
      items: [],
      offset: 20,
      hasMore: false,
    });
    for (const page of [
      { items: [], offset: 0, hasMore: false },
      { items: [], offset: 20, hasMore: 'yes' },
      { items: Array(21).fill({ id, revision, details }), offset: 20, hasMore: true },
      {
        items: [
          { id, revision, details },
          { id, revision, details },
        ],
        offset: 20,
        hasMore: false,
      },
    ])
      expect(() => decodeSavedTravelerPage(page, 20)).toThrow(SavedTravelerContractError);
  });
  it('requires the exact operation status and matching strong ETag and ID', () => {
    expect(decodeSavedTravelerReceipt({ id, revision }, id, 201, 201, `"${revision}"`)).toEqual({ id, revision });
    for (const [status, tag] of [
      [200, `"${revision}"`],
      [201, `W/"${revision}"`],
      [201, `"${id}"`],
      [201, null],
    ] as const)
      expect(() => decodeSavedTravelerReceipt({ id, revision }, id, status, 201, tag)).toThrow(
        SavedTravelerContractError,
      );
  });
});
