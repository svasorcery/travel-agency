import type { FlightCreationStatus, FlightPurchasedService } from '@travel/api-client';
import { flightAncillaryDifferences } from './flight-ancillary-differences';

const bag: FlightPurchasedService = {
  selectionKey: 'ase_bag_20',
  kind: 'checked-baggage',
  bookingPassengerId: '11111111-1111-4111-8111-111111111111',
  segments: [{ leg: 0, segment: 0 }],
  quantity: 1,
  lineTotal: { amount: '10.00', currency: 'GBP' },
  seatDesignator: null,
  disclosures: null,
  baggage: { maximumWeightKg: 20, maximumHeightCm: null, maximumDepthCm: null, maximumLengthCm: null },
};
function status(selected: FlightPurchasedService[], actual: FlightPurchasedService[]): FlightCreationStatus {
  const total = { amount: '75.00', currency: 'GBP' };
  return {
    aggregateId: '22222222-2222-4222-8222-222222222222',
    state: 'Matches',
    bookingStatus: 'Held',
    passengerCount: 1,
    bookingPassengerIds: [bag.bookingPassengerId],
    itinerary: null,
    accepted: {
      quoteRevision: '33333333-3333-4333-8333-333333333333',
      baseFare: { amount: '50.00', currency: 'GBP' },
      extras: { amount: '25.00', currency: 'GBP' },
      total,
      services: selected,
      expiresAt: '2030-01-01T12:00:00Z',
      noticeVersion: 'booking-services-v1',
    },
    actual: { total, services: actual },
    heldUntil: '2030-01-01T12:00:00Z',
    canConfirm: true,
    canCancel: true,
    canRefresh: false,
    observedAt: '2030-01-01T10:00:00Z',
  };
}
describe('selected versus actual service summary', () => {
  it('matches different bags on the same passenger and segments despite new supplier IDs and ordering', () => {
    const heavy = {
      ...bag,
      selectionKey: 'ase_bag_23',
      lineTotal: { amount: '15.00', currency: 'GBP' },
      baggage: { maximumWeightKg: 23, maximumHeightCm: null, maximumDepthCm: null, maximumLengthCm: null },
    };
    expect(
      flightAncillaryDifferences(
        status(
          [bag, heavy],
          [
            { ...heavy, selectionKey: 'ser_second' },
            { ...bag, selectionKey: 'ser_first' },
          ],
        ),
      ),
    ).toEqual([]);
  });
  it('still shows a same-price seat replacement', () => {
    const seat = { ...bag, kind: 'seat' as const, seatDesignator: '12A', disclosures: [], baggage: null };
    const differences = flightAncillaryDifferences(
      status([seat], [{ ...seat, selectionKey: 'ser_seat', seatDesignator: '12B' }]),
    );
    expect(differences).toHaveLength(1);
    expect(differences[0]).toContain('12A');
    expect(differences[0]).toContain('12B');
  });
});
