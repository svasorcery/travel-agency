// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../tests/fixtures/flights-booking.json';
import { decodeFlightQuoteResponse, FlightQuoteContractError } from './flights-quote.decoder';

describe('Flights quote response decoder', () => {
  it('uses the same four-leg and historical shape rule as search and order', () => {
    const quote = structuredClone(fixtures.oneWay.response);
    const itinerary = {
      ...quote.offer.itinerary,
      journeyKind: 'multi-leg',
      slices: Array.from({ length: 4 }, () => structuredClone(quote.offer.itinerary.slices[0])),
    };
    const current = { ...quote, offer: { ...quote.offer, itinerary } };
    expect(decodeFlightQuoteResponse(current).offer.itinerary.slices).toHaveLength(4);
    expect(() =>
      decodeFlightQuoteResponse({
        ...current,
        offer: { ...current.offer, itinerary: { ...itinerary, journeyKind: undefined } },
      }),
    ).toThrow(FlightQuoteContractError);
    const historical = structuredClone(fixtures.roundTrip.response);
    historical.offer.itinerary.slices[1].segments[0].departAt =
      historical.offer.itinerary.slices[0].segments[0].departAt;
    Object.assign(historical.offer.itinerary, { journeyKind: 'round-trip' });
    expect(decodeFlightQuoteResponse(historical).offer.itinerary.slices).toHaveLength(2);
  });
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

describe('quote passenger binding', () => {
  it('rejects absent binding and impossible calendar dates', () => {
    const response = fixtures.oneWay.response;
    expect(() => decodeFlightQuoteResponse({ ...response, binding: undefined })).toThrow(FlightQuoteContractError);
    const binding = {
      revision: '11111111-1111-4111-8111-111111111111',
      passengerCount: 2,
      firstDepartureLocalDate: '2030-02-30',
      slots: [
        { bookingPassengerId: '22222222-2222-4222-8222-222222222222', kind: 'adult' },
        { bookingPassengerId: '22222222-2222-4222-8222-222222222222', kind: 'adult' },
      ],
    };
    expect(() =>
      decodeFlightQuoteResponse({ ...response, offer: { ...response.offer, passengerCount: 2 }, binding }),
    ).toThrow(FlightQuoteContractError);
  });
});

describe('exact group binding', () => {
  it.each(['groupTwo', 'groupNine'] as const)('decodes %s local slots', (key) => {
    const quote = decodeFlightQuoteResponse(fixtures[key].response);
    expect(quote.binding.slots).toHaveLength(fixtures[key].request.passengerCount);
  });
  it('rejects duplicate IDs regardless of case, empty revisions, count mismatch and unsupported kinds', () => {
    const response = fixtures.groupTwo.response;
    for (const binding of [
      { ...response.binding, revision: '00000000-0000-0000-0000-000000000000' },
      { ...response.binding, passengerCount: 1 },
      { ...response.binding, firstDepartureLocalDate: '2030-02-29' },
      {
        ...response.binding,
        slots: [
          response.binding.slots[0],
          {
            ...response.binding.slots[0],
            bookingPassengerId: response.binding.slots[0].bookingPassengerId.toUpperCase(),
          },
        ],
      },
      { ...response.binding, slots: [{ ...response.binding.slots[0], kind: 'child' }, response.binding.slots[1]] },
    ])
      expect(() => decodeFlightQuoteResponse({ ...response, binding })).toThrow(FlightQuoteContractError);
  });
});
