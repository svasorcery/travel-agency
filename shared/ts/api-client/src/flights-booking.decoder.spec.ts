import {
  decodeConfirmedOrderResponse,
  decodeHeldOrderResponse,
  FlightBookingContractError,
} from './flights-booking.decoder';

const aggregateId = '88b83d41-0194-2098-c1f6-fe7351d41cf2';

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
});
