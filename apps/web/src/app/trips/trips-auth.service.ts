import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import type Keycloak from 'keycloak-js';
import { TRAVEL_KEYCLOAK_FACTORY, type TravelAuthStatus, validateTravelSubject } from '../auth/keycloak-session';
import { resolveTravelWebAuthConfig } from '../auth/travel-web-auth-config';

export function safeTripReturn(path?: string): string {
  if (!path || path !== path.trim()) return '/trips';
  if (path === '/trips') return path;
  return /^\/trips\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(path) &&
    !path.endsWith('00000000-0000-0000-0000-000000000000')
    ? path
    : '/trips';
}
@Injectable({ providedIn: 'root' })
export class TripsAuthService {
  readonly isDemo = false;
  readonly status = signal<TravelAuthStatus>({ kind: 'anonymous' });
  readonly identityEpoch = signal(0);
  private readonly document = inject(DOCUMENT);
  private readonly platform = inject(PLATFORM_ID);
  private readonly factory = inject(TRAVEL_KEYCLOAK_FACTORY);
  private client: Keycloak | null = null;
  private creation: Promise<Keycloak> | null = null;
  private initialization: Promise<boolean> | null = null;
  private loginTask: Promise<boolean> | null = null;
  private owner: string | null = null;
  hasOidcCallback(): boolean {
    if (!isPlatformBrowser(this.platform)) return false;
    const q = new URLSearchParams(this.document.defaultView?.location.search);
    return q.has('code') || q.has('error');
  }
  async initializeFromCallback(): Promise<boolean> {
    if (!this.hasOidcCallback()) return false;
    try {
      return await this.authenticate(false);
    } finally {
      const b = this.document.defaultView;
      if (b) {
        const url = new URL(b.location.href);
        for (const k of ['code', 'state', 'session_state', 'iss', 'error', 'error_description', 'error_uri'])
          url.searchParams.delete(k);
        b.history.replaceState(null, '', url.pathname + url.search + url.hash);
      }
    }
  }
  beginLogin(path?: string): Promise<boolean> {
    if (this.loginTask) return this.loginTask;
    const task = this.authenticate(true, path).finally(() => {
      if (this.loginTask === task) this.loginTask = null;
    });
    this.loginTask = task;
    return task;
  }
  async accessToken(): Promise<string | null> {
    const c = this.client,
      e = this.identityEpoch();
    if (!c?.authenticated) throw new Error('Trips authentication required.');
    try {
      await c.updateToken(30);
      if (c !== this.client || e !== this.identityEpoch()) throw new Error('Session changed.');
      if (!c.token || !c.tokenParsed) throw new Error('Missing token.');
      this.accept(c);
      return c.token;
    } catch {
      this.deny(e);
      throw new Error('Trips authentication unavailable.');
    }
  }
  deny(epoch: number): void {
    if (epoch !== this.identityEpoch()) return;
    this.clear();
    this.status.set({ kind: 'error', message: 'Сеанс входа истёк. Войдите снова.' });
  }
  async logout(): Promise<void> {
    const c = this.client,
      config = this.config();
    this.clear();
    if (c && config)
      try {
        await c.logout({ redirectUri: config.redirectUri });
      } catch {
        /* State is already erased. */
      }
  }
  private clear(): void {
    this.identityEpoch.update((e) => e + 1);
    this.client?.clearToken();
    this.client = null;
    this.creation = null;
    this.initialization = null;
    this.loginTask = null;
    this.owner = null;
    this.status.set({ kind: 'anonymous' });
  }
  private accept(client: Keycloak): void {
    const owner = validateTravelSubject(client.tokenParsed!, () => new Error('Trips claims rejected.')).toLowerCase();
    if (this.owner !== null && owner !== this.owner) this.identityEpoch.update((e) => e + 1);
    this.owner = owner;
    this.status.set({ kind: 'authenticated', userId: owner });
  }
  private config() {
    if (!isPlatformBrowser(this.platform)) return null;
    const b = this.document.defaultView;
    if (!b) return null;
    const c = resolveTravelWebAuthConfig(b.location, b.__TRAVEL_FLIGHTS_AUTH__ ?? null);
    if (!c) return null;
    try {
      const issuer = new URL(c.url);
      if (issuer.username || issuer.password || issuer.search || issuer.hash) return null;
    } catch {
      return null;
    }
    return { ...c, redirectUri: b.location.origin + '/trips' };
  }
  private async authenticate(login: boolean, path?: string): Promise<boolean> {
    const config = this.config();
    if (!config) {
      this.status.set({ kind: 'unavailable', message: 'Вход для этого окружения не настроен.' });
      return false;
    }
    const epoch = this.identityEpoch();
    try {
      if (!this.client) {
        this.creation ??= this.factory(config).then((c) => {
          if (epoch !== this.identityEpoch()) {
            c.clearToken();
            throw new Error('Session changed.');
          }
          this.client = c;
          return c;
        });
        await this.creation;
      }
      const client = this.client!;
      this.initialization ??= client.init({ pkceMethod: 'S256', responseMode: 'query' });
      const ok = await this.initialization;
      if (epoch !== this.identityEpoch() || client !== this.client) return false;
      if (ok && client.tokenParsed) {
        this.accept(client);
        return true;
      }
      if (!login) throw new Error('Callback rejected.');
      this.status.set({ kind: 'redirecting' });
      await client.login({ scope: 'openid', redirectUri: new URL(config.redirectUri).origin + safeTripReturn(path) });
      return false;
    } catch {
      if (epoch === this.identityEpoch()) {
        this.clear();
        this.status.set({ kind: 'error', message: 'Не удалось подтвердить вход. Попробуйте снова.' });
      }
      return false;
    }
  }
}
