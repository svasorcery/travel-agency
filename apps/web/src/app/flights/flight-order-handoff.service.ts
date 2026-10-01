import { effect, Injectable, inject } from '@angular/core';
import { FlightsAuthService } from './flights-auth.service';

type RecentHold = { aggregateId: string; ownerUserId: string; recordedAt: number };
const recentHoldLifetimeMs = 30_000;

@Injectable({ providedIn: 'root' })
export class FlightOrderHandoffService {
  private readonly auth = inject(FlightsAuthService);
  private outcome: { aggregateId: string; ownerUserId: string; status: 'Held' | 'Confirmed' } | null = null;
  private recentHold: RecentHold | null = null;

  constructor() {
    effect(() => this.currentOwner());
  }

  rememberHeld(aggregateId: string, ownerUserId: string): void {
    const owner = this.currentOwner();
    if (owner === null || owner !== ownerUserId.toLowerCase()) return;
    this.outcome = { aggregateId, ownerUserId: owner, status: 'Held' };
    this.recentHold = { aggregateId, ownerUserId: owner, recordedAt: Date.now() };
  }

  rememberConfirmed(aggregateId: string, ownerUserId: string): void {
    const owner = this.currentOwner();
    if (owner === null || owner !== ownerUserId.toLowerCase()) return;
    this.outcome = { aggregateId, ownerUserId: owner, status: 'Confirmed' };
  }

  recentHeld(ownerUserId: string): string | null {
    return this.liveRecentHold(ownerUserId)?.aggregateId ?? null;
  }

  recentHeldDeadline(ownerUserId: string): number | null {
    const hold = this.liveRecentHold(ownerUserId);
    return hold === null ? null : hold.recordedAt + recentHoldLifetimeMs;
  }

  clear(): void {
    this.outcome = null;
    this.recentHold = null;
  }

  takeOutcome(aggregateId: string, ownerUserId: string | null): 'Held' | 'Confirmed' | null {
    const owner = this.currentOwner();
    const matches = owner !== null && ownerUserId?.toLowerCase() === owner && this.outcome?.aggregateId === aggregateId;
    const status = matches ? (this.outcome?.status ?? null) : null;
    this.outcome = null;
    return status;
  }

  private currentOwner(): string | null {
    const status = this.auth.status();
    const owner = status.kind === 'authenticated' ? status.userId.toLowerCase() : null;
    if (
      owner === null ||
      (this.outcome !== null && this.outcome.ownerUserId !== owner) ||
      (this.recentHold !== null && this.recentHold.ownerUserId !== owner)
    )
      this.clear();
    return owner;
  }

  private liveRecentHold(ownerUserId: string): RecentHold | null {
    const owner = this.currentOwner();
    if (this.recentHold !== null && Date.now() >= this.recentHold.recordedAt + recentHoldLifetimeMs) {
      this.recentHold = null;
    }
    return owner !== null && owner === ownerUserId.toLowerCase() ? this.recentHold : null;
  }
}
