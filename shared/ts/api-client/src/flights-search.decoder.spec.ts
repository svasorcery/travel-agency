// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-search.json';
import { decodeFlightSearchResponse, FlightSearchContractError } from './flights-search.decoder';

describe('Flights search response decoder', () => {
  it.each(['oneWay', 'roundTrip'] as const)('accepts the %s HTTP contract example', (caseName) => {
    expect(decodeFlightSearchResponse(fixtures[caseName].response)).toEqual(fixtures[caseName].response);
  });

  it('accepts a new additive response field', () => {
    expect(decodeFlightSearchResponse({ ...fixtures.oneWay.response, serverNote: 'extra' }).offers).toHaveLength(2);
  });

  it('rejects an offer with neither booking nor partner reference', () => {
    const offer = { ...fixtures.oneWay.response.offers[0], providerOfferRef: null, expiresAt: null };
    expect(() => decodeFlightSearchResponse({ offers: [offer], partialFailures: [] })).toThrow(
      FlightSearchContractError,
    );
  });

  it('rejects an offer that combines booking and partner references', () => {
    const offer = {
      ...fixtures.oneWay.response.offers[0],
      deeplinkUrl: 'https://partner.invalid/fixture',
      partnerName: 'Example Partner',
    };
    expect(() => decodeFlightSearchResponse({ offers: [offer], partialFailures: [] })).toThrow(
      FlightSearchContractError,
    );
  });

  it('rejects an invalid itinerary and amount rather than rendering a blank card', () => {
    const base = fixtures.oneWay.response.offers[0];
    for (const offer of [
      { ...base, totalAmount: Number.POSITIVE_INFINITY },
      { ...base, itinerary: { ...base.itinerary, slices: [] } },
      {
        ...base,
        itinerary: {
          ...base.itinerary,
          slices: [{ ...base.itinerary.slices[0], duration: 'tomorrow' }],
        },
      },
      {
        ...base,
        itinerary: {
          ...base.itinerary,
          slices: [
            {
              ...base.itinerary.slices[0],
              segments: [{ ...base.itinerary.slices[0].segments[0], departAt: '2030-06-10' }],
            },
          ],
        },
      },
      { ...base, fetchedAt: '2030-02-30T08:00:00+00:00' },
      { ...base, fetchedAt: '2030-06-01T24:00:00+00:00' },
      { ...base, fetchedAt: '2030-06-01T08:00:00+15:00' },
      { ...base, fetchedAt: '2030-06-01T08:00:00+14:30' },
    ]) {
      expect(() => decodeFlightSearchResponse({ offers: [offer], partialFailures: [] })).toThrow(
        FlightSearchContractError,
      );
    }
  });

  it('accepts empty and partial results and rejects malformed provider failures', () => {
    expect(decodeFlightSearchResponse(fixtures.empty.response).offers).toEqual([]);
    expect(decodeFlightSearchResponse(fixtures.emptyPartial.response).partialFailures).toHaveLength(1);
    expect(() => decodeFlightSearchResponse({ offers: [], partialFailures: [{ provider: 'duffel' }] })).toThrow(
      FlightSearchContractError,
    );
  });
});
