import { installTravelFramePolicy } from './travel-frame-policy';

describe('Travel frame document policy', () => {
  it('uses the canonical IDN issuer origin from the existing resolver', () => {
    const doc = document.implementation.createHTMLDocument();
    const url = 'https://идентификация.рф/auth';
    installTravelFramePolicy(
      doc,
      { hostname: 'travel.example', origin: 'https://travel.example', port: '' },
      { url, realm: 'travel', clientId: 'travel-web', environment: 'production' },
    );
    expect(doc.querySelector('meta[data-travel-frame-policy]')?.getAttribute('content')).toContain(new URL(url).origin);
  });
  it('retains existing directives and validates configured auth before bootstrap', () => {
    const doc = document.implementation.createHTMLDocument();
    const existing = doc.createElement('meta');
    existing.httpEquiv = 'Content-Security-Policy';
    existing.content = "script-src 'self'; frame-src 'self' https://identity.example";
    doc.head.append(existing);
    installTravelFramePolicy(
      doc,
      { hostname: 'travel.example', origin: 'https://travel.example', port: '' },
      { url: 'https://identity.example/auth', realm: 'travel', clientId: 'travel-web', environment: 'production' },
    );
    expect(existing.content).toBe("script-src 'self'; frame-src 'self' https://identity.example");
    expect(doc.querySelector('meta[data-travel-frame-policy]')?.getAttribute('content')).toBe(
      "frame-src 'self' https://yandex.st/rasp/media/apicc/copyright_vert_mono.html https://identity.example",
    );
  });
  it('rejects unsupported auth URL and retains existing development fallback over ignored runtime', () => {
    const doc = document.implementation.createHTMLDocument();
    installTravelFramePolicy(
      doc,
      { hostname: 'localhost', origin: 'http://localhost:4200', port: '4200' },
      { url: 'https://ignored.example/', realm: 'travel', clientId: 'travel-web', environment: 'production' },
    );
    expect(doc.querySelector('meta[data-travel-frame-policy]')?.getAttribute('content')).toContain(
      'http://localhost:8180',
    );
    expect(doc.querySelector('meta[data-travel-frame-policy]')?.getAttribute('content')).not.toContain(
      'ignored.example',
    );
    const bad = document.implementation.createHTMLDocument();
    installTravelFramePolicy(
      bad,
      { hostname: 'travel.example', origin: 'https://travel.example', port: '' },
      { url: 'javascript:alert(1)', realm: 'travel', clientId: 'travel-web', environment: 'production' },
    );
    expect(bad.querySelector('meta[data-travel-frame-policy]')?.getAttribute('content')).not.toContain('javascript:');
  });
});
