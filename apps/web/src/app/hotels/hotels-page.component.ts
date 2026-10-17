import { isPlatformBrowser } from '@angular/common';
import { Component, DestroyRef, effect, inject, PLATFORM_ID, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  type HotelGuestParty,
  type HotelOffer,
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
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  constructor() {
    effect(() => {
      this.auth.identityEpoch();
      this.clearPrivate();
    });
    inject(DestroyRef).onDestroy(() => {
      this.sequence++;
      if (this.refreshTimer) clearInterval(this.refreshTimer);
      this.party = this.emptyParty(1);
    });
    if (this.browser) void this.initialize();
  }
  private async initialize() {
    const authenticated = await this.auth.initializeFromCallback();
    if (authenticated) await this.loadPrivate();
    this.route.paramMap.subscribe(() => {
      if (!this.busy() && this.auth.status().kind === 'authenticated') void this.loadPrivate();
    });
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
  }
  async find() {
    const seq = ++this.sequence;
    this.busy.set(true);
    this.message.set('');
    this.search.set(null);
    this.rates.set(null);
    this.selected = null;
    this.reservation.set(null);
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
  async chooseProperty(offer: HotelOffer) {
    const snapshot = this.search();
    if (!snapshot) return;
    const seq = ++this.sequence;
    this.busy.set(true);
    this.rates.set(null);
    this.message.set('');
    try {
      const result = await firstValueFrom(this.api.rates(snapshot.id, offer.selectionKey));
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
      this.changedTerms.set(old !== null && JSON.stringify(old.accepted) !== JSON.stringify(result.accepted));
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
  expired(value: string) {
    return Date.parse(value) <= Date.now();
  }
  async buy() {
    const quote = this.reservation();
    if (!quote || quote.attemptId || !this.consent || this.expired(quote.validUntil)) return;
    const seq = ++this.sequence,
      epoch = this.auth.identityEpoch();
    this.busy.set(true);
    this.message.set('');
    try {
      const token = await this.auth.accessToken();
      if (seq !== this.sequence || epoch !== this.auth.identityEpoch()) return;
      const acceptedParty = structuredClone(this.party);
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
      this.party = this.emptyParty(this.adults);
      this.consent = false;
      if (seq === this.sequence && epoch === this.auth.identityEpoch()) {
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
      status === 429 ? 'Сейчас много запросов. Подождите немного; покупка ещё не отправлена.' : fallback,
    );
  }
  money(terms: HotelTerms) {
    return `${terms.total.amount} ${terms.total.currency}`;
  }
  sourceName(key: string) {
    return key === 'duffel-stays' ? 'Duffel Stays' : 'Тестовый источник';
  }
}
