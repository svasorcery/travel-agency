export interface TravelWebAuthRuntimeConfig {
  url: string;
  realm: string;
  clientId: string;
  environment: 'staging' | 'production';
}

export type TravelWebAuthEnvironment = 'development' | 'staging' | 'production';

export interface TravelWebAuthLocation {
  hostname: string;
  origin: string;
  port: string;
}

export interface TravelWebAuthConfig extends Omit<TravelWebAuthRuntimeConfig, 'environment'> {
  redirectUri: string;
  environment: TravelWebAuthEnvironment;
}

declare global {
  interface Window {
    __TRAVEL_FLIGHTS_AUTH__?: TravelWebAuthRuntimeConfig;
  }
}

export function resolveTravelWebAuthConfig(
  location: TravelWebAuthLocation | null,
  runtimeConfig: TravelWebAuthRuntimeConfig | null,
): TravelWebAuthConfig | null {
  if (location === null || location.port === '4201') return null;

  if (location.hostname === 'localhost' && location.port === '4200') {
    return {
      url: 'http://localhost:8180',
      realm: 'travel',
      clientId: 'travel-web',
      redirectUri: `${location.origin}/flights`,
      environment: 'development',
    };
  }

  if (
    location.hostname === '127.0.0.1' &&
    (location.port === '4205' || location.port === '4206') &&
    location.origin === `http://127.0.0.1:${location.port}`
  ) {
    return {
      url: `http://127.0.0.1:${location.port === '4205' ? '8185' : '8186'}`,
      realm: 'travel',
      clientId: 'travel-web',
      redirectUri: `${location.origin}/flights`,
      environment: 'development',
    };
  }

  if (
    runtimeConfig === null ||
    runtimeConfig.realm !== 'travel' ||
    runtimeConfig.clientId !== 'travel-web' ||
    (runtimeConfig.environment !== 'staging' && runtimeConfig.environment !== 'production') ||
    typeof runtimeConfig.url !== 'string'
  ) {
    return null;
  }

  try {
    const appOrigin = new URL(location.origin);
    const authUrl = new URL(runtimeConfig.url);
    if (appOrigin.protocol !== 'https:' || authUrl.protocol !== 'https:') return null;
    return {
      url: `${authUrl.origin}${authUrl.pathname.replace(/\/$/, '')}`,
      realm: runtimeConfig.realm,
      clientId: runtimeConfig.clientId,
      redirectUri: `${location.origin}/flights`,
      environment: runtimeConfig.environment,
    };
  } catch {
    return null;
  }
}
