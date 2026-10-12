// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../tests/fixtures/flights-booking.json';
import {
  decodeFlightAncillaryCatalog,
  decodeFlightCreationStatus,
  decodeFlightPurchase,
  FlightAncillaryContractError,
  sameFlightMoney,
} from './flights-ancillaries.decoder';
import { decodeFlightQuoteResponse } from './flights-quote.decoder';

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
  it.each([892, 2147483647])(
    'full catalog retains informational included quantities %s without purchase caps',
    (quantity) => {
      const quote = decodeFlightQuoteResponse(booking.oneWay.response);
      const catalog = {
        aggregateId: quote.aggregateId,
        quoteRevision: quote.binding.revision,
        baseFare: { amount: String(quote.offer.totalAmount), currency: quote.offer.currency },
        expiresAt: quote.offer.expiresAt,
        services: [],
        allowances: [
          {
            bookingPassengerId: quote.binding.slots[0].bookingPassengerId,
            segment: { leg: 0, segment: 0 },
            checkedQuantity: quantity,
            carryOnQuantity: quantity,
          },
        ],
        seatMaps: [],
        seatsUnavailable: false,
        unsupportedPricing: false,
      };
      expect(decodeFlightAncillaryCatalog(catalog, quote)).toEqual(catalog);
    },
  );
  it.each(['checkedQuantity', 'carryOnQuantity'])(
    'full catalog rejects informational Int32 overflow in %s',
    (field) => {
      const quote = decodeFlightQuoteResponse(booking.oneWay.response);
      const catalog = {
        aggregateId: quote.aggregateId,
        quoteRevision: quote.binding.revision,
        baseFare: { amount: String(quote.offer.totalAmount), currency: quote.offer.currency },
        expiresAt: quote.offer.expiresAt,
        services: [],
        allowances: [
          {
            bookingPassengerId: quote.binding.slots[0].bookingPassengerId,
            segment: { leg: 0, segment: 0 },
            checkedQuantity: null,
            carryOnQuantity: null,
            [field]: 2147483648,
          },
        ],
        seatMaps: [],
        seatsUnavailable: false,
        unsupportedPricing: false,
      };
      expect(() => decodeFlightAncillaryCatalog(catalog, quote)).toThrow(FlightAncillaryContractError);
    },
  );
  it('keeps selected purchased service quantity capped at 99', () => {
    expect(() => decodeFlightPurchase({ ...purchase, services: [{ ...purchase.services[0], quantity: 100 }] })).toThrow(
      FlightAncillaryContractError,
    );
  });
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
    for (const bookingVersion of [undefined, null, 0, 12])
      expect(decodeFlightCreationStatus({ ...status, bookingVersion }, id).bookingVersion).toBe(bookingVersion);
    for (const bookingVersion of [-1, 1.5, Number.MAX_SAFE_INTEGER + 1, '12'])
      expect(() => decodeFlightCreationStatus({ ...status, bookingVersion }, id)).toThrow(FlightAncillaryContractError);
    expect(() => decodeFlightCreationStatus({ ...status, canConfirm: true }, id)).toThrow(FlightAncillaryContractError);
    expect(() => decodeFlightCreationStatus({ ...status, canCancel: true }, id)).toThrow(FlightAncillaryContractError);
  });
});
