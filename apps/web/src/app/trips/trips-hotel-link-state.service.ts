import { computed, DestroyRef, effect, Injectable, inject, signal, untracked } from '@angular/core';
import {
  HotelLinkRequestError,
  type TripHotelLink,
  type TripHotelLinkStatus,
  TripsHotelLinkApiService,
} from '@travel/api-client';
import { EMPTY, filter, firstValueFrom, type Observable, Subject, takeUntil } from 'rxjs';
import { TripsStateService } from './trips-state.service';

interface Scope {
  id: string;
  owner: string;
  epoch: number;
}
type HotelReadAccess = Readonly<{ allowed: boolean }>;

@Injectable()
export class TripsHotelLinkStateService {
  private readonly editor = inject(TripsStateService);
  private readonly api = inject(TripsHotelLinkApiService);
  private readonly scope = computed<Scope | null>(
    () => {
      const auth = this.editor.auth.status();
      const id = this.editor.selectedId();
      return auth.kind === 'authenticated' &&
        id &&
        this.editor.baseline()?.tripId === id &&
        this.editor.status() !== 'not-found'
        ? { id, owner: auth.userId, epoch: this.editor.auth.identityEpoch() }
        : null;
    },
    { equal: (a, b) => a === b || (!!a && !!b && a.id === b.id && a.owner === b.owner && a.epoch === b.epoch) },
  );
  private readonly active = signal<Scope | null>(null);
  private readonly cancel = new Subject<void>();
  private readonly accessChanges = new Subject<HotelReadAccess>();
  private generation = 0;
  private readonly availability = signal<'checking' | 'disabled' | 'enabled' | 'unavailable'>('checking');
  private readonly metadata = signal<TripHotelLink | null>(null);
  private readonly observation = signal<{ status: TripHotelLinkStatus; access: HotelReadAccess } | null>(null);
  private readonly note = signal('');
  private readonly working = signal<'read' | 'remove' | null>(null);
  readonly capability = computed(() => (this.currentScope() ? this.availability() : 'checking'));
  readonly link = computed(() => (this.capability() === 'enabled' ? this.metadata() : null));
  readonly status = computed<TripHotelLinkStatus | null>(() => {
    const link = this.link();
    if (!link?.hasLink) return null;
    const access = this.editor.auth.hotelReadAccess();
    if (!access.allowed) return { revision: link.revision, state: 'unavailable' };
    const observed = this.observation();
    return observed?.access === access ? observed.status : null;
  });
  readonly confirmed = computed(() => {
    const status = this.status();
    return status?.state === 'confirmed' ? status : null;
  });
  readonly busy = computed(() => (this.currentScope() ? this.working() : null));
  readonly message = computed(() => (this.currentScope() ? this.note() : ''));
  readonly canUnlink = computed(() => this.link()?.hasLink === true && this.busy() === null);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.reset(null));
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        this.reset(scope);
        if (scope) void this.refresh();
      });
    });
    effect(() => {
      const access = this.editor.auth.hotelReadAccess();
      untracked(() => {
        this.observation.update((observed) => (observed?.access === access ? observed : null));
        this.accessChanges.next(access);
      });
    });
  }

  private currentScope(): Scope | null {
    const scope = this.scope();
    return scope === this.active() ? scope : null;
  }

  private reset(scope: Scope | null) {
    this.generation++;
    this.cancel.next();
    this.active.set(scope);
    this.availability.set('checking');
    this.metadata.set(null);
    this.observation.set(null);
    this.note.set('');
    this.working.set(null);
  }

  private current(scope: Scope, generation: number) {
    return this.currentScope() === scope && generation === this.generation;
  }

  private async request<T>(scope: Scope, generation: number, send: (token: string) => Observable<T>) {
    const token = await this.editor.auth.accessToken();
    if (!token || !this.current(scope, generation)) throw new HotelLinkRequestError('authentication');
    try {
      return await firstValueFrom(send(token).pipe(takeUntil(this.cancel)));
    } catch (error) {
      if (
        error instanceof HotelLinkRequestError &&
        error.kind === 'authentication' &&
        this.current(scope, generation)
      ) {
        this.editor.auth.deny(scope.epoch);
        this.reset(null);
      }
      throw error;
    }
  }

  async refresh() {
    const scope = this.currentScope();
    if (!scope || this.working() === 'remove') return;
    const generation = ++this.generation;
    this.cancel.next();
    this.observation.set(null);
    this.metadata.set(null);
    this.note.set('');
    this.working.set('read');
    try {
      const { enabled } = await this.request(scope, generation, (token) => this.api.availability(token));
      if (!this.current(scope, generation)) return;
      this.availability.set(enabled ? 'enabled' : 'disabled');
      if (!enabled) return;
      const link = await this.request(scope, generation, (token) => this.api.metadata(scope.id, token));
      if (!this.current(scope, generation)) return;
      this.metadata.set(link);
      if (link.hasLink) await this.observe(scope, generation, link);
    } catch (error) {
      if (!this.current(scope, generation)) return;
      const missing = error instanceof HotelLinkRequestError && error.kind === 'notFound';
      if (missing || this.availability() !== 'enabled') {
        this.metadata.set(null);
        this.availability.set(missing ? 'disabled' : 'unavailable');
      }
      if (!missing) this.note.set(this.metadata()?.hasLink ? 'Заказ недоступен.' : 'Не удалось прочитать связь.');
    } finally {
      if (this.current(scope, generation)) this.working.set(null);
    }
  }

  private async observe(scope: Scope, generation: number, link: TripHotelLink) {
    const access = this.editor.auth.hotelReadAccess();
    if (!access.allowed) return;
    const accessCurrent = () => this.editor.auth.hotelReadAccess() === access;
    try {
      const status = await this.request(scope, generation, (token) =>
        accessCurrent()
          ? this.api.status(scope.id, token).pipe(takeUntil(this.accessChanges.pipe(filter((next) => next !== access))))
          : EMPTY,
      );
      if (!this.current(scope, generation) || !accessCurrent()) return;
      if (status.revision !== link.revision || status.state === 'empty') {
        this.metadata.set(null);
        this.note.set('Связь изменилась. Обновите сведения.');
      } else {
        this.observation.set({ status, access });
      }
    } catch (error) {
      if (accessCurrent()) throw error;
    }
  }

  async unlink() {
    const scope = this.currentScope();
    const link = this.link();
    if (!scope || !link || !this.canUnlink()) return;
    const generation = ++this.generation;
    this.working.set('remove');
    this.observation.set(null);
    this.note.set('');
    let dispatched = false;
    try {
      const result = await this.request(scope, generation, (token) => {
        const request = this.api.patch(scope.id, link, { op: 'remove' }, token);
        dispatched = true;
        return request;
      });
      if (!this.current(scope, generation)) return;
      this.metadata.set(result);
      this.note.set('Связь с заказом снята.');
    } catch (error) {
      if (!this.current(scope, generation)) return;
      const kind = error instanceof HotelLinkRequestError ? error.kind : 'transport';
      this.metadata.set(null);
      this.note.set(
        kind === 'conflict'
          ? 'Связь изменилась. Обновите сведения перед новым действием.'
          : dispatched && ['transport', 'deadline', 'contract', 'unavailable'].includes(kind)
            ? 'Результат снятия связи неизвестен. Обновите сведения перед новым действием.'
            : 'Не удалось снять связь. Обновите сведения перед новым действием.',
      );
    } finally {
      if (this.current(scope, generation)) this.working.set(null);
    }
  }
}
