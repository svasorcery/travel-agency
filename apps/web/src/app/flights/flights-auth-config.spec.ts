import { resolveFlightsAuthConfig } from './flights-auth-config';

describe('Flights auth runtime config', () => {
  it('allows the checked-in local client only on port 4200', () => {
    expect(
      resolveFlightsAuthConfig({ hostname: 'localhost', origin: 'http://localhost:4200', port: '4200' }, null),
    ).toEqual({
      url: 'http://localhost:8180',
      realm: 'travel',
      clientId: 'travel-web',
      redirectUri: 'http://localhost:4200/flights',
      environment: 'development',
    });
  });

  it('never enables real Keycloak on the isolated demo port', () => {
    expect(
      resolveFlightsAuthConfig(
        { hostname: 'localhost', origin: 'http://localhost:4201', port: '4201' },
        { url: 'https://auth.example.test', realm: 'travel', clientId: 'travel-web', environment: 'production' },
      ),
    ).toBeNull();
  });

  it('requires explicit runtime configuration outside the local development origin', () => {
    expect(
      resolveFlightsAuthConfig({ hostname: 'travel.example', origin: 'https://travel.example', port: '' }, null),
    ).toBeNull();
    expect(
      resolveFlightsAuthConfig(
        { hostname: 'travel.example', origin: 'https://travel.example', port: '' },
        { url: 'https://auth.example.test', realm: 'travel', clientId: 'travel-web', environment: 'production' },
      )?.redirectUri,
    ).toBe('https://travel.example/flights');
    expect(
      resolveFlightsAuthConfig(
        { hostname: 'travel.example', origin: 'https://travel.example', port: '' },
        { url: 'http://localhost:8180', realm: 'travel', clientId: 'travel-web', environment: 'production' },
      ),
    ).toBeNull();
    expect(
      resolveFlightsAuthConfig(
        { hostname: 'travel.example', origin: 'http://travel.example', port: '' },
        { url: 'https://auth.example.test', realm: 'travel', clientId: 'travel-web', environment: 'production' },
      ),
    ).toBeNull();
    expect(
      resolveFlightsAuthConfig(
        { hostname: 'test.travel.example', origin: 'https://test.travel.example', port: '' },
        { url: 'https://auth.example.test', realm: 'travel', clientId: 'travel-web', environment: 'staging' },
      )?.environment,
    ).toBe('staging');
  });
});
