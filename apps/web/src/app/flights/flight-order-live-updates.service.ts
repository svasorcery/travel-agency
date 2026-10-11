import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject, signal } from '@angular/core';
import { FlightOrderEventsError, openFlightOrderEvents } from '@travel/api-client';

export type OrderLiveReaderName = 'creation' | 'order' | 'cancellation' | 'refund';
export interface OrderLiveReadResult {
  outcome: 'ok' | 'stale' | 'missing' | 'error' | 'stopped' | 'notApplicable' | 'auth';
  version: number | null;
  status?: number;
}
export interface OrderLiveContext {
  key: string;
  aggregateId: string;
  getToken: () => Promise<string | null>;
  isCurrent: () => boolean;
  canStream: () => boolean;
  orderApplicable?: () => boolean;
  readers: Record<OrderLiveReaderName, (token: string | null, signal: AbortSignal) => Promise<OrderLiveReadResult>>;
  onPause: (paused: boolean) => void;
  onAuthorizationFailure: (status?: number) => void;
  allowFictionalAnonymous?: boolean;
  transport?: typeof openFlightOrderEvents;
}
const names: OrderLiveReaderName[] = ['creation', 'order', 'cancellation', 'refund'];
const backoff = [1000, 2000, 5000, 10_000, 30_000];
export const ORDER_EVENTS_TRANSPORT = new InjectionToken<typeof openFlightOrderEvents>(
  'Flights order events transport',
  {
    providedIn: 'root',
    factory: () => openFlightOrderEvents,
  },
);

/** Observation for one order page. It owns no booking commands or business outcomes. */
@Injectable()
export class FlightOrderLiveUpdatesService {
  private readonly document = inject(DOCUMENT);
  private readonly destroy = inject(DestroyRef);
  private readonly transport = inject(ORDER_EVENTS_TRANSPORT);
  private context: OrderLiveContext | null = null;
  private generation = 0;
  private connectionGeneration = 0;
  private connection: AbortController | null = null;
  private read: AbortController | null = null;
  private paused = false;
  private queued = false;
  private streamDisabled = false;
  private token: string | null | undefined;
  private retries = 0;
  private catchupUntil = 0;
  private exhausted = false;
  private freshnessEpoch = 0;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private reconnect: ReturnType<typeof setTimeout> | null = null;
  private watchdog: ReturnType<typeof setTimeout> | null = null;
  private catchupDeadline: ReturnType<typeof setTimeout> | null = null;
  private lastDisconnectReadAt = Number.NEGATIVE_INFINITY;
  private readonly results = signal<Partial<Record<OrderLiveReaderName, OrderLiveReadResult>>>({});
  readonly targetVersion = signal(0);
  readonly synchronizing = signal(false);
  readonly message = signal('');
  readonly active = signal(false);

  constructor() {
    const visibility = () => this.updatePause();
    this.document.addEventListener('visibilitychange', visibility);
    this.document.defaultView?.addEventListener('online', visibility);
    this.document.defaultView?.addEventListener('offline', visibility);
    this.destroy.onDestroy(() => {
      this.stop();
      this.document.removeEventListener('visibilitychange', visibility);
      this.document.defaultView?.removeEventListener('online', visibility);
      this.document.defaultView?.removeEventListener('offline', visibility);
    });
  }

  start(context: OrderLiveContext): void {
    if (this.context?.key === context.key) return;
    this.stop();
    this.context = context;
    this.paused = this.unavailable();
    this.active.set(!this.paused);
    context.onPause(this.paused);
    if (!this.paused) {
      this.requestRefresh('open');
      if (context.canStream()) void this.connect();
    }
  }

  stop(): void {
    this.generation++;
    this.context?.onPause(true);
    this.context = null;
    this.active.set(false);
    this.read?.abort();
    this.read = null;
    this.queued = false;
    this.closeConnection();
    this.clearTimers();
    this.results.set({});
    this.targetVersion.set(0);
    this.synchronizing.set(false);
    this.message.set('');
    this.token = undefined;
    this.retries = 0;
    this.streamDisabled = false;
    this.catchupUntil = 0;
    this.exhausted = false;
    this.lastDisconnectReadAt = Number.NEGATIVE_INFINITY;
  }

  prerequisitesReady(...required: OrderLiveReaderName[]): boolean {
    if (!this.active()) return false;
    return required.every((name) => {
      const value = this.results()[name];
      return (
        value?.outcome === 'notApplicable' ||
        (value?.outcome === 'ok' &&
          (this.targetVersion() === 0 || (value.version !== null && value.version >= this.targetVersion())))
      );
    });
  }

  requestRefresh(reason: string, minVersion?: number): void {
    if (!this.current()) return;
    if (['open', 'connect', 'resume', 'disconnect'].includes(reason)) {
      this.freshnessEpoch++;
      this.results.update((prior) =>
        Object.fromEntries(
          Object.entries(prior).map(([name, value]) => [
            name,
            value.outcome === 'notApplicable' ? value : { ...value, outcome: 'stopped' },
          ]),
        ),
      );
    }
    if (minVersion !== undefined) {
      if (minVersion <= this.targetVersion()) return;
      this.raiseTarget(minVersion);
      if (this.exhausted) return;
    }
    if (this.read) {
      this.queued = true;
      return;
    }
    this.clearReadTimer();
    void this.readCycle();
  }

  private current(generation = this.generation): boolean {
    return generation === this.generation && this.context !== null && this.context.isCurrent() && !this.paused;
  }
  private unavailable(): boolean {
    return this.document.hidden || this.document.defaultView?.navigator.onLine === false;
  }
  private updatePause(): void {
    if (!this.context) return;
    const paused = this.unavailable();
    if (paused === this.paused) return;
    this.paused = paused;
    this.active.set(!paused);
    this.context.onPause(paused);
    if (paused) {
      this.read?.abort();
      this.read = null;
      this.queued = false;
      this.closeConnection();
      this.clearTimers();
    } else {
      // Resume starts a new observation epoch after an arbitrarily long pause.
      this.catchupUntil = 0;
      this.exhausted = false;
      this.requestRefresh('resume');
      if (this.context.canStream()) void this.connect();
    }
  }
  private raiseTarget(version: number): void {
    if (version <= this.targetVersion()) return;
    this.targetVersion.set(version);
    if (this.catchupUntil === 0) this.catchupUntil = Date.now() + 30_000;
  }

  private async readCycle(): Promise<void> {
    const context = this.context;
    if (!context || !this.current() || this.read) return;
    const generation = this.generation;
    const freshnessEpoch = this.freshnessEpoch;
    const initialOrderApplicability = context.orderApplicable?.();
    const controller = new AbortController();
    this.read = controller;
    const active = () => this.current(generation) && this.read === controller && !controller.signal.aborted;
    const tokenDeadline = setTimeout(() => {
      if (active()) {
        this.message.set('Не удалось обновить данные. Показан последний полученный результат.');
        this.results.update((prior) =>
          Object.fromEntries(names.map((name) => [name, { outcome: 'error', version: prior[name]?.version ?? null }])),
        );
        controller.abort();
      }
    }, 15_000);
    try {
      const token = await this.waitToken(context.getToken(), controller.signal);
      clearTimeout(tokenDeadline);
      if (!active()) return;
      if (this.connection && token !== this.token) {
        this.closeConnection();
        void this.connect(token);
      }
      await Promise.allSettled(
        names.map(async (name) => {
          let result: OrderLiveReadResult;
          try {
            result = await context.readers[name](token, controller.signal);
          } catch {
            result = { outcome: 'error', version: null };
          }
          if (!active()) return;
          if (result.outcome === 'auth') {
            this.deny(result.status);
            return;
          }
          this.results.update((prior) => ({
            ...prior,
            [name]:
              freshnessEpoch === this.freshnessEpoch || result.outcome === 'notApplicable'
                ? result
                : { ...result, outcome: 'stopped' },
          }));
          if (result.outcome === 'ok' && result.version !== null) this.raiseTarget(result.version);
        }),
      );
      if (!active()) return;
      const orderApplicable = context.orderApplicable?.();
      if (orderApplicable === false)
        this.results.update((prior) => ({ ...prior, order: { outcome: 'notApplicable', version: null } }));
      else if (orderApplicable === true && this.results().order?.outcome === 'notApplicable') {
        this.results.update((prior) => ({ ...prior, order: { outcome: 'stopped', version: null } }));
        if (initialOrderApplicability === false) this.queued = true;
      }
      const values = this.results();
      if (names.every((name) => values[name]?.outcome === 'missing') && this.targetVersion() === 0) {
        this.stop();
        return;
      }
      const failed = names.some((name) => values[name]?.outcome === 'error' || values[name]?.outcome === 'missing');
      const behind = names.some((name) => {
        const value = values[name];
        return (
          value?.outcome !== 'notApplicable' &&
          (value?.outcome === 'stale' ||
            value?.outcome === 'stopped' ||
            (this.targetVersion() > 0 &&
              (value?.version === null || value?.version === undefined || value.version < this.targetVersion())))
        );
      });
      this.synchronizing.set(behind);
      if (behind && this.catchupUntil === 0 && !this.exhausted) this.catchupUntil = Date.now() + 30_000;
      this.message.set(failed ? 'Не удалось обновить данные. Показан последний полученный результат.' : '');
      if (!behind && !failed) {
        this.catchupUntil = 0;
        this.exhausted = false;
      }
      if (behind && this.catchupUntil > 0 && Date.now() >= this.catchupUntil) this.exhausted = true;
      if (!this.connection && !this.reconnect && !this.streamDisabled && context.canStream()) void this.connect(token);
    } catch {
      if (active()) this.deny();
    } finally {
      clearTimeout(tokenDeadline);
      if (this.read === controller) {
        this.read = null;
        this.clearCatchupDeadline();
        if (this.current(generation)) {
          if (this.queued && !this.exhausted) {
            this.queued = false;
            this.requestRefresh('follow-up');
          } else {
            this.queued = false;
            this.scheduleRead();
          }
        }
      }
    }
  }

  private scheduleRead(): void {
    this.clearReadTimer();
    if (!this.current()) return;
    const quick = this.synchronizing() && !this.exhausted && this.catchupUntil > Date.now();
    this.timer = setTimeout(
      () => {
        this.timer = null;
        this.requestRefresh('fallback');
      },
      quick ? Math.min(2000, this.catchupUntil - Date.now()) : 30_000,
    );
    if (quick) {
      this.catchupDeadline = setTimeout(() => {
        this.exhausted = true;
        this.read?.abort();
        this.read = null;
        this.queued = false;
        this.scheduleRead();
      }, this.catchupUntil - Date.now());
    }
  }

  private async connect(knownToken?: string | null): Promise<void> {
    const context = this.context;
    if (!context || !this.current() || this.connection || this.streamDisabled || !context.canStream()) return;
    const generation = this.generation;
    const connectionGeneration = ++this.connectionGeneration;
    const controller = new AbortController();
    this.connection = controller;
    const active = () => this.current(generation) && connectionGeneration === this.connectionGeneration;
    let failure: FlightOrderEventsError | null = null;
    let openedAt: number | null = null;
    this.armWatchdog(controller, 15_000);
    try {
      const token = knownToken === undefined ? await this.waitToken(context.getToken(), controller.signal) : knownToken;
      if (!active() || controller.signal.aborted) return;
      this.token = token;
      await (context.transport ?? this.transport)({
        aggregateId: context.aggregateId,
        accessToken: token,
        signal: controller.signal,
        allowFictionalAnonymous: context.allowFictionalAnonymous,
        onOpen: () => {
          if (active() && !controller.signal.aborted) {
            openedAt = Date.now();
            this.armWatchdog(controller, 45_000);
            this.requestRefresh('connect');
          }
        },
        onActivity: () => {
          if (active()) this.armWatchdog(controller, 45_000);
        },
        onHint: (hint) => {
          if (active() && !controller.signal.aborted && hint.orderId === context.aggregateId.toLowerCase())
            this.requestRefresh('signal', hint.streamVersion);
        },
      });
    } catch (error) {
      if (!active()) return;
      failure = error instanceof FlightOrderEventsError ? error : new FlightOrderEventsError('auth');
      if (failure.code === 'auth') {
        this.deny(failure.status ?? undefined);
        return;
      }
      if (failure.code === 'disabled') this.streamDisabled = true;
    } finally {
      if (active()) {
        this.connection = null;
        if (this.watchdog) clearTimeout(this.watchdog);
        this.watchdog = null;
        if (openedAt !== null && Date.now() - openedAt >= 30_000) this.retries = 0;
        if (Date.now() - this.lastDisconnectReadAt >= 30_000) {
          this.lastDisconnectReadAt = Date.now();
          this.requestRefresh('disconnect');
        }
        if (!this.streamDisabled) {
          const minimum =
            failure?.status && failure.status >= 400 && failure.status < 500
              ? 30_000
              : backoff[Math.min(this.retries++, backoff.length - 1)];
          const delay = Math.max(minimum, failure?.retryAfterMs ?? 0);
          this.reconnect = setTimeout(() => {
            this.reconnect = null;
            void this.connect();
          }, delay);
        }
      }
    }
  }
  private deny(status?: number): void {
    const context = this.context;
    this.stop();
    context?.onAuthorizationFailure(status);
  }
  private waitToken(promise: Promise<string | null>, signal: AbortSignal): Promise<string | null> {
    return new Promise((resolve, reject) => {
      const abort = () => reject(new FlightOrderEventsError('network'));
      signal.addEventListener('abort', abort, { once: true });
      promise.then(
        (value) => {
          signal.removeEventListener('abort', abort);
          resolve(value);
        },
        (error) => {
          signal.removeEventListener('abort', abort);
          reject(error);
        },
      );
      if (signal.aborted) abort();
    });
  }
  private armWatchdog(controller: AbortController, ms: number): void {
    if (this.watchdog) clearTimeout(this.watchdog);
    this.watchdog = setTimeout(() => controller.abort(), ms);
  }
  private closeConnection(): void {
    this.connectionGeneration++;
    this.connection?.abort();
    this.connection = null;
    if (this.watchdog) clearTimeout(this.watchdog);
    this.watchdog = null;
  }
  private clearCatchupDeadline(): void {
    if (this.catchupDeadline) clearTimeout(this.catchupDeadline);
    this.catchupDeadline = null;
  }
  private clearReadTimer(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }
  private clearTimers(): void {
    this.clearReadTimer();
    this.clearCatchupDeadline();
    if (this.reconnect) clearTimeout(this.reconnect);
    this.reconnect = null;
  }
}
