import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import type { FlightAncillaryCatalog, FlightAncillarySelection } from '@travel/api-client';

@Component({
  selector: 'app-flight-baggage-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-baggage-picker.component.html',
  styleUrl: './flight-baggage-picker.component.scss',
})
export class FlightBaggagePickerComponent {
  readonly catalog = input.required<FlightAncillaryCatalog>();
  readonly passengerIds = input.required<string[]>();
  readonly draft = input.required<FlightAncillarySelection[]>();
  readonly disabled = input(false);
  readonly changed = output<FlightAncillarySelection>();
  quantity(key: string): number {
    return this.draft().find((s) => s.selectionKey === key)?.quantity ?? 0;
  }
  choices(maximum: number): number[] {
    return Array.from({ length: maximum + 1 }, (_, n) => n);
  }
  passenger(id: string): string {
    const index = this.passengerIds().indexOf(id);
    return index < 0 ? 'Пассажир' : `Пассажир ${index + 1}`;
  }
  select(key: string, event: Event): void {
    if (event.target instanceof HTMLSelectElement)
      this.changed.emit({ selectionKey: key, quantity: Number(event.target.value) });
  }
}
