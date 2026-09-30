const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

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
  const subject = claims.sub;
  if (typeof subject !== 'string' || !GUID.test(subject) || subject === EMPTY_GUID) {
    throw new FlightsAuthContractError('sub');
  }

  const audiences = Array.isArray(claims.aud) ? claims.aud : [claims.aud];
  if (!audiences.includes('travel-web')) throw new FlightsAuthContractError('aud');

  if (typeof claims.scope !== 'string' || !claims.scope.split(/\s+/).includes('flights:book')) {
    throw new FlightsAuthContractError('scope');
  }

  return subject;
}
