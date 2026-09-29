import type { FlightOffer } from '@travel/api-client';
// Shared canonical HTTP fixture, test only.
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-search.json';
import { summarizeSearchResponse, toFlightOfferView } from './flight-results';

describe('Flights search results', () => {
  it('renders the bookable route with supplied UTC offset and actual flight details', () => {
    const offer = toFlightOfferView(fixtures.oneWay.response.offers[0] as FlightOffer);
    expect(offer.kind).toBe('bookable');
    if (offer.kind !== 'bookable') throw new Error('Expected bookable offer');
    expect(offer.slices).toHaveLength(1);
    expect(offer.slices[0].departure).toBe('10.06.2030, 10:00 UTC+03:00');
    expect(offer.slices[0].arrival).toBe('10.06.2030, 12:00 UTC+03:00');
    expect(offer.slices[0].transferCount).toBe(0);
    expect(offer.slices[0].duration).toBe('2 ч 0 мин');
  });

  it('shows two real bookable slices while keeping a partner result as a limited summary', () => {
    const [bookable, partner] = fixtures.roundTrip.response.offers.map((offer) =>
      toFlightOfferView(offer as FlightOffer),
    );
    expect(bookable.kind).toBe('bookable');
    if (bookable.kind !== 'bookable') throw new Error('Expected bookable offer');
    expect(bookable.slices).toHaveLength(2);
    expect(partner.kind).toBe('partner');
    if (partner.kind !== 'partner') throw new Error('Expected partner offer');
    expect(partner.direction).toBe('LED → DME');
    expect(partner).not.toHaveProperty('transferCount');
    expect(partner).not.toHaveProperty('arrival');
  });

  it('reports foreign currency without claiming an FX error or changing offer order', () => {
    const response = fixtures.mixedCurrency.response as unknown as Parameters<typeof summarizeSearchResponse>[0];
    const summary = summarizeSearchResponse(response);
    expect(summary.currencyMismatch).toBe(true);
    expect(summary.partial).toBe(false);
    expect(summary.offers[0].price).toContain('EUR');
    expect(summary.offers[0].provider).toBe('duffel');
  });

  it('keeps empty and partial-provider messages independent', () => {
    const summary = summarizeSearchResponse(
      fixtures.emptyPartial.response as Parameters<typeof summarizeSearchResponse>[0],
    );
    expect(summary.offers).toEqual([]);
    expect(summary.partial).toBe(true);
    expect(summary.currencyMismatch).toBe(false);
  });

  it('formats day-prefixed fractional duration without rounding up a minute', () => {
    const source = fixtures.oneWay.response.offers[0];
    const offer = toFlightOfferView({
      ...source,
      itinerary: { ...source.itinerary, totalDuration: '1.02:03:04.5' },
    } as FlightOffer);
    expect(offer.kind).toBe('bookable');
    if (offer.kind !== 'bookable') throw new Error('Expected bookable offer');
    expect(offer.totalDuration).toBe('26 ч 3 мин');
  });

  it('keeps every leg of a connecting route with its own offset and calendar date', () => {
    const source = fixtures.oneWay.response.offers[0];
    const connecting = {
      ...source,
      itinerary: {
        slices: [
          {
            origin: 'LED',
            destination: 'DME',
            duration: '14:00:00',
            segments: [
              {
                origin: 'LED',
                destination: 'DXB',
                departAt: '2030-06-10T22:30:00+03:00',
                arriveAt: '2030-06-11T05:30:00+04:00',
                carrierCode: 'SU',
                flightNumber: 'SU310',
                cabinClass: 'economy',
              },
              {
                origin: 'DXB',
                destination: 'DME',
                departAt: '2030-06-11T07:30:00+04:00',
                arriveAt: '2030-06-11T12:30:00+03:00',
                carrierCode: 'EK',
                flightNumber: 'EK311',
                cabinClass: 'economy',
              },
            ],
          },
        ],
        totalDuration: '14:00:00',
        isRoundTrip: false,
      },
    } as FlightOffer;
    const offer = toFlightOfferView(connecting);
    expect(offer.kind).toBe('bookable');
    if (offer.kind !== 'bookable') throw new Error('Expected bookable offer');
    expect(offer.slices[0].segments.map((segment) => segment.route)).toEqual(['LED → DXB', 'DXB → DME']);
    expect(offer.slices[0].segments[0].arrival).toBe('11.06.2030, 05:30 UTC+04:00');
    expect(offer.slices[0].segments[1].arrival).toBe('11.06.2030, 12:30 UTC+03:00');
  });
});
