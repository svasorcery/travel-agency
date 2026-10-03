import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, effect, Injectable, inject, signal, untracked } from '@angular/core';
import type {
  CancelledFlightOrderResponse,
  ConfirmedFlightOrderResponse,
  FlightOrderResponse,
  FlightQuoteResponse,
  HeldFlightOrderResponse,
  HoldFlightOrderRequest,
} from '@travel/api-client';
import { FlightsBookingApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
import { FlightOrdersFeedService } from './flight-orders-feed.service';
import { FlightsAuthService } from './flights-auth.service';

export type OrderOperationState = 'pending' | 'success' | 'rejected' | 'conflict' | 'unknown';
export interface OrderOperationView {
  attemptId: string;
  receiptId: number | null;
  kind: 'confirm' | 'cancel';
  state: OrderOperationState;
  message: string;
  retryable: boolean;
  result: ConfirmedFlightOrderResponse | CancelledFlightOrderResponse | null;
}
interface Attempt extends Omit<OrderOperationView, 'attemptId'> {
  id: string;
  owner: string;
  key: string;
  body: { aggregateId: string };
  dispatchedAt: number | null;
  hadUnknown: boolean;
}

export interface HoldOperationView {
  aggregateId: string;
  state: OrderOperationState;
  message: string;
  errorCode: string | null;
  result: HeldFlightOrderResponse | null;
}
interface HoldAttempt extends HoldOperationView {
  owner: string;
  epoch: number;
  key: string;
  rawBody: string | null;
  createdAt: number;
  expiresAt: number;
  dispatchedAt: number | null;
  hadUnknown: boolean;
}

const REPLAY_WINDOW_MS = 24 * 60 * 60_000;
const REJECTION_STATUS: Readonly<Record<string, number>> = {
  'flights.providercancellationnotsupported': 409,
  'flights.providerordermissing': 409,
  'flights.ordernotcancellable': 409,
  'flights.invalidstate': 409,
  'flights.holdexpired': 409,
  'flights.orderpricechanged': 409,
  'flights.offernotfound': 404,
  'flights.commandinvalid': 400,
  'flights.idempotencykey.missing': 400,
};

/** Tab-local command evidence. It cannot serialize writes from other tabs or recover after reload. */
@Injectable({ providedIn: 'root' })
export class FlightOrderOperationsService {
  private readonly auth = inject(FlightsAuthService);
  private readonly api = inject(FlightsBookingApiService);
  private readonly feed = inject(FlightOrdersFeedService);
  readonly revision = signal(0);
  private holdAttempt: HoldAttempt | null = null;
  private holdTimer: ReturnType<typeof setTimeout> | null = null;
  private attempts = new Map<string, Attempt>();
  private terminal = new Map<string, { order: CancelledFlightOrderResponse; receivedAt: number }>();
  private confirmedIds = new Set<string>();
  private owner: string | null = null;
  private authKind = '';
  private authEpoch = this.auth.identityEpoch?.() ?? 0;
  private epoch = 0;
  private receiptCounter = 0;

  constructor() {
    effect(() => {
      this.auth.status();
      this.auth.identityEpoch?.();
      untracked(() => this.syncAuth());
    });
    inject(DestroyRef).onDestroy(() => this.clear());
  }

  startHold(
    body: HoldFlightOrderRequest,
    quote: FlightQuoteResponse,
    accepted: boolean,
    owner: string,
    demo: boolean,
  ): boolean {
    if (
      !this.matchesOwner(owner) ||
      this.blocksBooking(owner) ||
      !accepted ||
      body.aggregateId !== quote.aggregateId ||
      body.passengers.length !== 1 ||
      !Number.isFinite(Date.parse(quote.offer.expiresAt)) ||
      Date.parse(quote.offer.expiresAt) <= Date.now()
    )
      return false;
    const attempt: HoldAttempt = {
      aggregateId: body.aggregateId,
      owner: owner.toLowerCase(),
      epoch: this.epoch,
      key: crypto.randomUUID(),
      rawBody: JSON.stringify(body),
      createdAt: Date.now(),
      expiresAt: Date.parse(quote.offer.expiresAt),
      dispatchedAt: null,
      hadUnknown: false,
      state: 'pending',
      message: '',
      errorCode: null,
      result: null,
    };
    this.eraseHoldBody();
    this.holdAttempt = attempt;
    this.holdTimer = setTimeout(() => {
      this.expireHoldBody();
      this.changed();
    }, REPLAY_WINDOW_MS);
    this.changed();
    void this.executeHold(attempt, demo);
    return true;
  }

  holdOperation(owner: string): HoldOperationView | null {
    this.revision();
    if (!this.matchesOwner(owner)) return null;
    this.expireHoldBody();
    const attempt = this.holdAttempt;
    return attempt === null
      ? null
      : {
          aggregateId: attempt.aggregateId,
          state: attempt.state,
          message: attempt.message,
          errorCode: attempt.errorCode,
          result: attempt.result === null ? null : structuredClone(attempt.result),
        };
  }

  blocksHold(owner: string): boolean {
    const state = this.holdOperation(owner)?.state;
    return state === 'pending' || state === 'unknown' || state === 'conflict';
  }

  blocksBooking(owner: string): boolean {
    return this.matchesOwner(owner) && this.blocksBookingInSession();
  }

  /** Non-PII tab/session barrier remains visible while same-session auth is temporarily unavailable. */
  blocksBookingInSession(): boolean {
    this.revision();
    this.syncAuth();
    this.expireHoldBody();
    const holdState = this.holdAttempt?.state;
    return (
      holdState === 'pending' ||
      holdState === 'unknown' ||
      holdState === 'conflict' ||
      [...this.attempts.values()].some(
        (attempt) =>
          attempt.kind === 'confirm' &&
          (attempt.state === 'pending' || attempt.state === 'unknown' || attempt.state === 'conflict'),
      )
    );
  }

  private async executeHold(attempt: HoldAttempt, demo: boolean): Promise<void> {
    try {
      const token = demo ? null : await this.auth.accessToken();
      if (!this.isCurrentHold(attempt)) return;
      this.expireHoldBody();
      if (attempt.rawBody === null || attempt.expiresAt <= Date.now()) {
        attempt.state = 'rejected';
        attempt.errorCode = 'flights.offerexpired';
        attempt.message = 'Срок предложения истёк. Обновите цену перед новым оформлением.';
        this.eraseHoldBody();
        return;
      }
      attempt.dispatchedAt = Date.now();
      const held = await firstValueFrom(this.api.holdRaw(attempt.rawBody, attempt.aggregateId, attempt.key, token));
      if (!this.isCurrentHold(attempt)) return;
      attempt.state = 'success';
      attempt.result = held;
      attempt.message = '';
      attempt.errorCode = null;
      this.eraseHoldBody();
    } catch (error) {
      if (!this.isCurrentHold(attempt)) return;
      const code = this.problemCode(error);
      attempt.errorCode = code;
      if (
        !attempt.hadUnknown &&
        (attempt.dispatchedAt === null ||
          (error instanceof HttpErrorResponse &&
            ((error.status === 413 && code === 'flights.requesttoolarge') ||
              (error.status === 400 &&
                code !== null &&
                (code.startsWith('passengerinfo.') ||
                  ['flights.commandinvalid', 'flights.idempotencykey.missing', 'flights.offerexpired'].includes(
                    code,
                  ))))))
      ) {
        attempt.state = 'rejected';
        attempt.message =
          attempt.dispatchedAt === null
            ? 'Сеанс входа истёк до отправки. Вернитесь к проверке цены и войдите снова.'
            : code === 'flights.requesttoolarge'
              ? 'Данные пассажира превышают допустимый размер запроса. Сократите данные и повторите оформление.'
              : code === 'flights.offerexpired'
                ? 'Срок предложения истёк. Обновите цену перед новым оформлением.'
                : 'Действие отклонено. Проверьте данные пассажира.';
        this.eraseHoldBody();
      } else {
        attempt.state = 'unknown';
        attempt.hadUnknown = true;
        attempt.message =
          code === 'flights.idempotencyconflict'
            ? 'Ключ запроса связан с другим содержимым. Исход удержания требует ручной проверки.'
            : code === 'flights.invalidstate'
              ? 'Состояние заказа требует проверки. Не создавайте новое удержание.'
              : 'Исход удержания неизвестен. Не повторяйте удержание. Требуется проверка заказа.';
      }
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.denyAuthorization('Сеанс входа истёк или доступ запрещён. Войдите снова.');
      }
    } finally {
      if (this.isCurrentHold(attempt)) this.changed();
    }
  }

  private isCurrentHold(attempt: HoldAttempt): boolean {
    return this.matchesOwner(attempt.owner) && attempt.epoch === this.epoch && this.holdAttempt === attempt;
  }

  private eraseHoldBody(): void {
    if (this.holdAttempt !== null) this.holdAttempt.rawBody = null;
    if (this.holdTimer !== null) clearTimeout(this.holdTimer);
    this.holdTimer = null;
  }

  private expireHoldBody(): void {
    const attempt = this.holdAttempt;
    if (attempt === null || attempt.rawBody === null || Date.now() - attempt.createdAt < REPLAY_WINDOW_MS) return;
    this.eraseHoldBody();
    if (attempt.state === 'pending' || attempt.state === 'unknown') {
      attempt.state = attempt.dispatchedAt === null ? 'rejected' : 'unknown';
      attempt.hadUnknown = attempt.dispatchedAt !== null;
      attempt.message = 'Данные пассажира удалены по сроку хранения. Исход удержания требует проверки.';
    }
  }

  startConfirm(id: string, owner: string): boolean {
    return !this.blocksBooking(owner) && this.start('confirm', id, owner);
  }
  startCancel(id: string, owner: string): boolean {
    return this.start('cancel', id, owner);
  }

  operation(id: string, owner: string): OrderOperationView | null {
    this.revision();
    if (!this.matchesOwner(owner)) return null;
    const attempt = this.attempts.get(id.toLowerCase());
    return attempt === undefined
      ? null
      : {
          kind: attempt.kind,
          attemptId: attempt.key,
          receiptId: attempt.receiptId,
          state: attempt.state,
          message: attempt.message,
          retryable: attempt.retryable,
          result: attempt.result === null ? null : structuredClone(attempt.result),
        };
  }

  blocksWrite(id: string, owner: string): boolean {
    const state = this.operation(id, owner)?.state;
    return state === 'pending' || state === 'unknown' || state === 'conflict';
  }

  confirmed(id: string, owner: string): boolean {
    this.revision();
    return this.matchesOwner(owner) && this.confirmedIds.has(id.toLowerCase());
  }

  cancelled(id: string, owner: string): CancelledFlightOrderResponse | null {
    this.revision();
    if (!this.matchesOwner(owner)) return null;
    const known = this.terminal.get(id.toLowerCase());
    return known === undefined ? null : structuredClone(known.order);
  }

  cancellationDeadline(id: string, owner: string): number | null {
    this.revision();
    return this.matchesOwner(owner) ? (this.terminal.get(id.toLowerCase())?.receivedAt ?? null) : null;
  }

  overlay(order: FlightOrderResponse, owner: string): FlightOrderResponse {
    const known = this.cancelled(order.aggregateId, owner);
    if (known !== null) return order.status === 'Refunded' ? order : known;
    return this.confirmed(order.aggregateId, owner) && order.status === 'Held'
      ? { ...order, status: 'Confirmed' }
      : order;
  }

  observeProjection(order: FlightOrderResponse, owner: string): void {
    if (!this.matchesOwner(owner) || order.status !== 'Refunded' || order.refundedAt === null) return;
    const id = order.aggregateId.toLowerCase();
    const known = this.terminal.get(id);
    if (known === undefined || known.order.status === 'Refunded') return;
    known.order = structuredClone(order) as CancelledFlightOrderResponse;
    this.feed.patchKnownOutcome(owner, known.order);
    this.changed();
  }

  retry(id: string, owner: string): boolean {
    if (!this.matchesOwner(owner)) return false;
    const attempt = this.attempts.get(id.toLowerCase());
    if (attempt?.kind === 'confirm') return false;
    if (!attempt?.retryable || (attempt.state !== 'unknown' && attempt.state !== 'conflict')) return false;
    if (this.expireReplay(attempt)) return false;
    void this.execute(attempt);
    return true;
  }

  clear(): void {
    this.epoch++;
    this.eraseHoldBody();
    this.holdAttempt = null;
    this.attempts.clear();
    this.terminal.clear();
    this.confirmedIds.clear();
    this.changed();
  }

  denyAuthorization(message: string): void {
    this.auth.status.set({ kind: 'error', message });
    this.syncAuth();
    this.feed.clear();
  }

  private start(kind: Attempt['kind'], id: string, owner: string): boolean {
    const canonical = id.toLowerCase();
    if (!this.matchesOwner(owner) || this.blocksWrite(canonical, owner) || this.terminal.has(canonical)) return false;
    if (kind === 'confirm' && this.confirmedIds.has(canonical)) return false;
    const attempt: Attempt = {
      kind,
      id: canonical,
      owner: owner.toLowerCase(),
      key: crypto.randomUUID(),
      receiptId: null,
      body: Object.freeze({ aggregateId: canonical }),
      dispatchedAt: null,
      hadUnknown: false,
      state: 'pending',
      message: '',
      retryable: false,
      result: null,
    };
    this.attempts.set(canonical, attempt);
    void this.execute(attempt);
    return true;
  }

  private async execute(attempt: Attempt): Promise<void> {
    const epoch = this.epoch;
    const previousState = attempt.state;
    attempt.state = 'pending';
    attempt.retryable = false;
    attempt.message = '';
    this.changed();
    try {
      const token = await this.auth.accessToken();
      if (!this.isCurrent(attempt, epoch)) return;
      if (this.expireReplay(attempt, previousState)) return;
      attempt.dispatchedAt ??= Date.now();
      const result =
        attempt.kind === 'cancel'
          ? await firstValueFrom(this.api.cancel(attempt.id, attempt.key, token))
          : await firstValueFrom(this.api.confirm(attempt.body, attempt.key, token));
      if (!this.isCurrent(attempt, epoch)) return;
      attempt.state = 'success';
      attempt.receiptId = ++this.receiptCounter;
      attempt.result = result;
      if (attempt.kind === 'cancel') {
        const order = result as CancelledFlightOrderResponse;
        this.terminal.set(attempt.id, { order: structuredClone(order), receivedAt: Date.now() });
        this.feed.patchKnownOutcome(attempt.owner, order);
      } else this.confirmedIds.add(attempt.id);
    } catch (error) {
      if (!this.isCurrent(attempt, epoch)) return;
      const code = this.problemCode(error);
      if (attempt.dispatchedAt === null) {
        attempt.state = 'rejected';
        attempt.message = 'Сеанс входа истёк до отправки. Войдите снова и проверьте заказ.';
      } else if (code === 'flights.idempotencyinflight') {
        attempt.state = 'conflict';
        attempt.retryable = attempt.kind === 'cancel';
        attempt.hadUnknown = true;
        attempt.message =
          attempt.kind === 'cancel'
            ? 'Запрос ещё обрабатывается. Подождите и повторите тот же запрос.'
            : 'Исход подтверждения неизвестен. Не повторяйте подтверждение. Требуется проверка заказа.';
      } else if (
        code === 'flights.idempotencyconflict' ||
        code === 'flights.concurrencyconflict' ||
        (error instanceof HttpErrorResponse &&
          error.status === 409 &&
          (code === null || REJECTION_STATUS[code] !== 409))
      ) {
        attempt.state = 'conflict';
        attempt.hadUnknown = true;
        attempt.message = 'Состояние операции требует проверки. Новый ключ не создаётся.';
      } else if (
        !attempt.hadUnknown &&
        error instanceof HttpErrorResponse &&
        code !== null &&
        REJECTION_STATUS[code] === error.status
      ) {
        attempt.state = 'rejected';
        attempt.message =
          code === 'flights.providercancellationnotsupported'
            ? 'Отмена у поставщика в этой версии недоступна. Заказ не изменён.'
            : code === 'flights.holdexpired'
              ? 'Срок удержания истёк. Начните оформление заново с поиска.'
              : 'Действие отклонено. Обновите состояние заказа перед новым действием.';
      } else {
        attempt.state = 'unknown';
        attempt.hadUnknown = true;
        attempt.retryable = attempt.kind === 'cancel';
        attempt.message =
          attempt.kind === 'confirm'
            ? 'Исход подтверждения неизвестен. Не повторяйте подтверждение. Требуется проверка заказа.'
            : `Исход отмены неизвестен. Повторите тот же запрос с тем же ключом или проверьте статус.`;
      }
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.denyAuthorization('Сеанс входа истёк или доступ запрещён. Войдите снова.');
      }
    } finally {
      if (this.isCurrent(attempt, epoch)) this.changed();
    }
  }

  private expireReplay(attempt: Attempt, state: OrderOperationState = attempt.state): boolean {
    if (attempt.dispatchedAt === null || Date.now() - attempt.dispatchedAt < REPLAY_WINDOW_MS) return false;
    attempt.state = state;
    attempt.retryable = false;
    attempt.message = 'Срок повторения запроса истёк. Нужна проверка заказа; новый ключ не создаётся.';
    this.changed();
    return true;
  }

  private isCurrent(attempt: Attempt, epoch: number): boolean {
    return this.matchesOwner(attempt.owner) && epoch === this.epoch && this.attempts.get(attempt.id) === attempt;
  }

  private matchesOwner(owner: string): boolean {
    const current = this.syncAuth();
    return current !== null && current === owner.toLowerCase();
  }

  private syncAuth(): string | null {
    const status = this.auth.status();
    const owner = status.kind === 'authenticated' ? status.userId.toLowerCase() : null;
    const authEpoch = this.auth.identityEpoch?.() ?? 0;
    const epochChanged = authEpoch !== this.authEpoch;
    this.authEpoch = authEpoch;
    if (
      epochChanged ||
      (status.kind === 'anonymous' &&
        (this.authKind !== 'anonymous' || this.attempts.size > 0 || this.holdAttempt !== null)) ||
      (owner !== null && this.owner !== null && owner !== this.owner)
    ) {
      this.clear();
      this.owner = owner;
    } else if (owner === null && status.kind !== this.authKind) {
      this.epoch++;
      this.terminal.clear();
      if (this.holdAttempt !== null && this.holdAttempt.state !== 'success' && this.holdAttempt.state !== 'rejected') {
        if (this.holdAttempt.dispatchedAt === null) {
          this.eraseHoldBody();
          this.holdAttempt = null;
        } else {
          this.holdAttempt.state = 'unknown';
          this.holdAttempt.hadUnknown = true;
          this.holdAttempt.result = null;
          this.holdAttempt.message = 'Исход удержания неизвестен. Не повторяйте удержание. Требуется проверка заказа.';
        }
      }
      for (const [id, attempt] of this.attempts) {
        if (attempt.kind === 'confirm' && (attempt.state === 'success' || attempt.state === 'rejected')) continue;
        if (attempt.dispatchedAt === null) this.attempts.delete(id);
        else {
          attempt.state = 'unknown';
          attempt.hadUnknown = true;
          attempt.retryable = attempt.kind === 'cancel';
          attempt.result = null;
          if (attempt.kind === 'confirm')
            attempt.message = 'Исход подтверждения неизвестен. Не повторяйте подтверждение. Требуется проверка заказа.';
        }
      }
      this.changed();
    }
    this.owner ??= owner;
    this.authKind = status.kind;
    return owner;
  }

  private problemCode(error: unknown): string | null {
    if (!(error instanceof HttpErrorResponse)) return null;
    const type: unknown = error.error?.type;
    const prefix = 'https://travel.local/errors/';
    return typeof type === 'string' && type.startsWith(prefix) ? type.slice(prefix.length).toLowerCase() : null;
  }

  private changed(): void {
    this.revision.update((value) => value + 1);
  }
}
