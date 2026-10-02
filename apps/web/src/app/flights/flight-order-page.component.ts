import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  effect,
  Injector,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FlightBookingContractError, type FlightOrderResponse, FlightsBookingApiService } from '@travel/api-client';
import { firstValueFrom, Subject, TimeoutError, takeUntil } from 'rxjs';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { formatFlightPrice } from './flight-results';
import { FlightsAuthService } from './flights-auth.service';
import { isDemoSource } from './flights-source-mode';

type LoadState =
  | 'auth'
  | 'forbidden'
  | 'loading'
  | 'ready'
  | 'updating'
  | 'notFound'
  | 'error'
  | 'malformed'
  | 'invalid';
type ConfirmState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'blocked' | 'expired';

const POLL_INTERVAL_MS = 2_000;
const POLL_WINDOW_MS = 30_000;
const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

@Component({
  selector: 'app-flight-order-page',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-order-page.component.html',
  styleUrl: './flight-order-page.component.scss',
})
export class FlightOrderPageComponent {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(FlightsBookingApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly handoff = inject(FlightOrderHandoffService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly routeChanged = new Subject<void>();
  private readonly pollStopped = new Subject<void>();
  private deadlineTimer: ReturnType<typeof setTimeout> | null = null;
  private requestVersion = 0;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;
  private inFlightGeneration = -1;
  private pollStartedAt = 0;
  private readonly operations = inject(FlightOrderOperationsService);
  private seenReceipt: number | null = null;
  private seenRejectedAttempt: string | null = null;
  private reviewStatus: FlightOrderResponse['status'] | undefined;
  private readonly viewOwnerId = signal<string | null>(null);
  private readonly viewEpoch = signal(0);

  readonly isDemo = isDemoSource();
  readonly aggregateId = signal<string | null>(null);
  readonly order = signal<FlightOrderResponse | null>(null);
  readonly loadState = signal<LoadState>('loading');
  readonly commandConfirmed = signal(false);
  readonly commandHeld = signal(false);
  readonly pollingEnded = signal(false);
  private readonly activeOperation = computed(() => {
    const id = this.aggregateId();
    const owner = this.viewOwnerId();
    return id === null || owner === null ? null : this.operations.operation(id, owner);
  });
  readonly confirmState = computed<ConfirmState>(() => {
    const operation = this.activeOperation();
    if (operation?.kind !== 'confirm' || operation.state === 'success') return 'idle';
    if (operation.state === 'conflict' && !operation.retryable) return 'blocked';
    return operation.state === 'rejected' ? 'blocked' : operation.state;
  });
  readonly confirmMessage = computed(() =>
    this.activeOperation()?.kind === 'confirm' ? (this.activeOperation()?.message ?? null) : null,
  );
  readonly cancellationReview = signal(false);
  private readonly needsStatusRefresh = signal(false);
  readonly cancelOperation = computed(() =>
    this.activeOperation()?.kind === 'cancel' ? this.activeOperation() : null,
  );
  readonly canCancel = computed(() => {
    const current = this.visibleOrder();
    const owner = this.viewOwnerId();
    return (
      this.sameOwner() &&
      owner !== null &&
      current !== null &&
      this.viewState() === 'ready' &&
      !this.needsStatusRefresh() &&
      (current.status === 'Held' || current.status === 'Confirmed') &&
      !this.operations.blocksWrite(current.aggregateId, owner)
    );
  });
  readonly sameOwner = computed(() => {
    const auth = this.auth.status();
    return (
      auth.kind === 'authenticated' &&
      auth.userId.toLowerCase() === this.viewOwnerId()?.toLowerCase() &&
      (this.auth.identityEpoch?.() ?? 0) === this.viewEpoch()
    );
  });
  readonly visibleOrder = computed(() => {
    const owner = this.viewOwnerId();
    const id = this.aggregateId();
    if (!this.sameOwner() || owner === null || id === null) return null;
    const raw = this.order();
    return raw === null ? this.operations.cancelled(id, owner) : this.operations.overlay(raw, owner);
  });
  readonly visibleConfirmed = computed(
    () =>
      this.sameOwner() &&
      (this.commandConfirmed() ||
        (this.aggregateId() !== null &&
          this.viewOwnerId() !== null &&
          this.operations.confirmed(this.aggregateId()!, this.viewOwnerId()!))),
  );
  readonly visibleHeld = computed(() => this.sameOwner() && this.commandHeld());
  readonly viewState = computed<LoadState>(() =>
    this.loadState() === 'forbidden'
      ? 'forbidden'
      : this.viewOwnerId() !== null && !this.sameOwner()
        ? 'auth'
        : this.loadState(),
  );
  readonly displayStatus = computed(() => {
    const status = this.visibleOrder()?.status;
    if (this.visibleConfirmed() && (status === undefined || status === 'Held')) return 'Confirmed';
    return this.visibleHeld() && status === undefined ? 'Held' : status;
  });
  readonly testWallet = computed(() => this.isDemo || this.auth.isTestEnvironment());

  constructor() {
    effect(() => {
      this.operations.revision();
      this.aggregateId();
      this.auth.status();
      if (this.cancellationReview() && (!this.canCancel() || this.displayStatus() !== this.reviewStatus))
        this.cancellationReview.set(false);
      untracked(() => {
        const id = this.aggregateId();
        const owner = this.viewOwnerId();
        if (id === null || owner === null || !this.sameOwner()) return;
        const operation = this.operations.operation(id, owner);
        if (operation === null) return;
        if (operation.state === 'rejected' && operation.attemptId !== this.seenRejectedAttempt) {
          this.seenRejectedAttempt = operation.attemptId;
          this.needsStatusRefresh.set(true);
          return;
        }
        if (operation.state !== 'success' || operation.receiptId === this.seenReceipt) return;
        this.seenReceipt = operation.receiptId;
        this.commandHeld.set(false);
        if (operation.kind === 'confirm') this.commandConfirmed.set(true);
        const status = this.order()?.status;
        this.loadState.set(
          operation.kind === 'cancel' && (status === 'Cancelled' || status === 'Refunded')
            ? 'ready'
            : operation.kind === 'confirm' && status !== undefined && status !== 'Held'
              ? 'ready'
              : 'updating',
        );
        this.pollStartedAt =
          operation.kind === 'cancel' ? (this.operations.cancellationDeadline(id, owner) ?? Date.now()) : Date.now();
        this.pollingEnded.set(false);
        this.clearPoll();
        this.schedulePoll(this.generation);
      });
    });
    afterNextRender(() => {
      this.element.nativeElement.ownerDocument.defaultView?.scrollTo({ top: 0, behavior: 'instant' });
      this.element.nativeElement.querySelector<HTMLElement>('h1')?.focus({ preventScroll: true });
    });
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      void this.openOrder(params.get('aggregateId'));
    });
    this.destroyRef.onDestroy(() => {
      this.clearPoll();
      this.pollStopped.next();
      this.pollStopped.complete();
      this.routeChanged.next();
      this.routeChanged.complete();
    });
  }

  refresh(): void {
    this.clearPoll();
    this.requestVersion++;
    this.pollStopped.next();
    this.inFlightGeneration = -1;
    if (this.aggregateId() === null || this.viewState() === 'auth') return;
    void this.loadOrder(this.generation);
  }

  confirm(): void {
    const current = this.visibleOrder();
    const owner = this.viewOwnerId();
    if (
      current?.status !== 'Held' ||
      owner === null ||
      this.visibleConfirmed() ||
      this.viewState() !== 'ready' ||
      this.confirmState() !== 'idle'
    )
      return;
    this.operations.startConfirm(current.aggregateId, owner);
  }

  retryConfirm(): void {
    const id = this.aggregateId();
    const owner = this.viewOwnerId();
    if (id !== null && owner !== null && this.sameOwner()) this.operations.retry(id, owner);
  }

  requestCancellation(): void {
    if (!this.canCancel()) return;
    this.reviewStatus = this.displayStatus();
    this.cancellationReview.set(true);
    this.focusCancellation('[data-action="dismiss-cancel"]', true);
  }

  dismissCancellation(): void {
    this.cancellationReview.set(false);
    this.focusCancellation('[data-action="cancel-order"]', false);
  }

  acceptCancellation(): void {
    const current = this.visibleOrder();
    const owner = this.viewOwnerId();
    if (
      !this.cancellationReview() ||
      !this.canCancel() ||
      current === null ||
      owner === null ||
      this.displayStatus() !== this.reviewStatus
    )
      return;
    this.cancellationReview.set(false);
    this.operations.startCancel(current.aggregateId, owner);
  }

  retryCancellation(): void {
    this.retryConfirm();
  }

  private focusCancellation(selector: string, reviewing: boolean): void {
    const generation = this.generation;
    afterNextRender(
      () => {
        if (
          generation === this.generation &&
          !this.destroyRef.destroyed &&
          this.sameOwner() &&
          this.cancellationReview() === reviewing
        )
          this.element.nativeElement.querySelector<HTMLElement>(selector)?.focus();
      },
      { injector: this.injector },
    );
  }

  async login(): Promise<void> {
    const id = this.aggregateId();
    if (id === null) return;
    const generation = this.generation;
    const authenticated = await this.auth.beginLogin(`/flights/orders/${id}`);
    if (authenticated && generation === this.generation && id === this.aggregateId()) {
      await this.openOrder(id);
    }
  }

  formatPrice(amount: number, currency: string): string {
    return formatFlightPrice(amount, currency);
  }

  formatFlightCode(carrierCode: string, flightNumber: string): string {
    return flightNumber.toUpperCase().startsWith(carrierCode.toUpperCase())
      ? flightNumber
      : `${carrierCode} ${flightNumber}`;
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

  private async openOrder(id: string | null): Promise<void> {
    this.generation++;
    const generation = this.generation;
    this.clearPoll();
    this.routeChanged.next();
    this.inFlightGeneration = -1;
    this.order.set(null);
    this.viewOwnerId.set(null);
    this.pollingEnded.set(false);
    this.commandConfirmed.set(false);
    this.commandHeld.set(false);
    this.cancellationReview.set(false);
    this.seenReceipt = null;
    this.seenRejectedAttempt = null;
    if (id === null || !GUID.test(id) || id === '00000000-0000-0000-0000-000000000000') {
      this.aggregateId.set(null);
      this.loadState.set('invalid');
      return;
    }
    this.aggregateId.set(id);
    this.pollStartedAt = Date.now();
    this.loadState.set('loading');
    if (!this.isDemo && this.auth.hasOidcCallback()) {
      if (!(await this.auth.initializeFromCallback()) || generation !== this.generation) {
        if (generation === this.generation) this.loadState.set('auth');
        return;
      }
    }
    if (this.auth.status().kind !== 'authenticated') {
      this.handoff.takeOutcome(id, null);
      this.loadState.set('auth');
      return;
    }
    const auth = this.auth.status();
    this.viewOwnerId.set(auth.kind === 'authenticated' ? auth.userId : null);
    this.viewEpoch.set(this.auth.identityEpoch?.() ?? 0);
    const outcome = this.handoff.takeOutcome(id, auth.kind === 'authenticated' ? auth.userId : null);
    this.commandConfirmed.set(outcome === 'Confirmed');
    this.commandHeld.set(outcome === 'Held');
    this.loadState.set(outcome === null ? 'loading' : 'updating');
    void this.loadOrder(generation);
  }

  private async loadOrder(generation: number, polling = false): Promise<void> {
    const id = this.aggregateId();
    if (id === null || this.inFlightGeneration === generation) return;
    this.inFlightGeneration = generation;
    const requestVersion = ++this.requestVersion;
    const isCurrent = () => generation === this.generation && requestVersion === this.requestVersion;
    try {
      const token = this.isDemo ? null : await this.auth.accessToken();
      if (!isCurrent()) return;
      if (!this.sameOwner()) {
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
        return;
      }
      if (polling && Date.now() - this.pollStartedAt >= POLL_WINDOW_MS) return;
      const request = this.api.getOrder(id, token).pipe(takeUntil(this.routeChanged));
      const response = await firstValueFrom(polling ? request.pipe(takeUntil(this.pollStopped)) : request);
      if (!isCurrent()) return;
      if (!this.sameOwner()) {
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
        return;
      }
      this.order.set(response);
      const projectionOwner = this.viewOwnerId();
      if (projectionOwner !== null) this.operations.observeProjection(response, projectionOwner);
      this.needsStatusRefresh.set(false);
      if (response.status === 'Held') this.commandHeld.set(false);
      else {
        this.commandHeld.set(false);
        this.commandConfirmed.set(false);
      }
      const owner = this.viewOwnerId();
      const cancelled = owner === null ? null : this.operations.cancelled(id, owner);
      this.loadState.set(
        (cancelled !== null && response.status !== 'Cancelled' && response.status !== 'Refunded') ||
          (this.visibleConfirmed() && response.status === 'Held')
          ? 'updating'
          : 'ready',
      );
    } catch (error) {
      if (!isCurrent()) return;
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.order.set(null);
        this.loadState.set(
          this.visibleConfirmed() ||
            this.commandHeld() ||
            (this.viewOwnerId() !== null && this.operations.cancelled(id, this.viewOwnerId()!) !== null)
            ? 'updating'
            : 'notFound',
        );
      } else if (error instanceof HttpErrorResponse && error.status === 401) {
        this.operations.denyAuthorization('Сеанс входа истёк. Войдите снова.');
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
      } else if (error instanceof HttpErrorResponse && error.status === 403) {
        this.operations.denyAuthorization('Доступ к заказу запрещён. Проверьте права доступа.');
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('forbidden');
      } else if (
        error instanceof FlightBookingContractError ||
        (error instanceof HttpErrorResponse && error.status === 200)
      ) {
        this.order.set(null);
        this.loadState.set('malformed');
      } else if (error instanceof HttpErrorResponse || error instanceof TimeoutError) {
        this.loadState.set('error');
      } else {
        this.loadState.set('auth');
      }
    } finally {
      if (isCurrent()) {
        this.inFlightGeneration = -1;
        this.schedulePoll(generation);
      }
    }
  }

  private schedulePoll(generation: number): void {
    const state = this.viewState();
    const status = this.order()?.status;
    if (
      (state !== 'ready' && state !== 'updating') ||
      status === 'Ticketed' ||
      status === 'Cancelled' ||
      status === 'Refunded'
    )
      return;
    if (Date.now() - this.pollStartedAt >= POLL_WINDOW_MS) {
      this.pollingEnded.set(true);
      return;
    }
    this.clearPoll();
    this.deadlineTimer = setTimeout(
      () => {
        if (generation !== this.generation) return;
        this.requestVersion++;
        this.pollStopped.next();
        this.inFlightGeneration = -1;
        if (this.pollTimer !== null) clearTimeout(this.pollTimer);
        this.pollTimer = null;
        this.pollingEnded.set(true);
      },
      Math.max(0, this.pollStartedAt + POLL_WINDOW_MS - Date.now()),
    );
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (generation === this.generation) void this.loadOrder(generation, true);
    }, POLL_INTERVAL_MS);
  }

  private clearPoll(): void {
    if (this.deadlineTimer !== null) clearTimeout(this.deadlineTimer);
    this.deadlineTimer = null;
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
}
