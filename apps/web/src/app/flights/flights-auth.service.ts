import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, InjectionToken, inject, PLATFORM_ID, signal } from '@angular/core';
import type Keycloak from 'keycloak-js';
import type { KeycloakConfig } from 'keycloak-js';
import type { FlightsAuthStatus } from './flights-auth.types';
import { type FlightsAuthConfig, resolveFlightsAuthConfig } from './flights-auth-config';
import { validateFlightsAccessTokenClaims } from './flights-auth-contract';

export type { FlightsAuthStatus } from './flights-auth.types';

export type FlightsKeycloakFactory = (config: KeycloakConfig) => Promise<Keycloak>;

export const FLIGHTS_KEYCLOAK_FACTORY = new InjectionToken<FlightsKeycloakFactory>('FLIGHTS_KEYCLOAK_FACTORY', {
  providedIn: 'root',
  factory: () => async (config) => {
    const { default: KeycloakClient } = await import('keycloak-js');
    return new KeycloakClient(config);
  },
});

@Injectable({ providedIn: 'root' })
export class FlightsAuthService {
  readonly isDemo = false;
  readonly status = signal<FlightsAuthStatus>({ kind: 'anonymous' });

  isTestEnvironment(): boolean {
    const environment = this.getConfig()?.environment;
    return environment === 'development' || environment === 'staging';
  }

  private readonly platformId = inject(PLATFORM_ID);
  private readonly document = inject(DOCUMENT);
  private readonly createKeycloak = inject(FLIGHTS_KEYCLOAK_FACTORY);
  private client: Keycloak | null = null;
  private initTask: Promise<boolean> | null = null;

  hasOidcCallback(): boolean {
    const location = this.document.defaultView?.location;
    if (!isPlatformBrowser(this.platformId) || location === undefined) return false;
    const params = new URLSearchParams(location.search);
    return params.has('code') || params.has('error');
  }

  async initializeFromCallback(): Promise<boolean> {
    if (!this.hasOidcCallback()) return false;
    const config = this.getConfig();
    if (config === null) {
      this.status.set({ kind: 'unavailable', message: 'Вход для этого окружения не настроен.' });
      this.clearOidcCallbackUrl();
      return false;
    }

    try {
      const client = await this.getClient(config);
      const authenticated = await this.initialize(client);
      if (!authenticated || client.tokenParsed === undefined) throw new Error('callback rejected');
      const userId = validateFlightsAccessTokenClaims(client.tokenParsed);
      this.status.set({ kind: 'authenticated', userId });
      this.clearOidcCallbackUrl();
      return true;
    } catch {
      this.status.set({ kind: 'error', message: 'Не удалось подтвердить вход. Попробуйте войти ещё раз.' });
      this.clearOidcCallbackUrl();
      return false;
    }
  }

  async beginLogin(returnPath?: string): Promise<boolean> {
    if (!isPlatformBrowser(this.platformId)) return false;
    const config = this.getConfig();
    if (config === null) {
      this.status.set({ kind: 'unavailable', message: 'Вход для этого окружения не настроен.' });
      return false;
    }

    try {
      const client = await this.getClient(config);
      const authenticated = await this.initialize(client);
      if (authenticated && client.tokenParsed !== undefined) {
        const userId = validateFlightsAccessTokenClaims(client.tokenParsed);
        this.status.set({ kind: 'authenticated', userId });
        return true;
      }

      this.status.set({ kind: 'redirecting' });
      const orderPath =
        returnPath === '/flights/orders' ||
        /^\/flights\/orders\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(returnPath ?? '')
          ? returnPath
          : null;
      const redirectUri = orderPath === null ? config.redirectUri : `${new URL(config.redirectUri).origin}${orderPath}`;
      await client.login({ scope: 'openid flights:book', redirectUri });
      return false;
    } catch {
      this.status.set({ kind: 'error', message: 'Не удалось начать вход. Попробуйте ещё раз.' });
      return false;
    }
  }

  async accessToken(): Promise<string | null> {
    const client = this.client;
    if (client === null || !client.authenticated) throw new Error('Authentication is required.');
    try {
      await client.updateToken(30);
      const claims = client.tokenParsed;
      const token = client.token;
      if (claims === undefined || token === undefined) throw new Error('Access token unavailable.');
      const userId = validateFlightsAccessTokenClaims(claims);
      this.status.set({ kind: 'authenticated', userId });
      return token;
    } catch {
      this.status.set({ kind: 'error', message: 'Сеанс входа истёк. Войдите снова.' });
      throw new Error('Authentication expired.');
    }
  }

  async logout(): Promise<void> {
    const config = this.getConfig();
    if (this.client === null || config === null) {
      this.client = null;
      this.initTask = null;
      this.status.set({ kind: 'anonymous' });
      return;
    }
    try {
      await this.client.logout({ redirectUri: config.redirectUri });
    } catch {
      this.status.set({ kind: 'error', message: 'Не удалось завершить выход. Закройте эту вкладку.' });
    }
  }

  private initialize(client: Keycloak): Promise<boolean> {
    if (this.initTask === null) {
      this.initTask = client.init({ pkceMethod: 'S256', responseMode: 'query' }).catch((error: unknown) => {
        this.initTask = null;
        if (this.client === client) this.client = null;
        throw error;
      });
    }
    return this.initTask;
  }

  private getClient(config: FlightsAuthConfig): Promise<Keycloak> {
    if (this.client === null) {
      return this.createKeycloak({
        url: config.url,
        realm: config.realm,
        clientId: config.clientId,
      }).then((client) => {
        this.client = client;
        return client;
      });
    }
    return Promise.resolve(this.client);
  }

  private getConfig(): FlightsAuthConfig | null {
    if (!isPlatformBrowser(this.platformId)) return null;
    const browser = this.document.defaultView;
    if (browser === null) return null;
    return resolveFlightsAuthConfig(browser.location, browser.__TRAVEL_FLIGHTS_AUTH__ ?? null);
  }

  private clearOidcCallbackUrl(): void {
    const browser = this.document.defaultView;
    if (browser === null) return;
    const url = new URL(browser.location.href);
    for (const key of ['code', 'state', 'session_state', 'iss', 'error', 'error_description', 'error_uri']) {
      url.searchParams.delete(key);
    }
    browser.history.replaceState(null, '', `${url.pathname}${url.search}${url.hash}`);
  }
}
