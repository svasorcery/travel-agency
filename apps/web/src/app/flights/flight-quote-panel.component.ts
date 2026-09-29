import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FlightOfferComponent } from './flight-offer.component';
import type { QuoteState } from './flight-quote';
import { formatFlightPrice, formatOffsetTime } from './flight-results';

@Component({
  selector: 'app-flight-quote-panel',
  standalone: true,
  imports: [FlightOfferComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-quote-panel.component.html',
  styleUrl: './flight-quote-panel.component.scss',
})
export class FlightQuotePanelComponent {
  readonly state = input.required<Exclude<QuoteState, { kind: 'idle' }>>();
  readonly isDemo = input.required<boolean>();
  readonly now = input.required<number>();
  readonly retryRequested = output<void>();
  readonly requoteRequested = output<void>();
  readonly acceptRequested = output<void>();
  readonly formatOffsetTime = formatOffsetTime;
  readonly formatFlightPrice = formatFlightPrice;

  expired(expiresAt: string): boolean {
    return Date.parse(expiresAt) <= this.now();
  }
}
