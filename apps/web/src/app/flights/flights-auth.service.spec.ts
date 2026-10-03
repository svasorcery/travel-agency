import { DOCUMENT } from '@angular/common';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type Keycloak from 'keycloak-js';
import type { KeycloakConfig } from 'keycloak-js';
import { FLIGHTS_KEYCLOAK_FACTORY, FlightsAuthService, type FlightsKeycloakFactory } from './flights-auth.service';

describe('FlightsAuthService', () => {
  it('does not keep a client from a factory that finishes after logout', async () => {
    let resolveFactory!: (client: Keycloak) => void;
    const client = {
      authenticated: true,
      token: 'fictional-token',
      tokenParsed: { sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59', aud: 'travel-web', scope: 'openid flights:book' },
      init: vi.fn().mockResolvedValue(true),
      updateToken: vi.fn().mockResolvedValue(true),
    } as unknown as Keycloak;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: DOCUMENT,
          useValue: {
            defaultView: {
              location: {
                hostname: 'localhost',
                port: '4200',
                origin: 'http://localhost:4200',
                href: 'http://localhost:4200/flights',
                search: '',
              },
            },
          },
        },
        { provide: PLATFORM_ID, useValue: 'browser' },
        {
          provide: FLIGHTS_KEYCLOAK_FACTORY,
          useValue: () => new Promise<Keycloak>((resolve) => (resolveFactory = resolve)),
        },
      ],
    });
    const auth = TestBed.inject(FlightsAuthService);
    const login = auth.beginLogin();
    await Promise.resolve();
    await auth.logout();
    resolveFactory(client);
    await expect(login).resolves.toBe(false);
    expect(client.init).not.toHaveBeenCalled();
    await expect(auth.accessToken()).rejects.toThrow();
    expect(auth.status().kind).toBe('anonymous');
  });
  it('invalidates logout synchronously and rejects a token refresh that finishes afterwards', async () => {
    let resolveRefresh!: () => void;
    let resolveLogout!: () => void;
    const client = {
      authenticated: true,
      token: 'fictional-token',
      tokenParsed: { sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59', aud: 'travel-web', scope: 'openid flights:book' },
      init: vi.fn().mockResolvedValue(true),
      updateToken: vi.fn().mockImplementation(() => new Promise<void>((resolve) => (resolveRefresh = resolve))),
      logout: vi.fn().mockImplementation(() => new Promise<void>((resolve) => (resolveLogout = resolve))),
      clearToken: vi.fn(),
    } as unknown as Keycloak;
    const browser = {
      location: {
        hostname: 'localhost',
        port: '4200',
        origin: 'http://localhost:4200',
        href: 'http://localhost:4200/flights',
        search: '',
      },
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: vi.fn().mockResolvedValue(client) },
      ],
    });
    const auth = TestBed.inject(FlightsAuthService);
    await auth.beginLogin();
    const token = auth.accessToken();
    const tokenFailure = expect(token).rejects.toThrow();
    const logout = auth.logout();
    expect(auth.status().kind).toBe('anonymous');
    resolveRefresh();
    await tokenFailure;
    expect(auth.status().kind).toBe('anonymous');
    resolveLogout();
    await logout;
  });
  it('does not initialize OIDC during an ordinary anonymous visit', async () => {
    const keycloakFactory = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: keycloakFactory }],
    });

    const auth = TestBed.inject(FlightsAuthService);

    await expect(auth.initializeFromCallback()).resolves.toBe(false);
    expect(auth.status()).toEqual({ kind: 'anonymous' });
    expect(keycloakFactory).not.toHaveBeenCalled();
  });

  it('uses code flow with S256 only after the user starts login', async () => {
    const client = {
      authenticated: false,
      tokenParsed: undefined,
      init: vi.fn().mockResolvedValue(false),
      login: vi.fn().mockResolvedValue(undefined),
    } as unknown as Keycloak;
    const factory = vi.fn<FlightsKeycloakFactory>().mockResolvedValue(client);
    const browser = {
      location: {
        hostname: 'localhost',
        port: '4200',
        origin: 'http://localhost:4200',
        href: 'http://localhost:4200/flights',
        search: '',
      },
      __TRAVEL_FLIGHTS_AUTH__: undefined,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: factory },
      ],
    });

    const auth = TestBed.inject(FlightsAuthService);
    expect(auth.hasOidcCallback()).toBe(false);
    expect(factory).not.toHaveBeenCalled();
    await auth.beginLogin();

    expect(factory).toHaveBeenCalledWith({
      url: 'http://localhost:8180',
      realm: 'travel',
      clientId: 'travel-web',
    } satisfies KeycloakConfig);
    expect(client.init).toHaveBeenCalledWith({ pkceMethod: 'S256', responseMode: 'query' });
    expect(client.login).toHaveBeenCalledWith({
      scope: 'openid flights:book',
      redirectUri: 'http://localhost:4200/flights',
    });
    expect(auth.status()).toEqual({ kind: 'redirecting' });
  });

  it('returns a direct order link to the same owner-scoped route after login', async () => {
    const client = {
      authenticated: false,
      tokenParsed: undefined,
      init: vi.fn().mockResolvedValue(false),
      login: vi.fn().mockResolvedValue(undefined),
    } as unknown as Keycloak;
    const browser = {
      location: { hostname: 'localhost', port: '4200', origin: 'http://localhost:4200' },
      __TRAVEL_FLIGHTS_AUTH__: undefined,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: vi.fn().mockResolvedValue(client) },
      ],
    });
    const auth = TestBed.inject(FlightsAuthService);
    await auth.beginLogin('/flights/orders/88b83d41-0194-2098-c1f6-fe7351d41cf2');
    expect(client.login).toHaveBeenCalledWith({
      scope: 'openid flights:book',
      redirectUri: 'http://localhost:4200/flights/orders/88b83d41-0194-2098-c1f6-fe7351d41cf2',
    });
    await auth.beginLogin('https://evil.example/steal');
    expect(client.login).toHaveBeenLastCalledWith({
      scope: 'openid flights:book',
      redirectUri: 'http://localhost:4200/flights',
    });
    await auth.beginLogin('/flights/orders');
    expect(client.login).toHaveBeenLastCalledWith({
      scope: 'openid flights:book',
      redirectUri: 'http://localhost:4200/flights/orders',
    });
    await auth.beginLogin('/flights/travelers');
    expect(client.login).toHaveBeenLastCalledWith({
      scope: 'openid flights:book',
      redirectUri: 'http://localhost:4200/flights/travelers',
    });
    for (const path of [
      '/flights/orders?owner=ignored',
      '/flights/orders#ignored',
      '/flights/orders/invalid',
      '/flights/travelers?owner=ignored',
      '/flights/travelers#ignored',
      '/flights/travelers/other',
      '//evil.example/flights/travelers',
    ]) {
      await auth.beginLogin(path);
      expect(client.login).toHaveBeenLastCalledWith({
        scope: 'openid flights:book',
        redirectUri: 'http://localhost:4200/flights',
      });
    }
  });

  it('can retry initialization after a transient Keycloak failure', async () => {
    const client = {
      authenticated: false,
      tokenParsed: undefined,
      init: vi.fn().mockRejectedValueOnce(new Error('temporarily offline')).mockResolvedValueOnce(false),
      login: vi.fn().mockResolvedValue(undefined),
    } as unknown as Keycloak;
    const browser = {
      location: { hostname: 'localhost', port: '4200', origin: 'http://localhost:4200' },
      __TRAVEL_FLIGHTS_AUTH__: undefined,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: vi.fn().mockResolvedValue(client) },
      ],
    });

    const auth = TestBed.inject(FlightsAuthService);
    await expect(auth.beginLogin()).resolves.toBe(false);
    expect(auth.status().kind).toBe('error');
    await expect(auth.beginLogin()).resolves.toBe(false);
    expect(client.init).toHaveBeenCalledTimes(2);
    expect(client.login).toHaveBeenCalledTimes(1);
    expect(auth.status().kind).toBe('redirecting');
  });

  it('marks an explicitly configured HTTPS staging origin as using the test wallet', () => {
    const browser = {
      location: { hostname: 'test.travel.example', port: '', origin: 'https://test.travel.example' },
      __TRAVEL_FLIGHTS_AUTH__: {
        url: 'https://auth.example.test',
        realm: 'travel',
        clientId: 'travel-web',
        environment: 'staging',
      },
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });

    expect(TestBed.inject(FlightsAuthService).isTestEnvironment()).toBe(true);
  });

  it('accepts callback claims only after the adapter exchanges and validates the code', async () => {
    const client = {
      authenticated: true,
      tokenParsed: {
        sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59',
        aud: ['account', 'travel-web'],
        scope: 'openid flights:book',
      },
      init: vi.fn().mockResolvedValue(true),
      login: vi.fn(),
    } as unknown as Keycloak;
    const history = { replaceState: vi.fn() };
    const browser = {
      location: {
        hostname: 'localhost',
        port: '4200',
        origin: 'http://localhost:4200',
        href: 'http://localhost:4200/flights?code=transient&state=transient',
        search: '?code=transient&state=transient',
      },
      history,
      __TRAVEL_FLIGHTS_AUTH__: undefined,
    };
    const factory = vi.fn<FlightsKeycloakFactory>().mockResolvedValue(client);
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: factory },
      ],
    });

    const auth = TestBed.inject(FlightsAuthService);

    await expect(auth.initializeFromCallback()).resolves.toBe(true);
    expect(client.init).toHaveBeenCalledWith({ pkceMethod: 'S256', responseMode: 'query' });
    expect(auth.status()).toEqual({ kind: 'authenticated', userId: client.tokenParsed?.sub });
    expect(history.replaceState).toHaveBeenCalled();
  });

  it('refuses a callback token with the wrong audience', async () => {
    const client = {
      authenticated: true,
      tokenParsed: {
        sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59',
        aud: ['account'],
        scope: 'openid flights:book',
      },
      init: vi.fn().mockResolvedValue(true),
    } as unknown as Keycloak;
    const factory = vi.fn<FlightsKeycloakFactory>().mockResolvedValue(client);
    const browser = {
      location: {
        hostname: 'localhost',
        port: '4200',
        origin: 'http://localhost:4200',
        href: 'http://localhost:4200/flights?code=transient&state=transient',
        search: '?code=transient&state=transient',
      },
      history: { replaceState: vi.fn() },
      __TRAVEL_FLIGHTS_AUTH__: undefined,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: DOCUMENT, useValue: { defaultView: browser } },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: FLIGHTS_KEYCLOAK_FACTORY, useValue: factory },
      ],
    });

    const auth = TestBed.inject(FlightsAuthService);
    await expect(auth.initializeFromCallback()).resolves.toBe(false);
    expect(auth.status().kind).toBe('error');
  });
});
