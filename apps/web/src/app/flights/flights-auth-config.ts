export interface FlightsAuthRuntimeConfig {
  url: string;
  realm: string;
  clientId: string;
  environment: 'staging' | 'production';
}

export type FlightsAuthEnvironment = 'development' | 'staging' | 'production';

export interface FlightsAuthLocation {
  hostname: string;
  origin: string;
  port: string;
}

export interface FlightsAuthConfig extends Omit<FlightsAuthRuntimeConfig, 'environment'> {
  redirectUri: string;
  environment: FlightsAuthEnvironment;
}

declare global {
  interface Window {
    __TRAVEL_FLIGHTS_AUTH__?: FlightsAuthRuntimeConfig;
  }
}

export function resolveFlightsAuthConfig(
  location: FlightsAuthLocation | null,
  runtimeConfig: FlightsAuthRuntimeConfig | null,
): FlightsAuthConfig | null {
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
