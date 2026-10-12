import { validateTravelSubject } from '../auth/keycloak-session';

export class FlightsAuthContractError extends Error {
  constructor(readonly field: 'sub' | 'aud' | 'scope') {
    super(`The Keycloak access token is missing a valid ${field} claim.`);
    this.name = 'FlightsAuthContractError';
  }
}

export interface FlightsAccessTokenClaims {
  sub?: unknown;
  aud?: unknown;
  scope?: unknown;
}

export function validateFlightsAccessTokenClaims(claims: FlightsAccessTokenClaims): string {
  const subject = validateTravelSubject(claims, (field) => new FlightsAuthContractError(field));

  if (typeof claims.scope !== 'string' || !claims.scope.split(/\s+/).includes('flights:book')) {
    throw new FlightsAuthContractError('scope');
  }

  return subject;
}
