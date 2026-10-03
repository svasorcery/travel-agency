// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-search.json';
import { decodeFlightItinerary, decodeFlightSearchResponse, FlightSearchContractError } from './flights-search.decoder';

describe('ordered journey transport', () => {
  const old = fixtures.roundTrip.response.offers[0].itinerary;
  const explicit = (count: number) => ({
    ...old,
    journeyKind: 'multi-leg',
    isRoundTrip: false,
    slices: Array.from({ length: count }, (_, index) => ({
      ...old.slices[0],
      destination: index === 0 ? old.slices[0].destination : 'VKO',
      segments: old.slices[0].segments.map((segment, n, segments) => ({
        ...segment,
        destination: n === segments.length - 1 && index > 0 ? 'VKO' : segment.destination,
      })),
    })),
  });
  it.each([2, 3, 4])('accepts explicit %s independent legs', (count) => {
    expect(decodeFlightItinerary(explicit(count)).slices).toHaveLength(count);
  });
  it('rejects a present kind contradicting the one-way geometry', () => {
    const value = structuredClone(fixtures.oneWay.response);
    Object.assign(value.offers[0].itinerary, { journeyKind: 'round-trip' });
    expect(() => decodeFlightSearchResponse(value)).toThrow(FlightSearchContractError);
  });
  it.each([3, 4])('rejects kindless %s legs', (count) => {
    const value = explicit(count);
    expect(() => decodeFlightItinerary({ ...value, journeyKind: undefined })).toThrow(FlightSearchContractError);
  });
  it('rejects inconsistent flags, kind, slice and segment geometry', () => {
    for (const value of [
      { ...explicit(2), journeyKind: 'round-trip' },
      { ...old, journeyKind: 'unsupported' },
      { ...old, isRoundTrip: false },
      { ...old, slices: [{ ...old.slices[0], origin: 'AAA' }] },
      { ...explicit(2), journeyKind: undefined },
    ])
      expect(() => decodeFlightItinerary(value)).toThrow(FlightSearchContractError);
  });
  it('reads historical mirrored inversion both without kind and with current DTO kind', () => {
    const value = structuredClone(old);
    value.slices[1].segments[0].departAt = value.slices[0].segments[0].departAt;
    expect(decodeFlightItinerary(value)).toEqual(value);
    expect(decodeFlightItinerary({ ...value, journeyKind: 'round-trip' }).isRoundTrip).toBe(true);
  });
  it('accepts the journey capability skip', () => {
    expect(
      decodeFlightSearchResponse({
        ...fixtures.empty.response,
        skippedProviders: [{ provider: 'travelpayouts', reasonCode: 'journey-unsupported' }],
      }).skippedProviders,
    ).toHaveLength(1);
  });
});

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
