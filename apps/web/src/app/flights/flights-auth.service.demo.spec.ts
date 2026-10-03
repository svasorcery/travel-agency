import { FlightsAuthService } from './flights-auth.service.demo';

describe('isolated fictional identity replacement', () => {
  it('retains the selected in-memory owner across login and invalidates the identity epoch on switch/logout', async () => {
    const auth = new FlightsAuthService();
    expect(auth.status()).toEqual({ kind: 'anonymous' });
    await auth.beginLogin();
    expect(auth.status()).toEqual({ kind: 'authenticated', userId: 'demo-only' });
    auth.switchDemoOwner('demo-other');
    expect(auth.identityEpoch()).toBe(1);
    expect(auth.status()).toEqual({ kind: 'authenticated', userId: 'demo-other' });
    await auth.logout();
    expect(auth.identityEpoch()).toBe(2);
    expect(auth.status()).toEqual({ kind: 'anonymous' });
    await auth.beginLogin();
    expect(auth.status()).toEqual({ kind: 'authenticated', userId: 'demo-other' });
    expect(await auth.accessToken()).toBeNull();
  });
});
