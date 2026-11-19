import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { TripFlightCalendarCard } from './trips-flight-calendar';
import {
  flightObservationRequiresReview,
  linkedFlightBookingLabels,
  staleFlightObservation,
} from './trips-flight-labels';

@Component({
  selector: 'app-trips-flight-calendar-cards',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-flight-calendar-cards.component.html',
  styles: `
    .flight-calendar-group { margin-block: 0.75rem 1rem }
    h4 { margin: 0 0 0.5rem; font-size: 1rem }
    .flight-calendar-card {
      background: oklch(0.97 0.018 245);
      border: 1px solid oklch(0.84 0.03 245);
      border-inline-start: 3px solid oklch(0.51 0.115 245);
      border-radius: 8px;
      margin-block-end: 0.5rem;
      padding: 0.75rem 1rem;
    }
    p { margin: 0 0 0.35rem }
    .source, .notice { font-size: 0.875rem }
    .source { color: oklch(0.35 0.064 245) }
    .endpoints { margin: 0.5rem 0 0; padding-inline-start: 1.25rem }
    .endpoints li + li { margin-block-start: 0.25rem }
    time { font-variant-numeric: tabular-nums }
    a { display: inline-flex; align-items: center; min-block-size: 44px; color: oklch(0.39 0.09 245) }
  `,
})
export class TripsFlightCalendarCardsComponent {
  readonly cards = input.required<readonly TripFlightCalendarCard[]>();
  readonly heading = input.required<string>();
  readonly bookingLabels = linkedFlightBookingLabels;
  readonly staleNotice = staleFlightObservation;
  readonly reviewNotice = flightObservationRequiresReview;

  localTime(value: string): string {
    const hasUtcSuffix = value.endsWith('Z');
    const offset = hasUtcSuffix ? '+00:00' : value.slice(-6);
    const clock = value.slice(11, hasUtcSuffix ? -1 : -6).replace(/:00(?:\.0+)?$/, '');
    const date = value.slice(0, 10).split('-').reverse().join('.');
    return `${date}, ${clock} UTC${offset}`;
  }
}
