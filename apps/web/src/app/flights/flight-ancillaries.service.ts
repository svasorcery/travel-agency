import { HttpErrorResponse } from '@angular/common/http';
import { effect, Injectable, inject, signal, untracked } from '@angular/core';
import type { FlightAncillaryCatalog, FlightAncillarySelection, FlightQuoteResponse } from '@travel/api-client';
import { FlightsAncillariesApiService, FlightsQuoteApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
import { FlightsAuthService } from './flights-auth.service';

/** Transient choices only. The final server quote is the accepted purchase. */
@Injectable()
export class FlightAncillariesService {
  private readonly auth = inject(FlightsAuthService);
  private readonly api = inject(FlightsAncillariesApiService);
  private readonly quotes = inject(FlightsQuoteApiService);
  readonly catalog = signal<FlightAncillaryCatalog | null>(null);
  readonly draft = signal<FlightAncillarySelection[]>([]);
  readonly busy = signal(false);
  readonly message = signal<string | null>(null);
  private quoteId: string | null = null;
  private quoteRevision: string | null = null;
  private identity: string | null = null;
  private generation = 0;
  constructor() {
    effect(() => {
      this.auth.status();
      this.auth.identityEpoch?.();
      untracked(() => {
        if (this.identity !== this.context()) this.clear();
      });
    });
  }
  private context(): string | null {
    const a = this.auth.status();
    return a.kind === 'authenticated' ? `${a.userId}:${this.auth.identityEpoch?.() ?? 0}` : null;
  }
  bind(quote: FlightQuoteResponse): void {
    const context = this.context();
    if (context !== this.identity || quote.aggregateId !== this.quoteId) {
      this.clear();
      this.identity = context;
      this.quoteId = quote.aggregateId;
      this.draft.set(
        (quote.purchase?.services ?? []).map((s) => ({ selectionKey: s.selectionKey, quantity: s.quantity })),
      );
    } else if (this.quoteRevision !== quote.binding.revision) {
      this.generation++;
      this.catalog.set(null);
      this.busy.set(false);
    }
    this.quoteRevision = quote.binding.revision;
  }
  matchesQuote(quote: FlightQuoteResponse): boolean {
    const canonical = (items: FlightAncillarySelection[]) =>
      JSON.stringify([...items].sort((a, b) => a.selectionKey.localeCompare(b.selectionKey)));
    return (
      canonical(this.draft()) ===
      canonical((quote.purchase?.services ?? []).map((s) => ({ selectionKey: s.selectionKey, quantity: s.quantity })))
    );
  }
  setQuantity(selectionKey: string, quantity: number): boolean {
    if (!this.busy() && quantity === 0 && this.draft().some((s) => s.selectionKey === selectionKey)) {
      this.draft.update((items) => items.filter((s) => s.selectionKey !== selectionKey));
      this.message.set(null);
      return true;
    }
    const service = this.catalog()?.services.find((s) => s.selectionKey === selectionKey);
    if (
      this.busy() ||
      !service?.selectable ||
      !Number.isSafeInteger(quantity) ||
      quantity < 0 ||
      quantity > service.maximumQuantity
    )
      return false;
    let draft = this.draft().filter((s) => s.selectionKey !== selectionKey);
    if (quantity > 0) {
      if (service.kind === 'seat') {
        const scope = service.segments[0];
        const conflict = this.catalog()?.services.find(
          (s) =>
            s.selectionKey !== selectionKey &&
            s.kind === 'seat' &&
            s.physicalSeat === service.physicalSeat &&
            s.segments[0]?.leg === scope?.leg &&
            s.segments[0]?.segment === scope?.segment &&
            draft.some((d) => d.selectionKey === s.selectionKey),
        );
        if (conflict) {
          this.message.set('Это место уже выбрано для другого пассажира.');
          return false;
        }
        const samePassengerScope = new Set(
          this.catalog()
            ?.services.filter(
              (s) =>
                s.kind === 'seat' &&
                s.bookingPassengerIds[0] === service.bookingPassengerIds[0] &&
                s.segments[0]?.leg === scope?.leg &&
                s.segments[0]?.segment === scope?.segment,
            )
            .map((s) => s.selectionKey),
        );
        draft = draft.filter((d) => !samePassengerScope.has(d.selectionKey));
      }
      draft.push({ selectionKey, quantity });
    }
    this.draft.set(draft);
    this.message.set(null);
    return true;
  }
  quantity(key: string): number {
    return this.draft().find((d) => d.selectionKey === key)?.quantity ?? 0;
  }
  async load(quote: FlightQuoteResponse, includeSeats: boolean): Promise<void> {
    this.bind(quote);
    const identity = this.context();
    if (identity === null || this.busy()) return;
    const generation = ++this.generation;
    this.busy.set(true);
    this.message.set(null);
    try {
      const token = this.auth.isDemo ? null : await this.auth.accessToken();
      if (generation !== this.generation || identity !== this.context()) return;
      const catalog = await firstValueFrom(
        this.api.getCatalog(
          { aggregateId: quote.aggregateId, quoteRevision: quote.binding.revision, includeSeats },
          quote,
          token,
        ),
      );
      if (generation !== this.generation || identity !== this.context()) return;
      this.catalog.set(catalog);
      if (
        this.draft().some(
          (d) =>
            !catalog.services.some(
              (s) => s.selectionKey === d.selectionKey && s.selectable && d.quantity <= s.maximumQuantity,
            ),
        )
      )
        this.message.set('Часть выбранных услуг сейчас недоступна. Измените выбор; услуги не удалены автоматически.');
    } catch (error) {
      if (generation === this.generation && identity === this.context())
        this.message.set(
          error instanceof HttpErrorResponse && error.status === 409
            ? 'Предложение изменилось. Обновите цену, сохранив выбор и данные пассажиров.'
            : 'Не удалось прочитать услуги. Можно продолжить без нового выбора или повторить чтение.',
        );
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  async review(quote: FlightQuoteResponse): Promise<FlightQuoteResponse | null> {
    const identity = this.context();
    if (identity === null || this.busy()) return null;
    const generation = ++this.generation;
    const choices = structuredClone(this.draft());
    this.busy.set(true);
    this.message.set(null);
    try {
      const token = this.auth.isDemo ? null : await this.auth.accessToken();
      if (generation !== this.generation || identity !== this.context()) return null;
      const current = await firstValueFrom(
        this.quotes.quote(
          {
            provider: quote.offer.provider,
            providerOfferRef: quote.offer.providerOfferRef,
            aggregateId: quote.aggregateId,
            passengerCount: quote.binding.passengerCount,
            selections: choices,
          },
          token,
        ),
      );
      if (generation !== this.generation || identity !== this.context()) return null;
      this.catalog.set(null);
      return current;
    } catch {
      if (generation === this.generation && identity === this.context())
        this.message.set('Не удалось проверить выбранные условия. Выбор сохранён; проверьте доступность услуг.');
      return null;
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  clear(): void {
    this.generation++;
    this.identity = null;
    this.quoteId = null;
    this.quoteRevision = null;
    this.catalog.set(null);
    this.draft.set([]);
    this.busy.set(false);
    this.message.set(null);
  }
}
