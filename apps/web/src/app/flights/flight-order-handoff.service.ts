import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class FlightOrderHandoffService {
  private confirmed: { aggregateId: string; ownerUserId: string } | null = null;

  rememberConfirmed(aggregateId: string, ownerUserId: string): void {
    this.confirmed = { aggregateId, ownerUserId };
  }

  takeConfirmed(aggregateId: string, ownerUserId: string | null): boolean {
    const matches =
      ownerUserId !== null && this.confirmed?.aggregateId === aggregateId && this.confirmed.ownerUserId === ownerUserId;
    this.confirmed = null;
    return matches;
  }
}
