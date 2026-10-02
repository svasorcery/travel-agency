import { expect, type Page, test } from '@playwright/test';

async function isolate(page: Page) {
  await page.route('**/*', async (route) => {
    if (new URL(route.request().url()).origin !== 'http://127.0.0.1:4201')
      throw new Error('Unexpected external request');
    await route.continue();
  });
}

async function seed(page: Page, count: number, month: number) {
  const ids: string[] = [];
  for (let i = 0; i < count; i++) {
    const date = new Date(Date.UTC(2034, month, i + 1)).toISOString().slice(0, 10);
    const quote = await page.request.post('/api/flights/orders/quote', {
      data: { provider: 'duffel', providerOfferRef: `off_fixture_ow_${date}` },
    });
    expect(quote.ok()).toBeTruthy();
    const { aggregateId } = await quote.json();
    const hold = await page.request.post('/api/flights/orders/hold', {
      headers: { 'Idempotency-Key': `aaaaaaaa-aaaa-4aaa-8aaa-${String(month * 100 + i + 1).padStart(12, '0')}` },
      data: {
        aggregateId,
        passengers: [
          {
            givenName: 'Fictional',
            familyName: 'Traveler',
            dateOfBirth: '1990-01-01',
            gender: 'unspecified',
            email: 'fictional@example.test',
            phone: '+79001234567',
          },
        ],
      },
    });
    expect(hold.ok()).toBeTruthy();
    ids.push(aggregateId);
  }
  return ids;
}

test('cancel preserves the updated B4 row, anchor and focus on Back and explicit return', async ({ page }) => {
  await isolate(page);
  await seed(page, 45, 2);
  await page.goto('/flights/orders');
  await page.getByRole('button', { name: 'Демо вход' }).click();
  const rows = page.locator('[data-order-id]');
  await expect(rows).toHaveCount(20);
  await page.locator('[data-list-end]').scrollIntoViewIfNeeded();
  await expect(rows).toHaveCount(40);
  const ids = await rows.evaluateAll((elements) =>
    elements.map((element) => (element as HTMLElement).dataset['orderId']),
  );
  const selected = rows.nth(31);
  const id = await selected.getAttribute('data-order-id');
  if (id === null) throw new Error('Fixture order missing');
  const held = await (await page.request.get(`/api/flights/orders/${id}`)).json();
  await selected.evaluate((element) => element.scrollIntoView({ block: 'center', behavior: 'instant' }));
  const top = await selected.evaluate((element) => element.getBoundingClientRect().top);
  let sent = false;
  const cancelCalls: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.endsWith('/cancel')) {
      sent = true;
      cancelCalls.push(request.url());
    }
  });
  await page.route(`**/api/flights/orders/${id}`, async (route) => {
    if (sent && route.request().method() === 'GET') await route.fulfill({ status: 200, json: held });
    else await route.fallback();
  });
  await selected.locator('a').click();
  await page.getByRole('button', { name: 'Отменить заказ', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Оставить заказ' })).toBeFocused();
  expect(cancelCalls).toHaveLength(0);
  await page.getByRole('button', { name: 'Да, отменить заказ' }).click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён' })).toBeVisible();
  expect(cancelCalls).toHaveLength(1);
  await expect(page.getByText('Демо-заказ отменён.', { exact: false })).toBeVisible();
  await page.screenshot({ path: 'test-results/flights-b5-cancel-desktop.png' });
  await page.goBack();
  await expect(rows).toHaveCount(40);
  const row = page.locator(`[data-order-id="${id}"]`);
  const link = page.locator(`[data-order-link="${id}"]`);
  await expect(row).toContainText('Отменён');
  await expect(link).toBeFocused();
  expect(
    await rows.evaluateAll((elements) => elements.map((element) => (element as HTMLElement).dataset['orderId'])),
  ).toEqual(ids);
  await expect
    .poll(async () => Math.abs((await row.evaluate((element) => element.getBoundingClientRect().top)) - top))
    .toBeLessThanOrEqual(4);
  await link.click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён' })).toBeVisible();
  await page.setViewportSize({ width: 360, height: 780 });
  await page.getByRole('link', { name: 'К списку заказов' }).click();
  await expect(rows).toHaveCount(40);
  await expect(row).toContainText('Отменён');
  await expect(link).toBeFocused();
  await expect
    .poll(async () => Math.abs((await row.evaluate((element) => element.getBoundingClientRect().top)) - top))
    .toBeLessThanOrEqual(4);
  await page.screenshot({ path: 'test-results/flights-b5-feed-mobile.png' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  expect(
    await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } })),
  ).not.toMatch(/fictional@example|Traveler|cancelledAt|Idempotency/);
});

test('lost cancellation response retries the identical bodyless request and reload requires login', async ({
  page,
}) => {
  await isolate(page);
  const [id] = await seed(page, 1, 7);
  let first = true;
  const writes: { key: string | undefined; body: string | null; method: string }[] = [];
  await page.route('**/api/flights/orders/*/cancel', async (route) => {
    const request = route.request();
    writes.push({ key: request.headers()['idempotency-key'], body: request.postData(), method: request.method() });
    const response = await route.fetch();
    if (first) {
      first = false;
      await route.abort('failed');
    } else {
      expect(response.headers()['idempotency-replay']).toBe('true');
      await route.fulfill({ response });
    }
  });
  await page.goto(`/flights/orders/${id}`);
  await page.getByRole('button', { name: 'Демо вход' }).click();
  await page.getByRole('button', { name: 'Отменить заказ', exact: true }).click();
  await page.getByRole('button', { name: 'Да, отменить заказ' }).click();
  await expect(page.getByText('Исход отмены неизвестен.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Подтвердить заказ', exact: true })).not.toBeVisible();
  await page.getByRole('button', { name: 'Повторить ту же отмену' }).click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён' })).toBeVisible();
  expect(writes).toHaveLength(2);
  expect(writes[1]).toEqual(writes[0]);
  expect(writes[0].body).toBeNull();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Демо вход' })).toBeVisible();
  await page.getByRole('button', { name: 'Демо вход' }).click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён' })).toBeVisible();
  expect(writes).toHaveLength(2);
});
