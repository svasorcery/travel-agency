import { describe, expect, it } from 'vitest';
import { resolveTravelWebAuthConfig } from './travel-web-auth-config';

describe('Trips owned local OIDC origins', () => {
  for (const [webPort, oidcPort] of [
    ['4205', '8185'],
    ['4206', '8186'],
  ]) {
    it(`binds only the exact HTTP loopback origin on ${webPort} to ${oidcPort}`, () => {
      const origin = `http://127.0.0.1:${webPort}`;
      expect(resolveTravelWebAuthConfig({ hostname: '127.0.0.1', origin, port: webPort }, null)).toEqual({
        url: `http://127.0.0.1:${oidcPort}`,
        realm: 'travel',
        clientId: 'travel-web',
        redirectUri: `${origin}/flights`,
        environment: 'development',
      });
    });
  }

  for (const origin of [
    'http://localhost:4205',
    'http://localhost:4206',
    'http://127.0.0.1:4200',
    'http://127.0.0.1:4201',
    'http://127.0.0.1:4203',
    'http://127.0.0.1:4207',
    'https://127.0.0.1:4205',
    'https://127.0.0.1:4206',
    'http://[::1]:4205',
    'http://127.0.0.2:4205',
    'http://example.test:4205',
  ]) {
    it(`does not infer local authentication for ${origin}`, () => {
      expect(resolveTravelWebAuthConfig(new URL(origin), null)).toBeNull();
    });
  }

  it('refuses a mismatched origin even with loopback hostname and allowed port fields', () => {
    expect(
      resolveTravelWebAuthConfig({ hostname: '127.0.0.1', port: '4205', origin: 'http://example.test:4205' }, null),
    ).toBeNull();
  });

  it('retains the existing development origin and production configuration', () => {
    expect(resolveTravelWebAuthConfig(new URL('http://localhost:4200'), null)?.url).toBe('http://localhost:8180');
    expect(
      resolveTravelWebAuthConfig(new URL('https://travel.example.test'), {
        url: 'https://auth.example.test',
        realm: 'travel',
        clientId: 'travel-web',
        environment: 'production',
      }),
    ).toEqual({
      url: 'https://auth.example.test',
      realm: 'travel',
      clientId: 'travel-web',
      redirectUri: 'https://travel.example.test/flights',
      environment: 'production',
    });
    expect(resolveTravelWebAuthConfig(null, null)).toBeNull();
  });
});
