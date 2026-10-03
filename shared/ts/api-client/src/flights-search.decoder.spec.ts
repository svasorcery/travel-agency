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
    expect(() => decodeFlightSearchResponse({ ...fixtures.oneWay.response, offers: [offer] })).toThrow(
      'offers[0].variant',
    );
  });

  it('rejects an offer that combines booking and partner references', () => {
    const offer = {
      ...fixtures.oneWay.response.offers[0],
      deeplinkUrl: 'https://partner.invalid/fixture',
      partnerName: 'Example Partner',
    };
    expect(() => decodeFlightSearchResponse({ ...fixtures.oneWay.response, offers: [offer] })).toThrow(
      'offers[0].variant',
    );
  });

  it('rejects an invalid itinerary and amount rather than rendering a blank card', () => {
    const base = fixtures.oneWay.response.offers[0];
    const malformed: [unknown, string][] = [
      [{ ...base, totalAmount: Number.POSITIVE_INFINITY }, 'offers[0].totalAmount'],
      [{ ...base, itinerary: { ...base.itinerary, slices: [] } }, 'offers[0].itinerary'],
      [
        { ...base, itinerary: { ...base.itinerary, slices: [{ ...base.itinerary.slices[0], duration: 'tomorrow' }] } },
        'offers[0].itinerary.slices[0].duration',
      ],
      [
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
        'offers[0].itinerary.slices[0].segments[0].departAt',
      ],
      ...[
        '2030-02-30T08:00:00+00:00',
        '2030-06-01T24:00:00+00:00',
        '2030-06-01T08:00:00+15:00',
        '2030-06-01T08:00:00+14:30',
      ].map((fetchedAt): [unknown, string] => [{ ...base, fetchedAt }, 'offers[0].fetchedAt']),
    ];
    for (const [offer, field] of malformed)
      expect(() => decodeFlightSearchResponse({ ...fixtures.oneWay.response, offers: [offer] })).toThrow(field);
  });

  it('accepts empty and partial results and rejects malformed provider failures', () => {
    expect(decodeFlightSearchResponse(fixtures.empty.response).offers).toEqual([]);
    expect(decodeFlightSearchResponse(fixtures.emptyPartial.response).partialFailures).toHaveLength(1);
    expect(() =>
      decodeFlightSearchResponse({ ...fixtures.empty.response, partialFailures: [{ provider: 'duffel' }] }),
    ).toThrow('partialFailures[0].errorCode');
  });
});

describe('mandatory group capability', () => {
  it.each(['groupTwo', 'groupNine'] as const)('accepts %s group totals and separate skips', (key) => {
    expect(decodeFlightSearchResponse(fixtures[key].response).skippedProviders).toHaveLength(1);
  });
  it('fails closed on missing count/skips and inconsistent eligibility', () => {
    const response = fixtures.oneWay.response;
    for (const value of [
      { ...response, skippedProviders: undefined },
      { ...response, offers: [{ ...response.offers[0], passengerCount: undefined }] },
      { ...response, offers: [{ ...response.offers[0], holdEligible: false, holdIneligibilityReason: null }] },
      { ...response, offers: [{ ...response.offers[0], passengerCount: 10 }] },
    ])
      expect(() => decodeFlightSearchResponse(value)).toThrow(FlightSearchContractError);
  });
});
