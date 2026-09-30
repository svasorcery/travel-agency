import { FlightsAuthContractError, validateFlightsAccessTokenClaims } from './flights-auth-contract';

describe('Flights access-token contract', () => {
  it('accepts a GUID subject, the configured audience, and flights:book scope', () => {
    expect(
      validateFlightsAccessTokenClaims({
        sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59',
        aud: ['account', 'travel-web'],
        scope: 'openid flights:book',
      }),
    ).toBe('bfb631f9-c340-4f2a-bcd7-9427d17d8c59');
  });

  it('rejects tokens that cannot authorize a Flight booking', () => {
    const claims = {
      sub: 'bfb631f9-c340-4f2a-bcd7-9427d17d8c59',
      aud: ['account'],
      scope: 'openid flights:book',
    };
    for (const invalid of [
      { ...claims, sub: 'not-a-guid' },
      { ...claims, aud: 'account' },
      { ...claims, scope: 'openid profile' },
      { ...claims, sub: '00000000-0000-0000-0000-000000000000' },
    ]) {
      expect(() => validateFlightsAccessTokenClaims(invalid)).toThrow(FlightsAuthContractError);
    }
  });
});
