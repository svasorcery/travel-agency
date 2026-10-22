import { describe, expect, it } from 'vitest';
import { decodeRailSearch, decodeRailSuggestions } from '../../shared/ts/api-client/src/rail.decoder';
import { decodeRailDetails } from '../../shared/ts/api-client/src/rail-details.decoder';
import { railDetailsFixture } from '../../shared/ts/api-client/tests/rail-details-fixture';
import { railFixture } from '../../tests/fixtures/rail-schedule';
import { fictionalRailDetails, fictionalRailSearch, fictionalRailSuggestions } from '../demo/rail-search.mjs';

describe('authored fictional Rail uses the public decoder', () => {
  const now = Date.parse('2026-10-20T12:00:00Z');
  it.each([
    'normal',
    'detail-partial',
    'detail-no-match',
    'detail-recheck-failed',
    'detail-route-failed',
    'detail-quota',
  ])('detail %s obeys the same strict decoder', (preset) => {
    const f = railDetailsFixture(now);
    const search = fictionalRailSearch(f.request.search, now);
    const j = search.items[0];
    const request = {
      ...f.request,
      selection: { key: j.key, transport: j.transport, departure: j.departure, arrival: j.arrival },
    };
    expect(decodeRailDetails(fictionalRailDetails(request, now, preset), request, now, f.authority).profile).toBe(
      'fictional',
    );
  });
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
