import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  effect,
  HostListener,
  Injector,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import {
  FlightBookingContractError,
  type FlightOrderListResponse,
  type FlightOrderResponse,
  FlightsBookingApiService,
} from '@travel/api-client';
import { EmptyError, firstValueFrom, Subject, TimeoutError, takeUntil } from 'rxjs';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { FlightOrdersFeedService, type FlightOrdersFeedSnapshot } from './flight-orders-feed.service';
import { formatCabinClass, formatFlightPrice, formatOffsetTime, groundGapNote, journeyLabel } from './flight-results';
import { FlightsAuthService } from './flights-auth.service';
import { isDemoSource } from './flights-source-mode';

type LoadState = 'auth' | 'loading' | 'ready' | 'error' | 'malformed' | 'forbidden';
type MoreState = 'idle' | 'loading' | 'error' | 'malformed' | 'buffered';
type RequestMode = 'first' | 'auto' | 'manual' | 'poll';
const PAGE_SIZE = 20;

@Component({
  selector: 'app-flight-orders-page',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-orders-page.component.html',
  styleUrl: './flight-orders-page.component.scss',
})
export class FlightOrdersPageComponent {
  readonly formatOffsetTime = formatOffsetTime;
  readonly formatCabinClass = formatCabinClass;
  readonly groundGapNote = groundGapNote;
  readonly journeyLabel = journeyLabel;
  private readonly api = inject(FlightsBookingApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly feed = inject(FlightOrdersFeedService);
  private readonly handoff = inject(FlightOrderHandoffService);
  private readonly operations = inject(FlightOrderOperationsService);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly invalidated = new Subject<void>();
  private readonly pollStopped = new Subject<void>();
  private observer: IntersectionObserver | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private projectionDeadlineTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;
  private requestVersion = 0;
  private activeMode: RequestMode | null = null;
  private busy = false;
  private nextOffset = 0;
  private autoAllowed = true;
  private restoring = false;
  private openedId: string | null = null;
  private pendingAppend: FlightOrderListResponse | null = null;
  private readonly ownerId = signal<string | null>(null);
  private readonly ownerEpoch = signal(0);
  private readonly orders = signal<FlightOrderResponse[]>([]);
  readonly isDemo = isDemoSource();
  readonly loadState = signal<LoadState>('loading');
  readonly moreState = signal<MoreState>('idle');
  readonly hasMore = signal(false);
  readonly announcement = signal('');
  readonly projectionWaiting = signal(false);
  readonly pollingEnded = signal(false);
  readonly authStatus = this.auth.status;
  readonly authMessage = computed(() => {
    const status = this.auth.status();
    return status.kind === 'error' || status.kind === 'unavailable' ? status.message : null;
  });
  readonly sameOwner = computed(() => {
    const auth = this.auth.status();
    return (
      auth.kind === 'authenticated' &&
      auth.userId.toLowerCase() === this.ownerId()?.toLowerCase() &&
      this.ownerEpoch() === (this.auth.identityEpoch?.() ?? 0)
    );
  });
  readonly visibleOrders = computed(() =>
    this.sameOwner() ? this.orders().map((order) => this.operations.overlay(order, this.ownerId()!)) : [],
  );
  readonly viewState = computed<LoadState>(() =>
    this.ownerId() !== null && !this.sameOwner() ? 'auth' : this.loadState(),
  );

  constructor() {
    effect(() => {
      if (this.ownerId() !== null && !this.sameOwner()) this.deny('auth');
    });
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationStart) this.rememberPosition();
    });
    this.destroyRef.onDestroy(() => {
      this.generation++;
      this.invalidated.next();
      this.invalidated.complete();
      this.pollStopped.next();
      this.pollStopped.complete();
      this.observer?.disconnect();
      this.clearPoll();
    });
    void this.initialize();
  }

  async login(): Promise<void> {
    const generation = this.generation;
    if ((await this.auth.beginLogin('/flights/orders')) && generation === this.generation) await this.initialize();
  }

  refresh(): void {
    if ((this.busy && this.activeMode !== 'poll') || this.viewState() === 'auth' || this.viewState() === 'forbidden')
      return;
    this.reset();
    this.feed.clear();
    this.loadState.set('loading');
    this.render(() => this.document.defaultView?.scrollTo({ top: 0, behavior: 'instant' }));
    void this.requestPage(0, 'first');
  }

  loadMore(): void {
    if (
      !this.sameOwner() ||
      (this.busy && this.activeMode !== 'poll') ||
      !this.hasMore() ||
      this.loadState() !== 'ready'
    )
      return;
    this.stopProjectionWait();
    if (this.pendingAppend !== null) {
      const response = this.pendingAppend;
      this.pendingAppend = null;
      this.append(response, true);
    } else void this.requestPage(this.nextOffset, 'manual');
  }

  rememberPosition(aggregateId?: string): void {
    const owner = this.ownerId();
    if (owner === null || !this.sameOwner() || this.loadState() !== 'ready') return;
    if (aggregateId !== undefined) this.openedId = aggregateId;
    const cards = [...this.host.nativeElement.querySelectorAll<HTMLElement>('[data-order-id]')];
    const anchor =
      cards.find((card) => card.dataset['orderId'] === this.openedId) ??
      cards.find((card) => card.getBoundingClientRect().bottom > 0);
    const focused = this.document.activeElement?.closest<HTMLElement>('[data-order-link]');
    this.feed.save(owner, {
      items: this.orders(),
      nextOffset: this.nextOffset,
      hasMore: this.hasMore(),
      anchorId: anchor?.dataset['orderId'] ?? null,
      anchorTop: anchor?.getBoundingClientRect().top ?? 0,
      scrollY: this.document.defaultView?.scrollY ?? 0,
      focusId: this.openedId ?? focused?.dataset['orderLink'] ?? null,
    });
  }

  @HostListener('window:wheel')
  @HostListener('window:touchmove')
  armAuto(): void {
    if (!this.restoring && this.moreState() === 'idle') {
      this.autoAllowed = true;
      this.observeEnd();
    }
  }

  @HostListener('window:keydown', ['$event'])
  scrollKey(event: KeyboardEvent): void {
    if (['ArrowDown', 'PageDown', 'End', ' '].includes(event.key) && event.target !== this.moreButton()) this.armAuto();
  }

  statusLabel(status: FlightOrderResponse['status']): string {
    return {
      Held: 'Удержан',
      Confirmed: 'Подтверждён, билет ещё не выписан',
      Ticketed: 'Билет выписан',
      Cancelled: 'Отменён',
      Refunded: 'Возврат оформлен',
    }[status];
  }

  formatPrice(order: FlightOrderResponse): string {
    return formatFlightPrice(order.totalAmount, order.currency);
  }
  formatTime(value: string): string {
    return new Intl.DateTimeFormat('ru-RU', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(new Date(value));
  }

  private async initialize(): Promise<void> {
    this.reset();
    const generation = this.generation;
    this.loadState.set('loading');
    try {
      if (!this.isDemo && this.auth.hasOidcCallback() && !(await this.auth.initializeFromCallback())) {
        if (generation === this.generation) this.deny('auth');
        return;
      }
      const auth = this.auth.status();
      if (generation !== this.generation) return;
      if (auth.kind !== 'authenticated') {
        this.deny('auth');
        return;
      }
      const owner = auth.userId;
      const epoch = this.auth.identityEpoch?.() ?? 0;
      const token = await this.auth.accessToken();
      if (generation !== this.generation) return;
      const current = this.auth.status();
      if (
        current.kind !== 'authenticated' ||
        current.userId.toLowerCase() !== owner.toLowerCase() ||
        epoch !== (this.auth.identityEpoch?.() ?? 0)
      ) {
        this.deny('auth');
        return;
      }
      this.ownerId.set(owner);
      this.ownerEpoch.set(epoch);
      const snapshot = this.feed.returningFromOrder() ? this.feed.restore(owner) : null;
      if (snapshot !== null) {
        this.orders.set(snapshot.items.map((order) => this.operations.overlay(order, owner)));
        this.nextOffset = snapshot.nextOffset;
        this.hasMore.set(snapshot.hasMore);
        this.loadState.set('ready');
        this.restorePosition(snapshot);
      } else {
        this.render(() => this.document.defaultView?.scrollTo({ top: 0, behavior: 'instant' }));
        await this.requestPage(0, 'first', token);
      }
    } catch {
      if (generation === this.generation) this.deny('auth');
    }
  }

  private reset(): void {
    this.generation++;
    this.requestVersion++;
    this.activeMode = null;
    this.invalidated.next();
    this.pollStopped.next();
    this.observer?.disconnect();
    this.clearPoll();
    this.busy = false;
    this.nextOffset = 0;
    this.autoAllowed = true;
    this.restoring = false;
    this.openedId = null;
    this.pendingAppend = null;
    this.orders.set([]);
    this.hasMore.set(false);
    this.moreState.set('idle');
    this.announcement.set('');
    this.projectionWaiting.set(false);
    this.pollingEnded.set(false);
  }

  private async requestPage(offset: number, mode: RequestMode, suppliedToken?: string | null): Promise<void> {
    if (this.busy || !this.sameOwner()) return;
    const generation = this.generation;
    const requestVersion = ++this.requestVersion;
    const isCurrentRequest = () => generation === this.generation && requestVersion === this.requestVersion;
    this.activeMode = mode;
    this.busy = true;
    const more = mode === 'auto' || mode === 'manual';
    if (more) this.moreState.set('loading');
    try {
      const token = suppliedToken === undefined ? await this.auth.accessToken() : suppliedToken;
      if (!isCurrentRequest()) return;
      if (!this.sameOwner()) {
        this.deny('auth');
        return;
      }
      if (mode === 'poll') {
        const owner = this.ownerId();
        if (!this.projectionWaiting() || owner === null || this.handoff.recentHeldDeadline(owner) === null) {
          this.stopProjectionWait();
          return;
        }
      }
      const request = this.api.listOrders(offset, token).pipe(takeUntil(this.invalidated));
      const response = await firstValueFrom(mode === 'poll' ? request.pipe(takeUntil(this.pollStopped)) : request);
      if (!isCurrentRequest()) return;
      if (!this.sameOwner()) {
        this.deny('auth');
        return;
      }
      for (const order of response.items) this.operations.observeProjection(order, this.ownerId()!);
      if (mode === 'auto' && this.document.activeElement === this.moreButton()) {
        this.pendingAppend = response;
        this.moreState.set('buffered');
        this.announcement.set('Следующие заказы готовы. Нажмите «Показать ещё».');
      } else if (more) this.append(response, mode === 'manual');
      else {
        this.orders.set(
          response.items.slice(0, PAGE_SIZE).map((order) => this.operations.overlay(order, this.ownerId()!)),
        );
        this.nextOffset = PAGE_SIZE;
        this.hasMore.set(response.items.length > PAGE_SIZE);
        this.loadState.set('ready');
        this.render(() => this.observeEnd());
        this.schedulePoll();
      }
    } catch (error) {
      if (!isCurrentRequest()) return;
      if (mode === 'poll' && error instanceof EmptyError && !this.projectionWaiting()) return;
      if (
        !this.sameOwner() ||
        !(
          error instanceof HttpErrorResponse ||
          error instanceof TimeoutError ||
          error instanceof FlightBookingContractError
        )
      ) {
        this.deny('auth');
      } else if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.deny(error.status === 401 ? 'auth' : 'forbidden');
      } else {
        const malformed =
          error instanceof FlightBookingContractError || (error instanceof HttpErrorResponse && error.status === 200);
        if (more) {
          this.moreState.set(malformed ? 'malformed' : 'error');
          this.autoAllowed = false;
        } else if (mode === 'poll') {
          this.pollingEnded.set(true);
          this.projectionWaiting.set(false);
        } else this.loadState.set(malformed ? 'malformed' : 'error');
      }
    } finally {
      if (isCurrentRequest()) {
        this.busy = false;
        this.activeMode = null;
      }
    }
  }

  private append(response: FlightOrderListResponse, focusNew: boolean): void {
    const existing = new Map(this.orders().map((item) => [item.aggregateId.toLowerCase(), item]));
    const newItems: FlightOrderResponse[] = [];
    for (const item of response.items.slice(0, PAGE_SIZE)) {
      const id = item.aggregateId.toLowerCase();
      if (!existing.has(id)) newItems.push(item);
      existing.set(id, this.operations.overlay(item, this.ownerId()!));
    }
    this.orders.set([...existing.values()]);
    this.nextOffset = response.offset + PAGE_SIZE;
    this.hasMore.set(response.items.length > PAGE_SIZE);
    this.moreState.set('idle');
    this.announcement.set(
      newItems.length === 0
        ? 'Новых заказов в этой порции нет. Список мог измениться.'
        : `Добавлено заказов: ${newItems.length}.`,
    );
    if (newItems.length === 0) this.autoAllowed = false;
    this.render(() => {
      if (focusNew && newItems.length > 0) this.orderLink(newItems[0].aggregateId)?.focus();
      this.observeEnd();
    });
  }

  private restorePosition(snapshot: FlightOrdersFeedSnapshot): void {
    this.restoring = true;
    this.autoAllowed = false;
    this.render(() => {
      const browser = this.document.defaultView;
      const anchor = [...this.host.nativeElement.querySelectorAll<HTMLElement>('[data-order-id]')].find(
        (card) => card.dataset['orderId'] === snapshot.anchorId,
      );
      if (snapshot.focusId !== null) this.orderLink(snapshot.focusId)?.focus({ preventScroll: true });
      browser?.scrollTo({
        top:
          anchor === undefined
            ? snapshot.scrollY
            : browser.scrollY + anchor.getBoundingClientRect().top - snapshot.anchorTop,
        behavior: 'instant',
      });
      this.restoring = false;
      this.observeEnd();
    });
  }

  private observeEnd(): void {
    this.observer?.disconnect();
    const browser = this.document.defaultView;
    const end = this.host.nativeElement.querySelector('[data-list-end]');
    if (
      browser === null ||
      end === null ||
      typeof browser.IntersectionObserver !== 'function' ||
      !this.hasMore() ||
      this.viewState() !== 'ready'
    )
      return;
    this.observer = new browser.IntersectionObserver(
      (entries) => {
        if (
          entries.some((entry) => entry.isIntersecting) &&
          this.autoAllowed &&
          !this.restoring &&
          this.moreState() === 'idle' &&
          this.document.activeElement !== this.moreButton()
        ) {
          this.stopProjectionWait();
          void this.requestPage(this.nextOffset, 'auto');
        }
      },
      { rootMargin: '0px 0px 400px 0px' },
    );
    this.observer.observe(end);
  }

  private schedulePoll(): void {
    this.clearPoll();
    const owner = this.ownerId();
    const id = owner === null ? null : this.handoff.recentHeld(owner);
    const deadline = owner === null ? null : this.handoff.recentHeldDeadline(owner);
    if (owner === null || id === null || deadline === null) {
      if (this.projectionWaiting()) this.pollingEnded.set(true);
      this.projectionWaiting.set(false);
      return;
    }
    if (this.orders().some((order) => order.aggregateId.toLowerCase() === id.toLowerCase())) {
      this.projectionWaiting.set(false);
      return;
    }
    this.projectionWaiting.set(true);
    const generation = this.generation;
    this.projectionDeadlineTimer = setTimeout(
      () => {
        if (generation === this.generation) this.stopProjectionWait();
      },
      Math.max(0, deadline - Date.now()),
    );
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (generation !== this.generation) return;
      if (Date.now() >= deadline || this.handoff.recentHeld(owner) === null) {
        this.stopProjectionWait();
        return;
      }
      void this.requestPage(0, 'poll');
    }, 2_000);
  }

  private deny(state: 'auth' | 'forbidden'): void {
    this.reset();
    this.ownerId.set(null);
    this.feed.clear();
    this.handoff.clear();
    this.loadState.set(state);
  }
  private clearPoll(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    if (this.projectionDeadlineTimer !== null) clearTimeout(this.projectionDeadlineTimer);
    this.pollTimer = null;
    this.projectionDeadlineTimer = null;
  }
  private stopProjectionWait(): void {
    this.clearPoll();
    if (this.activeMode === 'poll') {
      this.requestVersion++;
      this.activeMode = null;
      this.busy = false;
    }
    this.pollStopped.next();
    if (this.projectionWaiting()) {
      this.projectionWaiting.set(false);
      this.pollingEnded.set(true);
    }
  }
  private render(callback: () => void): void {
    const generation = this.generation;
    afterNextRender(
      () => {
        if (generation === this.generation && !this.destroyRef.destroyed) callback();
      },
      { injector: this.injector },
    );
  }
  private orderLink(id: string): HTMLElement | undefined {
    return [...this.host.nativeElement.querySelectorAll<HTMLElement>('[data-order-link]')].find(
      (link) => link.dataset['orderLink']?.toLowerCase() === id.toLowerCase(),
    );
  }
  private moreButton(): Element | null {
    return this.host.nativeElement.querySelector('[data-action="load-more"]');
  }
}
