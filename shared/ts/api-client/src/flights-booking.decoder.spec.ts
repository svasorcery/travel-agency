import {
  decodeConfirmedOrderResponse,
  decodeFlightOrderResponse,
  decodeHeldOrderResponse,
  FlightBookingContractError,
} from './flights-booking.decoder';

const aggregateId = '88b83d41-0194-2098-c1f6-fe7351d41cf2';

const order = {
  aggregateId,
  status: 'Held',
  totalAmount: 5400,
  currency: 'RUB',
  itinerary: {
    slices: [
      {
        origin: 'LED',
        destination: 'DME',
        duration: '02:00:00',
        segments: [
          {
            origin: 'LED',
            destination: 'DME',
            departAt: '2030-06-10T10:00:00+03:00',
            arriveAt: '2030-06-10T12:00:00+03:00',
            carrierCode: 'SU',
            flightNumber: '100',
            cabinClass: 'economy',
          },
        ],
      },
    ],
    totalDuration: '02:00:00',
    isRoundTrip: false,
  },
  ticketNumbers: [],
  bookedAt: '2030-06-01T10:00:00Z',
  ticketedAt: null,
  cancelledAt: null,
  refundedAt: null,
};

describe('Flights booking response decoders', () => {
  it('accepts a held response with the server deadline', () => {
    expect(
      decodeHeldOrderResponse({
        aggregateId,
        providerOrderId: 'fake-provider-order',
        heldUntil: '2030-06-10T10:15:00Z',
      }),
    ).toEqual({ aggregateId, providerOrderId: 'fake-provider-order', heldUntil: '2030-06-10T10:15:00Z' });
  });

  it('rejects a missing or invalid hold deadline and an empty aggregate id', () => {
    for (const response of [
      { aggregateId, providerOrderId: 'fake-provider-order', heldUntil: 'tomorrow' },
      {
        aggregateId: '00000000-0000-0000-0000-000000000000',
        providerOrderId: 'fake',
        heldUntil: '2030-06-10T10:15:00Z',
      },
      { aggregateId, providerOrderId: '', heldUntil: '2030-06-10T10:15:00Z' },
    ]) {
      expect(() => decodeHeldOrderResponse(response)).toThrow(FlightBookingContractError);
    }
  });

  it('accepts only the command outcome Confirmed, never Ticketed', () => {
    expect(decodeConfirmedOrderResponse({ aggregateId, status: 'Confirmed', paymentRef: null })).toEqual({
      aggregateId,
      status: 'Confirmed',
      paymentRef: null,
    });
    expect(() => decodeConfirmedOrderResponse({ aggregateId, status: 'Ticketed', paymentRef: null })).toThrow(
      FlightBookingContractError,
    );
  });

  it('decodes the owner-scoped order view and each supported state', () => {
    for (const status of ['Held', 'Confirmed', 'Ticketed', 'Cancelled', 'Refunded']) {
      const response = { ...order, status, ticketNumbers: status === 'Ticketed' ? ['TKT-001'] : [] };
      expect(decodeFlightOrderResponse(response, aggregateId)).toEqual(response);
    }
  });

  it('accepts an uppercase direct-link GUID when the server normalizes it to lowercase', () => {
    expect(decodeFlightOrderResponse(order, aggregateId.toUpperCase()).aggregateId).toBe(aggregateId);
  });

  it('rejects an order for another aggregate or an unusable projection', () => {
    for (const response of [
      { ...order, aggregateId: '11111111-1111-1111-1111-111111111111' },
      { ...order, status: 'OfferQuoted' },
      { ...order, status: 'Ticketed', ticketNumbers: [] },
      { ...order, ticketNumbers: [12] },
      { ...order, itinerary: { ...order.itinerary, slices: [] } },
      { ...order, bookedAt: 'not-a-date' },
      { ...order, bookedAt: '2030-02-31T10:00:00Z' },
    ]) {
      expect(() => decodeFlightOrderResponse(response, aggregateId)).toThrow(FlightBookingContractError);
    }
  });
});
