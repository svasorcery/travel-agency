import type { Server } from 'node:http';
import { expect, type Page, test } from '@playwright/test';
import { isFictionalApiRequest } from './fictional-route-guard';

const action = (page: Page, name: string) => page.locator(`[data-action="${name}"]`);
async function checkout(page: Page) {
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.locator('#flight-departure').fill('2037-07-20');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await action(page, 'quote').click();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await page.getByLabel('Обращение').selectOption('mr');
  await page.locator('[formControlName="gender"]').selectOption('male');
  await page.getByLabel('Имя').fill('Fictional');
  await page.getByLabel('Фамилия').fill('Traveler');
  await page.getByLabel('Дата рождения').fill('1990-01-01');
  await page.getByLabel('Email').fill('fictional@example.test');
  await page.getByLabel('Телефон').fill('+441234567890');
  await action(page, 'load-baggage').click();
  await page.locator('[data-service="checked-baggage"]').first().selectOption('2');
  await action(page, 'load-seats').click();
  const seat = action(page, 'select-seat').first();
  await expect(seat).toBeVisible();
  await seat.selectOption({ index: 1 });
  await action(page, 'review-ancillaries').click();
  await expect(action(page, 'accept-purchase')).toBeVisible();
  await expect(page.getByLabel('Имя')).toHaveValue('Fictional');
  await expect(action(page, 'hold')).toBeDisabled();
  await action(page, 'accept-purchase').click();
}

for (const preset of ['purchase-success', 'purchase-diff', 'purchase-unknown']) {
  test(`ancillary ${preset} uses one frozen hold and truthful GET/reload actions`, async ({ page }) => {
    // Each preset starts a separate fictional API. No live scenario control or external forwarding.
    // Test-only shared demo fixture, outside the application module graph.
    // eslint-disable-next-line @nx/enforce-module-boundaries
    const { createDemoServer } = await import('../../../tools/demo/flights-search-api.mjs');
    const server = createDemoServer({ ancillaryPreset: preset }) as Server;
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (address === null || typeof address === 'string') throw new Error('Fictional listener unavailable');
    const origin = `http://127.0.0.1:${address.port}`;
    const writes: string[] = [];
    let lostResponse = false;
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (
        url.origin !== 'http://127.0.0.1:4201' ||
        url.username ||
        url.password ||
        Object.keys(request.headers()).some((h) => h.toLowerCase() === 'authorization')
      )
        throw new Error('Unexpected fictional browser request');
      if (!url.pathname.startsWith('/api/')) {
        await route.continue();
        return;
      }
      if (!isFictionalApiRequest(request.url(), request.method(), request.headers()))
        throw new Error('Fictional API guard refusal');
      const headers = request.headers();
      const body = request.postData();
      const response = await fetch(origin + url.pathname + url.search, {
        method: request.method(),
        redirect: 'error',
        headers: {
          ...(headers['content-type'] ? { 'Content-Type': headers['content-type'] } : {}),
          ...(headers['idempotency-key'] ? { 'Idempotency-Key': headers['idempotency-key'] } : {}),
        },
        ...(body !== null ? { body } : {}),
      });
      if (request.method() === 'POST' && /\/(hold|confirm)$/.test(url.pathname)) writes.push(url.pathname);
      // Browser loss after saved Matches must recover successfully, independently from supplier loss.
      if (preset === 'purchase-success' && url.pathname.endsWith('/hold') && !lostResponse) {
        lostResponse = true;
        await route.abort('failed');
        return;
      }
      await route.fulfill({
        status: response.status,
        contentType: response.headers.get('content-type') ?? 'application/json',
        headers: { 'Cache-Control': 'no-store', 'X-Travel-Demo': 'fixtures' },
        body: await response.text(),
      });
    });
    try {
      await checkout(page);
      await action(page, 'hold').click();
      await expect(page).toHaveURL(/\/flights\/orders\/[0-9a-f-]{36}$/i);
      if (preset === 'purchase-success') {
        await expect(action(page, 'confirm-order')).toBeVisible({ timeout: 10_000 });
        await page.reload();
        await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
        await expect(action(page, 'confirm-order')).toBeVisible();
        await action(page, 'confirm-order').click();
        await expect(page.getByRole('heading', { name: 'Заказ подтверждён', exact: true })).toBeVisible();
      } else if (preset === 'purchase-diff') {
        await expect(page.getByRole('heading', { name: 'Заказ создан с отличиями' })).toBeVisible();
        await expect(page.getByText(/Выбрано:.*место 12A.*В заказе:.*место 12C/)).toBeVisible();
        await expect(action(page, 'confirm-order')).toHaveCount(0);
        await page.getByRole('button', { name: 'Получить условия отмены' }).click();
        await expect(page.getByText('Возврат поставщика агентству', { exact: true })).toBeVisible();
        await page.getByLabel('Согласен на отмену всего заказа на указанных условиях возврата.').check();
        await action(page, 'consent-cancellation').click();
        await expect(page.getByRole('heading', { name: 'Заказ отменён', exact: true })).toBeVisible();
      } else {
        await expect(page.getByText(/Исход создания заказа не доказан/)).toBeVisible();
        await expect(action(page, 'confirm-order')).toHaveCount(0);
        await expect(page.getByRole('button', { name: 'Получить условия отмены' })).toHaveCount(0);
        await page.reload();
        await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
        await expect(page.getByText(/Исход создания заказа не доказан/)).toBeVisible();
      }
      expect(writes.filter((p) => p.endsWith('/hold'))).toHaveLength(1);
      expect(writes.filter((p) => p.endsWith('/confirm'))).toHaveLength(preset === 'purchase-success' ? 1 : 0);
      const storage = await page.evaluate(() =>
        JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }),
      );
      expect(storage).not.toMatch(/fictional@example|Traveler|quoteRevision|Idempotency|ase_|seat_/);
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
}
