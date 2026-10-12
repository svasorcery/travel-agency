import { Injectable, signal } from '@angular/core';
import type { SupportMode } from '@travel/api-client';
import type { FlightsAuthStatus } from '../flights/flights-auth.types';
@Injectable({ providedIn: 'root' })
export class SupportAuthService {
  readonly isDemo = true;
  readonly status = signal<FlightsAuthStatus>({ kind: 'anonymous' });
  readonly mode = signal<SupportMode>('customer');
  readonly identityEpoch = signal(0);
  selectMode(mode: SupportMode): void {
    if (this.mode() !== mode) {
      this.identityEpoch.update((v) => v + 1);
      this.mode.set(mode);
      this.status.set({ kind: 'anonymous' });
    }
  }
  hasOidcCallback(): boolean {
    return false;
  }
  async initializeFromCallback(): Promise<boolean> {
    return false;
  }
  async beginLogin(_path?: string): Promise<boolean> {
    void _path;
    this.status.set({
      kind: 'authenticated',
      userId:
        this.mode() === 'operator' ? '22222222-2222-4222-8222-222222222222' : '11111111-1111-4111-8111-111111111111',
    });
    return true;
  }
  async accessToken(): Promise<string | null> {
    if (this.status().kind !== 'authenticated') throw new Error('Login required.');
    return this.mode() === 'operator' ? 'demo-support-operator' : 'demo-support-customer';
  }
  deny(epoch: number): void {
    if (epoch !== this.identityEpoch()) return;
    this.identityEpoch.update((v) => v + 1);
    this.status.set({ kind: 'error', message: 'Доступ к обращениям утрачен. Войдите снова.' });
  }
  async logout(): Promise<void> {
    this.identityEpoch.update((v) => v + 1);
    this.status.set({ kind: 'anonymous' });
  }
}
