import type { SupportMode } from '@travel/api-client';
import { type TravelTokenClaims, validateTravelSubject } from '../auth/keycloak-session';
import {
  resolveTravelWebAuthConfig,
  type TravelWebAuthLocation,
  type TravelWebAuthRuntimeConfig,
} from '../auth/travel-web-auth-config';

export interface SupportAuthRuntimeConfig {
  url: string;
  realm: string;
  clientId: string;
  environment: 'staging' | 'production';
}
export interface SupportAuthConfig extends Omit<SupportAuthRuntimeConfig, 'environment'> {
  redirectUri: string;
  environment: 'development' | 'staging' | 'production';
}
declare global {
  interface Window {
    __TRAVEL_SUPPORT_OPERATOR_AUTH__?: SupportAuthRuntimeConfig;
  }
}
export function validateSupportClaims(claims: TravelTokenClaims, mode: SupportMode): string {
  const subject = validateTravelSubject(claims, () => new Error('Support authentication rejected.'));
  if (
    mode === 'operator' &&
    (claims.azp !== 'travel-support-operator' ||
      typeof claims.scope !== 'string' ||
      !claims.scope.split(/\s+/).includes('support:operator'))
  )
    throw new Error('Support operator permission required.');
  return subject.toLowerCase();
}
export function safeSupportReturn(path: string | undefined, mode: SupportMode): string {
  const root = mode === 'operator' ? '/support/operator' : '/support';
  const resource = '[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}';
  if (!path || path !== path.trim() || path.endsWith('00000000-0000-0000-0000-000000000000')) return root;
  if (
    path === root ||
    new RegExp('^' + root + '/cases/' + resource + '$', 'i').test(path) ||
    (mode === 'customer' && new RegExp('^/support\\?orderId=' + resource + '$', 'i').test(path))
  )
    return path!;
  return root;
}
export function resolveSupportAuthConfig(
  location: TravelWebAuthLocation | null,
  runtime: SupportAuthRuntimeConfig | TravelWebAuthRuntimeConfig | null,
  mode: SupportMode,
): SupportAuthConfig | null {
  if (location === null || location.port === '4201') return null;
  if (mode === 'customer') {
    const customer = resolveTravelWebAuthConfig(location, runtime);
    if (customer === null) return null;
    try {
      const issuer = new URL(runtime?.url ?? customer.url);
      if (issuer.username || issuer.password || issuer.search || issuer.hash) return null;
    } catch {
      return null;
    }
    return { ...customer, redirectUri: location.origin + '/support' };
  }
  if (location.hostname === 'localhost' && location.port === '4200')
    return {
      url: 'http://localhost:8180',
      realm: 'travel',
      clientId: 'travel-support-operator',
      redirectUri: location.origin + '/support/operator',
      environment: 'development',
    };
  if (
    runtime === null ||
    runtime.realm !== 'travel' ||
    runtime.clientId !== 'travel-support-operator' ||
    !['staging', 'production'].includes(runtime.environment)
  )
    return null;
  try {
    const app = new URL(location.origin),
      issuer = new URL(runtime.url);
    if (
      app.protocol !== 'https:' ||
      issuer.protocol !== 'https:' ||
      issuer.username ||
      issuer.password ||
      issuer.search ||
      issuer.hash
    )
      return null;
    return {
      ...runtime,
      url: issuer.origin + issuer.pathname.replace(/\/$/, ''),
      redirectUri: app.origin + '/support/operator',
    };
  } catch {
    return null;
  }
}
