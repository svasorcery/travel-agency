import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { type SavedTripOption, TripsSavedOptionsApiService } from '@travel/api-client';
import { EMPTY, from, switchMap } from 'rxjs';
import { TripOptionCardComponent } from './trip-option-card.component';
import { TripsStateService } from './trips-state.service';

@Component({
  selector: 'app-trips-saved-option',
  standalone: true,
  imports: [TripOptionCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (current(); as entry) {
      @if (entry.saved; as item) {
        <section id="trip-saved-option" aria-labelledby="trip-saved-heading">
          <h3 id="trip-saved-heading">Сохранённый вариант</h3>
          <p>Историческая оценка вымышленного варианта на
            <time data-option-asof [attr.datetime]="item.preview.asOfUtc">{{ dateTime(item.preview.asOfUtc) }}</time>.
            Сохранено <time data-saved-at [attr.datetime]="item.savedAtUtc">{{ dateTime(item.savedAtUtc) }}</time>.</p>
          @if (datesChanged()) {
            <p>Город или даты плана изменились. Здесь показаны исходные дни поездки в Петербург.</p>
          }
          <app-trip-option-card [result]="item.preview" [brief]="item.brief" [historical]="true" />
        </section>
      } @else if (entry.unavailable) {
        <p role="status">Сохранённый вариант сейчас недоступен.</p>
        <button type="button" (click)="retry()">Перечитать сохранённый вариант</button>
      }
    } @else {
      <p role="status">Загружаем сохранённый вариант…</p>
    }
  `,
})
export class TripsSavedOptionComponent {
  readonly id = input.required<string>();
  private readonly api = inject(TripsSavedOptionsApiService);
  private readonly editor = inject(TripsStateService);
  private readonly version = signal(0);
  private readonly scope = computed(() => {
    const id = this.id();
    return this.editor.authorized() && this.editor.baseline()?.tripId === id
      ? `${this.editor.auth.identityEpoch()}:${id}`
      : null;
  });
  private readonly entry = signal<{ scope: string; saved: SavedTripOption | null; unavailable: boolean } | null>(null);
  readonly current = computed(() => {
    const entry = this.entry();
    return entry && entry.scope === this.scope() ? entry : null;
  });
  readonly datesChanged = computed(() => {
    const saved = this.current()?.saved?.brief,
      now = this.editor.buffer()?.brief;
    return (
      !!now &&
      !!saved &&
      (now.destinationId !== saved.destinationId || now.startDate !== saved.startDate || now.endDate !== saved.endDate)
    );
  });
  constructor() {
    effect((onCleanup) => {
      this.version();
      const scope = this.scope();
      if (!scope) return;
      const id = this.id();
      const epoch = this.editor.auth.identityEpoch();
      const subscription = untracked(() =>
        from(this.editor.auth.accessToken())
          .pipe(switchMap((token) => (this.scope() === scope ? this.api.read(id, token) : EMPTY)))
          .subscribe({
            next: (saved) => {
              if (this.scope() === scope) this.entry.set({ scope, saved, unavailable: false });
            },
            error: (error) => {
              if (this.scope() !== scope) return;
              if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403))
                this.editor.auth.deny(epoch);
              else this.entry.set({ scope, saved: null, unavailable: true });
            },
          }),
      );
      onCleanup(() => subscription.unsubscribe());
    });
  }
  retry(): void {
    this.entry.set(null);
    this.version.update((v) => v + 1);
  }
  dateTime(value: string): string {
    return (
      new Intl.DateTimeFormat('ru-RU', {
        dateStyle: 'long',
        timeStyle: 'short',
        timeZone: 'UTC',
      }).format(new Date(value)) + ' UTC'
    );
  }
}
