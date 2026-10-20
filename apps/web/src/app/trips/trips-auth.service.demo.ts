import { Injectable, signal } from '@angular/core';
import type { TravelAuthStatus } from '../auth/keycloak-session';
// Explicit trips-demo build replacement. Never selected by normal or production builds.
@Injectable({ providedIn: 'root' })
export class TripsAuthService {
  readonly isDemo = true;
  readonly identityEpoch = signal(0);
  readonly status = signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' });
  private alternate = false;
  hasOidcCallback(): boolean {
    return false;
  }
  async initializeFromCallback(): Promise<boolean> {
    return false;
  }
  async beginLogin(_path?: string): Promise<boolean> {
    this.status.set({
      kind: 'authenticated',
      userId: this.alternate ? 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' : 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
    });
    return true;
  }
  async accessToken(): Promise<string | null> {
    if (this.status().kind !== 'authenticated') throw new Error('Fictional Trips session required.');
    return this.alternate ? 'fictional-trips-owner-b' : 'fictional-trips-owner-a';
  }
  deny(epoch: number): void {
    if (epoch !== this.identityEpoch()) return;
    this.identityEpoch.update((e) => e + 1);
    this.status.set({ kind: 'anonymous' });
  }
  async logout(): Promise<void> {
    this.identityEpoch.update((e) => e + 1);
    this.status.set({ kind: 'anonymous' });
    this.alternate = !this.alternate;
  }
}
