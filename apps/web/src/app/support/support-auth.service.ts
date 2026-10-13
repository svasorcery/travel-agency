import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import type { SupportMode } from '@travel/api-client';
import type Keycloak from 'keycloak-js';
import { TRAVEL_KEYCLOAK_FACTORY } from '../auth/keycloak-session';
import type { FlightsAuthStatus } from '../flights/flights-auth.types';
import {
  resolveSupportAuthConfig,
  type SupportAuthConfig,
  safeSupportReturn,
  validateSupportClaims,
} from './support-auth-contract';

@Injectable({ providedIn: 'root' })
export class SupportAuthService {
  readonly isDemo = false;
  readonly status = signal<FlightsAuthStatus>({ kind: 'anonymous' });
  readonly mode = signal<SupportMode>('customer');
  readonly identityEpoch = signal(0);
  private readonly document = inject(DOCUMENT);
  private readonly platform = inject(PLATFORM_ID);
  private readonly factory = inject(TRAVEL_KEYCLOAK_FACTORY);
  private client: Keycloak | null = null;
  private creation: Promise<Keycloak> | null = null;
  private loginTask: Promise<boolean> | null = null;
  private initialization: Promise<boolean> | null = null;
  private identity: string | null = null;
  selectMode(mode: SupportMode): void {
    if (mode === this.mode()) return;
    this.invalidate();
    this.mode.set(mode);
  }
  hasOidcCallback(): boolean {
    const browser = this.document.defaultView;
    if (!isPlatformBrowser(this.platform) || !browser) return false;
    const query = new URLSearchParams(browser.location.search);
    return query.has('code') || query.has('error');
  }
  async initializeFromCallback(): Promise<boolean> {
    if (!this.hasOidcCallback()) return false;
    try {
      return await this.authenticate(false);
    } finally {
      this.clearCallback();
    }
  }
  beginLogin(path?: string): Promise<boolean> {
    if (this.loginTask !== null) return this.loginTask;
    const task = this.authenticate(true, path).finally(() => {
      if (this.loginTask === task) this.loginTask = null;
    });
    this.loginTask = task;
    return task;
  }
  async accessToken(): Promise<string | null> {
    const client = this.client,
      epoch = this.identityEpoch(),
      mode = this.mode();
    if (client === null || !client.authenticated) throw new Error('Support login required.');
    try {
      await client.updateToken(30);
      if (client !== this.client || epoch !== this.identityEpoch() || mode !== this.mode())
        throw new Error('Session changed.');
      if (!client.token || !client.tokenParsed) throw new Error('Token missing.');
      this.accept(validateSupportClaims(client.tokenParsed, mode));
      return client.token;
    } catch {
      if (client === this.client && epoch === this.identityEpoch()) this.deny(epoch);
      throw new Error('Support session unavailable.');
    }
  }
  deny(epoch: number): void {
    if (epoch !== this.identityEpoch()) return;
    this.invalidate();
    this.status.set({ kind: 'error', message: 'Доступ к обращениям утрачен. Войдите снова.' });
  }
  async logout(): Promise<void> {
    const client = this.client,
      config = this.config();
    this.invalidate();
    if (client && config) {
      try {
        await client.logout({ redirectUri: config.redirectUri });
      } catch {
        /* Private state was already erased. */
      }
    }
  }
  private invalidate(): void {
    this.identityEpoch.update((value) => value + 1);
    this.client?.clearToken();
    this.client = null;
    this.creation = null;
    this.loginTask = null;
    this.initialization = null;
    this.identity = null;
    this.status.set({ kind: 'anonymous' });
  }
  private accept(owner: string): void {
    if (this.identity !== null && this.identity !== owner) this.identityEpoch.update((value) => value + 1);
    this.identity = owner;
    this.status.set({ kind: 'authenticated', userId: owner });
  }
  private async authenticate(login: boolean, path?: string): Promise<boolean> {
    if (!isPlatformBrowser(this.platform)) return false;
    const config = this.config();
    if (!config) {
      this.status.set({ kind: 'unavailable', message: 'Вход для этого окружения не настроен.' });
      return false;
    }
    const epoch = this.identityEpoch(),
      mode = this.mode();
    try {
      let client = this.client;
      if (!client) {
        if (this.creation === null) {
          const task = this.factory({ url: config.url, realm: config.realm, clientId: config.clientId })
            .then((value) => {
              if (epoch !== this.identityEpoch() || mode !== this.mode()) {
                value.clearToken();
                throw new Error('Session changed.');
              }
              this.client = value;
              return value;
            })
            .finally(() => {
              if (this.creation === task) this.creation = null;
            });
          this.creation = task;
        }
        client = await this.creation;
        if (epoch !== this.identityEpoch() || mode !== this.mode()) {
          client.clearToken();
          return false;
        }
        this.client = client;
      }
      this.initialization ??= client.init({ pkceMethod: 'S256', responseMode: 'query' });
      const authenticated = await this.initialization;
      if (epoch !== this.identityEpoch() || mode !== this.mode() || client !== this.client) return false;
      if (authenticated && client.tokenParsed) {
        this.accept(validateSupportClaims(client.tokenParsed, mode));
        return true;
      }
      if (!login) throw new Error('Callback rejected.');
      this.status.set({ kind: 'redirecting' });
      await client.login({
        scope: mode === 'operator' ? 'openid support:operator' : 'openid',
        redirectUri: new URL(config.redirectUri).origin + safeSupportReturn(path, mode),
      });
      return false;
    } catch {
      if (epoch === this.identityEpoch() && mode === this.mode()) {
        this.invalidate();
        this.status.set({ kind: 'error', message: 'Не удалось подтвердить вход. Попробуйте ещё раз.' });
      }
      return false;
    }
  }
  private config(): SupportAuthConfig | null {
    if (!isPlatformBrowser(this.platform)) return null;
    const browser = this.document.defaultView;
    return browser === null
      ? null
      : resolveSupportAuthConfig(
          browser.location,
          this.mode() === 'operator'
            ? (browser.__TRAVEL_SUPPORT_OPERATOR_AUTH__ ?? null)
            : (browser.__TRAVEL_FLIGHTS_AUTH__ ?? null),
          this.mode(),
        );
  }
  private clearCallback(): void {
    const browser = this.document.defaultView;
    if (!browser) return;
    const url = new URL(browser.location.href);
    for (const key of ['code', 'state', 'session_state', 'iss', 'error', 'error_description', 'error_uri'])
      url.searchParams.delete(key);
    browser.history.replaceState(null, '', url.pathname + url.search + url.hash);
  }
}
