import { DOCUMENT } from '@angular/common';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TRAVEL_KEYCLOAK_FACTORY } from '../auth/keycloak-session';
import { safeTripReturn, TripsAuthService } from './trips-auth.service';

function setup() {
  let complete: () => void = () => {
    throw new Error('Deferred test gate not initialized.');
  };
  const refreshed = new Promise<boolean>((resolve) => {
    complete = () => resolve(true);
  });
  const client = {
    authenticated: true,
    token: 'fictional-token',
    tokenParsed: { sub: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', aud: 'travel-web' },
    init: vi.fn().mockResolvedValue(true),
    updateToken: vi.fn().mockResolvedValue(true),
    clearToken: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
    login: vi.fn(),
  };
  const factory = vi.fn().mockResolvedValue(client);
  TestBed.configureTestingModule({
    providers: [
      { provide: TRAVEL_KEYCLOAK_FACTORY, useValue: factory },
      {
        provide: DOCUMENT,
        useValue: {
          defaultView: {
            location: {
              hostname: 'localhost',
              port: '4200',
              origin: 'http://localhost:4200',
              search: '',
              href: 'http://localhost:4200/trips',
            },
            history: { replaceState: vi.fn() },
          },
        },
      },
    ],
  });
  return { auth: TestBed.inject(TripsAuthService), client, factory, refreshed, complete };
}
describe('Trips ephemeral owner authentication', () => {
  it('accepts only same module return paths', () => {
    expect(safeTripReturn('/trips')).toBe('/trips');
    expect(safeTripReturn('/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')).toContain('aaaaaaaa');
    for (const path of [
      'https://example.test',
      '//example.test',
      '/support',
      '/trips?private=text',
      '/trips/00000000-0000-0000-0000-000000000000',
      '/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\n',
    ])
      expect(safeTripReturn(path)).toBe('/trips');
  });
  it('invalidates owner epochs when refreshed claims change and clears tokens on logout', async () => {
    const { auth, client } = setup();
    expect(await auth.beginLogin('/trips')).toBe(true);
    expect(await auth.accessToken()).toBe('fictional-token');
    const epoch = auth.identityEpoch();
    client.tokenParsed.sub = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
    await auth.accessToken();
    expect(auth.identityEpoch()).toBe(epoch + 1);
    await auth.logout();
    expect(auth.status().kind).toBe('anonymous');
    expect(client.clearToken).toHaveBeenCalledOnce();
  });
  it('rejects refresh replies that finish after logout', async () => {
    const { auth, client, refreshed, complete } = setup();
    await auth.beginLogin();
    client.updateToken.mockReturnValue(refreshed);
    const token = auth.accessToken();
    const rejected = expect(token).rejects.toThrow();
    await auth.logout();
    complete();
    await rejected;
    expect(auth.status().kind).toBe('anonymous');
  });
  it('does not create Keycloak or read a token during SSR', async () => {
    const factory = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: PLATFORM_ID, useValue: 'server' },
        { provide: TRAVEL_KEYCLOAK_FACTORY, useValue: factory },
      ],
    });
    const auth = TestBed.inject(TripsAuthService);
    expect(await auth.beginLogin()).toBe(false);
    expect(factory).not.toHaveBeenCalled();
  });
});
