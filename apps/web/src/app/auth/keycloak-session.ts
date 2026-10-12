import { InjectionToken } from '@angular/core';
import type Keycloak from 'keycloak-js';
import type { KeycloakConfig } from 'keycloak-js';

export type TravelKeycloakFactory = (config: KeycloakConfig) => Promise<Keycloak>;
export const TRAVEL_KEYCLOAK_FACTORY = new InjectionToken<TravelKeycloakFactory>('TRAVEL_KEYCLOAK_FACTORY', {
  providedIn: 'root',
  factory: () => async (config) => {
    const { default: KeycloakClient } = await import('keycloak-js');
    return new KeycloakClient(config);
  },
});
export interface TravelTokenClaims {
  sub?: unknown;
  aud?: unknown;
  scope?: unknown;
  azp?: unknown;
}
export function validateTravelSubject(claims: TravelTokenClaims, fail: (field: 'sub' | 'aud') => Error): string {
  if (
    typeof claims.sub !== 'string' ||
    !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(claims.sub) ||
    claims.sub === '00000000-0000-0000-0000-000000000000'
  )
    throw fail('sub');
  if (!(Array.isArray(claims.aud) ? claims.aud : [claims.aud]).includes('travel-web')) throw fail('aud');
  return claims.sub;
}
