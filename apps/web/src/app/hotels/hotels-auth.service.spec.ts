import { DOCUMENT } from '@angular/common';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type Keycloak from 'keycloak-js';
import { TRAVEL_KEYCLOAK_FACTORY } from '../auth/keycloak-session';
import { HotelsAuthService } from './hotels-auth.service';

function session() {
  const client = {
    authenticated: true,
    token: 'fictional',
    tokenParsed: { sub: '11111111-1111-4111-8111-111111111111', aud: 'travel-web', scope: 'openid hotels:book' },
    onAuthLogout: undefined as (() => void) | undefined,
    init: vi.fn().mockResolvedValue(true),
    updateToken: vi.fn().mockResolvedValue(true),
    clearToken() {
      if (this.token) {
        this.token = '';
        this.authenticated = false;
        this.onAuthLogout?.();
      }
    },
  };
  return client;
}

describe('Hotels authentication invalidation', () => {
  it('invalidates the session immediately on external logout and ignores an old client callback', async () => {
    const first = session();
    const second = session();
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
                href: 'http://localhost:4200/hotels',
                search: '',
                pathname: '/hotels',
              },
            },
          },
        },
        {
          provide: TRAVEL_KEYCLOAK_FACTORY,
          useValue: vi
            .fn()
            .mockResolvedValueOnce(first as unknown as Keycloak)
            .mockResolvedValueOnce(second as unknown as Keycloak),
        },
      ],
    });
    const auth = TestBed.inject(HotelsAuthService);
    await auth.beginLogin();
    const epoch = auth.identityEpoch();
    first.clearToken();
    expect(auth.status().kind).not.toBe('authenticated');
    expect(auth.identityEpoch()).toBeGreaterThan(epoch);
    await auth.beginLogin();
    const replacementEpoch = auth.identityEpoch();
    first.onAuthLogout?.();
    expect(auth.status().kind).toBe('authenticated');
    expect(auth.identityEpoch()).toBe(replacementEpoch);
  });
});
