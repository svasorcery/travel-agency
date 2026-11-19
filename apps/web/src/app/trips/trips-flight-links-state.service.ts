import { computed, DestroyRef, effect, Injectable, inject, signal, untracked } from '@angular/core';
import {
  type AvailableFlightObservation,
  type FlightLinkCandidate,
  type FlightLinkOperation,
  FlightLinkRequestError,
  type TripFlightLinks,
  TripsFlightLinksApiService,
} from '@travel/api-client';
import { firstValueFrom, type Observable, Subject, takeUntil } from 'rxjs';
import { TripsStateService } from './trips-state.service';

export interface TripFlightLinkCard {
  orderId: string;
  state: 'loading' | 'available' | 'stale' | 'unavailable' | 'notFound';
  facts?: AvailableFlightObservation;
  highestVersion?: string;
}
interface Scope {
  id: string;
  owner: string;
  epoch: number;
}
function same(a: Scope | null, b: Scope | null) {
  return a === b || (!!a && !!b && a.id === b.id && a.owner === b.owner && a.epoch === b.epoch);
}
@Injectable()
export class TripsFlightLinksStateService {
  private readonly editor = inject(TripsStateService);
  private readonly api = inject(TripsFlightLinksApiService);
  private readonly scope = computed<Scope | null>(
    () => {
      const session = this.editor.auth.status(),
        saved = this.editor.baseline();
      return session.kind === 'authenticated' && saved && this.editor.selectedId() === saved.tripId
        ? { id: saved.tripId, owner: session.userId, epoch: this.editor.auth.identityEpoch() }
        : null;
    },
    { equal: same },
  );
  private active: Scope | null = null;
  private cancelled = new Subject<void>();
  private read = 0;
  private pickerGeneration = 0;
  readonly capability = signal<'checking' | 'disabled' | 'enabled' | 'unavailable'>('checking');
  readonly visibleCapability = computed(() => {
    const scope = this.scope();
    const capability = this.capability();
    return scope && this.active === scope ? capability : 'checking';
  });
  readonly saved = signal<TripFlightLinks | null>(null);
  readonly cards = signal<TripFlightLinkCard[]>([]);
  readonly calendarCards = computed(() => (this.visibleCapability() === 'enabled' ? this.cards() : []));
  readonly busy = signal<'read' | 'patch' | null>(null);
  readonly barrier = signal<'unknown' | 'conflict' | null>(null);
  readonly message = signal('');
  readonly picker = signal(false);
  readonly listing = signal<'loading' | 'ready' | 'unavailable'>('ready');
  readonly candidates = signal<FlightLinkCandidate[]>([]);
  readonly nextOffset = signal<number | null>(null);
  readonly limitReached = signal(false);
  readonly clean = computed(() => this.editor.canCaptureSavedFoundation());
  readonly canMutate = computed(() => this.clean() && this.saved() !== null && !this.busy() && !this.barrier());
  constructor() {
    inject(DestroyRef).onDestroy(() => this.reset(null));
    effect(() => {
      const scope = this.scope();
      untracked(() => this.reset(scope));
    });
  }
  private reset(scope: Scope | null) {
    this.cancelled.next();
    this.active = scope;
    this.read++;
    this.closePicker();
    this.capability.set('checking');
    this.busy.set(null);
    this.barrier.set(null);
    this.message.set('');
    this.saved.set(null);
    this.cards.set([]);
    if (scope) void this.checkAvailability();
  }
  private current(scope: Scope) {
    return this.active === scope && same(scope, this.scope());
  }
  private async request<T>(scope: Scope, send: (token: string) => Observable<T>, valid: () => boolean = () => true) {
    const token = await this.editor.auth.accessToken();
    if (!token || !this.current(scope) || !valid()) throw new FlightLinkRequestError('authentication');
    try {
      return await firstValueFrom(send(token).pipe(takeUntil(this.cancelled)));
    } catch (error) {
      if (error instanceof FlightLinkRequestError && error.kind === 'authentication' && this.current(scope)) {
        this.editor.auth.deny(scope.epoch);
        this.reset(null);
      }
      throw error;
    }
  }
  async checkAvailability() {
    const scope = this.active;
    if (!scope || !this.current(scope)) return;
    this.capability.set('checking');
    try {
      const enabled = await this.request(scope, (t) => this.api.availability(t));
      if (!this.current(scope)) return;
      this.capability.set(enabled ? 'enabled' : 'disabled');
      if (enabled) await this.readCollection();
    } catch (e) {
      if (this.current(scope))
        this.capability.set(e instanceof FlightLinkRequestError && e.kind === 'notFound' ? 'disabled' : 'unavailable');
    }
  }
  private apply(saved: TripFlightLinks, preserveObservations = false) {
    const old = this.saved(),
      cards = this.cards();
    if (
      old &&
      (BigInt(saved.linksRevision) < BigInt(old.linksRevision) ||
        (saved.linksRevision === old.linksRevision && JSON.stringify(saved.links) !== JSON.stringify(old.links)))
    ) {
      throw new FlightLinkRequestError('contract');
    }
    this.cards.set(
      saved.links.map((link) => {
        const prior = old?.links.find((l) => l.orderId === link.orderId && l.linkedAt === link.linkedAt);
        const card = prior && cards.find((c) => c.orderId === link.orderId);
        if (preserveObservations && card) return card;
        return { ...card, orderId: link.orderId, state: card?.facts ? 'stale' : 'loading' };
      }),
    );
    this.saved.set(saved);
  }
  private markStale() {
    this.cards.update((cards) => cards.map((c) => ({ ...c, state: c.facts ? 'stale' : 'unavailable' })));
  }
  private clearMissingCollection(error: unknown) {
    if (!(error instanceof FlightLinkRequestError) || error.kind !== 'notFound') return false;
    this.saved.set(null);
    this.cards.set([]);
    this.barrier.set(null);
    this.closePicker();
    this.message.set('Поездка или её авиазаказы больше недоступны.');
    return true;
  }
  async refresh() {
    if (this.clean()) await this.readCollection();
  }
  private async readCollection() {
    const scope = this.active;
    if (!scope || !this.current(scope) || this.busy() || this.capability() !== 'enabled') return;
    const generation = ++this.read;
    this.busy.set('read');
    this.message.set('');
    try {
      const saved = await this.request(scope, (t) => this.api.metadata(scope.id, t));
      if (!this.current(scope) || this.read !== generation) return;
      const recovery = this.barrier() === 'unknown';
      this.apply(saved);
      this.barrier.set(null);
      if (recovery) this.message.set('Прочитан текущий состав поездки. Результат прежнего запроса неизвестен.');
      await this.observe(scope, generation, saved);
    } catch (error) {
      if (this.current(scope) && this.read === generation) {
        if (this.clearMissingCollection(error)) return;
        this.markStale();
        this.barrier.set(this.barrier() ?? 'conflict');
        this.message.set('Не удалось прочитать сохранённые связи. Обновите сведения.');
      }
    } finally {
      if (this.current(scope) && this.read === generation) this.busy.set(null);
    }
  }
  private async observe(scope: Scope, generation: number, saved: TripFlightLinks) {
    if (!saved.links.length) return;
    try {
      const status = await this.request(scope, (t) => this.api.status(scope.id, t));
      if (!this.current(scope) || generation !== this.read || this.saved() !== saved) return;
      if (
        status.linksRevision !== saved.linksRevision ||
        status.items.length !== saved.links.length ||
        saved.links.some((l) => !status.items.some((s) => s.orderId === l.orderId))
      ) {
        this.markStale();
        this.barrier.set('conflict');
        this.message.set('Состав поездки изменился. Обновите сведения.');
        return;
      }
      this.cards.update((cards) =>
        cards.map((card) => {
          const item = status.items.find((i) => i.orderId === card.orderId)!;
          if (item.state === 'notFound')
            return { orderId: card.orderId, state: 'notFound', highestVersion: card.highestVersion };
          if (item.state === 'unavailable') return { ...card, state: card.facts ? 'stale' : 'unavailable' };
          if (
            item.state === 'available' &&
            (!card.highestVersion || BigInt(item.streamVersion) >= BigInt(card.highestVersion))
          )
            return { orderId: card.orderId, state: 'available', facts: item, highestVersion: item.streamVersion };
          return { ...card, state: card.facts ? 'stale' : 'unavailable' };
        }),
      );
    } catch (error) {
      if (this.current(scope) && this.read === generation) {
        if (this.clearMissingCollection(error)) return;
        this.markStale();
        this.message.set('Сведения Flights сейчас недоступны. Связи сохранены.');
      }
    }
  }
  async mutate(op: FlightLinkOperation, orderId: string) {
    const scope = this.active,
      saved = this.saved();
    if (!scope || !this.current(scope) || !saved || !this.canMutate()) return;
    const linked = saved.links.some((l) => l.orderId === orderId);
    if (
      (op === 'add' &&
        (linked ||
          saved.links.length === 4 ||
          !this.picker() ||
          !this.candidates().some((c) => c.orderId === orderId))) ||
      (op === 'remove' && !linked)
    )
      return;
    const foundation = this.editor.captureSavedFoundation();
    if (!foundation) return;
    const generation = ++this.read;
    let dispatched = false;
    this.busy.set('patch');
    this.message.set('');
    try {
      const next = await this.request(
        scope,
        (t) => {
          dispatched = true;
          return this.api.patch(saved, op, orderId, t);
        },
        () => this.editor.isSavedFoundationCurrent(foundation),
      );
      if (!this.current(scope) || generation !== this.read) return;
      this.apply(next, op === 'remove');
      this.closePicker();
      this.message.set(op === 'add' ? 'Заказ прикреплён.' : 'Связь удалена.');
      if (op === 'add') await this.observe(scope, generation, next);
    } catch (e) {
      if (!this.current(scope) || generation !== this.read) return;
      const kind = e instanceof FlightLinkRequestError ? e.kind : 'transport';
      if (!dispatched) {
        this.message.set('Сохраните изменения плана и повторно выберите действие.');
      } else if (kind === 'conflict' || kind === 'limit') {
        this.barrier.set('conflict');
        this.message.set('Состав поездки изменился. Обновите сведения.');
      } else if (kind === 'notFound' || kind === 'authentication' || kind === 'invalid') {
        if (op === 'remove' && this.clearMissingCollection(e)) return;
        this.message.set('Не удалось изменить связь. Проверьте доступ к поездке и заказу.');
      } else {
        this.barrier.set('unknown');
        this.message.set('Результат записи неизвестен. Обновите сведения, чтобы прочитать сохранённый состав.');
      }
    } finally {
      if (this.current(scope) && generation === this.read) this.busy.set(null);
    }
  }
  closePicker() {
    this.pickerGeneration++;
    this.picker.set(false);
    this.candidates.set([]);
    this.listing.set('ready');
    this.nextOffset.set(null);
    this.limitReached.set(false);
  }
  async loadCandidates(append = false) {
    const scope = this.active;
    if (
      !scope ||
      !this.current(scope) ||
      !this.canMutate() ||
      this.saved()!.links.length >= 4 ||
      this.listing() === 'loading'
    )
      return;
    const offset = append ? this.nextOffset() : 0;
    if (offset === null) return;
    const gen = ++this.pickerGeneration;
    this.picker.set(true);
    this.listing.set('loading');
    if (!append) {
      this.candidates.set([]);
      this.limitReached.set(false);
      this.nextOffset.set(null);
    }
    try {
      const result = await this.request(
        scope,
        (t) => this.api.candidates(scope.id, offset, t),
        () => gen === this.pickerGeneration,
      );
      if (!this.current(scope) || gen !== this.pickerGeneration) return;
      const merged = new Map(this.candidates().map((c) => [c.orderId, c]));
      for (const item of result.items) merged.set(item.orderId, item);
      this.candidates.set([...merged.values()]);
      this.nextOffset.set(result.nextOffset);
      this.limitReached.set(result.limitReached);
      this.listing.set('ready');
    } catch {
      if (this.current(scope) && gen === this.pickerGeneration) this.listing.set('unavailable');
    }
  }
}
