import {
  type FlightLinkLeg,
  flightLinkInstant,
  type LinkedBookingStatus,
  type TripBrief,
  type TripCity,
} from '@travel/api-client';
import type { TripFlightLinkCard } from './trips-flight-links-state.service';

// Airport-to-city evidence is recorded in the trips-flight-calendar OpenSpec design.
const airportCities = new Map<string, TripCity>([
  ['KGD', 'kaliningrad'],
  ['LED', 'saint-petersburg'],
  ['SVO', 'moscow'],
  ['DME', 'moscow'],
  ['VKO', 'moscow'],
]);

type CalendarBrief = Pick<TripBrief, 'startDate' | 'endDate' | 'destinationId'>;
type OutsideSection = 'before' | 'across' | 'after';
type Placement = ({ date: string } | { section: OutsideSection }) & { at: string };

export interface TripFlightCalendarCard {
  key: string;
  orderId: string;
  leg: FlightLinkLeg;
  bookingStatus: LinkedBookingStatus;
  reviewRequired: boolean;
  stale: boolean;
}

function position(date: string, { startDate, endDate }: CalendarBrief) {
  if (date < startDate) return 'before';
  if (date > endDate) return 'after';
  return 'inside';
}

function placement(leg: FlightLinkLeg, brief: CalendarBrief): Placement {
  const departureDate = leg.departAt.slice(0, 10);
  const arrivalDate = leg.arriveAt.slice(0, 10);
  const departure = position(departureDate, brief);
  const arrival = position(arrivalDate, brief);
  if (departure === 'inside' && arrival === 'inside') {
    const originCity = airportCities.get(leg.origin);
    const destinationCity = airportCities.get(leg.destination);
    if (originCity && originCity !== brief.destinationId && destinationCity === brief.destinationId)
      return { date: arrivalDate, at: leg.arriveAt };
  }
  if (departure === 'inside') return { date: departureDate, at: leg.departAt };
  if (arrival === 'inside') return { date: arrivalDate, at: leg.arriveAt };
  return { section: departure === arrival ? departure : 'across', at: leg.departAt };
}

export function projectTripFlightCalendar(
  brief: CalendarBrief | null,
  observations: readonly Pick<TripFlightLinkCard, 'state' | 'facts'>[],
) {
  const outside: Record<OutsideSection, TripFlightCalendarCard[]> = { before: [], across: [], after: [] };
  const days = new Map<string, TripFlightCalendarCard[]>();
  if (!brief) return { ...outside, days };

  const placed: { card: TripFlightCalendarCard; target: Placement; instant: bigint }[] = [];
  for (const { state, facts } of observations) {
    if ((state !== 'available' && state !== 'stale') || !facts?.route) continue;
    for (const [index, leg] of facts.route.entries()) {
      const target = placement(leg, brief);
      placed.push({
        target,
        instant: flightLinkInstant(target.at),
        card: {
          key: `${facts.orderId}:${index}`,
          orderId: facts.orderId,
          leg,
          bookingStatus: facts.bookingStatus,
          reviewRequired: facts.reviewRequired,
          stale: state === 'stale',
        },
      });
    }
  }

  // Stable sort keeps the attachment and source-leg order when anchors are equal.
  placed.sort((a, b) => (a.instant < b.instant ? -1 : a.instant > b.instant ? 1 : 0));
  for (const { card, target } of placed) {
    if ('section' in target) {
      outside[target.section].push(card);
    } else {
      const cards = days.get(target.date);
      if (cards) cards.push(card);
      else days.set(target.date, [card]);
    }
  }
  return { ...outside, days };
}
