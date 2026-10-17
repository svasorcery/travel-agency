import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import type Keycloak from 'keycloak-js';
import { TRAVEL_KEYCLOAK_FACTORY, type TravelAuthStatus, validateTravelSubject } from '../auth/keycloak-session';
import { resolveTravelWebAuthConfig } from '../auth/travel-web-auth-config';

@Injectable({ providedIn: 'root' })
export class HotelsAuthService {
  readonly isDemo = false;
  readonly status = signal<TravelAuthStatus>({ kind: 'anonymous' });
  readonly identityEpoch = signal(0);
  private readonly document = inject(DOCUMENT);
  private readonly platform = inject(PLATFORM_ID);
  private readonly factory = inject(TRAVEL_KEYCLOAK_FACTORY);
  private client: Keycloak | null = null;
  private login: Promise<boolean> | null = null;
  private owner: string | null = null;
  private claims(client: Keycloak): string {
    const claims = client.tokenParsed;
    if (!claims || typeof claims['scope'] !== 'string' || !claims['scope'].split(' ').includes('hotels:book'))
      throw new Error('Hotels permission missing.');
    return validateTravelSubject(claims, () => new Error('Hotels identity invalid.'));
  }
  async initializeFromCallback(): Promise<boolean> {
    if (!isPlatformBrowser(this.platform)) return false;
    const browser = this.document.defaultView;
    if (!browser || !new URLSearchParams(browser.location.search).has('code')) return false;
    try {
      return await this.authenticate(false);
    } finally {
      const url = new URL(browser.location.href);
      for (const key of ['code', 'state', 'session_state', 'iss', 'error', 'error_description'])
        url.searchParams.delete(key);
      browser.history.replaceState(null, '', url.pathname + url.search);
    }
  }
  beginLogin(): Promise<boolean> {
    this.login ??= this.authenticate(true).finally(() => {
      this.login = null;
    });
    return this.login;
  }
  private async authenticate(redirect: boolean): Promise<boolean> {
    if (!isPlatformBrowser(this.platform)) return false;
    const browser = this.document.defaultView;
    const config = browser && resolveTravelWebAuthConfig(browser.location, browser.__TRAVEL_FLIGHTS_AUTH__ ?? null);
    if (!config) {
      this.status.set({ kind: 'unavailable', message: 'Вход для этого окружения не настроен.' });
      return false;
    }
    const epoch = this.identityEpoch();
    try {
      const client = await this.factory(config);
      if (epoch !== this.identityEpoch()) {
        client.clearToken();
        return false;
      }
      this.client = client;
      client.onAuthLogout = () => {
        if (this.client === client) this.deny();
      };
      const authenticated = await client.init({ pkceMethod: 'S256', responseMode: 'query' });
      if (epoch !== this.identityEpoch() || client !== this.client) return false;
      if (authenticated) {
        this.accept(this.claims(client));
        return true;
      }
      if (!redirect) throw new Error('Callback invalid.');
      this.status.set({ kind: 'redirecting' });
      const path = browser!.location.pathname;
      await client.login({
        scope: 'openid hotels:book',
        redirectUri:
          browser!.location.origin +
          (/^\/hotels(?:\/reservations(?:\/[0-9a-f-]{36})?)?$/.test(path) ? path : '/hotels'),
      });
      return false;
    } catch {
      if (epoch === this.identityEpoch()) this.deny();
      return false;
    }
  }
  async accessToken(): Promise<string | null> {
    const client = this.client,
      epoch = this.identityEpoch();
    try {
      if (!client?.authenticated) throw new Error('Login required.');
      await client.updateToken(30);
      if (epoch !== this.identityEpoch() || client !== this.client || !client.token)
        throw new Error('Session changed.');
      this.accept(this.claims(client));
      return client.token;
    } catch {
      if (epoch === this.identityEpoch()) this.deny();
      throw new Error('Hotels session unavailable.');
    }
  }
  deny(): void {
    const client = this.client;
    this.client = null;
    this.identityEpoch.update((v) => v + 1);
    this.owner = null;
    this.status.set({ kind: 'error', message: 'Доступ утрачен. Войдите снова.' });
    client?.clearToken();
  }
  private accept(owner: string): void {
    if (this.owner !== null && this.owner !== owner) this.identityEpoch.update((v) => v + 1);
    this.owner = owner;
    this.status.set({ kind: 'authenticated', userId: owner });
  }
  async logout(): Promise<void> {
    const client = this.client,
      browser = this.document.defaultView;
    this.deny();
    this.status.set({ kind: 'anonymous' });
    if (client && browser) {
      try {
        await client.logout({ redirectUri: browser.location.origin + '/hotels' });
      } catch {
        /* RAM already cleared. */
      }
    }
  }
}
