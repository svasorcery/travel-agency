import {
  decodeFlightCreationStatus,
  decodeFlightPurchase,
  FlightAncillaryContractError,
  sameFlightMoney,
} from './flights-ancillaries.decoder';

const id = '11111111-1111-1111-1111-111111111111';
const revision = '22222222-2222-2222-2222-222222222222';
const purchase = {
  quoteRevision: revision,
  baseFare: { amount: '50.00', currency: 'GBP' },
  extras: { amount: '20.00', currency: 'GBP' },
  total: { amount: '70.00', currency: 'GBP' },
  services: [
    {
      selectionKey: 'ase_fictional',
      kind: 'checked-baggage',
      bookingPassengerId: id,
      segments: [
        { leg: 0, segment: 0 },
        { leg: 0, segment: 1 },
      ],
      quantity: 2,
      lineTotal: { amount: '20.00', currency: 'GBP' },
      seatDesignator: null,
      disclosures: null,
      baggage: { maximumWeightKg: 23, maximumHeightCm: null, maximumDepthCm: 40, maximumLengthCm: null },
    },
  ],
  expiresAt: '2030-01-01T12:00:00Z',
  noticeVersion: 'booking-services-v1',
};
describe('Flight services reader', () => {
  it('keeps exact strings and counts the multi-segment quantity-inclusive line once', () => {
    expect(decodeFlightPurchase(purchase)).toEqual(purchase);
    expect(sameFlightMoney({ amount: '0.10', currency: 'GBP' }, { amount: '0.1', currency: 'GBP' })).toBe(true);
    expect(() => decodeFlightPurchase({ ...purchase, total: { amount: '90.00', currency: 'GBP' } })).toThrow(
      FlightAncillaryContractError,
    );
    expect(() => decodeFlightPurchase({ ...purchase, extras: { amount: '40.00', currency: 'GBP' } })).toThrow(
      FlightAncillaryContractError,
    );
  });
  it('unknown actual terms remain null and cannot enable confirm or cancel', () => {
    const status = {
      aggregateId: id,
      state: 'ManualReviewRequired',
      bookingStatus: 'OfferQuoted',
      passengerCount: 1,
      bookingPassengerIds: [id],
      itinerary: null,
      accepted: purchase,
      actual: null,
      heldUntil: null,
      canConfirm: false,
      canCancel: false,
      canRefresh: false,
      observedAt: '2030-01-01T10:00:00Z',
    };
    expect(decodeFlightCreationStatus(status, id).actual).toBeNull();
    expect(() => decodeFlightCreationStatus({ ...status, canConfirm: true }, id)).toThrow(FlightAncillaryContractError);
    expect(() => decodeFlightCreationStatus({ ...status, canCancel: true }, id)).toThrow(FlightAncillaryContractError);
  });
});
