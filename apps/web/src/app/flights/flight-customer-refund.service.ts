import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, Injectable, inject, signal } from '@angular/core';
import { type CustomerRefundStatus, FlightsCustomerRefundApiService } from '@travel/api-client';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
import type { OrderLiveReadResult } from './flight-order-live-updates.service';
import { FlightsAuthService } from './flights-auth.service';
import { isDemoSource } from './flights-source-mode';

const pollingStates = new Set(['Assessing', 'Ready', 'DispatchClaimed', 'Pending', 'Unknown']);
const pollingWindowMs = 190_000;

@Injectable({ providedIn: 'root' })
export class FlightCustomerRefundService {
  private readonly api = inject(FlightsCustomerRefundApiService);
  private readonly auth = inject(FlightsAuthService);
  private readonly document = inject(DOCUMENT);
  private readonly destroy = inject(DestroyRef);
  private readonly state = signal<CustomerRefundStatus | null>(null);
  private readonly viewIdentity = signal<string | null>(null);
  private readonly stopReads = new Subject<void>();
  private bookingId: string | null = null;
  private generation = 0;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private openedAt = 0;
  private readEpoch = 0;
  private paused = false;
  private runningRead: Promise<OrderLiveReadResult> | null = null;
  private coordinate: (() => void) | null = null;

  readonly snapshot = computed(() =>
    this.viewIdentity() !== null && this.currentIdentity() === this.viewIdentity() ? this.state() : null,
  );
  readonly message = signal('');
  readonly busy = signal(false);
  readonly pollingEnded = signal(false);
  readonly blocksNewOperations = computed(() => this.snapshot()?.supplierReport?.state === 'Unverified');

  constructor() {
    effect(() => {
      if (this.viewIdentity() !== null && this.currentIdentity() !== this.viewIdentity()) this.close();
    });
    const visibility = () => {
      if (this.coordinate) return;
      this.clearPoll();
      if (!this.document.hidden && this.isCurrent(this.generation) && Date.now() - this.openedAt < pollingWindowMs)
        this.read();
    };
    this.document.addEventListener('visibilitychange', visibility);
    this.destroy.onDestroy(() => {
      this.close();
      this.stopReads.complete();
      this.document.removeEventListener('visibilitychange', visibility);
    });
  }

  open(aggregateId: string, owner: string): void {
    this.close();
    const auth = this.auth.status();
    const identity = this.currentIdentity();
    if (!identity || auth.kind !== 'authenticated' || auth.userId.toLowerCase() !== owner.toLowerCase()) return;
    this.bookingId = aggregateId.toLowerCase();
    this.viewIdentity.set(identity);
    this.openedAt = Date.now();
    this.read();
  }

  close(): void {
    this.generation++;
    this.invalidateReads();
    this.clearPoll();
    this.bookingId = null;
    this.viewIdentity.set(null);
    this.state.set(null);
    this.busy.set(false);
    this.message.set('');
    this.pollingEnded.set(false);
  }

  read(): void {
    if (this.paused) return;
    if (this.coordinate) this.coordinate();
    else void this.load();
  }

  setReadCoordinator(request: (() => void) | null): void {
    this.coordinate = request;
  }
  setObservationPaused(paused: boolean): void {
    this.paused = paused;
    if (paused) {
      this.invalidateReads();
      this.clearPoll();
      this.busy.set(false);
    }
  }
  async readForLiveUpdates(token: string | null, signal: AbortSignal): Promise<OrderLiveReadResult> {
    if (this.paused || signal.aborted) return { outcome: 'stopped', version: null };
    const abort = () => this.invalidateReads();
    signal.addEventListener('abort', abort, { once: true });
    try {
      return await this.load(token);
    } finally {
      signal.removeEventListener('abort', abort);
    }
  }
  private invalidateReads(): void {
    this.readEpoch++;
    this.stopReads.next();
    this.runningRead = null;
    this.busy.set(false);
  }
  private load(token?: string | null): Promise<OrderLiveReadResult> {
    if (this.runningRead) return this.runningRead;
    const task = this.loadCore(token);
    this.runningRead = task;
    void task.finally(() => {
      if (this.runningRead === task) this.runningRead = null;
    });
    return task;
  }

  private currentIdentity(): string | null {
    const auth = this.auth.status();
    return auth.kind === 'authenticated' ? auth.userId.toLowerCase() + ':' + this.auth.identityEpoch() : null;
  }

  private isCurrent(generation: number): boolean {
    return (
      generation === this.generation &&
      this.bookingId !== null &&
      this.viewIdentity() !== null &&
      this.currentIdentity() === this.viewIdentity()
    );
  }

  private async loadCore(providedToken?: string | null): Promise<OrderLiveReadResult> {
    const generation = this.generation,
      bookingId = this.bookingId;
    const epoch = this.readEpoch;
    const current = () => this.isCurrent(generation) && epoch === this.readEpoch && !this.paused;
    if (!bookingId || !current()) return { outcome: 'stopped', version: null };
    this.clearPoll();
    this.busy.set(true);
    this.message.set('');
    try {
      const token = providedToken === undefined ? await this.auth.accessToken() : providedToken;
      if (!current()) return { outcome: 'stopped', version: null };
      if (token === null && !isDemoSource()) throw new Error('SessionUnavailable');
      const next = await firstValueFrom(this.api.status(bookingId, token).pipe(takeUntil(this.stopReads)));
      if (!current()) return { outcome: 'stopped', version: null };
      const previous = this.state();
      if (
        previous !== null &&
        (next.bookingVersion < previous.bookingVersion ||
          (previous.caseRevision !== null && (next.caseRevision === null || next.caseRevision < previous.caseRevision)))
      )
        return { outcome: 'stale', version: previous.bookingVersion };
      if (
        previous === null ||
        (next.bookingVersion > previous.bookingVersion &&
          (previous.caseRevision === null ||
            (next.caseRevision !== null && next.caseRevision >= previous.caseRevision)))
      ) {
        this.state.set(next);
      }
      return { outcome: 'ok', version: this.state()?.bookingVersion ?? null };
    } catch (error) {
      if (!current()) return { outcome: 'stopped', version: null };
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.close();
        this.message.set('Доступ к заказу недоступен. Войдите снова и обновите состояние.');
        return { outcome: 'auth', status: error.status, version: null };
      } else {
        this.message.set('Сейчас не удалось прочитать состояние возврата. Последний полученный результат сохранён.');
      }
      return {
        outcome: error instanceof HttpErrorResponse && error.status === 404 ? 'missing' : 'error',
        version: this.state()?.bookingVersion ?? null,
      };
    } finally {
      if (current()) {
        this.busy.set(false);
        this.schedule();
      }
    }
  }

  private schedule(): void {
    this.clearPoll();
    if (this.paused) return;
    const value = this.snapshot();
    if (!value || !pollingStates.has(value.state)) return;
    const remaining = pollingWindowMs - (Date.now() - this.openedAt);
    if (remaining <= 0) {
      this.pollingEnded.set(true);
      return;
    }
    if (this.document.hidden) return;
    const generation = this.generation;
    this.pollTimer = setTimeout(
      () => {
        this.pollTimer = null;
        if (!this.isCurrent(generation)) return;
        if (Date.now() - this.openedAt >= pollingWindowMs) this.pollingEnded.set(true);
        else this.read();
      },
      Math.min(Date.now() - this.openedAt < 30_000 ? 2_000 : 10_000, remaining),
    );
  }

  private clearPoll(): void {
    if (this.pollTimer !== null) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }
}
