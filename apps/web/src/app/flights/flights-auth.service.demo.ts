import { Injectable, signal } from '@angular/core';
import type { FlightsAuthStatus } from './flights-auth.types';

@Injectable({ providedIn: 'root' })
export class FlightsAuthService {
  readonly isDemo = true;
  readonly status = signal<FlightsAuthStatus>({ kind: 'anonymous' });
  readonly identityEpoch = signal(0);

  isTestEnvironment(): boolean {
    return true;
  }

  hasOidcCallback(): boolean {
    return false;
  }

  async initializeFromCallback(): Promise<boolean> {
    return false;
  }

  async beginLogin(_returnPath?: string): Promise<boolean> {
    void _returnPath;
    this.status.set({ kind: 'authenticated', userId: 'demo-only' });
    return true;
  }

  async accessToken(): Promise<string | null> {
    return null;
  }

  async logout(): Promise<void> {
    this.identityEpoch.update((value) => value + 1);
    this.status.set({ kind: 'anonymous' });
  }
}
