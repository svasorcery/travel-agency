import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, it } from 'vitest';
import { TripsContractError } from './trips.decoder';
import { decodeSavedTripOption } from './trips-option.decoder';

const fixture = JSON.parse(
  readFileSync(resolve(import.meta.dirname, '../../../../tests/fixtures/trips-saved-option-v1.json'), 'utf8'),
) as { history: Record<string, unknown>; invalid: { name: string; path: string; value: unknown }[] };

it('accepts the historical v1 reference without the current discovery or release', () => {
  expect(decodeSavedTripOption({ savedOption: fixture.history })).toEqual(fixture.history);
});

it.each(fixture.invalid)('rejects historical v1 when $name', ({ path, value }) => {
  const savedOption = structuredClone(fixture.history);
  const segments = path.split('.');
  const field = segments.pop()!;
  const parent = segments.reduce((node, key) => node[key] as Record<string, unknown>, savedOption);
  expect(Object.hasOwn(parent, field)).toBe(true);
  parent[field] = value;
  expect(() => decodeSavedTripOption({ savedOption })).toThrow(TripsContractError);
});
