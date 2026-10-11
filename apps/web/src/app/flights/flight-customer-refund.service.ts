import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, Injectable, inject, signal } from '@angular/core';
import { type CustomerRefundStatus, FlightsCustomerRefundApiService } from '@travel/api-client';
import { firstValueFrom, Subject, takeUntil } from 'rxjs';
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
  private readingGeneration = -1;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private openedAt = 0;

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
    this.stopReads.next();
    this.clearPoll();
    this.bookingId = null;
    this.viewIdentity.set(null);
    this.state.set(null);
    this.busy.set(false);
    this.message.set('');
    this.pollingEnded.set(false);
  }

  read(): void {
    void this.load();
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

  private async load(): Promise<void> {
    const generation = this.generation,
      bookingId = this.bookingId;
    if (!bookingId || !this.isCurrent(generation) || this.readingGeneration === generation) return;
    this.clearPoll();
    this.readingGeneration = generation;
    this.busy.set(true);
    this.message.set('');
    try {
      const token = await this.auth.accessToken();
      if (!this.isCurrent(generation)) return;
      if (token === null && !isDemoSource()) throw new Error('SessionUnavailable');
      const next = await firstValueFrom(this.api.status(bookingId, token).pipe(takeUntil(this.stopReads)));
      if (!this.isCurrent(generation)) return;
      const previous = this.state();
      if (
        previous === null ||
        (next.bookingVersion > previous.bookingVersion &&
          (previous.caseRevision === null ||
            (next.caseRevision !== null && next.caseRevision >= previous.caseRevision)))
      ) {
        this.state.set(next);
      }
    } catch (error) {
      if (!this.isCurrent(generation)) return;
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.close();
        this.message.set('Доступ к заказу недоступен. Войдите снова и обновите состояние.');
      } else {
        this.message.set('Сейчас не удалось прочитать состояние возврата. Последний полученный результат сохранён.');
      }
    } finally {
      if (this.isCurrent(generation)) {
        this.readingGeneration = -1;
        this.busy.set(false);
        this.schedule();
      }
    }
  }

  private schedule(): void {
    this.clearPoll();
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
