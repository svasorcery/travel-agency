import { Injectable, signal } from '@angular/core';
import type { TravelAuthStatus } from '../auth/keycloak-session';

// Build-only replacement for the isolated local real-Host fixture. Never selected by normal builds.
@Injectable({ providedIn: 'root' })
export class HotelsAuthService {
  readonly isDemo = true;
  readonly status = signal<TravelAuthStatus>({ kind: 'authenticated', userId: '785e39a4-b13f-4fd8-835b-1ab619cd0e14' });
  readonly identityEpoch = signal(0);
  async initializeFromCallback(): Promise<boolean> {
    return this.status().kind === 'authenticated';
  }
  async beginLogin(): Promise<boolean> {
    this.status.set({ kind: 'authenticated', userId: '785e39a4-b13f-4fd8-835b-1ab619cd0e14' });
    return true;
  }
  async accessToken(): Promise<string | null> {
    if (this.status().kind !== 'authenticated') throw new Error('Fictional session unavailable.');
    return 'fictional-hotels-owner';
  }
  deny(): void {
    this.identityEpoch.update((value) => value + 1);
    this.status.set({ kind: 'error', message: 'Доступ утрачен. Войдите снова.' });
  }
  async logout(): Promise<void> {
    this.deny();
    this.status.set({ kind: 'anonymous' });
  }
}
