import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  type ConfirmedFlightOrderResponse,
  type FlightOffer,
  FlightQuoteContractError,
  FlightSearchContractError,
  type FlightSearchRequest,
  FlightsQuoteApiService,
  FlightsSearchApiService,
  type HeldFlightOrderResponse,
} from '@travel/api-client';
import { TravelButton } from '@travel/ui-kit';
import { catchError, map, of, Subject, startWith, switchMap, TimeoutError } from 'rxjs';
import { FlightsBookingPanelComponent } from './flight-booking-panel.component';
import { FlightOfferComponent } from './flight-offer.component';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
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
import { FlightsAuthService } from './flights-auth.service';
import { takeFlightsBookingDraft, writeFlightsBookingDraft } from './flights-booking-draft';
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
  imports: [
    ReactiveFormsModule,
    TravelButton,
    FlightOfferComponent,
    FlightQuotePanelComponent,
    FlightsBookingPanelComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flights-page.component.html',
  styleUrl: './flights-page.component.scss',
})
export class FlightsPageComponent {
  private readonly router = inject(Router);
  private readonly orderHandoff = inject(FlightOrderHandoffService);
  private readonly api = inject(FlightsSearchApiService);
  private readonly quoteApi = inject(FlightsQuoteApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly document = inject(DOCUMENT);
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

  openConfirmedOrder(confirmed: ConfirmedFlightOrderResponse): void {
    const auth = this.auth.status();
    if (auth.kind === 'authenticated') this.orderHandoff.rememberConfirmed(confirmed.aggregateId, auth.userId);
    void this.router.navigate(['/flights/orders', confirmed.aggregateId]);
  }

  rememberHeldOrder(held: HeldFlightOrderResponse): void {
    const auth = this.auth.status();
    if (auth.kind === 'authenticated') this.orderHandoff.rememberHeld(held.aggregateId, auth.userId);
  }
  readonly checkoutStarted = signal(false);
  readonly checkoutMessage = signal<string | null>(null);
  readonly authStatus = this.auth.status;
  private postLoginQuoteReady = false;

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
                  providerOfferRef: intent.providerOfferRef,
                  provider: intent.provider,
                  aggregateId: intent.aggregateId,
                })
                .pipe(
                  map((quote): QuoteState => {
                    if (
                      quote.offer.provider !== intent.provider ||
                      quote.offer.providerOfferRef !== intent.providerOfferRef ||
                      (intent.aggregateId !== null && quote.aggregateId !== intent.aggregateId)
                    ) {
                      throw new FlightQuoteContractError('offer.reference');
                    }
                    const changed =
                      intent.source === null
                        ? quote.priceChanged
                        : quote.priceChanged || quoteDiffersFromSearch(intent.source, quote.offer);
                    return {
                      kind: 'ready',
                      intent,
                      quote,
                      view: toFlightOfferView(quote.offer) as BookableOfferView,
                      searchView:
                        intent.source === null ? null : (toFlightOfferView(intent.source) as BookableOfferView),
                      routeChanged:
                        intent.source === null ? false : itineraryDiffersFromSearch(intent.source, quote.offer),
                      changed,
                      requiresAcceptance: intent.source === null || changed,
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
        if (value.kind === 'ready' && value.intent.source === null) {
          this.postLoginQuoteReady = true;
          this.checkoutMessage.set('Цена и маршрут обновлены после входа. Проверьте предложение перед оформлением.');
        }
        if (value.kind === 'ready') this.scheduleQuoteExpiry(value.quote.offer.expiresAt);
      });

    this.destroyRef.onDestroy(() => this.clearQuoteExpiry());

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.submitted.set(false);
      this.commands.next(null);
      this.quoteCommands.next(null);
      this.checkoutStarted.set(false);
      this.checkoutMessage.set(null);
      this.postLoginQuoteReady = false;
    });

    if (this.auth.hasOidcCallback()) void this.resumeBookingAfterLogin();
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
    this.checkoutStarted.set(false);
    this.checkoutMessage.set(null);
    this.postLoginQuoteReady = false;
    this.quoteCommands.next({
      source: offer,
      provider: offer.provider,
      providerOfferRef: offer.providerOfferRef,
      aggregateId: null,
    });
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
    this.checkoutStarted.set(false);
    this.quoteCommands.next({ ...current.intent, aggregateId: current.quote.aggregateId });
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

  async beginBooking(): Promise<void> {
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    if (Date.parse(current.quote.offer.expiresAt) <= Date.now()) {
      this.checkoutMessage.set('Срок предложения истёк. Обновите цену перед оформлением.');
      return;
    }
    this.checkoutMessage.set(null);

    if (this.auth.status().kind === 'authenticated') {
      if (this.postLoginQuoteReady) {
        this.checkoutStarted.set(true);
        this.checkoutMessage.set(null);
        return;
      }
      this.postLoginQuoteReady = false;
      this.quoteCommands.next({ ...current.intent, source: null, aggregateId: current.quote.aggregateId });
      this.checkoutMessage.set('После входа повторно проверяем цену и маршрут…');
      return;
    }

    if (this.isDemo) {
      if (await this.auth.beginLogin()) {
        this.postLoginQuoteReady = false;
        this.quoteCommands.next({ ...current.intent, source: null, aggregateId: current.quote.aggregateId });
        this.checkoutMessage.set('Демо вход завершён. Повторно проверяем вымышленное предложение…');
      } else this.checkoutMessage.set('Не удалось открыть вымышленное демо оформление.');
      return;
    }

    const storage = this.bookingDraftStorage();
    if (storage === null) {
      this.checkoutMessage.set('Браузер не предоставил временное хранилище для возврата после входа.');
      return;
    }

    try {
      writeFlightsBookingDraft(storage, {
        provider: current.intent.provider,
        providerOfferRef: current.intent.providerOfferRef,
        aggregateId: current.quote.aggregateId,
      });
    } catch {
      this.checkoutMessage.set('Не удалось временно сохранить предложение для возврата после входа.');
      return;
    }

    const continued = await this.auth.beginLogin();
    if (continued) {
      const draft = takeFlightsBookingDraft(storage);
      if (draft === null) {
        this.checkoutMessage.set('Черновик оформления истёк. Проверьте предложение ещё раз.');
        return;
      }
      this.postLoginQuoteReady = false;
      this.quoteCommands.next({ ...draft, source: null, aggregateId: draft.aggregateId });
      return;
    }

    if (this.auth.status().kind !== 'redirecting') {
      takeFlightsBookingDraft(storage);
      const status = this.auth.status();
      this.checkoutMessage.set(
        status.kind === 'unavailable' || status.kind === 'error' ? status.message : 'Вход не выполнен.',
      );
    }
  }

  logout(): void {
    this.checkoutStarted.set(false);
    void this.auth.logout();
  }

  refreshCheckoutQuote(): void {
    if (!this.checkoutStarted()) return;
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    this.checkoutStarted.set(false);
    this.postLoginQuoteReady = false;
    this.quoteCommands.next({ ...current.intent, source: null, aggregateId: current.quote.aggregateId });
    this.checkoutMessage.set('Повторно проверяем цену и маршрут. Данные пассажира удалены.');
  }

  restartCheckoutSearch(): void {
    this.checkoutStarted.set(false);
    this.postLoginQuoteReady = false;
    this.commands.next(null);
    this.quoteCommands.next(null);
    this.checkoutMessage.set('Срок удержания истёк. Выполните новый поиск и проверьте цену заново.');
  }

  private async resumeBookingAfterLogin(): Promise<void> {
    const authenticated = await this.auth.initializeFromCallback();
    const storage = this.bookingDraftStorage();
    const draft = storage === null ? null : takeFlightsBookingDraft(storage);
    if (!authenticated || draft === null) {
      this.checkoutMessage.set(
        authenticated
          ? 'Черновик оформления истёк или отсутствует. Проверьте предложение заново.'
          : 'Вход не выполнен. Проверьте предложение и попробуйте снова.',
      );
      return;
    }
    this.postLoginQuoteReady = false;
    this.quoteCommands.next({ ...draft, source: null, aggregateId: draft.aggregateId });
  }

  private bookingDraftStorage(): Storage | null {
    try {
      return this.document.defaultView?.sessionStorage ?? null;
    } catch {
      return null;
    }
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
