// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-search.json';
import { decodeFlightSearchResponse, FlightSearchContractError } from './flights-search.decoder';

function response() {
  const offer = structuredClone(fixtures.oneWay.response.offers[0]);
  return {
    offers: [offer],
    partialFailures: [],
    skippedProviders: [],
    ranking: {
      policy: 'price-first-v1',
      requestedCurrency: 'RUB',
      entries: [
        {
          offerId: offer.id,
          currency: 'RUB',
          rank: 1,
          sourceAmount: offer.totalAmount,
          sourceCurrency: 'RUB',
          priceState: 'native',
          durationSeconds: 7200,
          transfers: 0,
          limitations: [] as string[],
        },
      ],
    },
  };
}

describe('explainable ranking contract', () => {
  it.each(['rankedOneWay', 'rankedRoundTrip', 'rankingFxFailure'] as const)(
    'accepts the shared %s demonstration contract',
    (key) => {
      expect(decodeFlightSearchResponse(fixtures[key].response)).toEqual(fixtures[key].response);
    },
  );
  it('accepts complete factors and legacy absence', () => {
    expect(decodeFlightSearchResponse(response())).toEqual(response());
    expect(decodeFlightSearchResponse(fixtures.oneWay.response)).toEqual(fixtures.oneWay.response);
  });
  it.each([
    { offerId: 'other-offer' },
    { currency: 'EUR' },
    { rank: 2 },
    { sourceAmount: -1 },
    { sourceAmount: Number.NaN },
    { priceState: 'converted' },
    { durationSeconds: -1 },
    { durationSeconds: 3600 },
    { transfers: 1 },
    { limitations: ['invented'] },
    { limitations: ['fx-unavailable'] },
    { sourceCurrency: 'EUR' },
  ])('rejects inconsistent factors %j', (change) => {
    const value = response();
    Object.assign(value.ranking.entries[0], change);
    expect(() => decodeFlightSearchResponse(value)).toThrow(FlightSearchContractError);
  });
  it('rejects unknown policies and missing or duplicate entries', () => {
    for (const ranking of [
      { ...response().ranking, policy: 'unknown' },
      { ...response().ranking, entries: [] },
      { ...response().ranking, entries: [...response().ranking.entries, ...response().ranking.entries] },
    ])
      expect(() => decodeFlightSearchResponse({ ...response(), ranking })).toThrow(FlightSearchContractError);
  });
  it('requires unknown partner factors even when its synthetic itinerary has times', () => {
    const value = response();
    value.offers[0] = structuredClone(fixtures.oneWay.response.offers[1]);
    value.ranking.entries[0].offerId = value.offers[0].id;
    value.ranking.entries[0].sourceAmount = value.offers[0].totalAmount;
    expect(() => decodeFlightSearchResponse(value)).toThrow(FlightSearchContractError);
  });
});
