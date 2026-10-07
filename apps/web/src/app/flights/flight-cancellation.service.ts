import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, Injectable, inject, signal } from '@angular/core';
import type {
  CancellationStatusResponse,
  CancellationTerms,
  FlightOrderResponse,
  FlightOrderStatus,
} from '@travel/api-client';
import { FlightsCancellationApiService } from '@travel/api-client';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import { FlightsAuthService } from './flights-auth.service';
import { isDemoSource } from './flights-source-mode';

type Stage = 'prepare' | 'consent' | 'abandon' | 'refresh';
type Admission = { stage: Stage; raw: string; generation: number; sent: boolean };
type CachedStatus = { value: CancellationStatusResponse; expiresAt: number; refreshAt: number };
const terminal = new Set(['Succeeded', 'Rejected', 'Abandoned', 'Expired']);
const statusRank: Readonly<Record<FlightOrderStatus | 'OfferQuoted', number>> = {
  OfferQuoted: 0,
  Held: 1,
  Confirmed: 2,
  Ticketed: 3,
  Cancelled: 4,
  Refunded: 5,
};

@Injectable({ providedIn: 'root' })
export class FlightCancellationService {
  private readonly api = inject(FlightsCancellationApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly document = inject(DOCUMENT);
  private readonly destroy = inject(DestroyRef);
  private readonly state = signal<CancellationStatusResponse | null>(null);
  private readonly clock = signal(Date.now());
  private readonly stopReads = new Subject<void>();
  private readonly known = new Map<string, CachedStatus>();
  private cacheIdentity: string | null = null;
  private readonly identity = signal<string | null>(null);
  private generation = 0;
  private readGeneration = -1;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private expiryTimer: ReturnType<typeof setTimeout> | null = null;
  private openedAt = 0;
  private readUntil = 0;
  private expiresAt = 0;
  private refreshAt = 0;
  private lastAdmission: Admission | null = null;

  readonly aggregateId = signal<string | null>(null);
  readonly accepted = signal(false);
  readonly busy = signal(false);
  readonly loadState = signal<'idle' | 'loading' | 'ready' | 'error' | 'auth' | 'forbidden'>('idle');
  readonly message = signal('');
  readonly snapshot = computed(() => (this.currentIdentity() === this.identity() ? this.state() : null));
  readonly terms = computed(() => this.snapshot()?.operation?.terms ?? null);
  readonly termsFresh = computed(() => {
    this.clock();
    return this.terms() !== null && Date.now() < this.expiresAt;
  });
  readonly blocksConfirmation = computed(() => {
    const value = this.snapshot();
    return (
      this.loadState() !== 'ready' ||
      value === null ||
      value.blockingConfirmation !== null ||
      (value.operation !== null && !terminal.has(value.operation.phase))
    );
  });
  readonly canPrepare = computed(() => {
    const value = this.snapshot();
    return (
      !this.busy() &&
      this.loadState() === 'ready' &&
      value !== null &&
      value.isCurrentOperation &&
      ['Held', 'Confirmed', 'Ticketed'].includes(value.bookingStatus) &&
      value.blockingConfirmation === null &&
      (value.operation === null || terminal.has(value.operation.phase))
    );
  });
  readonly canConsent = computed(() => !this.busy() && this.accepted() && this.consentUsable());
  readonly canAbandon = computed(
    () => !this.busy() && ['TermsReady', 'UnsupportedTerms'].includes(this.snapshot()?.operation?.phase ?? ''),
  );
  readonly canRefresh = computed(() => {
    this.clock();
    const op = this.snapshot()?.operation;
    return (
      !this.busy() &&
      op !== undefined &&
      op !== null &&
      !terminal.has(op.phase) &&
      op.dispatchState !== 'NotDispatched' &&
      !op.readPending &&
      Date.now() >= this.refreshAt &&
      ['Preparing', 'DispatchClaimed', 'Unknown', 'ManualReviewRequired'].includes(op.phase)
    );
  });

  constructor() {
    this.cacheIdentity = this.currentIdentity();
    effect(() => {
      const identity = this.currentIdentity();
      if (identity !== this.cacheIdentity) {
        this.generation++;
        this.stopReads.next();
        this.clearTimers();
        this.known.clear();
        this.cacheIdentity = identity;
        this.identity.set(null);
        this.state.set(null);
        this.accepted.set(false);
        this.busy.set(false);
        this.lastAdmission = null;
        this.aggregateId.set(null);
        this.loadState.set('auth');
      }
    });
    const visibility = () => {
      if (this.document.hidden) this.clearPoll();
      else if (this.aggregateId() !== null && this.isCurrent(this.generation)) this.read();
    };
    this.document.addEventListener('visibilitychange', visibility);
    this.destroy.onDestroy(() => {
      this.generation++;
      this.clearTimers();
      this.stopReads.next();
      this.stopReads.complete();
      this.document.removeEventListener('visibilitychange', visibility);
    });
  }

  open(aggregateId: string, owner: string): void {
    const identity = this.currentIdentity();
    const auth = this.auth.status();
    if (identity === null || auth.kind !== 'authenticated' || auth.userId.toLowerCase() !== owner.toLowerCase()) return;
    if (identity !== this.cacheIdentity) {
      this.known.clear();
      this.cacheIdentity = identity;
    }
    this.generation++;
    this.stopReads.next();
    this.clearTimers();
    this.readGeneration = -1;
    this.identity.set(identity);
    this.aggregateId.set(aggregateId.toLowerCase());
    this.accepted.set(false);
    this.busy.set(false);
    this.lastAdmission = null;
    this.message.set('');
    this.openedAt = Date.now();
    this.readUntil = 0;
    const known = this.known.get(aggregateId.toLowerCase());
    this.expiresAt = known?.expiresAt ?? 0;
    this.refreshAt = known?.refreshAt ?? 0;
    this.state.set(known?.value ?? null);
    this.clock.set(Date.now());
    this.loadState.set(this.state() === null ? 'loading' : 'ready');
    this.scheduleViewTime();
    this.read();
  }
  close(): void {
    this.generation++;
    this.stopReads.next();
    this.clearTimers();
    this.identity.set(null);
    this.aggregateId.set(null);
    this.state.set(null);
    this.accepted.set(false);
    this.busy.set(false);
    this.lastAdmission = null;
    this.loadState.set('idle');
  }
  read(): void {
    void this.load();
  }
  knownStatus(id: string, owner: string): CancellationStatusResponse | null {
    this.state();
    const auth = this.auth.status();
    return this.currentIdentity() === this.cacheIdentity &&
      auth.kind === 'authenticated' &&
      auth.userId.toLowerCase() === owner.toLowerCase()
      ? (this.known.get(id.toLowerCase())?.value ?? null)
      : null;
  }
  overlayOrder(order: FlightOrderResponse, owner: string): FlightOrderResponse {
    const known = this.knownStatus(order.aggregateId, owner);
    if (
      known === null ||
      known.bookingStatus === 'OfferQuoted' ||
      statusRank[known.bookingStatus] <= statusRank[order.status]
    )
      return order;
    // UI state overlay only: preserve observed dates and booking total; never synthesize a refund/payout.
    return { ...order, status: known.bookingStatus };
  }
  overlayStatus(projected: FlightOrderStatus | null): FlightOrderStatus | null {
    const known = this.snapshot()?.bookingStatus;
    return known === undefined || known === 'OfferQuoted'
      ? projected
      : projected === null || statusRank[known] >= statusRank[projected]
        ? known
        : projected;
  }
  prepare(): boolean {
    const value = this.snapshot();
    if (!this.canPrepare() || value === null) return false;
    return this.dispatch(
      'prepare',
      JSON.stringify({
        aggregateId: value.aggregateId,
        operationId: crypto.randomUUID(),
        expectedBookingVersion: value.bookingVersion,
      }),
    );
  }
  consent(): boolean {
    const value = this.snapshot(),
      op = value?.operation,
      terms = op?.terms;
    if (!this.canConsent() || !value || !op || !terms) return false;
    return this.dispatch(
      'consent',
      JSON.stringify({
        aggregateId: value.aggregateId,
        operationId: op.operationId,
        expectedOperationRevision: op.revision,
        termsRevision: terms.revision,
        termsHash: terms.hash,
        noticeVersion: terms.noticeVersion,
        accepted: true,
      }),
    );
  }
  abandon(): boolean {
    const value = this.snapshot(),
      op = value?.operation;
    if (!this.canAbandon() || !value || !op) return false;
    return this.dispatch(
      'abandon',
      JSON.stringify({
        aggregateId: value.aggregateId,
        operationId: op.operationId,
        expectedOperationRevision: op.revision,
      }),
    );
  }
  refresh(): boolean {
    const value = this.snapshot(),
      op = value?.operation;
    if (!this.canRefresh() || !value || !op) return false;
    this.readUntil = Date.now() + 10_000;
    return this.dispatch(
      'refresh',
      JSON.stringify({
        aggregateId: value.aggregateId,
        operationId: op.operationId,
        expectedOperationRevision: op.revision,
        refreshRequestId: crypto.randomUUID(),
      }),
    );
  }

  private currentIdentity(): string | null {
    const value = this.auth.status();
    return value.kind === 'authenticated'
      ? value.userId.toLowerCase() + ':' + (this.auth.identityEpoch?.() ?? 0)
      : null;
  }
  private isCurrent(generation: number): boolean {
    return (
      generation === this.generation &&
      this.aggregateId() !== null &&
      this.identity() !== null &&
      this.identity() === this.currentIdentity()
    );
  }
  private consentUsable(): boolean {
    const value = this.snapshot();
    return (
      value !== null &&
      value.isCurrentOperation &&
      value.blockingConfirmation === null &&
      value.operation?.phase === 'TermsReady' &&
      this.termsFresh() &&
      this.loadState() === 'ready'
    );
  }
  private termKey(terms: CancellationTerms | null | undefined): string {
    return terms === null || terms === undefined ? '' : terms.hash + ':' + terms.revision + ':' + terms.noticeVersion;
  }
  private update(value: CancellationStatusResponse, generation: number): void {
    if (!this.isCurrent(generation) || value.aggregateId.toLowerCase() !== this.aggregateId()) return;
    if (!value.isCurrentOperation) {
      queueMicrotask(() => this.read());
      return;
    }
    const prior = this.state();
    if (
      prior !== null &&
      (value.bookingVersion < prior.bookingVersion ||
        (value.currentOperationId === prior.currentOperationId &&
          value.operation &&
          prior.operation &&
          value.operation.revision < prior.operation.revision) ||
        statusRank[value.bookingStatus] < statusRank[prior.bookingStatus] ||
        (value.currentOperationId === prior.currentOperationId &&
          prior.operation?.outcome === 'Succeeded' &&
          value.operation?.outcome !== 'Succeeded'))
    )
      return;
    if (
      prior?.bookingStatus !== value.bookingStatus ||
      this.termKey(prior?.operation?.terms) !== this.termKey(value.operation?.terms) ||
      value.operation?.phase !== 'TermsReady'
    )
      this.accepted.set(false);
    this.state.set(value);
    this.loadState.set('ready');
    const server = Date.parse(value.serverNow);
    this.expiresAt = Date.now() + Math.max(0, Date.parse(value.operation?.terms?.expiresAt ?? '') - server);
    this.refreshAt = Date.now() + Math.max(0, Date.parse(value.operation?.nextRefreshAt ?? value.serverNow) - server);
    this.known.set(value.aggregateId.toLowerCase(), { value, expiresAt: this.expiresAt, refreshAt: this.refreshAt });
    this.clock.set(Date.now());
    this.scheduleViewTime();
  }
  private revokeView(): void {
    this.generation++;
    this.readGeneration = -1;
    this.stopReads.next();
    this.busy.set(false);
    this.lastAdmission = null;
    this.readUntil = 0;
    const id = this.aggregateId();
    if (id !== null) this.known.delete(id);
    this.state.set(null);
    this.accepted.set(false);
    this.clearTimers();
  }
  private validateToken(token: string | null): void {
    if (token === null && !(isDemoSource() && this.auth.isDemo)) throw new Error('Authentication unavailable.');
  }
  private async load(): Promise<void> {
    const generation = this.generation,
      id = this.aggregateId();
    if (id === null || !this.isCurrent(generation) || this.readGeneration === generation) return;
    this.readGeneration = generation;
    try {
      const token = await this.auth.accessToken();
      this.validateToken(token);
      if (!this.isCurrent(generation)) return;
      const value = await firstValueFrom(this.api.status(id, token).pipe(takeUntil(this.stopReads)));
      if (!this.isCurrent(generation)) return;
      this.update(value, generation);
    } catch (error) {
      if (!this.isCurrent(generation)) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.loadState.set(error.status === 401 ? 'auth' : 'forbidden');
        this.revokeView();
      } else {
        this.loadState.set(this.state() === null ? 'error' : 'ready');
        this.message.set('Не удалось обновить состояние. Повторная отмена не требуется; обновите статус.');
      }
    } finally {
      if (this.isCurrent(generation)) {
        this.readGeneration = -1;
        this.schedule();
      }
    }
  }
  private dispatch(stage: Stage, raw: string): boolean {
    if (this.busy() || !this.isCurrent(this.generation)) return false;
    const admission: Admission = { stage, raw, generation: this.generation, sent: false };
    this.lastAdmission = admission;
    this.busy.set(true);
    this.message.set('');
    void this.execute(admission);
    return true;
  }
  private async execute(admission: Admission): Promise<void> {
    const key = this.termKey(this.terms());
    try {
      const token = await this.auth.accessToken();
      this.validateToken(token);
      if (!this.isCurrent(admission.generation)) return;
      if (
        admission.stage === 'consent' &&
        (!this.consentUsable() || !this.accepted() || key !== this.termKey(this.terms()))
      ) {
        this.message.set('Условия изменились или истекли. Проверьте их снова.');
        return;
      }
      admission.sent = true;
      const request = this.api[admission.stage](admission.raw, token);
      const value = await firstValueFrom(request);
      if (!this.isCurrent(admission.generation)) return;
      this.update(value, admission.generation);
    } catch (error) {
      if (!this.isCurrent(admission.generation)) return;
      if (!admission.sent) this.message.set('Сеанс входа истёк до отправки. Войдите снова и проверьте заказ.');
      else if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.revokeView();
        this.loadState.set(error.status === 401 ? 'auth' : 'forbidden');
        this.message.set('Доступ к операции недоступен. После входа сначала проверьте состояние.');
      } else {
        this.message.set(
          error instanceof HttpErrorResponse && error.status === 429
            ? 'Проверка выполняется не чаще раза в минуту. Дождитесь следующей доступной проверки.'
            : 'Ответ не подтверждает результат. Проверяем сохранённое состояние; отмену повторно не отправляем.',
        );
        this.read();
      }
    } finally {
      if (this.isCurrent(admission.generation)) {
        this.busy.set(false);
        this.clock.set(Date.now());
        this.schedule();
      }
    }
  }
  private scheduleViewTime(): void {
    this.clearExpiry();
    const now = Date.now();
    const deadlines = [
      this.refreshAt,
      ...(this.state()?.operation?.phase === 'TermsReady' ? [this.expiresAt] : []),
    ].filter((time) => time > now);
    if (deadlines.length === 0) return;
    this.expiryTimer = setTimeout(
      () => {
        this.clock.set(Date.now());
        if (this.state()?.operation?.phase === 'TermsReady' && Date.now() >= this.expiresAt) this.accepted.set(false);
        this.scheduleViewTime();
      },
      Math.min(Math.min(...deadlines) - now + 1, 2_147_483_647),
    );
  }
  private schedule(): void {
    this.clearPoll();
    const value = this.snapshot(),
      op = value?.operation;
    if (
      this.document.hidden ||
      !this.isCurrent(this.generation) ||
      this.loadState() === 'auth' ||
      this.loadState() === 'forbidden'
    )
      return;
    if (!op) return;
    if (terminal.has(op.phase) || ['TermsReady', 'UnsupportedTerms'].includes(op.phase)) return;
    if (op.phase === 'ManualReviewRequired' && (!op.readPending || Date.now() >= this.readUntil)) return;
    const generation = this.generation;
    this.pollTimer = setTimeout(
      () => {
        this.pollTimer = null;
        if (this.isCurrent(generation)) this.read();
      },
      Date.now() - this.openedAt < 30_000 ? 2_000 : 10_000,
    );
  }
  private clearPoll(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
  private clearExpiry(): void {
    if (this.expiryTimer !== null) clearTimeout(this.expiryTimer);
    this.expiryTimer = null;
  }
  private clearTimers(): void {
    this.clearPoll();
    this.clearExpiry();
  }
}
