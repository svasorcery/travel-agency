import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  type ConfirmedFlightOrderResponse,
  type FlightOffer,
  FlightQuoteContractError,
  type FlightQuoteResponse,
  FlightSearchContractError,
  type FlightSearchRequest,
  FlightsQuoteApiService,
  FlightsSearchApiService,
  type HeldFlightOrderResponse,
} from '@travel/api-client';
import { TravelButton } from '@travel/ui-kit';
import { catchError, from, map, type Observable, of, Subject, startWith, switchMap, TimeoutError } from 'rxjs';
import { FlightsBookingPanelComponent } from './flight-booking-panel.component';
import { FlightOfferComponent } from './flight-offer.component';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { itineraryDiffersFromSearch, type QuoteIntent, type QuoteState, quoteDiffersFromSearch } from './flight-quote';
import { FlightQuotePanelComponent } from './flight-quote-panel.component';
import {
  type BookableOfferView,
  type FlightOfferView,
  summarizeSearchResponse,
  toFlightOfferView,
} from './flight-results';
import {
  addFlightLeg,
  createFlightSearchForm,
  type FlightLegField,
  type FlightSearchErrors,
  type FlightSearchIntent,
  fillFlightSearchExample,
  localToday,
  removeFlightLeg,
  toFlightSearchIntent,
} from './flight-search-form';
import { FlightsAuthService } from './flights-auth.service';
import { isDemoSource } from './flights-source-mode';

type PageState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | {
      kind: 'ready';
      offers: FlightOfferView[];
      rawOffers: FlightOffer[];
      passengerCount: FlightSearchRequest['passengerCount'];
      skipped: boolean;
      journeySkipped: boolean;
      partial: boolean;
      currencyMismatch: boolean;
      rankingAvailable?: boolean;
    }
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
    RouterLink,
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
  private readonly operations = inject(FlightOrderOperationsService);
  private readonly orderHandoff = inject(FlightOrderHandoffService);
  private readonly api = inject(FlightsSearchApiService);
  private readonly quoteApi = inject(FlightsQuoteApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly commands = new Subject<FlightSearchIntent | null>();
  private readonly quoteCommands = new Subject<QuoteIntent | null>();
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly form = createFlightSearchForm(localToday);
  readonly today = localToday;
  readonly legFields: readonly FlightLegField[] = ['origin', 'destination', 'departureDate'];
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
  // Public quote facts keep the mounted draft in this page; passenger data stays inside the panel.
  readonly checkoutQuote = signal<FlightQuoteResponse | null>(null);
  readonly checkoutQuoteAccepted = computed(() => {
    const state = this.quoteState();
    const quote = this.checkoutQuote();
    return (
      state.kind === 'ready' &&
      state.accepted &&
      quote !== null &&
      state.quote === quote &&
      this.readyAuthContext === this.authContext()
    );
  });
  readonly checkoutReviewRequired = computed(() => !this.checkoutQuoteAccepted());
  readonly checkoutMessage = signal<string | null>(null);
  readonly authStatus = this.auth.status;
  private postLoginQuoteReady = false;
  private readyAuthContext: string | null = null;
  private destroyed = false;
  private previousIdentity = this.identity();
  private previousTripType = this.form.controls.tripType.value;

  constructor() {
    this.removeLegacyBookingDraft();
    effect(() => {
      const identity = this.identity();
      untracked(() => {
        if (this.previousIdentity !== null && identity !== this.previousIdentity) {
          this.checkoutStarted.set(false);
          this.postLoginQuoteReady = false;
          this.quoteCommands.next(null);
        }
        this.previousIdentity = identity;
      });
    });
    this.commands
      .pipe(
        switchMap((request) =>
          request === null
            ? of<PageState>({ kind: 'idle' })
            : (request.kind === 'v2'
                ? this.api.searchMultiLeg(request.request)
                : this.api.search(request.request)
              ).pipe(
                map(
                  (response): PageState => ({
                    kind: 'ready',
                    rawOffers: response.offers,
                    passengerCount: request.request.passengerCount,
                    skipped: response.skippedProviders.some(
                      (skip) => skip.reasonCode === 'passenger-count-unsupported',
                    ),
                    journeySkipped: response.skippedProviders.some((skip) => skip.reasonCode === 'journey-unsupported'),
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
        switchMap((intent) => {
          const authContext = this.authContext();
          return intent === null
            ? of<QuoteState>({ kind: 'idle' })
            : this.quoteWithIdentity(intent).pipe(
                map((quote): QuoteState => {
                  if (authContext !== this.authContext()) return { kind: 'idle' };
                  if (
                    quote.binding.passengerCount !== intent.passengerCount ||
                    quote.offer.passengerCount !== intent.passengerCount ||
                    quote.offer.provider !== intent.provider ||
                    quote.offer.providerOfferRef !== intent.providerOfferRef ||
                    (intent.aggregateId !== null && quote.aggregateId !== intent.aggregateId)
                  ) {
                    throw new FlightQuoteContractError('offer.reference');
                  }
                  const previous = intent.previousQuote ?? intent.source;
                  const changed =
                    quote.priceChanged ||
                    (previous !== null && quoteDiffersFromSearch(previous, quote.offer)) ||
                    (intent.previousBinding !== undefined &&
                      JSON.stringify(intent.previousBinding) !== JSON.stringify(quote.binding));
                  return {
                    kind: 'ready',
                    intent,
                    quote,
                    view: toFlightOfferView(quote.offer) as BookableOfferView,
                    searchView: previous === null ? null : (toFlightOfferView(previous) as BookableOfferView),
                    routeChanged: previous !== null && itineraryDiffersFromSearch(previous, quote.offer),
                    changed,
                    requiresAcceptance: true,
                    accepted: false,
                  };
                }),
                catchError((error: unknown) =>
                  of<QuoteState>(
                    authContext !== this.authContext()
                      ? { kind: 'idle' }
                      : { kind: 'error', intent, message: quoteError(error, intent.aggregateId !== null) },
                  ),
                ),
                startWith<QuoteState>({ kind: 'loading', intent }),
              );
        }),
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
        if (value.kind === 'ready') {
          if (this.checkoutStarted()) this.checkoutQuote.set(value.quote);
          this.readyAuthContext = this.authContext();
          this.scheduleQuoteExpiry(value.quote.offer.expiresAt);
        }
      });

    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.clearQuoteExpiry();
    });

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const tripType = this.form.controls.tripType.value;
      if (tripType !== 'multiLeg' || this.previousTripType !== tripType) this.submitted.set(false);
      this.previousTripType = tripType;
      this.commands.next(null);
      this.quoteCommands.next(null);
      this.checkoutStarted.set(false);
      this.checkoutMessage.set(null);
      this.postLoginQuoteReady = false;
    });

    if (this.auth.hasOidcCallback()) void this.resumeBookingAfterLogin();
  }

  fieldError(field: Exclude<keyof FlightSearchErrors, 'legs'>): string | null {
    if (!this.submitted()) return null;
    const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
    return errors?.[field] ?? null;
  }

  legError(index: number, field: FlightLegField): string | null {
    if (!this.submitted()) return null;
    return (this.form.errors?.['search'] as FlightSearchErrors | undefined)?.legs?.[index]?.[field] ?? null;
  }

  addLeg(): void {
    const index = this.form.controls.legs.length;
    addFlightLeg(this.form);
    if (this.form.controls.legs.length > index)
      setTimeout(
        () => this.element.nativeElement.querySelector<HTMLInputElement>(`#flight-leg-${index}-origin`)?.focus(),
        0,
      );
  }

  removeLeg(index: number): void {
    const length = this.form.controls.legs.length;
    removeFlightLeg(this.form, index);
    if (this.form.controls.legs.length < length)
      setTimeout(
        () =>
          this.element.nativeElement
            .querySelector<HTMLInputElement>(`#flight-leg-${Math.min(index, length - 2)}-origin`)
            ?.focus(),
        0,
      );
  }

  submit(): void {
    if (this.state().kind === 'loading') return;
    this.quoteCommands.next(null);
    this.submitted.set(true);
    this.form.updateValueAndValidity({ emitEvent: false });
    if (this.form.invalid) {
      if (this.form.controls.tripType.value === 'multiLeg') {
        const errors = (this.form.errors?.['search'] as FlightSearchErrors | undefined)?.legs;
        const row = errors?.findIndex((entry) => Object.keys(entry).length > 0) ?? -1;
        const field = row >= 0 && errors ? Object.keys(errors[row])[0] : 'passengerCount';
        this.element.nativeElement
          .querySelector<HTMLInputElement>(row >= 0 ? `#flight-leg-${row}-${field}` : '#flight-passenger-count')
          ?.focus();
        return;
      }
      const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
      const first = errors ? Object.keys(errors)[0] : undefined;
      if (first) {
        this.element.nativeElement.querySelector<HTMLInputElement>(`[formControlName="${first}"]`)?.focus();
      }
      return;
    }
    this.commands.next(toFlightSearchIntent(this.form));
  }

  retry(): void {
    this.submit();
  }

  example(): void {
    fillFlightSearchExample(this.form, localToday());
  }

  checkOffer(id: string): void {
    if (this.bookingIsBlocked()) return;
    if (this.quoteState().kind === 'loading') return;
    const current = this.state();
    if (current.kind !== 'ready') return;
    const offer = current.rawOffers.find((item) => item.id === id);
    if (
      !offer ||
      offer.providerOfferRef === null ||
      !offer.holdEligible ||
      offer.passengerCount !== current.passengerCount
    )
      return;
    this.checkoutStarted.set(false);
    this.checkoutMessage.set(null);
    this.postLoginQuoteReady = false;
    this.quoteCommands.next({
      source: offer,
      passengerCount: current.passengerCount,
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
    if (this.bookingIsBlocked()) return;
    if (this.checkoutStarted()) {
      this.refreshCheckoutQuote();
      return;
    }
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    this.checkoutStarted.set(false);
    this.quoteCommands.next({
      ...current.intent,
      previousQuote: current.quote.offer,
      previousBinding: current.quote.binding,
      aggregateId: current.quote.aggregateId,
    });
  }

  retryQuote(): void {
    if (this.bookingIsBlocked()) return;
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
  private quoteWithIdentity(intent: QuoteIntent): Observable<FlightQuoteResponse> {
    const body = {
      providerOfferRef: intent.providerOfferRef,
      provider: intent.provider,
      aggregateId: intent.aggregateId,
      passengerCount: intent.passengerCount,
    };
    return this.isDemo || this.auth.status().kind !== 'authenticated'
      ? this.quoteApi.quote(body)
      : from(this.auth.accessToken()).pipe(switchMap((token) => this.quoteApi.quote(body, token)));
  }
  invalidateAncillaryAcceptance(): void {
    const current = this.quoteState();
    if (current.kind === 'ready') this.quoteState.set({ ...current, accepted: false });
  }
  useAncillaryQuote(quote: FlightQuoteResponse): void {
    const current = this.quoteState();
    if (
      current.kind !== 'ready' ||
      quote.aggregateId !== current.quote.aggregateId ||
      !this.checkoutStarted() ||
      this.auth.status().kind !== 'authenticated'
    )
      return;
    this.checkoutQuote.set(quote);
    this.quoteState.set({
      ...current,
      quote,
      view: toFlightOfferView(quote.offer) as BookableOfferView,
      accepted: false,
      changed: true,
    });
    this.clearQuoteExpiry();
    this.scheduleQuoteExpiry(quote.offer.expiresAt);
  }

  async beginBooking(): Promise<void> {
    const current = this.quoteState();
    if (current.kind !== 'ready' || !current.accepted || !current.quote.offer.holdEligible) return;
    if (this.bookingIsBlocked()) return;
    if (Date.parse(current.quote.offer.expiresAt) <= Date.now()) {
      this.checkoutMessage.set('Срок предложения истёк. Обновите цену перед оформлением.');
      return;
    }
    this.checkoutMessage.set(null);

    if (this.auth.status().kind === 'authenticated') {
      if (this.postLoginQuoteReady && this.readyAuthContext === this.authContext()) {
        this.checkoutQuote.set(current.quote);
        this.checkoutStarted.set(true);
        this.checkoutMessage.set(null);
        return;
      }
      this.postLoginQuoteReady = false;
      this.quoteCommands.next({
        ...current.intent,
        source: null,
        previousQuote: current.quote.offer,
        previousBinding: current.quote.binding,
        aggregateId: current.quote.aggregateId,
      });
      this.checkoutMessage.set('После входа повторно проверяем цену и маршрут…');
      return;
    }

    if (this.isDemo) {
      const continued = await this.auth.beginLogin();
      if (this.destroyed || this.quoteState() !== current) return;
      if (continued) {
        this.postLoginQuoteReady = false;
        this.quoteCommands.next({
          ...current.intent,
          source: null,
          previousQuote: current.quote.offer,
          previousBinding: current.quote.binding,
          aggregateId: current.quote.aggregateId,
        });
        this.checkoutMessage.set('Демо вход завершён. Повторно проверяем вымышленное предложение…');
      } else this.checkoutMessage.set('Не удалось открыть вымышленное демо оформление.');
      return;
    }

    this.checkoutMessage.set(
      'После входа выберите рейс и проверьте цену заново. Выбор хранится только в памяти страницы.',
    );
    const continued = await this.auth.beginLogin();
    if (this.destroyed || this.quoteState() !== current) return;
    if (continued) {
      // An existing OIDC session may authenticate without navigating away.
      this.postLoginQuoteReady = false;
      this.quoteCommands.next({
        ...current.intent,
        source: null,
        previousQuote: current.quote.offer,
        previousBinding: current.quote.binding,
        aggregateId: current.quote.aggregateId,
      });
      return;
    }
    const status = this.auth.status();
    if (status.kind !== 'redirecting') {
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
    if (this.bookingIsBlocked()) return;
    if (!this.checkoutStarted()) return;
    const current = this.quoteState();
    if (current.kind !== 'ready') return;
    this.postLoginQuoteReady = false;
    this.quoteCommands.next({
      ...current.intent,
      source: null,
      previousQuote: current.quote.offer,
      previousBinding: current.quote.binding,
      aggregateId: current.quote.aggregateId,
    });
    this.checkoutMessage.set(
      'Повторно проверяем цену и маршрут. Данные текущей группы остаются в памяти страницы; новое предложение нужно принять заново.',
    );
  }

  restartCheckoutSearch(): void {
    this.checkoutStarted.set(false);
    this.postLoginQuoteReady = false;
    this.commands.next(null);
    this.quoteCommands.next(null);
    this.checkoutMessage.set('Срок удержания истёк. Выполните новый поиск и проверьте цену заново.');
  }

  private bookingIsBlocked(): boolean {
    if (!this.operations.blocksBookingInSession()) return false;
    this.checkoutMessage.set('Исход предыдущей операции бронирования требует проверки. Не создавайте новое удержание.');
    return true;
  }

  private identity(): string | null {
    const status = this.auth.status();
    const epoch = this.auth.identityEpoch?.() ?? 0;
    return status.kind === 'authenticated' ? `${status.userId.toLowerCase()}:${epoch}` : null;
  }

  private authContext(): string {
    return this.identity() ?? `${this.auth.status().kind}:${this.auth.identityEpoch?.() ?? 0}`;
  }

  private async resumeBookingAfterLogin(): Promise<void> {
    const authenticated = await this.auth.initializeFromCallback();
    this.postLoginQuoteReady = false;
    this.checkoutStarted.set(false);
    this.quoteCommands.next(null);
    this.checkoutMessage.set(
      authenticated
        ? 'Вход выполнен. Выберите рейс и проверьте цену заново.'
        : 'Вход не выполнен. Проверьте предложение и попробуйте снова.',
    );
  }

  private removeLegacyBookingDraft(): void {
    try {
      this.document.defaultView?.sessionStorage.removeItem('travel.flights.booking.v1');
    } catch {
      // Best-effort removal of our exact retired key; never read or enumerate storage.
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
