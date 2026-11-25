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
  signal,
  untracked,
} from '@angular/core';
import {
  type TripBrief,
  type TripCurrency,
  type TripOptionAvailability,
  type TripOptionPreview,
  TripsContractError,
  TripsOptionApiService,
  tripBrief,
  tripDecimal,
  tripMinor,
} from '@travel/api-client';
import { EMPTY, from, type Subscription, switchMap, TimeoutError } from 'rxjs';
import { TripsStateService } from './trips-state.service';

type PreviewState = {
  ticket: object;
  draft: TripBrief;
  busy: boolean;
  message: string;
  result: TripOptionPreview | null;
};
const labels = {
  directionUnsupported: 'Пример доступен для поездки из Москвы в Санкт-Петербург и обратно.',
  dateRangeUnsupported: 'Для примера выберите два или три календарных дня с одной или двумя ночами.',
  groupUnsupported: 'В одной комнате примера могут разместиться один или два взрослых.',
  horizonUnsupported: 'Выбранные даты выходят за период подбора исторического примера.',
  flightUnavailable: 'На эти даты и состав группы нет перелёта туда и обратно.',
  hotelUnavailable: 'В гостиницах нет номера для всей группы на все ночи поездки.',
  timeIncompatible: 'Время перелёта несовместимо с заездом или выездом из гостиницы.',
  budgetExceeded: 'Известные обязательные расходы превышают указанный бюджет.',
  noWalks: 'Ни в один из дней не удалось вместить прогулку.',
  onlyCompatibleOption: 'Только это сочетание перелёта и проживания подошло по заданным условиям.',
  moreInterests: 'Программа охватывает больше выбранных интересов.',
  moreStops: 'В заданном темпе можно посетить больше мест.',
  completeFlightAndStay: 'При равной программе известна полная цена перелёта и проживания.',
  lowerComparablePrice: 'При равной программе и полноте цены это сочетание стоит меньше.',
  equalByStableIds: 'Варианты равноценны. Для воспроизводимости выбран первый по идентификаторам предложений.',
  noWalkingWindow: 'После перелёта, заезда или выезда и дороги времени на прогулку не остаётся.',
  noEligibleStops: 'В доступный интервал не помещается подходящая прогулка.',
  architecture: 'архитектура',
  culture: 'культура',
  nature: 'природа',
  history: 'история',
  food: 'еда',
  flightBase: 'Перелёт туда и обратно',
  hotelBase: 'Проживание за все ночи',
  includedTax: 'Включённые налоги и сборы',
  localFee: 'Местный сбор',
  meals: 'питание',
  transfers: 'трансферы',
};
@Component({
  selector: 'app-trips-option-preview',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-option-preview.component.html',
  styleUrl: './trips-option-preview.component.scss',
})
export class TripsOptionPreviewComponent {
  readonly draft = input.required<TripBrief>();
  private readonly editor = inject(TripsStateService);
  private readonly api = inject(TripsOptionApiService);
  private readonly destroy = inject(DestroyRef);
  readonly labels = labels;
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
  readonly costs = computed(() => {
    const view = this.view();
    const price = view?.result?.option?.price;
    if (!view || !price) return null;
    const amounts = new Map<TripCurrency, bigint>();
    const knownComponents = new Set<string>();
    let unknownMandatory = false;
    for (const line of price.components) {
      if (line.kind === 'unknownMandatory') unknownMandatory = true;
      if (line.kind !== 'travelerMandatory') continue;
      if (line.currency === null || line.amount === null) {
        unknownMandatory = true;
        continue;
      }
      knownComponents.add(line.component);
      amounts.set(line.currency, (amounts.get(line.currency) ?? 0n) + tripMinor(line.amount));
    }
    return {
      totals: Array.from(amounts, ([currency, minor]) => ({ currency, amount: tripDecimal(minor) })),
      foreignCurrency: Array.from(amounts).some(([currency, minor]) => currency !== price.currency && minor !== 0n),
      knownComponents,
      incomplete: unknownMandatory || !knownComponents.has('flightBase') || !knownComponents.has('hotelBase'),
      budget: view.draft.totalBudget,
    };
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
    this.destroy.onDestroy(() => this.request?.unsubscribe());
  }
  select(): void {
    const discovery = this.availability(),
      ticket = this.editor.captureBriefTicket();
    if (!discovery || !ticket || this.view()?.busy) return;
    this.request?.unsubscribe();
    const draft = this.draft(),
      snapshot = structuredClone(draft);
    const epoch = this.editor.auth.identityEpoch();
    const update = (message: string, busy = false, result: TripOptionPreview | null = null) =>
      this.attempt.set({ ticket, draft, busy, message, result });
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
        next: (result) => {
          if (current()) update(result.option ? 'Вариант поездки готов.' : 'Подбор завершён.', false, result);
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
  localTime(iso: string): string {
    return iso.slice(0, 10) + ' ' + iso.slice(11, 16);
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
  amount(amount: string, currency: string): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency,
      minimumFractionDigits: Number.isInteger(Number(amount)) ? 0 : 2,
    }).format(Number(amount));
  }
}
