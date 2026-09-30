import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class FlightOrderHandoffService {
  private outcome: { aggregateId: string; ownerUserId: string; status: 'Held' | 'Confirmed' } | null = null;

  rememberHeld(aggregateId: string, ownerUserId: string): void {
    this.outcome = { aggregateId, ownerUserId, status: 'Held' };
  }

  rememberConfirmed(aggregateId: string, ownerUserId: string): void {
    this.outcome = { aggregateId, ownerUserId, status: 'Confirmed' };
  }

  takeOutcome(aggregateId: string, ownerUserId: string | null): 'Held' | 'Confirmed' | null {
    const matches =
      ownerUserId !== null && this.outcome?.aggregateId === aggregateId && this.outcome.ownerUserId === ownerUserId;
    const status = matches ? (this.outcome?.status ?? null) : null;
    this.outcome = null;
    return status;
  }
}
