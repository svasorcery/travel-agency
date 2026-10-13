import { resolveSupportAuthConfig, safeSupportReturn, validateSupportClaims } from './support-auth-contract';

const subject = '22222222-2222-4222-8222-222222222222';
describe('Support authentication boundaries', () => {
  it('accepts customer without booking permission and requires both operator claims', () => {
    expect(validateSupportClaims({ sub: subject, aud: 'travel-web', scope: 'openid' }, 'customer')).toBe(subject);
    const claims = {
      sub: subject,
      aud: ['travel-web'],
      scope: 'openid support:operator',
      azp: 'travel-support-operator',
    };
    expect(validateSupportClaims(claims, 'operator')).toBe(subject);
    for (const invalid of [
      { ...claims, azp: 'travel-web' },
      { ...claims, scope: 'openid' },
      { ...claims, aud: 'other' },
      { ...claims, sub: '00000000-0000-0000-0000-000000000000' },
    ])
      expect(() => validateSupportClaims(invalid, 'operator')).toThrow();
  });
  it('only accepts callback paths belonging to the selected context', () => {
    expect(safeSupportReturn('/support/cases/' + subject, 'customer')).toBe('/support/cases/' + subject);
    expect(safeSupportReturn('/support?orderId=' + subject, 'customer')).toBe('/support?orderId=' + subject);
    expect(safeSupportReturn('/support?orderId=' + subject + '&body=private', 'customer')).toBe('/support');
    expect(safeSupportReturn('/support?orderId=' + subject, 'operator')).toBe('/support/operator');
    expect(safeSupportReturn('/support/cases/00000000-0000-0000-0000-000000000000', 'customer')).toBe('/support');
    for (const path of [
      '//evil.test',
      'https://evil.test',
      '/support/%2f%2fevil.test',
      '/support?body=private',
      '/support/operator',
      '/flights',
    ])
      expect(safeSupportReturn(path, 'customer')).toBe('/support');
    expect(safeSupportReturn('/support', 'operator')).toBe('/support/operator');
  });
  it('validates separate operator runtime client and rejects credential URLs', () => {
    const location = { hostname: 'app.test', origin: 'https://app.test', port: '' };
    const config = {
      url: 'https://auth.test',
      realm: 'travel',
      clientId: 'travel-support-operator',
      environment: 'production' as const,
    };
    expect(resolveSupportAuthConfig(location, config, 'operator')?.clientId).toBe('travel-support-operator');
    expect(resolveSupportAuthConfig(location, { ...config, clientId: 'travel-web' }, 'operator')).toBeNull();
    expect(
      resolveSupportAuthConfig(location, { ...config, url: 'https://user:secret@auth.test' }, 'operator'),
    ).toBeNull();
  });
});
