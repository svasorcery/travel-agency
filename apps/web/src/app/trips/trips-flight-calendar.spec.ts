import { type AvailableFlightObservation, decodeFlightStatus, type FlightLinkLeg } from '@travel/api-client';
import { projectTripFlightCalendar } from './trips-flight-calendar';

const tripId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const orderId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const range = { startDate: '2027-06-13', endDate: '2027-06-15', destinationId: 'saint-petersburg' } as const;
const leg = (departAt: string, arriveAt: string, origin = 'KGD', destination = 'LED'): FlightLinkLeg => ({
  origin,
  destination,
  departAt,
  arriveAt,
});
const at = (day: string, time: string, offset: string) => `2027-06-${day}T${time}:00${offset}`;
const outbound = leg(at('13', '23:40', '+02:00'), at('14', '02:10', '+03:00'));
const inbound = leg(at('14', '23:40', '+03:00'), at('15', '00:30', '+02:00'), 'LED', 'KGD');

function observation(route: FlightLinkLeg[], id = orderId) {
  const item = decodeFlightStatus(
    {
      tripId,
      linksRevision: '1',
      items: [
        {
          orderId: id,
          state: 'available',
          route,
          passengerCount: 1,
          bookingStatus: 'Ticketed',
          reviewRequired: false,
          streamVersion: '1',
          observedAt: '2027-05-12T00:00:00Z',
        },
      ],
    },
    tripId,
  ).items[0] as AvailableFlightObservation;
  return { state: 'available' as const, facts: item };
}

function locations(calendar: ReturnType<typeof projectTripFlightCalendar>) {
  return [
    ...calendar.before.map(() => 'before'),
    ...calendar.across.map(() => 'across'),
    ...[...calendar.days].flatMap(([date, cards]) => cards.map(() => date)),
    ...calendar.after.map(() => 'after'),
  ];
}

describe('placement of accepted linked flight legs', () => {
  it.each([
    ['before/before', '15', at('12', '20:40', '+02:00'), at('12', '23:10', '+03:00'), 'before'],
    ['before/inside', '15', at('12', '23:40', '+02:00'), at('13', '02:10', '+03:00'), '2027-06-13'],
    ['before/after', '13', at('12', '23:40', '+02:00'), at('14', '02:10', '+03:00'), 'across'],
    ['inside/before', '15', at('13', '04:00', '+14:00'), at('12', '17:00', '-10:00'), '2027-06-13'],
    ['inside/inside', '15', outbound.departAt, outbound.arriveAt, '2027-06-14'],
    ['inside/after', '15', at('15', '23:40', '+03:00'), at('16', '00:30', '+02:00'), '2027-06-15'],
    ['after/before', '13', at('14', '00:05', '+14:00'), at('12', '23:55', '-11:00'), 'across'],
    ['after/inside', '15', at('16', '04:00', '+14:00'), at('15', '17:00', '-10:00'), '2027-06-15'],
    ['after/after', '15', at('16', '00:40', '+03:00'), at('16', '01:30', '+02:00'), 'after'],
  ])('places %s exactly once', (_, lastDay, departAt, arriveAt, expected) => {
    const calendar = projectTripFlightCalendar({ ...range, endDate: `2027-06-${lastDay}` }, [
      observation([leg(departAt, arriveAt)]),
    ]);
    expect(locations(calendar)).toEqual([expected]);
  });

  it.each([
    [leg(at('12', '22:00', '+02:00'), at('13', '00:00', '+03:00')), '2027-06-13'],
    [leg(at('15', '00:00', '+03:00'), at('16', '01:30', '+02:00')), '2027-06-15'],
  ])('includes midnight based on the source local date', (source, day) => {
    const calendar = projectTripFlightCalendar(range, [observation([source])]);
    expect(locations(calendar)).toEqual([day]);
    expect(calendar.days.get(day)?.[0].leg).toEqual(source);
  });

  it.each([
    [at('13', '09:00', '+02:00'), at('13', '10:00', '+03:00')],
    [at('15', '09:00', '+02:00'), at('13', '10:00', '+03:00')],
    [at('13', '09:00', '+02:00'), at('15', '10:00', '+03:00')],
    [at('12', '23:40', '+02:00'), at('16', '02:10', '+03:00')],
  ])('rejects an invalid source chronology or duration before projection', (departAt, arriveAt) => {
    expect(() => observation([leg(departAt, arriveAt)])).toThrow();
  });

  it('prefers the known trip-city arrival and departure for two legs of one order', () => {
    const cards = projectTripFlightCalendar(range, [observation([outbound, inbound])]).days.get('2027-06-14');
    expect(cards?.map((card) => card.leg)).toEqual([outbound, inbound]);
    expect(new Set(cards?.map((card) => card.key)).size).toBe(2);
    expect(cards?.map((card) => card.orderId)).toEqual([orderId, orderId]);
  });

  it.each([
    [leg(outbound.departAt, outbound.arriveAt, 'AAA', 'LED'), 'saint-petersburg', '2027-06-13'],
    [outbound, 'moscow', '2027-06-13'],
    [leg(outbound.departAt, outbound.arriveAt, 'SVO', 'VKO'), 'moscow', '2027-06-13'],
    [leg(outbound.departAt, outbound.arriveAt, 'KGD', 'DME'), 'moscow', '2027-06-14'],
    [leg(outbound.departAt, at('13', '23:50', '+02:00')), 'saint-petersburg', '2027-06-13'],
  ] as const)(
    'uses the proven city match or the departure date and never duplicates a same-day leg',
    (source, destinationId, expected) => {
      expect(locations(projectTripFlightCalendar({ ...range, destinationId }, [observation([source])]))).toEqual([
        expected,
      ]);
    },
  );

  it('orders cards by exact anchor instant and keeps saved order for equal times', () => {
    const earlier = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
    const tied = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
    const laterArrival = '2027-06-14T02:10:00.0000002+03:00';
    const earlierArrival = '2027-06-14T01:10:00.0000001+02:00';
    const cards = projectTripFlightCalendar(range, [
      observation([{ ...outbound, arriveAt: laterArrival }]),
      observation([{ ...outbound, arriveAt: earlierArrival }], earlier),
      observation([{ ...outbound, arriveAt: laterArrival }], tied),
    ]).days.get('2027-06-14');
    expect(cards?.map((card) => card.orderId)).toEqual([earlier, orderId, tied]);
  });
});
