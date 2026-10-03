import type { Route } from '@playwright/test';

export function isFictionalApiRequest(url: string, method: string, headers: Record<string, string>): boolean {
  const target = new URL(url);
  if (
    target.origin !== 'http://127.0.0.1:4201' ||
    target.username ||
    target.password ||
    Object.keys(headers).some((key) => key.toLowerCase() === 'authorization')
  )
    return false;
  const path = target.pathname;
  if (path === '/api/flights/search') return method === 'POST';
  if (path === '/api/flights/orders' || path === '/api/flights/travelers') return method === 'GET';
  if (/^\/api\/flights\/orders\/(quote|hold|confirm)$/.test(path)) return method === 'POST';
  if (/^\/api\/flights\/orders\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(path)) return method === 'GET';
  if (/^\/api\/flights\/orders\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\/cancel$/i.test(path))
    return method === 'POST';
  return (
    /^\/api\/flights\/travelers\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(path) &&
    ['GET', 'PUT', 'DELETE'].includes(method)
  );
}
export async function fetchFictionalApi(route: Route) {
  const request = route.request();
  if (!isFictionalApiRequest(request.url(), request.method(), request.headers())) {
    await route.abort('blockedbyclient');
    throw new Error('Fictional API guard blocked a request before network dispatch.');
  }
  return route.fetch({ maxRedirects: 0 });
}
