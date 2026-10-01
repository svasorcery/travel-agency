import { expect, type Page, test } from '@playwright/test';

async function blockExternal(page: Page) {
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:4201' || url.pathname.startsWith('/events/')) {
      throw new Error('Unexpected non-demo request');
    }
    await route.continue();
  });
}

async function seedOrders(page: Page, count: number) {
  for (let i = 0; i < count; i++) {
    const date = new Date(Date.UTC(2031, 1, i + 1)).toISOString().slice(0, 10);
    const quote = await page.request.post('/api/flights/orders/quote', {
      data: { provider: 'duffel', providerOfferRef: `off_fixture_ow_${date}` },
    });
    expect(quote.ok()).toBeTruthy();
    const { aggregateId } = await quote.json();
    const hold = await page.request.post('/api/flights/orders/hold', {
      headers: { 'Idempotency-Key': `aaaaaaaa-aaaa-4aaa-8aaa-${String(i + 1).padStart(12, '0')}` },
      data: {
        aggregateId,
        passengers: [
          {
            givenName: 'Demo',
            familyName: 'Traveler',
            dateOfBirth: '1990-01-15',
            gender: 'male',
            email: 'demo@example.test',
            phone: '+79001234567',
          },
        ],
      },
    });
    expect(hold.ok()).toBeTruthy();
  }
}

test('automatic feed restores the selected order and loaded rows on browser Back and the B3 link', async ({ page }) => {
  await blockExternal(page);
  await seedOrders(page, 45);
  const listRequests: number[] = [];
  const messages: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api/flights/orders') {
      expect(request.headers()['authorization']).toBeUndefined();
      listRequests.push(Number(url.searchParams.get('offset')));
    }
  });
  page.on('console', (message) => messages.push(message.text()));
  await page.goto('/flights/orders');
  await page.getByRole('button', { name: 'Демо вход' }).click();
  const rows = page.locator('[data-order-id]');
  await expect(rows).toHaveCount(20);
  await page.screenshot({ path: 'test-results/flights-orders-top.png' });
  await page.locator('[data-list-end]').scrollIntoViewIfNeeded();
  await expect(rows).toHaveCount(40);
  const selected = rows.nth(31);
  const id = await selected.getAttribute('data-order-id');
  if (id === null) throw new Error('Missing fictional order ID');
  await selected.evaluate((element) => element.scrollIntoView({ block: 'center', behavior: 'instant' }));
  const top = await selected.evaluate((element) => element.getBoundingClientRect().top);
  const calls = listRequests.length;
  await selected.locator('a').click();
  await expect(page.getByRole('heading', { name: 'Состояние заказа' })).toBeVisible();
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0);
  await page.goBack();
  await expect(rows).toHaveCount(40);
  await expect
    .poll(async () =>
      Math.abs(
        (await page.locator(`[data-order-id="${id}"]`).evaluate((element) => element.getBoundingClientRect().top)) -
          top,
      ),
    )
    .toBeLessThanOrEqual(4);
  await expect(page.locator(`[data-order-link="${id}"]`)).toBeFocused();
  expect(listRequests).toHaveLength(calls);

  await page.locator(`[data-order-link="${id}"]`).click();
  await page.getByRole('link', { name: 'К списку заказов' }).click();
  await expect(rows).toHaveCount(40);
  await expect
    .poll(async () =>
      Math.abs(
        (await page.locator(`[data-order-id="${id}"]`).evaluate((element) => element.getBoundingClientRect().top)) -
          top,
      ),
    )
    .toBeLessThanOrEqual(4);
  await expect(page.locator(`[data-order-link="${id}"]`)).toBeFocused();
  expect(listRequests).toHaveLength(calls);
  await page.screenshot({ path: 'test-results/flights-orders-desktop.png' });

  await page.locator(`[data-order-link="${id}"]`).click();
  await page.setViewportSize({ width: 360, height: 780 });
  await page.getByRole('link', { name: 'К списку заказов' }).click();
  await expect(rows).toHaveCount(40);
  await expect
    .poll(async () =>
      Math.abs(
        (await page.locator(`[data-order-id="${id}"]`).evaluate((element) => element.getBoundingClientRect().top)) -
          top,
      ),
    )
    .toBeLessThanOrEqual(4);
  await expect(page.locator(`[data-order-link="${id}"]`)).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  await page.screenshot({ path: 'test-results/flights-orders-mobile.png' });

  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }),
  );
  expect(persisted).not.toMatch(/demo@example.test|Traveler|memory-only|ticketNumbers|nextOffset/);
  expect(page.url()).not.toMatch(/demo@example.test|Traveler|off_fixture_|token|offset/);
  expect(messages.join('\n')).not.toMatch(/demo@example.test|Traveler|79001234567/);

  await page.reload();
  await expect(page.getByRole('button', { name: 'Демо вход' })).toBeVisible();
  await page.getByRole('button', { name: 'Демо вход' }).click();
  await expect(rows).toHaveCount(20);
  expect(listRequests.at(-1)).toBe(0);
  expect(listRequests).toContain(20);
});

test('keyboard Show more preserves rows after an error and moves focus to the first added order', async ({ page }) => {
  await page.addInitScript(() => {
    Object.defineProperty(window, 'IntersectionObserver', { value: undefined, configurable: true });
  });
  await blockExternal(page);
  await seedOrders(page, 45);
  await page.goto('/flights/orders');
  await page.getByRole('button', { name: 'Демо вход' }).click();
  const rows = page.locator('[data-order-id]');
  await expect(rows).toHaveCount(20);
  await page.route('**/api/flights/orders?limit=21&offset=20', (route) =>
    route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{"status":503}' }),
  );
  const more = page.getByRole('button', { name: 'Показать ещё' });
  await more.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('alert')).toContainText('Следующие заказы не загрузились');
  await expect(rows).toHaveCount(20);
  await page.unroute('**/api/flights/orders?limit=21&offset=20');
  await page.getByRole('button', { name: 'Повторить загрузку' }).focus();
  await page.keyboard.press('Enter');
  await expect(rows).toHaveCount(40);
  await expect(rows.nth(20).locator('a')).toBeFocused();
});
