import { describe, expect, it } from 'vitest';
import { railFixture } from '../../../../tests/fixtures/rail-schedule';
import { fictionalRailSearch, fictionalRailSuggestions } from '../../../../tools/demo/rail-search.mjs';
import { decodeRailSearch, decodeRailSuggestions } from './rail.decoder';

describe('authored fictional Rail uses the public decoder', () => {
  const now = Date.parse('2026-10-20T12:00:00Z');
  it.each(['normal', 'empty', 'interval', 'failed', 'partial', 'result-expiry'])(
    'accepts %s without inventing another public outcome',
    (preset) => {
      const { request } = railFixture(now);
      expect(decodeRailSearch(fictionalRailSearch(request, now, preset), request, now).profile).toBe('fictional');
    },
  );
  it('accepts the same bounded station selection descriptor', () => {
    expect(decodeRailSuggestions(fictionalRailSuggestions('Учебная', now), now).suggestions).toHaveLength(3);
  });
});
