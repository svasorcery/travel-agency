import {
  resolveTravelWebAuthConfig,
  type TravelWebAuthLocation,
  type TravelWebAuthRuntimeConfig,
} from './travel-web-auth-config';
/** Add one frame-only policy before bootstrap. It cannot loosen another CSP. */
export function installTravelFramePolicy(
  document: Document,
  location: TravelWebAuthLocation,
  runtime: TravelWebAuthRuntimeConfig | null,
): void {
  if (document.querySelector('meta[data-travel-frame-policy]')) return;
  let authOrigin: string | null = null;
  const config = resolveTravelWebAuthConfig(location, runtime);
  if (config !== null) {
    try {
      const url = new URL(config.url);
      authOrigin = url.origin;
    } catch {
      /* Invalid configured auth remains closed. */
    }
  }
  const policy = document.createElement('meta');
  policy.httpEquiv = 'Content-Security-Policy';
  policy.dataset['travelFramePolicy'] = 'true';
  policy.content = `frame-src 'self' https://yandex.st/rasp/media/apicc/copyright_vert_mono.html${authOrigin === null ? '' : ` ${authOrigin}`}`;
  document.head.append(policy);
}
