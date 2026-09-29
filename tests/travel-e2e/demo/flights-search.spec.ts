import { expect, type Page, test } from '@playwright/test';

async function blockUnexpectedTraffic(page: Page, unexpected: string[]) {
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:4201') {
      unexpected.push(url.href);
      await route.abort();
      return;
    }
    if (
      (url.pathname.startsWith('/api/') &&
        !['/api/flights/search', '/api/flights/orders/quote'].includes(url.pathname)) ||
      url.pathname.startsWith('/events/')
    ) {
      unexpected.push(url.pathname);
      await route.abort();
      return;
    }
    await route.continue();
  });
}

test('anonymous one-way and round-trip search use the real demo proxy', async ({ page }) => {
  const unexpected: string[] = [];
  const searches: {
    body: Record<string, unknown>;
    authorization: string | undefined;
    contentType: string | undefined;
    currency: string | null;
  }[] = [];
  const demoHeaders: string[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/search') {
      searches.push({
        body: request.postDataJSON(),
        authorization: request.headers()['authorization'],
        contentType: request.headers()['content-type'],
        currency: new URL(request.url()).searchParams.get('currency'),
      });
    }
  });
  page.on('response', async (response) => {
    if (new URL(response.url()).pathname === '/api/flights/search') {
      demoHeaders.push((await response.allHeaders())['x-travel-demo']);
    }
  });

  await page.goto('/');
  await expect(page).toHaveURL(/\/flights$/);
  await expect(
    page.getByText('Демонстрационные данные. Поиск и проверка цены не обращаются к авиакомпаниям.'),
  ).toBeVisible();
  await expect(page.getByRole('heading', { name: /Сначала маршрут/ })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Статус системы' })).toHaveCount(0);
  expect(searches).toHaveLength(0);
  await page.reload();
  await expect(page.getByRole('heading', { name: /Сначала маршрут/ })).toBeVisible();
  expect(searches).toHaveLength(0);

  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  await expect(page.getByText('Детали маршрута уточняются у партнёра.', { exact: false })).toBeVisible();
  expect(searches).toHaveLength(1);
  expect(searches[0].body['returnDate']).toBeNull();
  expect(searches[0].body).toMatchObject({
    origin: 'LED',
    destination: 'DME',
    passengerCount: 1,
    cabinClass: 'economy',
  });
  expect(searches[0].contentType).toBe('application/json');
  expect(searches[0].currency).toBe('RUB');
  expect(searches[0].authorization).toBeUndefined();
  expect(demoHeaders).toEqual(['fixtures']);

  await page.getByLabel('Туда и обратно').check();
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU102')).toBeVisible();
  expect(searches).toHaveLength(2);
  expect(searches[1].body['returnDate']).not.toBeNull();
  expect(demoHeaders).toEqual(['fixtures', 'fixtures']);
  expect(unexpected).toEqual([]);
});

test('bookable quote crosses the demo proxy and exposes an explicitly changed price', async ({ page }) => {
  const unexpected: string[] = [];
  const quotes: { body: Record<string, unknown>; authorization: string | undefined }[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/orders/quote') {
      quotes.push({ body: request.postDataJSON(), authorization: request.headers()['authorization'] });
    }
  });
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  await expect(page.locator('button[data-action="quote"]')).toHaveCount(1);

  const quoteResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/flights/orders/quote',
  );
  await page.locator('button[data-action="quote"]').click();
  const response = await quoteResponse;
  expect(response.status()).toBe(200);
  expect((await response.allHeaders())['x-travel-demo']).toBe('fixtures');
  await expect(page.getByRole('heading', { name: 'Актуальная цена' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Актуальная цена' })).toBeInViewport();
  await expect(page.getByText('В демо цена и срок вымышлены.', { exact: false })).toBeVisible();
  await expect(page.getByText('Цена или маршрут изменились', { exact: false })).toBeVisible();
  await expect(page.getByText(/В поиске:.*После проверки:/)).toBeVisible();
  await expect(page.getByText('Мест зарегистрированного багажа (максимум на сегменте): 1')).toBeVisible();
  await page.locator('button[data-action="accept-quote"]').click();
  await expect(page.getByText('Актуальное предложение принято', { exact: false })).toBeVisible();
  await page.screenshot({ path: 'test-results/flights-quote-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 360, height: 780 });
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
  await page.screenshot({ path: 'test-results/flights-quote-mobile.png', fullPage: true });
  expect(quotes).toHaveLength(1);
  expect(quotes[0].body).toEqual({
    providerOfferRef: expect.stringMatching(/^off_fixture_ow_\d{4}-\d{2}-\d{2}$/),
    provider: 'duffel',
    aggregateId: null,
  });
  expect(quotes[0].authorization).toBeUndefined();
  expect(unexpected).toEqual([]);
});

test('empty demo result identifies the fixture boundary after a route change', async ({ page }) => {
  const unexpected: string[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('textbox', { name: 'Куда', exact: true }).fill('VKO');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText(/предложений не найдено/)).toBeVisible();
  await expect(
    page.getByText('Демонстрационные данные. Поиск и проверка цены не обращаются к авиакомпаниям.'),
  ).toBeVisible();
  expect(unexpected).toEqual([]);
});

test('invalid form has no HTTP call and an error can be retried', async ({ page }) => {
  const unexpected: string[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  let searches = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/search') searches++;
  });
  await page.goto('/flights');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('Введите код аэропорта из трёх латинских букв.').first()).toBeVisible();
  expect(searches).toBe(0);

  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.route('**/api/flights/search*', async (route) => {
    await route.fulfill({ status: 500, contentType: 'application/problem+json', body: '{"status":500}' });
  });
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText(/Сервис поиска временно недоступен/)).toBeVisible();
  await page.unroute('**/api/flights/search*');
  await page.getByRole('button', { name: 'Повторить поиск' }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  await expect(
    page.getByText('Демонстрационные данные. Поиск и проверка цены не обращаются к авиакомпаниям.'),
  ).toBeVisible();
  expect(searches).toBe(2);
  expect(unexpected).toEqual([]);
});

test('mobile layout keeps the form and results usable with keyboard focus', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 });
  await page.goto('/flights');
  await page.keyboard.press('Tab');
  await expect(page.locator(':focus')).toBeVisible();
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
  await page.screenshot({ path: 'test-results/flights-mobile.png', fullPage: true });

  await page.setViewportSize({ width: 1280, height: 800 });
  await page.screenshot({ path: 'test-results/flights-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 640, height: 800 });
  // At 200% browser zoom on a 1280px screen, the effective CSS viewport is about 640px.
  const zoomEquivalentWidth = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(zoomEquivalentWidth).toBeLessThanOrEqual(640);
});

test('unparseable HTTP JSON is shown as an unreadable result', async ({ page }) => {
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.route('**/api/flights/search*', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: 'not json' });
  });
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText(/Не удалось прочитать результаты/)).toBeVisible();
});

test('airport code pasted with surrounding spaces is normalized before search', async ({ page }) => {
  let origin: unknown;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/search') {
      origin = request.postDataJSON().origin;
    }
  });
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('textbox', { name: 'Откуда' }).fill(' led ');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  expect(origin).toBe('LED');
});
