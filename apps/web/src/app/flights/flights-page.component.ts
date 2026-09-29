import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import {
  type FlightOffer,
  FlightQuoteContractError,
  FlightSearchContractError,
  type FlightSearchRequest,
  FlightsQuoteApiService,
  FlightsSearchApiService,
} from '@travel/api-client';
import { TravelButton } from '@travel/ui-kit';
import { catchError, map, of, Subject, startWith, switchMap, TimeoutError } from 'rxjs';
import { FlightOfferComponent } from './flight-offer.component';
import { itineraryDiffersFromSearch, type QuoteIntent, type QuoteState, quoteDiffersFromSearch } from './flight-quote';
import { FlightQuotePanelComponent } from './flight-quote-panel.component';
import {
  type BookableOfferView,
  type FlightOfferView,
  summarizeSearchResponse,
  toFlightOfferView,
} from './flight-results';
import {
  createFlightSearchForm,
  type FlightSearchErrors,
  fillFlightSearchExample,
  localToday,
  toFlightSearchRequest,
} from './flight-search-form';
import { isDemoSource } from './flights-source-mode';

type PageState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ready'; offers: FlightOfferView[]; rawOffers: FlightOffer[]; partial: boolean; currencyMismatch: boolean }
  | { kind: 'error'; message: string };

function userError(error: unknown): string {
  if (error instanceof FlightSearchContractError) {
    return 'Не удалось прочитать результаты. Повторите поиск.';
  }
  if (error instanceof TimeoutError) return 'Поиск занял слишком много времени. Повторите попытку.';
  if (error instanceof HttpErrorResponse) {
    if (error.status === 200) return 'Не удалось прочитать результаты. Повторите поиск.';
    if (error.status === 400) return 'Проверьте условия поиска и повторите попытку.';
    if (error.status === 401 || error.status === 403) {
      return 'Поиск сейчас недоступен из-за настройки доступа.';
    }
    if (error.status === 0) return 'Нет соединения с поиском. Проверьте подключение и повторите попытку.';
  }
  return 'Сервис поиска временно недоступен. Повторите попытку.';
}

function quoteError(error: unknown, reQuote: boolean): string {
  const next = reQuote ? 'Повторите обновление вручную.' : 'Начните новую проверку вручную.';
  if (error instanceof FlightQuoteContractError || (error instanceof HttpErrorResponse && error.status === 200)) {
    return `Не удалось прочитать проверенное предложение. ${next}`;
  }
  if (error instanceof HttpErrorResponse) {
    if (error.status === 404 || error.status === 410)
      return 'Предложение больше недоступно. Выберите другой вариант или попробуйте проверить ещё раз.';
    if (error.status === 409) return 'Не удалось обновить предложение: конфликт состояния. Проверьте цену ещё раз.';
    if (error.status === 400) return 'Не удалось проверить выбранное предложение. Выберите другой вариант.';
    if (error.status === 401 || error.status === 403)
      return 'Анонимная проверка цены недоступна из-за настройки доступа.';
  }
  return `Ответ на проверку цены не получен. Исход запроса неизвестен. ${next}`;
}

@Component({
  selector: 'app-flights-page',
  standalone: true,
  imports: [ReactiveFormsModule, TravelButton, FlightOfferComponent, FlightQuotePanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flights-page.component.html',
  styleUrl: './flights-page.component.scss',
})
export class FlightsPageComponent {
  private readonly api = inject(FlightsSearchApiService);
  private readonly quoteApi = inject(FlightsQuoteApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly commands = new Subject<FlightSearchRequest | null>();
  private readonly quoteCommands = new Subject<QuoteIntent | null>();
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly form = createFlightSearchForm(localToday);
  readonly today = localToday;
  readonly submitted = signal(false);
  readonly state = signal<PageState>({ kind: 'idle' });
  readonly quoteState = signal<QuoteState>({ kind: 'idle' });
  readonly now = signal(Date.now());
  readonly isDemo = isDemoSource();

  constructor() {
    this.commands
      .pipe(
        switchMap((request) =>
          request === null
            ? of<PageState>({ kind: 'idle' })
            : this.api.search(request).pipe(
                map(
                  (response): PageState => ({
                    kind: 'ready',
                    rawOffers: response.offers,
                    ...summarizeSearchResponse(response),
                  }),
                ),
                catchError((error: unknown) => of<PageState>({ kind: 'error', message: userError(error) })),
                startWith<PageState>({ kind: 'loading' }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((value) => this.state.set(value));

    this.quoteCommands
      .pipe(
        switchMap((intent) =>
          intent === null
            ? of<QuoteState>({ kind: 'idle' })
            : this.quoteApi
                .quote({
                  providerOfferRef: intent.source.providerOfferRef,
                  provider: intent.source.provider,
                  aggregateId: intent.aggregateId,
                })
                .pipe(
                  map((quote): QuoteState => {
                    if (
                      quote.offer.provider !== intent.source.provider ||
                      quote.offer.providerOfferRef !== intent.source.providerOfferRef ||
                      (intent.aggregateId !== null && quote.aggregateId !== intent.aggregateId)
                    ) {
                      throw new FlightQuoteContractError('offer.reference');
                    }
                    return {
                      kind: 'ready',
                      intent,
                      quote,
                      view: toFlightOfferView(quote.offer) as BookableOfferView,
                      searchView: toFlightOfferView(intent.source) as BookableOfferView,
                      routeChanged: itineraryDiffersFromSearch(intent.source, quote.offer),
                      changed: quote.priceChanged || quoteDiffersFromSearch(intent.source, quote.offer),
                      accepted: false,
                    };
                  }),
                  catchError((error: unknown) =>
                    of<QuoteState>({ kind: 'error', intent, message: quoteError(error, intent.aggregateId !== null) }),
                  ),
                  startWith<QuoteState>({ kind: 'loading', intent }),
                ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((value) => {
        this.clearQuoteExpiry();
        this.now.set(Date.now());
        this.quoteState.set(value);
        if (value.kind === 'ready') this.scheduleQuoteExpiry(value.quote.offer.expiresAt);
      });

    this.destroyRef.onDestroy(() => this.clearQuoteExpiry());

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.submitted.set(false);
      this.commands.next(null);
      this.quoteCommands.next(null);
    });
  }

  fieldError(field: keyof FlightSearchErrors): string | null {
    if (!this.submitted()) return null;
    const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
    return errors?.[field] ?? null;
  }

  submit(): void {
    if (this.state().kind === 'loading') return;
    this.quoteCommands.next(null);
    this.submitted.set(true);
    this.form.updateValueAndValidity({ emitEvent: false });
    if (this.form.invalid) {
      const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
      const first = errors ? Object.keys(errors)[0] : undefined;
      if (first) {
        this.element.nativeElement.querySelector<HTMLInputElement>(`[formControlName="${first}"]`)?.focus();
      }
      return;
    }
    this.commands.next(toFlightSearchRequest(this.form));
  }

  retry(): void {
    this.submit();
  }

  example(): void {
    fillFlightSearchExample(this.form, localToday());
  }

  checkOffer(id: string): void {
    if (this.quoteState().kind === 'loading') return;
    const current = this.state();
    if (current.kind !== 'ready') return;
    const offer = current.rawOffers.find((item) => item.id === id);
    if (!offer || offer.providerOfferRef === null) return;
    this.quoteCommands.next({ source: offer, aggregateId: null });
    setTimeout(() => {
      if (this.quoteState().kind === 'idle') return;
      const heading = this.element.nativeElement.querySelector<HTMLElement>('#quote-heading');
      heading?.focus({ preventScroll: true });
      heading?.scrollIntoView?.({ block: 'start' });
    }, 0);
  }

  reQuote(): void {
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    this.quoteCommands.next({ source: current.intent.source, aggregateId: current.quote.aggregateId });
  }

  retryQuote(): void {
    const current = this.quoteState();
    if (current.kind !== 'error') return;
    this.quoteCommands.next(current.intent);
  }

  acceptQuote(): void {
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    const now = Date.now();
    this.now.set(now);
    if (Date.parse(current.quote.offer.expiresAt) <= now) return;
    this.quoteState.set({ ...current, accepted: true });
  }

  private clearQuoteExpiry(): void {
    if (this.expiryTimer !== null) clearTimeout(this.expiryTimer);
    this.expiryTimer = null;
  }

  private scheduleQuoteExpiry(expiresAt: string): void {
    const delay = Date.parse(expiresAt) - Date.now();
    if (delay <= 0 || delay >= 2_147_483_647) return;
    this.expiryTimer = setTimeout(() => this.now.set(Date.now()), delay + 1);
  }
}
