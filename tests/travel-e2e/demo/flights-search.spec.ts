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
        !(route.request().method() === 'GET' && /^\/api\/flights\/orders\/[0-9a-f-]{36}$/i.test(url.pathname)) &&
        ![
          '/api/flights/search',
          '/api/flights/orders/quote',
          '/api/flights/orders/hold',
          '/api/flights/orders/confirm',
          '/api/flights/orders',
        ].includes(url.pathname)) ||
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
        authorization: request.headers().authorization,
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
  const bookings: {
    path: string;
    body: Record<string, unknown>;
    authorization: string | undefined;
    key: string | undefined;
  }[] = [];
  const consoleMessages: string[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  page.on('console', (message) => consoleMessages.push(message.text()));
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/orders/quote') {
      quotes.push({ body: request.postDataJSON(), authorization: request.headers().authorization });
    }
    if (['/api/flights/orders/hold', '/api/flights/orders/confirm'].includes(new URL(request.url()).pathname)) {
      bookings.push({
        path: new URL(request.url()).pathname,
        body: request.postDataJSON(),
        authorization: request.headers().authorization,
        key: request.headers()['idempotency-key'],
      });
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

  await page.getByRole('button', { name: 'Оформить одного пассажира' }).click();
  await expect(page.getByText(/Цена и маршрут обновлены после входа/)).toBeVisible();
  await expect(page.getByText('После входа предложение проверено заново.', { exact: false })).toBeVisible();
  await page.locator('button[data-action="accept-quote"]').click();
  await page.getByRole('button', { name: 'Оформить одного пассажира' }).click();
  await expect(page.getByText(/Демо: вход и заказ имитируются/)).toBeVisible();
  await page.getByLabel('Имя').fill('Demo');
  await page.getByLabel('Фамилия').fill('Traveler');
  await page.getByLabel('Дата рождения').fill('1990-04-12');
  await page.getByLabel('Email').fill('demo@example.test');
  await page.getByLabel('Телефон').fill('+79161234567');

  const holdResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/flights/orders/hold',
  );
  await page.getByRole('button', { name: 'Удержать предложение' }).click();
  const heldResponse = await holdResponse;
  expect(heldResponse.status()).toBe(200);
  expect((await heldResponse.allHeaders())['x-travel-demo']).toBe('fixtures');
  await expect(page.getByText('Предложение удержано')).toBeVisible();
  await expect(page.getByText('тестовый кошелёк')).toBeVisible();
  await expect(page.locator('[data-action="open-held-order"]')).toHaveAttribute(
    'href',
    /\/flights\/orders\/[0-9a-f-]{36}$/i,
  );

  let firstOrderRead = true;
  await page.route('**/api/flights/orders/*', async (route) => {
    if (route.request().method() === 'GET' && firstOrderRead) {
      firstOrderRead = false;
      await route.fulfill({
        status: 404,
        contentType: 'application/problem+json',
        body: '{"status":404,"title":"Not Found"}',
      });
    } else {
      await route.fallback();
    }
  });
  await page.locator('[data-action="open-held-order"]').click();
  await expect(page).toHaveURL(/\/flights\/orders\/[0-9a-f-]{36}$/i);
  await expect(page.getByText('Обновляем удержание в проекции.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Подтвердить заказ' })).toBeVisible({ timeout: 10_000 });
  expect(firstOrderRead).toBe(false);

  const confirmResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/flights/orders/confirm',
  );
  await page.getByRole('button', { name: 'Подтвердить заказ' }).click();
  const confirmedResponse = await confirmResponse;
  expect(confirmedResponse.status()).toBe(200);
  expect((await confirmedResponse.allHeaders())['x-travel-demo']).toBe('fixtures');
  await expect(page.getByRole('heading', { name: 'Заказ подтверждён' })).toBeVisible();
  await expect(page.getByText(/билет ещё не выписан/i)).toBeVisible();
  await expect(page.getByText('Ticketed')).toHaveCount(0);
  await expect(page).toHaveURL(/\/flights\/orders\/[0-9a-f-]{36}$/i);
  await expect(page.getByRole('heading', { name: 'Билет выписан' })).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/^DEMO-TKT-/)).toBeVisible();
  await page.reload();
  await page.getByRole('button', { name: 'Демо вход' }).click();
  await expect(page.getByRole('heading', { name: 'Билет выписан' })).toBeVisible();
  await expect(page.getByText('Удержано до')).toHaveCount(0);

  expect(bookings).toHaveLength(2);
  expect(bookings.map((request) => request.path)).toEqual(['/api/flights/orders/hold', '/api/flights/orders/confirm']);
  expect(bookings[0].body['passengers']).toHaveLength(1);
  expect(bookings[0].authorization).toBeUndefined();
  expect(bookings[1].authorization).toBeUndefined();
  expect(bookings[0].key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
  expect(bookings[1].key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i);
  expect(bookings[0].key).not.toBe(bookings[1].key);
  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }),
  );
  expect(persisted).not.toContain('off_fixture_');
  expect(persisted).not.toContain('travel.flights.booking');
  for (const booking of bookings) expect(persisted).not.toContain(booking.key as string);
  expect(persisted).not.toContain('demo@example.test');
  expect(persisted).not.toContain('Demo');
  expect(persisted).not.toContain('Traveler');
  expect(persisted).not.toContain('+79161234567');
  expect(page.url()).not.toContain('demo@example.test');
  expect(page.url()).not.toContain('off_fixture_');
  expect(page.url()).not.toContain('Demo');
  expect(page.url()).not.toContain('Traveler');
  expect(consoleMessages.join('\n')).not.toContain('demo@example.test');
  expect(consoleMessages.join('\n')).not.toContain('Demo');
  expect(consoleMessages.join('\n')).not.toContain('Traveler');
  expect(consoleMessages.join('\n')).not.toContain('+79161234567');

  await page.screenshot({ path: 'test-results/flights-quote-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 360, height: 780 });
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }));
  expect(widths.content).toBeLessThanOrEqual(widths.viewport);
  await page.screenshot({ path: 'test-results/flights-quote-mobile.png', fullPage: true });
  expect(quotes).toHaveLength(2);
  expect(quotes[0].body).toEqual({
    providerOfferRef: expect.stringMatching(/^off_fixture_ow_\d{4}-\d{2}-\d{2}$/),
    provider: 'duffel',
    aggregateId: null,
  });
  expect(quotes[1].body).toEqual({
    providerOfferRef: quotes[0].body['providerOfferRef'],
    provider: 'duffel',
    aggregateId: expect.any(String),
  });
  expect(quotes[0].authorization).toBeUndefined();
  expect(quotes[1].authorization).toBeUndefined();
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

test('retired Travel draft is removed and reload never resumes a quote', async ({ page }) => {
  const unexpected: string[] = [];
  const writes: string[] = [];
  await blockUnexpectedTraffic(page, unexpected);
  await page.addInitScript(() => {
    sessionStorage.setItem('travel.flights.booking.v1', 'retired-fictional-intent');
    sessionStorage.setItem('unrelated-fixture', 'keep');
  });
  page.on('request', (request) => {
    if (request.method() === 'POST') writes.push(new URL(request.url()).pathname);
  });
  await page.goto('/flights');
  await expect(page.getByRole('heading', { name: /Сначала маршрут/ })).toBeVisible();
  expect(await page.evaluate(() => sessionStorage.getItem('travel.flights.booking.v1'))).toBeNull();
  expect(await page.evaluate(() => sessionStorage.getItem('unrelated-fixture'))).toBe('keep');
  await page.reload();
  await expect(page.getByRole('heading', { name: /Сначала маршрут/ })).toBeVisible();
  await expect(page.locator('app-flight-quote-panel')).toHaveCount(0);
  expect(writes).toEqual([]);
  expect(unexpected).toEqual([]);
});

for (const lostOperation of ['hold', 'confirm'] as const) {
  test(`lost ${lostOperation} response blocks another booking after SPA navigation`, async ({ page }) => {
    const unexpected: string[] = [];
    const writes: string[] = [];
    const consoleMessages: string[] = [];
    await blockUnexpectedTraffic(page, unexpected);
    page.on('console', (message) => consoleMessages.push(message.text()));
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (request.method() === 'POST' && path.startsWith('/api/flights/orders/')) writes.push(path);
    });
    await page.goto('/flights');
    await page.getByRole('button', { name: 'Подставить пример' }).click();
    await page.getByRole('button', { name: /Найти рейсы/ }).click();
    await page.locator('[data-action="quote"]').click();
    await page.locator('[data-action="accept-quote"]').click();
    await page.getByRole('button', { name: 'Оформить одного пассажира' }).click();
    await expect(page.getByText(/Цена и маршрут обновлены после входа/)).toBeVisible();
    await page.locator('[data-action="accept-quote"]').click();
    await page.getByRole('button', { name: 'Оформить одного пассажира' }).click();
    await page.getByLabel('Имя').fill('Fictional');
    await page.getByLabel('Фамилия').fill('Example');
    await page.getByLabel('Дата рождения').fill('1990-04-12');
    await page.getByLabel('Email').fill('unknown@example.test');
    await page.getByLabel('Телефон').fill('+79161234567');
    await page.route(`**/api/flights/orders/${lostOperation}`, (route) => route.abort());
    await page.getByRole('button', { name: 'Удержать предложение' }).click();
    if (lostOperation === 'confirm') {
      await page.locator('[data-action="confirm"]').click();
      await expect(page.getByText('Исход подтверждения неизвестен.', { exact: false })).toBeVisible();
      await page.locator('[data-action="open-held-order"]').click();
      await expect(page).toHaveURL(/\/flights\/orders\/[0-9a-f-]{36}$/i);
      await expect(
        page.getByText('Не повторяйте подтверждение. Проверка показывает только наблюдаемое состояние заказа.', {
          exact: true,
        }),
      ).toBeVisible();
      await expect(page.locator('[data-action="confirm-order"]')).toHaveCount(0);
    } else {
      await expect(page.getByText('Исход удержания неизвестен.', { exact: false })).toBeVisible();
      await expect(page.locator('[data-action="hold"]')).toHaveCount(0);
      await page.getByRole('link', { name: 'Мои заказы' }).click();
    }
    await page.getByRole('link', { name: 'Новый поиск' }).click();
    await page.getByRole('button', { name: 'Подставить пример' }).click();
    await page.getByRole('button', { name: /Найти рейсы/ }).click();
    await page.locator('[data-action="quote"]').click();
    await expect(
      page.getByText('Исход предыдущей операции бронирования требует проверки.', { exact: false }),
    ).toBeVisible();
    expect(writes.filter((path) => path.endsWith('/quote'))).toHaveLength(2);
    expect(writes.filter((path) => path.endsWith('/hold'))).toHaveLength(1);
    expect(writes.filter((path) => path.endsWith('/confirm'))).toHaveLength(lostOperation === 'confirm' ? 1 : 0);
    const persisted = await page.evaluate(() =>
      JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }),
    );
    for (const value of ['Fictional', 'Example', 'unknown@example.test', '+79161234567']) {
      expect(persisted).not.toContain(value);
      expect(page.url()).not.toContain(value);
      expect(consoleMessages.join('\n')).not.toContain(value);
    }
    expect(unexpected).toEqual([]);
  });
}
