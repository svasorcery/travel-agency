// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-booking.json';
import { decodeFlightQuoteResponse, FlightQuoteContractError } from './flights-quote.decoder';

describe('Flights quote response decoder', () => {
  it.each(['oneWay', 'roundTrip', 'reQuoteChanged'] as const)('accepts the %s HTTP example', (name) => {
    expect(decodeFlightQuoteResponse(fixtures[name].response)).toEqual(fixtures[name].response);
  });

  it('rejects partner offers and missing fare facts before checkout', () => {
    const response = fixtures.oneWay.response;
    const partner = {
      ...response.offer,
      providerOfferRef: null,
      expiresAt: null,
      deeplinkUrl: 'https://partner.invalid/fixture',
      partnerName: 'Partner',
    };
    expect(() => decodeFlightQuoteResponse({ ...response, offer: partner })).toThrow(FlightQuoteContractError);
    expect(() => decodeFlightQuoteResponse({ ...response, fareConditions: undefined })).toThrow(
      FlightQuoteContractError,
    );
  });

  it('rejects inconsistent price delta, baggage counts, and aggregate identifier', () => {
    const response = fixtures.oneWay.response;
    for (const changed of [
      { ...response, priceChanged: true },
      { ...response, fareConditions: { ...response.fareConditions, checkedBaggageQuantity: -1 } },
      { ...response, aggregateId: 'not-a-guid' },
      { ...response, aggregateId: '00000000-0000-0000-0000-000000000000' },
    ]) {
      expect(() => decodeFlightQuoteResponse(changed)).toThrow(FlightQuoteContractError);
    }
  });

  it('tolerates additive fields without dropping the checked quote', () => {
    expect(decodeFlightQuoteResponse({ ...fixtures.oneWay.response, serverNote: 'extra' }).aggregateId).toBe(
      fixtures.oneWay.response.aggregateId,
    );
  });
});
