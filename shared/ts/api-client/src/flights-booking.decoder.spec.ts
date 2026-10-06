import {
  decodeCancelledOrderResponse,
  decodeConfirmedOrderResponse,
  decodeFlightOrderListResponse,
  decodeFlightOrderResponse,
  decodeHeldOrderResponse,
  FlightBookingContractError,
} from './flights-booking.decoder';

const aggregateId = '88b83d41-0194-2098-c1f6-fe7351d41cf2';

const order = {
  passengerCount: 1,
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

describe('Cancellation command decoder', () => {
  const cancelled = { ...order, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' };
  it('accepts only the same order terminal snapshot and preserves Refunded no-op', () => {
    expect(decodeCancelledOrderResponse(cancelled, aggregateId.toUpperCase())).toEqual(cancelled);
    const refunded = { ...cancelled, status: 'Refunded', refundedAt: '2030-06-01T12:00:00Z' };
    expect(decodeCancelledOrderResponse(refunded, aggregateId)).toEqual(refunded);
  });
  it.each([
    null,
    {},
    order,
    { ...cancelled, cancelledAt: null },
    { ...cancelled, status: 'Ticketed' },
    { ...cancelled, aggregateId: '11111111-1111-1111-1111-111111111111' },
    { ...cancelled, status: 'Refunded', refundedAt: null },
  ])('rejects an unproven cancellation %#', (value) => {
    expect(() => decodeCancelledOrderResponse(value, aggregateId)).toThrow(FlightBookingContractError);
  });
});

describe('Flights order list response decoder', () => {
  it('reads explicit four-leg routes across detail/list/cancel without imposing fresh chronology', () => {
    const current = {
      ...order,
      itinerary: {
        ...order.itinerary,
        journeyKind: 'multi-leg',
        slices: Array.from({ length: 4 }, () => structuredClone(order.itinerary.slices[0])),
      },
    };
    expect(decodeFlightOrderResponse(current, aggregateId).itinerary.slices).toHaveLength(4);
    expect(decodeFlightOrderListResponse({ items: [current], limit: 21, offset: 0 }, 0).items).toHaveLength(1);
    expect(
      decodeCancelledOrderResponse(
        { ...current, status: 'Cancelled', cancelledAt: '2030-06-01T11:00:00Z' },
        aggregateId,
      ).itinerary.slices,
    ).toHaveLength(4);
    const oldSlice = structuredClone(order.itinerary.slices[0]);
    const back = {
      ...oldSlice,
      origin: 'DME',
      destination: 'LED',
      segments: oldSlice.segments.map((segment) => ({
        ...segment,
        origin: 'DME',
        destination: 'LED',
        departAt: '2030-06-10T11:00:00+03:00',
        arriveAt: '2030-06-10T13:00:00+03:00',
      })),
    };
    const historical = {
      ...order,
      itinerary: { slices: [oldSlice, back], totalDuration: '04:00:00', isRoundTrip: true },
    };
    expect(decodeFlightOrderResponse(historical, aggregateId).itinerary.slices).toHaveLength(2);
    expect(
      decodeFlightOrderResponse(
        { ...historical, itinerary: { ...historical.itinerary, journeyKind: 'round-trip' } },
        aggregateId,
      ).itinerary.slices,
    ).toHaveLength(2);
    expect(() =>
      decodeFlightOrderResponse(
        { ...current, itinerary: { ...current.itinerary, journeyKind: undefined } },
        aggregateId,
      ),
    ).toThrow(FlightBookingContractError);
  });
  const page = { items: [order], limit: 21, offset: 20 };

  it('accepts an empty page and a full lookahead page with unknown extra fields', () => {
    expect(decodeFlightOrderListResponse({ items: [], limit: 21, offset: 0 }, 0).items).toEqual([]);
    const items = Array.from({ length: 21 }, (_, index) => ({
      ...order,
      aggregateId: `11111111-1111-1111-1111-${String(index + 1).padStart(12, '0')}`,
      futureField: true,
    }));
    const response = { items, limit: 21, offset: 20, futureField: 'allowed' };
    expect(decodeFlightOrderListResponse(response, 20)).toEqual(response);
  });

  it.each([
    null,
    [],
    {},
    { ...page, items: undefined },
    { ...page, items: {} },
    { ...page, limit: 20 },
    { ...page, limit: '21' },
    { ...page, limit: 21.5 },
    { ...page, offset: 0 },
    { ...page, offset: '20' },
    { ...page, offset: 20.5 },
    { ...page, offset: -20 },
    { ...page, offset: Number.NaN },
    {
      ...page,
      items: Array.from({ length: 22 }, (_, index) => ({
        ...order,
        aggregateId: `11111111-1111-1111-1111-${String(index + 1).padStart(12, '0')}`,
      })),
    },
  ])('rejects a malformed envelope or mismatched paging contract %#', (response) => {
    expect(() => decodeFlightOrderListResponse(response, 20)).toThrow(FlightBookingContractError);
  });

  it('rejects the entire page when any item is corrupt', () => {
    for (const corrupt of [
      null,
      {},
      { ...order, aggregateId: 'not-a-guid' },
      { ...order, aggregateId: '00000000-0000-0000-0000-000000000000' },
      { ...order, aggregateId: '11111111-1111-1111-1111-111111111111', status: 'OfferQuoted' },
      { ...order, aggregateId: '11111111-1111-1111-1111-111111111111', itinerary: { ...order.itinerary, slices: [] } },
    ]) {
      expect(() => decodeFlightOrderListResponse({ ...page, items: [order, corrupt] }, 20)).toThrow(
        FlightBookingContractError,
      );
    }
  });

  it('rejects duplicate aggregate ids regardless of casing', () => {
    expect(() =>
      decodeFlightOrderListResponse(
        { ...page, items: [order, { ...order, aggregateId: aggregateId.toUpperCase() }] },
        20,
      ),
    ).toThrow(FlightBookingContractError);
  });
});

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

  it.each(['Confirmed', 'Ticketed', 'Cancelled', 'Refunded'])(
    'reads truthful completion/replay status %s',
    (status) => {
      const response = { aggregateId, status, paymentRef: null };
      expect(decodeConfirmedOrderResponse(response)).toEqual(response);
    },
  );
  it.each(['Held', 'OfferQuoted', 'invented'])('rejects incomplete confirmation status %s', (status) => {
    expect(() => decodeConfirmedOrderResponse({ aggregateId, status, paymentRef: null })).toThrow(
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

describe('mandatory order group count', () => {
  it.each([undefined, 0, 10, 1.5])('rejects count %s without ticket inference', (passengerCount) => {
    expect(() => decodeFlightOrderResponse({ ...order, passengerCount }, aggregateId)).toThrow(
      FlightBookingContractError,
    );
  });
  it.each([1, 2, 9])('accepts count %s as metadata only', (passengerCount) => {
    expect(decodeFlightOrderResponse({ ...order, passengerCount }, aggregateId).passengerCount).toBe(passengerCount);
  });
});
