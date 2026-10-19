import { isPlatformBrowser } from '@angular/common';
import { Component, DestroyRef, effect, inject, PLATFORM_ID, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  type HotelGuestParty,
  type HotelOffer,
  type HotelPropertyGroup,
  type HotelRatesSnapshot,
  type HotelReservation,
  type HotelSearchSnapshot,
  HotelsApiService,
  type HotelTerms,
  hotelStatusText,
} from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
import { HotelTermsComponent } from './hotel-terms.component';
import { HotelsAuthService } from './hotels-auth.service';

@Component({
  selector: 'app-hotels-page',
  standalone: true,
  imports: [FormsModule, RouterLink, HotelTermsComponent],
  templateUrl: './hotels-page.component.html',
  styleUrl: './hotels-page.component.scss',
})
export class HotelsPageComponent {
  readonly auth = inject(HotelsAuthService);
  private readonly api = inject(HotelsApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private routeResourceId: string | null = null;
  readonly detailRoute = this.route.snapshot.paramMap.has('id');
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  readonly search = signal<HotelSearchSnapshot | null>(null);
  readonly rates = signal<HotelRatesSnapshot | null>(null);
  readonly reservation = signal<HotelReservation | null>(null);
  readonly reservations = signal<HotelReservation[]>([]);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly changedTerms = signal(false);
  readonly statusText = hotelStatusText;
  area = 'london';
  adults = 1;
  checkIn = new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10);
  checkOut = new Date(Date.now() + 9 * 86400000).toISOString().slice(0, 10);
  party: HotelGuestParty = this.emptyParty(1);
  consent = false;
  private sequence = 0;
  private selected: HotelOffer | null = null;
  private submittedResourceId: string | null = null;
  private quotedProvider: string | null = null;
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  constructor() {
    let observedEpoch = this.auth.identityEpoch();
    effect(() => {
      const epoch = this.auth.identityEpoch();
      if (epoch !== observedEpoch) {
        observedEpoch = epoch;
        this.clearPrivate();
      }
    });
    this.destroyRef.onDestroy(() => {
      this.sequence++;
      if (this.refreshTimer) clearInterval(this.refreshTimer);
      this.party = this.emptyParty(1);
    });
    if (this.browser) void this.initialize();
  }
  private async initialize() {
    await this.auth.initializeFromCallback();
    if (this.destroyRef.destroyed) return;
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const id = params.get('id');
      if (id !== this.routeResourceId) {
        this.clearPrivate();
        this.selected = null;
        this.routeResourceId = id;
      }
      if (!this.busy() && this.auth.status().kind === 'authenticated') void this.loadPrivate();
    });
  }
  criteriaChanged() {
    this.sequence++;
    this.search.set(null);
    this.rates.set(null);
    this.selected = null;
    this.consent = false;
    this.party = this.emptyParty(this.adults);
    this.busy.set(false);
    this.message.set('Условия поиска изменились. Выполните поиск заново.');
  }
  private emptyParty(adults: number): HotelGuestParty {
    return {
      guests: Array.from({ length: adults }, () => ({ givenName: '', familyName: '' })),
      email: '',
      phone: '',
      adultsConfirmed: false,
      checkInConfirmed: false,
    };
  }
  private clearPrivate() {
    this.sequence++;
    this.reservation.set(null);
    this.reservations.set([]);
    this.party = this.emptyParty(this.adults);
    this.consent = false;
    this.changedTerms.set(false);
    this.busy.set(false);
    if (this.refreshTimer) clearInterval(this.refreshTimer);
    this.refreshTimer = null;
    this.submittedResourceId = null;
    this.quotedProvider = null;
  }
  async find() {
    const retained = this.submittedResourceId ?? (this.reservation()?.attemptId ? this.reservation()!.id : null);
    if (retained) {
      this.message.set(
        'Результат покупки ещё проверяется. Новая бронь может привести к двойному бронированию. Откройте сохранённый результат.',
      );
      await this.router.navigate(['/hotels/reservations', retained]);
      return;
    }
    const seq = ++this.sequence;
    this.busy.set(true);
    this.message.set('');
    this.search.set(null);
    this.rates.set(null);
    this.selected = null;
    this.consent = false;
    this.party = this.emptyParty(this.adults);
    if (this.refreshTimer) clearInterval(this.refreshTimer);
    this.refreshTimer = null;
    try {
      const result = await firstValueFrom(
        this.api.search({ areaKey: this.area, checkIn: this.checkIn, checkOut: this.checkOut, adults: this.adults }),
      );
      if (seq === this.sequence) this.search.set(result);
    } catch {
      if (seq === this.sequence) this.message.set('Не удалось выполнить поиск. Проверьте даты и попробуйте ещё раз.');
    } finally {
      if (seq === this.sequence) this.busy.set(false);
    }
  }
  async chooseGroup(group: HotelPropertyGroup) {
    if (group.offers[0]) await this.chooseProperty(group.offers[0], group.key);
  }
  async chooseProperty(offer: HotelOffer, propertyGroupKey?: string) {
    const snapshot = this.search();
    if (!snapshot) return;
    const seq = ++this.sequence;
    this.busy.set(true);
    this.rates.set(null);
    this.selected = null;
    this.consent = false;
    this.message.set('');
    try {
      const result = await firstValueFrom(
        propertyGroupKey
          ? this.api.groupRates(snapshot.id, propertyGroupKey)
          : this.api.rates(snapshot.id, offer.selectionKey),
      );
      if (seq === this.sequence) this.rates.set(result);
    } catch {
      if (seq === this.sequence) this.message.set('Предложение устарело или тарифы недоступны. Повторите поиск.');
    } finally {
      if (seq === this.sequence) this.busy.set(false);
    }
  }
  async login() {
    if (await this.auth.beginLogin()) await this.loadPrivate();
  }
  async chooseRate(offer: HotelOffer) {
    if (this.submittedResourceId) {
      this.message.set(
        'Покупка уже отправлена. Проверяйте сохранённый результат; повторное бронирование может создать две брони.',
      );
      await this.router.navigate(['/hotels/reservations', this.submittedResourceId]);
      return;
    }
    this.selected = offer;
    if (this.auth.status().kind !== 'authenticated' && !(await this.auth.beginLogin())) return;
    const rates = this.rates();
    if (!rates || Date.parse(rates.expiresAt) <= Date.now()) {
      this.message.set('Предложение устарело. Повторите поиск.');
      return;
    }
    const old = this.reservation();
    if (old?.attemptId) {
      this.message.set('Результат предыдущей покупки доступен в ваших бронях.');
      return;
    }
    const seq = ++this.sequence,
      epoch = this.auth.identityEpoch();
    this.busy.set(true);
    this.consent = false;
    this.message.set('');
    try {
      const token = await this.auth.accessToken();
      if (epoch !== this.auth.identityEpoch() || seq !== this.sequence) return;
      const result = await firstValueFrom(
        this.api.quote(old?.id ?? crypto.randomUUID(), rates.id, offer.selectionKey, old?.quoteRevision ?? null, token),
      );
      if (epoch !== this.auth.identityEpoch() || seq !== this.sequence) return;
      this.changedTerms.set(
        old !== null &&
          (JSON.stringify(old.accepted) !== JSON.stringify(result.accepted) ||
            (old.provider ?? this.quotedProvider) !== offer.provider),
      );
      this.quotedProvider = offer.provider;
      if (result.accepted === null) throw new Error('Quote unavailable.');
      this.reservation.set(result);
      this.party = this.emptyParty(result.accepted.occupancy.adults);
    } catch (error) {
      if (epoch === this.auth.identityEpoch() && seq === this.sequence)
        this.handleError(error, 'Не удалось обновить условия. Повторите поиск и выбор тарифа.');
    } finally {
      if (seq === this.sequence) this.busy.set(false);
    }
  }
  async refreshQuote() {
    if (this.selected) await this.chooseRate(this.selected);
  }
  canRefreshQuote() {
    const rates = this.rates();
    return this.selected !== null && rates !== null && !this.expired(rates.expiresAt);
  }
  expired(value: string) {
    return Date.parse(value) <= Date.now();
  }
  async buy() {
    const quote = this.reservation();
    if (!quote || quote.attemptId || this.submittedResourceId || !this.consent || this.expired(quote.validUntil))
      return;
    const seq = ++this.sequence,
      epoch = this.auth.identityEpoch();
    this.busy.set(true);
    this.message.set('');
    try {
      const token = await this.auth.accessToken();
      if (seq !== this.sequence || epoch !== this.auth.identityEpoch()) return;
      const acceptedParty = structuredClone(this.party);
      this.submittedResourceId = quote.id;
      const result = await firstValueFrom(
        this.api.buy(quote.id, crypto.randomUUID(), quote.quoteRevision, acceptedParty, token),
      );
      if (seq === this.sequence && epoch === this.auth.identityEpoch()) {
        this.reservation.set(result);
        this.startLocalRefresh();
      }
    } catch (error) {
      if (seq === this.sequence && epoch === this.auth.identityEpoch())
        this.handleError(
          error,
          'Ответ не получен. Откройте сохранённую бронь, чтобы проверить результат. Не отправляйте покупку повторно.',
        );
    } finally {
      if (seq === this.sequence && epoch === this.auth.identityEpoch()) {
        this.party = this.emptyParty(this.adults);
        this.consent = false;
        this.busy.set(false);
        // Navigate only after dispatch completion/loss; navigation can destroy this component.
        // The new route reads the retained resource and never reconstructs or resends the party.
        await this.router.navigate(['/hotels/reservations', quote.id]);
      }
    }
  }
  async loadPrivate() {
    if (!this.browser || this.auth.status().kind !== 'authenticated') return;
    const epoch = this.auth.identityEpoch(),
      seq = ++this.sequence,
      id = this.route.snapshot.paramMap.get('id');
    try {
      const token = await this.auth.accessToken();
      if (epoch !== this.auth.identityEpoch() || seq !== this.sequence) return;
      const values = await firstValueFrom(this.api.list(token));
      if (epoch !== this.auth.identityEpoch() || seq !== this.sequence) return;
      this.reservations.set(values);
      if (id) {
        const value = await firstValueFrom(this.api.detail(id, token));
        if (epoch === this.auth.identityEpoch() && seq === this.sequence) {
          const previous = this.reservation();
          if (previous?.id !== value.id || previous.quoteRevision !== value.quoteRevision) {
            this.consent = false;
            this.party = this.emptyParty(value.accepted?.occupancy.adults ?? 1);
            this.changedTerms.set(previous?.id === value.id);
            this.selected = null;
          }
          this.reservation.set(value);
          this.startLocalRefresh();
        }
      }
    } catch (error) {
      if (epoch === this.auth.identityEpoch() && seq === this.sequence)
        this.handleError(error, 'Не удалось прочитать бронь. Сохранённый результат не меняется из-за этой ошибки.');
    }
  }
  private startLocalRefresh() {
    const value = this.reservation();
    if (!value?.attemptId || ['Confirmed', 'NotCreated', 'Cancelled'].includes(value.outcome)) {
      if (this.refreshTimer) clearInterval(this.refreshTimer);
      this.refreshTimer = null;
      return;
    }
    if (this.refreshTimer) return;
    let remaining = 20;
    this.refreshTimer = setInterval(() => {
      if (--remaining <= 0) {
        if (this.refreshTimer) clearInterval(this.refreshTimer);
        return;
      }
      void this.loadPrivate();
    }, 15_000);
  }
  private handleError(error: unknown, fallback: string) {
    const status = (error as { status?: number })?.status;
    if (status === 401 || status === 403) this.auth.deny();
    this.message.set(
      status === 429
        ? 'Этот запрос временно отклонён из-за числа обращений. Проверяйте сохранённый результат покупки; ошибка запроса не меняет его.'
        : fallback,
    );
  }
  money(terms: HotelTerms) {
    return `${terms.total.amount} ${terms.total.currency}`;
  }
  sourceName(key: string) {
    return key === 'duffel-stays' ? 'Duffel Stays' : key === 'hotelbeds' ? 'Hotelbeds' : 'Тестовый источник';
  }
  rankingText(offer: HotelOffer): string {
    const ranking = offer.ranking;
    if (!ranking || ranking.scope !== 'WholeStay' || !ranking.total)
      return 'Недостаточно данных об обязательных платежах, чтобы сравнить полную сумму.';
    const amount = `${ranking.total.amount} ${ranking.total.currency}`;
    return ranking.reason === 'EqualTotalTechnicalOrder'
      ? `Одинаковая известная сумма ${amount} за всё проживание; постоянный порядок без ценового преимущества. Условия тарифов могут различаться.`
      : `Известная сумма обязательств за всё проживание: ${amount}. В неё входят платёж поставщику и отдельные обязательные доплаты.`;
  }
  reasonText(reason: string | null, fallback: string): string {
    const messages: Record<string, string> = {
      NotConfigured: 'Источник проживания не настроен. Поиск станет доступен после подключения.',
      Disabled: 'Источник проживания временно отключён.',
      FictionalDestinationUnsupported: 'Этот источник не поддерживает выдуманный город. Выберите обычное направление.',
      StayOutsideSourceBounds: 'Источник не поддерживает выбранные даты или область поиска.',
      AdultsUnsupported: 'Источник не поддерживает выбранное число гостей.',
      PurchaseUnsupported: 'Источник не поддерживает покупку выбранного тарифа.',
      BookingDisabled: 'Бронирование временно отключено.',
      EligibilityUnknownOrRestricted: 'Для тарифа требуется особое право на покупку, которое здесь не проверяется.',
      PaymentUnsupported: 'Способ оплаты этого тарифа не поддерживается.',
      LocalAmountsUnknown: 'Обязательные платежи в отеле пока неизвестны. Покупка недоступна.',
      CancellationUnknown: 'Условия отмены пока неизвестны. Покупка недоступна.',
      Capacity: 'Сейчас много запросов. Повторите поиск немного позже.',
      SupplierUnavailable: 'Источник проживания временно недоступен.',
      InvalidOrUnavailableReply: 'Не удалось получить достоверный ответ источника. Попробуйте позже.',
      SelectionExpired: 'Предложение устарело. Повторите поиск.',
      RightsNotCleared: 'Этот источник пока не подключён для совместного поиска.',
      LocalCapacity: 'Источник не опрошен: сейчас много запросов. Повторите поиск позже.',
      Timeout: 'Источник не успел ответить. Показаны доступные результаты других источников.',
      SupplierRateLimit: 'Источник временно ограничил число запросов.',
      SupplierAuthentication: 'Соединение с источником временно недоступно.',
      Decode: 'Не удалось проверить ответ источника.',
      ProfileUnsupported: 'Условия покупки этого тарифа здесь не поддерживаются.',
      ContextTooLarge: 'Часть предложений не удалось безопасно сохранить.',
      MappingExpired: 'Сведения о соответствии отелей устарели. Предложения показаны отдельно.',
      EvidenceConflict: 'Сведения об отеле различаются. Предложения показаны отдельно.',
      NoInventory: 'По выбранным условиям у источника нет вариантов.',
    };
    return (reason && Object.hasOwn(messages, reason) && messages[reason]) || fallback;
  }
}
