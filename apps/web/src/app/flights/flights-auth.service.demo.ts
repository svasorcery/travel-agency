import { Injectable, signal } from '@angular/core';
import type { FlightsAuthStatus } from './flights-auth.types';

@Injectable({ providedIn: 'root' })
export class FlightsAuthService {
  readonly isDemo = true;
  readonly status = signal<FlightsAuthStatus>({ kind: 'anonymous' });

  isTestEnvironment(): boolean {
    return true;
  }

  hasOidcCallback(): boolean {
    return false;
  }

  async initializeFromCallback(): Promise<boolean> {
    return false;
  }

  async beginLogin(): Promise<boolean> {
    this.status.set({ kind: 'authenticated', userId: 'demo-only' });
    return true;
  }

  async accessToken(): Promise<string | null> {
    return null;
  }

  async logout(): Promise<void> {
    this.status.set({ kind: 'anonymous' });
  }
}
