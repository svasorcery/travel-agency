import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  type ElementRef,
  effect,
  Injector,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  decodeHotelLinkStay,
  HotelLinkRequestError,
  type HotelReservation,
  type TripHotelLink,
  type TripPage,
  TripsApiService,
  TripsHotelLinkApiService,
} from '@travel/api-client';
import { firstValueFrom, type Observable, Subject, takeUntil } from 'rxjs';
import { HotelsAuthService } from './hotels-auth.service';

const failures = {
  conflict: 'Связь изменилась. Перечитайте сведения перед новым действием.',
  occupied: 'К поездке уже привязан другой заказ Hotels. Снять прежнюю связь можно в Trips.',
  ineligible: 'Заказ больше недоступен для привязки.',
  notFound: 'Поездка или заказ недоступны.',
  forbidden: 'Привязка сейчас недоступна.',
  invalid: 'Не удалось добавить заказ. Перечитайте сведения перед новым действием.',
  unknown: 'Результат записи неизвестен. Перечитайте сведения перед новым действием.',
};

@Component({
  selector: 'app-hotel-trip-link',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './hotel-trip-link.component.html',
  styles: [
    `
    section { border-top:1px solid var(--border,#d2dfdb); margin-top:1.25rem; padding-top:1rem }
    h3 {font-weight:650}
    p {overflow-wrap:anywhere}
    label {display:block; margin:.65rem 0 .3rem}
    select {display:block; width:100%; max-width:34rem; margin-bottom:.75rem}
    button, select {font:inherit; min-height:44px; padding:.55rem .8rem; border:1px solid currentColor; border-radius:.4rem}
    button {margin:.15rem .4rem .15rem 0}
    a, button, select {outline-offset:3px}
  `,
  ],
})
export class HotelTripLinkComponent {
  readonly reservation = input.required<HotelReservation>();
  private readonly auth = inject(HotelsAuthService);
  private readonly trips = inject(TripsApiService);
  private readonly api = inject(TripsHotelLinkApiService);
  private readonly injector = inject(Injector);
  private readonly cancel = new Subject<void>();
  private readonly scope = computed(
    () => {
      const auth = this.auth.status();
      const order = this.reservation();
      const actual = order.facts?.actual;
      if (
        auth.kind !== 'authenticated' ||
        order.outcome !== 'Confirmed' ||
        !order.active ||
        !order.privateContentAvailable ||
        order.reviewReasons.length ||
        !actual
      )
        return null;
      try {
        const stay = decodeHotelLinkStay(actual.property.name, actual.dates.checkIn, actual.dates.checkOut);
        return { order, stay, owner: auth.userId, epoch: this.auth.identityEpoch() };
      } catch {
        return null;
      }
    },
    { equal: (a, b) => a === b || (!!a && !!b && a.order === b.order && a.owner === b.owner && a.epoch === b.epoch) },
  );
  private readonly active = signal<ReturnType<typeof this.scope>>(null);
  private readonly eligible = signal(false);
  private generation = 0;
  readonly canAttach = computed(() => this.currentScope() !== null && this.eligible());
  readonly stay = computed(() => this.currentScope()?.stay ?? null);
  readonly opened = signal(false);
  readonly page = signal<TripPage | null>(null);
  readonly selectedId = signal('');
  readonly selected = computed(() => this.page()?.items.find((trip) => trip.tripId === this.selectedId()));
  readonly metadata = signal<TripHotelLink | null>(null);
  readonly attached = signal('');
  readonly message = signal('');
  readonly busy = signal<'list' | 'read' | 'add' | null>(null);
  private readonly trail = signal<(string | null)[]>([null]);
  readonly hasPrevious = computed(() => this.trail().length > 1);
  private readonly opener = viewChild<ElementRef<HTMLButtonElement>>('opener');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly tripChoice = viewChild<ElementRef<HTMLSelectElement>>('tripChoice');

  constructor() {
    inject(DestroyRef).onDestroy(() => this.reset());
    effect(() => {
      const scope = this.scope();
      untracked(() => {
        this.reset();
        this.active.set(scope);
        if (scope) void this.discover();
      });
    });
  }

  private currentScope() {
    const scope = this.scope();
    return scope === this.active() ? scope : null;
  }

  private reset() {
    this.generation++;
    this.cancel.next();
    this.active.set(null);
    this.eligible.set(false);
    this.clearPicker();
  }

  private clearPicker() {
    this.opened.set(false);
    this.trail.set([null]);
    this.page.set(null);
    this.selectedId.set('');
    this.metadata.set(null);
    this.attached.set('');
    this.message.set('');
    this.busy.set(null);
  }

  private async request<T>(generation: number, send: (token: string) => Observable<T>) {
    const scope = this.currentScope();
    const token = await this.auth.accessToken();
    if (!scope || !token || !this.current(generation) || scope !== this.currentScope())
      throw new HotelLinkRequestError('authentication');
    try {
      return await firstValueFrom(send(token).pipe(takeUntil(this.cancel)));
    } catch (error) {
      const denied =
        error instanceof HotelLinkRequestError
          ? error.kind === 'authentication'
          : error instanceof HttpErrorResponse && error.status === 401;
      if (denied && this.current(generation) && scope === this.currentScope()) {
        this.auth.deny();
        this.reset();
      }
      throw error;
    }
  }

  private current(generation: number) {
    return this.currentScope() !== null && this.generation === generation;
  }

  private async discover() {
    const generation = this.generation;
    try {
      const result = await this.request(generation, (token) => this.api.availability(token));
      if (this.current(generation)) this.eligible.set(result.enabled && result.canAttach);
    } catch {
      if (this.current(generation)) this.eligible.set(false);
    }
  }

  open() {
    if (!this.canAttach()) return;
    this.opened.set(true);
    this.focus(this.heading);
    void this.loadPage(this.trail());
  }

  close() {
    if (this.busy() === 'add') return;
    this.generation++;
    this.cancel.next();
    this.clearPicker();
    this.focus(this.opener);
  }

  private focus(target: () => ElementRef<HTMLElement> | undefined) {
    afterNextRender(() => target()?.nativeElement.focus(), { injector: this.injector });
  }

  next() {
    const cursor = this.page()?.nextCursor;
    if (cursor && !this.trail().includes(cursor)) void this.loadPage([...this.trail(), cursor]);
  }

  previous() {
    if (this.hasPrevious()) void this.loadPage(this.trail().slice(0, -1));
  }

  async loadPage(trail = this.trail()) {
    if (!this.canAttach() || !this.opened() || this.busy()) return;
    const moving = trail !== this.trail();
    const generation = ++this.generation;
    this.selectedId.set('');
    this.metadata.set(null);
    this.message.set('');
    this.busy.set('list');
    try {
      const page = await this.request(generation, (token) => this.trips.list(trail.at(-1) ?? null, token));
      if (!this.current(generation)) return;
      this.page.set(page);
      this.trail.set(trail);
      if (moving) this.focus(page.items.length ? this.tripChoice : this.heading);
    } catch {
      if (this.current(generation)) this.message.set('Список поездок недоступен. Обновите список.');
    } finally {
      if (this.current(generation)) this.busy.set(null);
    }
  }

  choose(id: string) {
    if (this.busy() === 'list' || this.busy() === 'add' || this.attached()) return;
    this.generation++;
    this.cancel.next();
    this.busy.set(null);
    this.selectedId.set(id);
    this.metadata.set(null);
    this.message.set('');
    if (id) void this.refresh();
  }

  async refresh() {
    const trip = this.selected();
    if (!trip || !this.canAttach() || !this.opened() || this.busy()) return;
    const generation = ++this.generation;
    this.metadata.set(null);
    this.message.set('');
    this.busy.set('read');
    try {
      const metadata = await this.request(generation, (token) => this.api.metadata(trip.tripId, token));
      if (this.current(generation)) this.metadata.set(metadata);
    } catch {
      if (this.current(generation)) this.message.set('Связь недоступна. Перечитайте сведения.');
    } finally {
      if (this.current(generation)) this.busy.set(null);
    }
  }

  async attach() {
    const scope = this.currentScope();
    const trip = this.selected();
    const saved = this.metadata();
    if (!scope || !trip || !saved || !this.canAttach() || !this.opened() || this.busy() || this.attached()) return;
    const generation = ++this.generation;
    this.busy.set('add');
    this.message.set('');
    this.focus(this.heading);
    try {
      await this.request(generation, (token) =>
        this.api.patch(trip.tripId, saved, { op: 'add', reservationId: scope.order.id }, token),
      );
      if (this.current(generation)) {
        this.attached.set(trip.tripId);
        this.message.set('Заказ прикреплён к поездке.');
      }
    } catch (error) {
      if (!this.current(generation)) return;
      this.metadata.set(null);
      const kind = error instanceof HotelLinkRequestError ? error.kind : 'unknown';
      this.message.set(kind in failures ? failures[kind as keyof typeof failures] : failures.unknown);
    } finally {
      if (this.current(generation)) this.busy.set(null);
    }
  }
}
