import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FlightBookingContractError, type FlightOrderResponse, FlightsBookingApiService } from '@travel/api-client';
import { firstValueFrom, Subject, TimeoutError, takeUntil } from 'rxjs';
import { FlightOrderHandoffService } from './flight-order-handoff.service';
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
type ConfirmAttempt = { body: { aggregateId: string }; key: string; mayHaveSucceeded: boolean };

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
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(FlightsBookingApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly handoff = inject(FlightOrderHandoffService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly routeChanged = new Subject<void>();
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;
  private inFlightGeneration = -1;
  private pollStartedAt = 0;
  private confirmAttempt: ConfirmAttempt | null = null;
  private readonly viewOwnerId = signal<string | null>(null);

  readonly isDemo = isDemoSource();
  readonly aggregateId = signal<string | null>(null);
  readonly order = signal<FlightOrderResponse | null>(null);
  readonly loadState = signal<LoadState>('loading');
  readonly commandConfirmed = signal(false);
  readonly commandHeld = signal(false);
  readonly pollingEnded = signal(false);
  readonly confirmState = signal<ConfirmState>('idle');
  readonly confirmMessage = signal<string | null>(null);
  readonly sameOwner = computed(() => {
    if (this.isDemo) return true;
    const auth = this.auth.status();
    return auth.kind === 'authenticated' && auth.userId === this.viewOwnerId();
  });
  readonly visibleOrder = computed(() => (this.sameOwner() ? this.order() : null));
  readonly visibleConfirmed = computed(() => this.sameOwner() && this.commandConfirmed());
  readonly visibleHeld = computed(() => this.sameOwner() && this.commandHeld());
  readonly viewState = computed<LoadState>(() =>
    this.viewOwnerId() !== null && !this.sameOwner() ? 'auth' : this.loadState(),
  );
  readonly displayStatus = computed(() => {
    const status = this.visibleOrder()?.status;
    if (this.visibleConfirmed() && (status === undefined || status === 'Held')) return 'Confirmed';
    return this.visibleHeld() && status === undefined ? 'Held' : status;
  });
  readonly testWallet = computed(() => this.isDemo || this.auth.isTestEnvironment());

  constructor() {
    afterNextRender(() => {
      this.element.nativeElement.ownerDocument.defaultView?.scrollTo({ top: 0, behavior: 'instant' });
      this.element.nativeElement.querySelector<HTMLElement>('h1')?.focus({ preventScroll: true });
    });
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      void this.openOrder(params.get('aggregateId'));
    });
    this.destroyRef.onDestroy(() => {
      this.clearPoll();
      this.routeChanged.next();
      this.routeChanged.complete();
    });
  }

  refresh(): void {
    this.clearPoll();
    if (this.aggregateId() === null || this.viewState() === 'auth') return;
    void this.loadOrder(this.generation);
  }

  confirm(): void {
    const current = this.visibleOrder();
    if (
      current?.status !== 'Held' ||
      this.visibleConfirmed() ||
      this.viewState() !== 'ready' ||
      this.confirmState() !== 'idle'
    )
      return;
    this.confirmAttempt = {
      body: { aggregateId: current.aggregateId },
      key: crypto.randomUUID(),
      mayHaveSucceeded: false,
    };
    void this.executeConfirm(this.confirmAttempt, this.generation);
  }

  retryConfirm(): void {
    if (
      !this.sameOwner() ||
      this.confirmAttempt === null ||
      (this.confirmState() !== 'unknown' && this.confirmState() !== 'conflict')
    )
      return;
    void this.executeConfirm(this.confirmAttempt, this.generation);
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
    this.confirmAttempt = null;
    this.confirmState.set('idle');
    this.confirmMessage.set(null);
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
    if (!this.isDemo && this.auth.status().kind !== 'authenticated') {
      this.handoff.takeOutcome(id, null);
      this.loadState.set('auth');
      return;
    }
    const auth = this.auth.status();
    this.viewOwnerId.set(auth.kind === 'authenticated' ? auth.userId : null);
    const outcome = this.handoff.takeOutcome(id, auth.kind === 'authenticated' ? auth.userId : null);
    this.commandConfirmed.set(outcome === 'Confirmed');
    this.commandHeld.set(outcome === 'Held');
    this.loadState.set(outcome === null ? 'loading' : 'updating');
    void this.loadOrder(generation);
  }

  private async executeConfirm(attempt: ConfirmAttempt, generation: number): Promise<void> {
    this.confirmState.set('pending');
    this.confirmMessage.set(null);
    let requestSent = false;
    try {
      const token = this.isDemo ? null : await this.auth.accessToken();
      if (generation !== this.generation) return;
      if (!this.sameOwner()) {
        this.confirmAttempt = null;
        this.confirmState.set('blocked');
        this.loadState.set('auth');
        return;
      }
      requestSent = true;
      attempt.mayHaveSucceeded = true;
      await firstValueFrom(this.api.confirm(attempt.body, attempt.key, token).pipe(takeUntil(this.routeChanged)));
      if (generation !== this.generation) return;
      this.confirmAttempt = null;
      this.commandConfirmed.set(true);
      this.commandHeld.set(false);
      this.confirmState.set('idle');
      this.loadState.set('updating');
      this.pollStartedAt = Date.now();
      this.pollingEnded.set(false);
      this.clearPoll();
      this.schedulePoll(generation);
    } catch (error) {
      if (generation !== this.generation) return;
      if (!requestSent && !attempt.mayHaveSucceeded) {
        this.confirmAttempt = null;
        this.confirmState.set('blocked');
        this.confirmMessage.set('Сеанс входа истёк до подтверждения. Войдите снова и проверьте заказ.');
        return;
      }
      if (error instanceof HttpErrorResponse) {
        const code = this.problemCode(error);
        if (code === 'flights.holdexpired') {
          this.confirmAttempt = null;
          this.confirmState.set('expired');
          this.confirmMessage.set('Срок удержания истёк. Начните оформление заново с поиска.');
          return;
        }
        if (error.status === 409) {
          if (code === 'flights.idempotencyinflight') {
            this.confirmState.set('conflict');
            this.confirmMessage.set('Подтверждение ещё обрабатывается. Подождите и повторите тот же запрос.');
          } else {
            this.confirmState.set('blocked');
            this.confirmMessage.set('Состояние заказа требует ручной проверки. Новый ключ не создаётся.');
          }
          return;
        }
      }
      if (
        error instanceof HttpErrorResponse &&
        error.status !== 0 &&
        error.status < 500 &&
        error.status !== 401 &&
        error.status !== 403
      ) {
        this.confirmAttempt = null;
        this.confirmState.set('blocked');
        this.confirmMessage.set('Подтверждение отклонено. Обновите состояние заказа перед новым действием.');
        return;
      }
      this.confirmState.set('unknown');
      this.confirmMessage.set(
        'Исход подтверждения неизвестен. Повторите тот же запрос с тем же ключом или обновите статус.',
      );
    }
  }

  private problemCode(error: HttpErrorResponse): string | null {
    const type = error.error && typeof error.error === 'object' ? (error.error as { type?: unknown }).type : null;
    return typeof type === 'string' ? (type.split('/').at(-1)?.toLowerCase() ?? null) : null;
  }

  private async loadOrder(generation: number): Promise<void> {
    const id = this.aggregateId();
    if (id === null || this.inFlightGeneration === generation) return;
    this.inFlightGeneration = generation;
    try {
      const token = this.isDemo ? null : await this.auth.accessToken();
      if (generation !== this.generation) return;
      if (!this.sameOwner()) {
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
        return;
      }
      const response = await firstValueFrom(this.api.getOrder(id, token).pipe(takeUntil(this.routeChanged)));
      if (generation !== this.generation) return;
      if (!this.sameOwner()) {
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
        return;
      }
      this.order.set(response);
      if (response.status === 'Held') this.commandHeld.set(false);
      else {
        this.commandHeld.set(false);
        this.commandConfirmed.set(false);
      }
      this.loadState.set(this.commandConfirmed() && response.status === 'Held' ? 'updating' : 'ready');
    } catch (error) {
      if (generation !== this.generation) return;
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.order.set(null);
        this.loadState.set(this.commandConfirmed() || this.commandHeld() ? 'updating' : 'notFound');
      } else if (error instanceof HttpErrorResponse && error.status === 401) {
        this.order.set(null);
        this.commandConfirmed.set(false);
        this.commandHeld.set(false);
        this.loadState.set('auth');
      } else if (error instanceof HttpErrorResponse && error.status === 403) {
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
      if (generation === this.generation) {
        this.inFlightGeneration = -1;
        this.schedulePoll(generation);
      }
    }
  }

  private schedulePoll(generation: number): void {
    const state = this.viewState();
    const status = this.visibleOrder()?.status;
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
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (generation === this.generation) void this.loadOrder(generation);
    }, POLL_INTERVAL_MS);
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
}
