import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type Keycloak from 'keycloak-js';
import { TRAVEL_KEYCLOAK_FACTORY } from '../auth/keycloak-session';
import { FlightOrderOperationsService } from '../flights/flight-order-operations.service';
import { FlightsAuthService, type FlightsAuthStatus } from '../flights/flights-auth.service';
import { SupportAuthService } from './support-auth.service';

const owner = '11111111-1111-4111-8111-111111111111';
const claims = { sub: owner, aud: 'travel-web', scope: 'openid support:operator', azp: 'travel-support-operator' };
function client(authenticated = true) {
  return {
    authenticated,
    token: 'fictional-token',
    tokenParsed: authenticated ? { ...claims } : undefined,
    init: vi.fn().mockResolvedValue(authenticated),
    login: vi.fn().mockResolvedValue(undefined),
    updateToken: vi.fn().mockResolvedValue(true),
    clearToken: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
  };
}
function setup(value = client(), factory = vi.fn().mockResolvedValue(value)) {
  TestBed.configureTestingModule({
    providers: [
      { provide: PLATFORM_ID, useValue: 'browser' },
      {
        provide: DOCUMENT,
        useValue: {
          defaultView: {
            location: {
              hostname: 'localhost',
              port: '4200',
              origin: 'http://localhost:4200',
              href: 'http://localhost:4200/support/operator',
              search: '',
              pathname: '/support/operator',
            },
          },
        },
      },
      { provide: TRAVEL_KEYCLOAK_FACTORY, useValue: factory },
    ],
  });
  return { auth: TestBed.inject(SupportAuthService), client: value, factory };
}
describe('SupportAuthService separate session contexts', () => {
  it('does not create a customer or operator client on anonymous navigation', async () => {
    const { auth, factory } = setup();
    auth.selectMode('operator');
    await auth.initializeFromCallback();
    expect(factory).not.toHaveBeenCalled();
    expect(auth.status().kind).toBe('anonymous');
  });
  it('requests operator authorization code flow with PKCE and a safe dedicated callback', async () => {
    const { auth, client: adapter, factory } = setup(client(false));
    auth.selectMode('operator');
    await auth.beginLogin('//evil.test');
    expect(factory).toHaveBeenCalledExactlyOnceWith({
      url: 'http://localhost:8180',
      realm: 'travel',
      clientId: 'travel-support-operator',
    });
    expect(adapter.init).toHaveBeenCalledWith({ pkceMethod: 'S256', responseMode: 'query' });
    expect(adapter.login).toHaveBeenCalledWith({
      scope: 'openid support:operator',
      redirectUri: 'http://localhost:4200/support/operator',
    });
  });
  it('coalesces repeated login clicks into one client creation and one redirect', async () => {
    const { auth, client: adapter, factory } = setup(client(false));
    auth.selectMode('operator');
    await Promise.all([auth.beginLogin(), auth.beginLogin()]);
    expect(factory).toHaveBeenCalledTimes(1);
    expect(adapter.init).toHaveBeenCalledTimes(1);
    expect(adapter.login).toHaveBeenCalledTimes(1);
  });
  it.each([
    { ...claims, scope: 'openid' },
    { ...claims, azp: 'travel-web' },
  ])('invalidates effective permission loss on refresh with the same GUID', async (invalid) => {
    const { auth, client: adapter } = setup();
    auth.selectMode('operator');
    await auth.beginLogin();
    const epoch = auth.identityEpoch();
    adapter.tokenParsed = invalid;
    await expect(auth.accessToken()).rejects.toThrow();
    expect(auth.identityEpoch()).toBeGreaterThan(epoch);
    expect(auth.status().kind).toBe('error');
    expect(adapter.clearToken).toHaveBeenCalled();
  });
  it('does not accept a factory or token refresh completion from an old mode', async () => {
    let resolve!: (value: Keycloak) => void;
    const adapter = client();
    const { auth } = setup(
      adapter,
      vi.fn().mockImplementation(() => new Promise<Keycloak>((done) => (resolve = done))),
    );
    auth.selectMode('operator');
    const login = auth.beginLogin();
    await Promise.resolve();
    auth.selectMode('customer');
    resolve(adapter as unknown as Keycloak);
    await expect(login).resolves.toBe(false);
    expect(adapter.init).not.toHaveBeenCalled();
    expect(auth.status().kind).toBe('anonymous');
  });
  it('ignores stale denial but current denial erases the same identity', async () => {
    const { auth } = setup();
    auth.selectMode('operator');
    await auth.beginLogin();
    const old = auth.identityEpoch();
    auth.selectMode('customer');
    await auth.beginLogin();
    auth.deny(old);
    expect(auth.status().kind).toBe('authenticated');
    auth.deny(auth.identityEpoch());
    expect(auth.status().kind).toBe('error');
  });
  it('rejects an old mode refresh without denying the replacement session', async () => {
    const { auth, client: adapter } = setup();
    auth.selectMode('operator');
    await auth.beginLogin();
    let complete!: () => void;
    adapter.updateToken.mockImplementation(() => new Promise<boolean>((resolve) => (complete = () => resolve(true))));
    const refresh = auth.accessToken();
    const failure = expect(refresh).rejects.toThrow();
    auth.selectMode('customer');
    await auth.beginLogin();
    complete();
    await failure;
    expect(auth.status().kind).toBe('authenticated');
  });
  it('preserves a Flights unknown confirmation when the same human switches Support client mode', async () => {
    const adapter = client(),
      factory = vi.fn().mockResolvedValue(adapter);
    const flights = {
      status: signal<FlightsAuthStatus>({ kind: 'authenticated', userId: owner }),
      identityEpoch: signal(0),
      accessToken: vi.fn().mockResolvedValue('customer-booking-token'),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: FlightsAuthService, useValue: flights },
        { provide: PLATFORM_ID, useValue: 'browser' },
        { provide: TRAVEL_KEYCLOAK_FACTORY, useValue: factory },
        {
          provide: DOCUMENT,
          useValue: {
            addEventListener: vi.fn(),
            removeEventListener: vi.fn(),
            defaultView: {
              location: { hostname: 'localhost', port: '4200', origin: 'http://localhost:4200', search: '' },
            },
          },
        },
      ],
    });
    const operations = TestBed.inject(FlightOrderOperationsService),
      auth = TestBed.inject(SupportAuthService),
      http = TestBed.inject(HttpTestingController);
    expect(operations.startConfirm(owner, owner)).toBe(true);
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne('/api/flights/orders/confirm').error(new ProgressEvent('offline'));
    await Promise.resolve();
    await Promise.resolve();
    expect(operations.operation(owner, owner)?.state).toBe('unknown');
    auth.selectMode('operator');
    await auth.beginLogin();
    auth.selectMode('customer');
    expect(operations.operation(owner, owner)?.state).toBe('unknown');
    expect(operations.blocksBooking(owner)).toBe(true);
    expect(flights.identityEpoch()).toBe(0);
    http.verify();
  });
});
