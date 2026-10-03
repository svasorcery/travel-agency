import { expect, type Route, test } from '@playwright/test';
import { fetchFictionalApi, isFictionalApiRequest } from './fictional-route-guard';

test('fictional fetch guard rejects foreign matching URLs, unsupported methods and credentials before network', async ({
  page,
}) => {
  const id = '11111111-1111-4111-8111-111111111111';
  for (const [url, method, headers] of [
    [`https://fictional.invalid/api/flights/travelers/${id}`, 'GET', {}],
    [`http://localhost:4201/api/flights/travelers/${id}`, 'GET', {}],
    [`http://127.0.0.1:4201/api/flights/travelers/${id}`, 'POST', {}],
    ['http://127.0.0.1:4201/api/flights/unknown', 'GET', {}],
    ['https://fictional.invalid/api/flights/search/v2?currency=RUB', 'POST', {}],
    ['http://127.0.0.1:4201/api/flights/search/v2?currency=RUB', 'GET', {}],
    ['http://127.0.0.1:4201/api/flights/search/v2?currency=RUB', 'POST', { authorization: 'fictional' }],
    ['http://127.0.0.1:4201/api/flights/search/v2/extra?currency=RUB', 'POST', {}],
    ['http://127.0.0.1:4201/api/flights/search/v2?currency=USD', 'POST', {}],
    ['http://127.0.0.1:4201/api/flights/search/v2?currency=RUB&extra=1', 'POST', {}],
    [`http://127.0.0.1:4201/api/flights/travelers/${id}`, 'GET', { Authorization: 'fictional' }],
  ] as const) {
    expect(isFictionalApiRequest(url, method, headers)).toBe(false);
    let fetches = 0;
    let aborted = false;
    const fixture = {
      request: () => ({ url: () => url, method: () => method, headers: () => headers }),
      abort: async () => {
        aborted = true;
      },
      fetch: async () => {
        fetches++;
        throw new Error('Unsafe synthetic fetch reached');
      },
    } as unknown as Route;
    await expect(fetchFictionalApi(fixture)).rejects.toThrow(
      'Fictional API guard blocked a request before network dispatch.',
    );
    expect(aborted).toBe(true);
    expect(fetches).toBe(0);
  }
  expect(isFictionalApiRequest(`http://127.0.0.1:4201/api/flights/travelers/${id}`, 'GET', {})).toBe(true);
  expect(isFictionalApiRequest('http://127.0.0.1:4201/api/flights/search/v2?currency=RUB', 'POST', {})).toBe(true);
  let options: unknown;
  const safe = {
    request: () => ({
      url: () => 'http://127.0.0.1:4201/api/flights/search/v2?currency=RUB',
      method: () => 'POST',
      headers: () => ({}),
    }),
    fetch: async (value: unknown) => {
      options = value;
      return {};
    },
  } as unknown as Route;
  await fetchFictionalApi(safe);
  expect(options).toEqual({ maxRedirects: 0 });
  await page.goto('/flights');
  const foreign = `https://fictional.invalid/api/flights/travelers/${id}`;
  let intercepted = false;
  let rejected = false;
  let handled!: () => void;
  const handlerDone = new Promise<void>((resolve) => (handled = resolve));
  await page.route(foreign, async (route) => {
    intercepted = true;
    try {
      await fetchFictionalApi(route);
    } catch (error) {
      expect((error as Error).message).toBe('Fictional API guard blocked a request before network dispatch.');
      rejected = true;
    } finally {
      handled();
    }
  });
  const failed = page.waitForEvent('requestfailed', (request) => request.url() === foreign);
  const outcome = await page.evaluate(async (url) => {
    try {
      await fetch(url);
      return 'unexpected-response';
    } catch {
      return 'blocked';
    }
  }, foreign);
  const request = await failed;
  await handlerDone;
  expect(intercepted).toBe(true);
  expect(rejected).toBe(true);
  expect(outcome).toBe('blocked');
  expect(request.failure()).not.toBeNull();
});
