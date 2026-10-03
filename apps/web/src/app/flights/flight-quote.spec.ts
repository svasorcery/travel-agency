import type { BookableFlightOffer } from '@travel/api-client';
// Shared canonical HTTP fixtures, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
// eslint-disable-next-line @nx/enforce-module-boundaries
import search from '../../../../../tests/fixtures/flights-search.json';
import { quoteDiffersFromSearch } from './flight-quote';

describe('quote comparison', () => {
  it('normalizes missing legacy kind and notices a leg-four-only cabin change without price change', () => {
    const original = structuredClone(search.oneWay.response.offers[0]) as BookableFlightOffer;
    const withKind = { ...original, itinerary: { ...original.itinerary, journeyKind: 'one-way' as const } };
    expect(quoteDiffersFromSearch(original, withKind)).toBe(false);
    original.itinerary = {
      ...original.itinerary,
      journeyKind: 'multi-leg',
      slices: Array.from({ length: 4 }, () => structuredClone(original.itinerary.slices[0])),
    };
    const changed = structuredClone(original);
    changed.itinerary.slices[3].segments[0].cabinClass = 'business';
    expect(changed.totalAmount).toBe(original.totalAmount);
    expect(quoteDiffersFromSearch(original, changed)).toBe(true);
  });
  it('requires review when the first quote price differs even if backend priceChanged is false', () => {
    expect(booking.oneWay.response.priceChanged).toBe(false);
    expect(
      quoteDiffersFromSearch(
        search.oneWay.response.offers[0] as BookableFlightOffer,
        booking.oneWay.response.offer as BookableFlightOffer,
      ),
    ).toBe(true);
  });

  it('does not treat fetched time or expiry alone as a changed route', () => {
    const searched = search.oneWay.response.offers[0] as BookableFlightOffer;
    const quoted = { ...searched, fetchedAt: '2030-06-01T08:10:00+00:00', expiresAt: '2030-06-01T08:30:00+00:00' };
    expect(quoteDiffersFromSearch(searched, quoted)).toBe(false);
  });

  it('requires review when a flight segment changes', () => {
    const searched = search.oneWay.response.offers[0] as BookableFlightOffer;
    const quoted = {
      ...searched,
      itinerary: {
        ...searched.itinerary,
        slices: [
          {
            ...searched.itinerary.slices[0],
            segments: [{ ...searched.itinerary.slices[0].segments[0], flightNumber: 'SU102' }],
          },
        ],
      },
    };
    expect(quoteDiffersFromSearch(searched, quoted)).toBe(true);
  });

  it('requires review when the reported duration changes without segment changes', () => {
    const searched = search.oneWay.response.offers[0] as BookableFlightOffer;
    const quoted = { ...searched, itinerary: { ...searched.itinerary, totalDuration: '03:00:00' } };
    expect(quoteDiffersFromSearch(searched, quoted)).toBe(true);
  });
});
