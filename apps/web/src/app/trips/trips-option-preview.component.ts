import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  HostListener,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import {
  type TripBrief,
  type TripOptionAvailability,
  type TripOptionPreview,
  type TripOptionSelection,
  TripsContractError,
  TripsOptionApiService,
  tripBrief,
} from '@travel/api-client';
import { EMPTY, from, type Subscription, switchMap, TimeoutError } from 'rxjs';
import { TripOptionCardComponent } from './trip-option-card.component';
import { TripsStateService } from './trips-state.service';

type PreviewState = {
  ticket: object;
  draft: TripBrief;
  busy: boolean;
  message: string;
  result: TripOptionPreview | null;
  selection: TripOptionSelection | null;
};
@Component({
  selector: 'app-trips-option-preview',
  standalone: true,
  imports: [TripOptionCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-option-preview.component.html',
  styleUrl: './trips-option-preview.component.scss',
})
export class TripsOptionPreviewComponent {
  readonly draft = input.required<TripBrief>();
  readonly saved = output<string>();
  readonly editor = inject(TripsStateService);
  private readonly api = inject(TripsOptionApiService);
  private readonly destroy = inject(DestroyRef);
  private readonly scope = computed(() => (this.editor.selectedId() === null ? this.editor.itineraryContext() : null));
  private readonly offer = signal<{ scope: object; value: TripOptionAvailability } | null>(null);
  readonly availability = computed(() => {
    const offer = this.offer();
    return offer && offer.scope === this.scope() ? offer.value : null;
  });
  private readonly attempt = signal<PreviewState | null>(null);
  readonly view = computed(() => {
    const state = this.attempt();
    return state &&
      this.availability() &&
      state.draft === this.draft() &&
      this.editor.isBriefTicketCurrent(state.ticket)
      ? state
      : null;
  });
  private request?: Subscription;
  constructor() {
    effect((onCleanup) => {
      const scope = this.scope();
      if (!scope) return;
      const epoch = this.editor.auth.identityEpoch();
      const discovery = untracked(() =>
        from(this.editor.auth.accessToken())
          .pipe(switchMap((token) => (scope === this.scope() ? this.api.availability(token) : EMPTY)))
          .subscribe({
            next: (value) => {
              if (scope === this.scope()) this.offer.set({ scope, value });
            },
            error: (error) => {
              if (scope === this.scope() && error instanceof HttpErrorResponse && error.status === 401)
                this.editor.auth.deny(epoch);
            },
          }),
      );
      onCleanup(() => {
        discovery.unsubscribe();
        this.offer.set(null);
      });
    });
    effect(() => {
      if (this.attempt() && !this.view())
        untracked(() => {
          this.request?.unsubscribe();
          this.attempt.set(null);
        });
    });
    effect((onCleanup) => {
      const view = this.view();
      if (!view?.selection) return;
      const remaining = Date.parse(view.selection.expiresAtUtc) - Date.now();
      if (remaining <= 0) this.expire(view);
      else {
        const timer = setTimeout(() => this.expire(view), Math.min(remaining, 2147483647));
        onCleanup(() => clearTimeout(timer));
      }
    });
    this.destroy.onDestroy(() => this.request?.unsubscribe());
  }
  select(): void {
    const discovery = this.availability(),
      ticket = this.editor.captureBriefTicket();
    if (!discovery || !ticket || this.view()?.busy || this.editor.optionSave.phase()) return;
    this.editor.optionSave.clear();
    this.request?.unsubscribe();
    const draft = this.draft(),
      snapshot = structuredClone(draft);
    const epoch = this.editor.auth.identityEpoch();
    const update = (
      message: string,
      busy = false,
      result: TripOptionPreview | null = null,
      selection: TripOptionSelection | null = null,
    ) => this.attempt.set({ ticket, draft, busy, message, result, selection });
    if (!snapshot.originId) {
      update('Укажите город отправления в поле «Откуда».');
      return;
    }
    try {
      tripBrief(snapshot);
    } catch {
      update('Проверьте даты, число взрослых и сумму бюджета в форме.');
      return;
    }
    update('Подбираем вариант поездки…', true);
    const current = () => this.view()?.ticket === ticket;
    this.request = from(this.editor.auth.accessToken())
      .pipe(switchMap((token) => (current() ? this.api.preview(snapshot, discovery, token) : EMPTY)))
      .subscribe({
        next: ({ preview: result, selection }) => {
          if (current())
            update(result.option ? 'Вариант поездки готов.' : 'Подбор завершён.', false, result, selection);
        },
        error: (error) => {
          if (!current()) return;
          const status = error instanceof HttpErrorResponse ? error.status : 0;
          if (status === 401) this.editor.auth.deny(epoch);
          else if (status === 404) this.offer.set(null);
          else
            update(
              error instanceof TripsContractError
                ? 'Ответ подбора не соответствует данным примера.'
                : error instanceof TimeoutError || status === 504
                  ? 'Время подбора истекло. Попробуйте ещё раз.'
                  : status === 429
                    ? 'Подбор занят. Попробуйте ещё раз.'
                    : status === 400 || status === 413
                      ? 'Проверьте значения полей поездки.'
                      : 'Данные для подбора сейчас недоступны.',
            );
        },
      });
  }
  async save(): Promise<void> {
    const view = this.view();
    if (!view?.selection || !view.result?.option) return;
    if (Date.parse(view.selection.expiresAtUtc) <= Date.now()) {
      this.expire(view);
      return;
    }
    const scope = this.scope();
    const id = await this.editor.optionSave.begin(
      view.selection,
      this.editor.buffer()?.title ?? '',
      view.draft,
      view.result,
    );
    if (id && scope === this.scope()) this.saved.emit(id);
  }
  private expire(view: PreviewState): void {
    if (this.attempt() === view)
      this.attempt.set({ ...view, selection: null, message: 'Срок выбора истёк. Подберите вариант снова.' });
  }
  changeTitle(change: () => void): void {
    const previous = this.view();
    change();
    if (!previous?.selection) return;
    const ticket = this.editor.captureBriefTicket();
    if (ticket) this.attempt.set({ ...previous, ticket });
  }
  @HostListener('window:pagehide') leave(): void {
    this.request?.unsubscribe();
    this.attempt.set(null);
  }
  dateLabel(iso: string): string {
    return new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(
      new Date(iso),
    );
  }
}
