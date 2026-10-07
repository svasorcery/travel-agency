import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import type {
  FlightAncillaryCatalog,
  FlightAncillarySelection,
  FlightAncillaryService,
  FlightSegmentAddress,
} from '@travel/api-client';

@Component({
  selector: 'app-flight-seat-picker',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-seat-picker.component.html',
  styleUrl: './flight-seat-picker.component.scss',
})
export class FlightSeatPickerComponent {
  readonly catalog = input.required<FlightAncillaryCatalog>();
  readonly passengerIds = input.required<string[]>();
  readonly draft = input.required<FlightAncillarySelection[]>();
  readonly disabled = input(false);
  readonly changed = output<FlightAncillarySelection>();
  private readonly byKey = computed(() => new Map(this.catalog().services.map((s) => [s.selectionKey, s])));
  private readonly chosen = computed(() => new Set(this.draft().map((s) => s.selectionKey)));
  private readonly byPassengerScope = computed(() => {
    const result = new Map<string, FlightAncillaryService[]>();
    for (const service of this.catalog().services) {
      const scope = service.segments[0];
      if (service.kind !== 'seat' || !service.selectable || scope === undefined) continue;
      for (const passenger of service.bookingPassengerIds) {
        const key = `${passenger}:${scope.leg}:${scope.segment}`;
        const list = result.get(key) ?? [];
        list.push(service);
        result.set(key, list);
      }
    }
    return result;
  });
  services(keys: string[], passenger: string): FlightAncillaryService[] {
    return keys
      .map((key) => this.byKey().get(key))
      .filter((s): s is FlightAncillaryService => s !== undefined && s.bookingPassengerIds.includes(passenger));
  }
  selected(key: string): boolean {
    return this.chosen().has(key);
  }
  choices(passenger: string, scope: FlightSegmentAddress): FlightAncillaryService[] {
    return this.byPassengerScope().get(`${passenger}:${scope.leg}:${scope.segment}`) ?? [];
  }
  current(passenger: string, scope: FlightSegmentAddress): string {
    return this.choices(passenger, scope).find((s) => this.selected(s.selectionKey))?.selectionKey ?? '';
  }
  select(passenger: string, scope: FlightSegmentAddress, event: Event): void {
    if (!(event.target instanceof HTMLSelectElement)) return;
    const key = event.target.value;
    const old = this.current(passenger, scope);
    if (key === '' && old) this.changed.emit({ selectionKey: old, quantity: 0 });
    else if (key) this.changed.emit({ selectionKey: key, quantity: 1 });
  }
}
