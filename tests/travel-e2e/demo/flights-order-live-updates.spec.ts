import type { Server } from 'node:http';
import { expect, test } from '@playwright/test';
import { isFictionalApiRequest } from './fictional-route-guard';

for (const preset of ['order-live-ticket', 'order-live-report', 'order-live-refund'] as const) {
  test(`${preset} updates an open page from SSE and keeps financial facts separate`, async ({ page }) => {
    // eslint-disable-next-line @nx/enforce-module-boundaries
    const { createDemoServer } = await import('../../../tools/demo/flights-search-api.mjs');
    // eslint-disable-next-line @nx/enforce-module-boundaries
    const { fictionalOrderLiveId } = await import('../../../tools/demo/flights-order-events.mjs');
    const server = createDemoServer({ orderEventsPreset: preset, orderEventsDelayMs: 1500 }) as Server;
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('Fictional listener unavailable');
    const origin = `http://127.0.0.1:${address.port}`;
    const id = fictionalOrderLiveId(preset);
    const writes: string[] = [];
    let streams = 0;
    let failRefund = false;
    await page.route('**/*', async (route) => {
      const request = route.request();
      const url = new URL(request.url());
      if (
        url.origin !== 'http://127.0.0.1:4201' ||
        url.username ||
        url.password ||
        Object.keys(request.headers()).some((name) => name.toLowerCase() === 'authorization')
      )
        throw new Error('Unexpected fictional request');
      if (url.pathname.startsWith('/events/')) {
        if (request.method() !== 'GET' || url.search !== '' || url.pathname !== `/events/flights/orders/${id}`)
          throw new Error('Unexpected fictional stream');
        streams++;
        await route.continue({ url: origin + url.pathname });
        return;
      }
      if (!url.pathname.startsWith('/api/')) {
        await route.continue();
        return;
      }
      if (!isFictionalApiRequest(request.url(), request.method(), request.headers()))
        throw new Error('Fictional guard refused request');
      if (request.method() !== 'GET') writes.push(url.pathname);
      if (failRefund && url.pathname.endsWith('/customer-refund')) {
        await route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{}' });
        return;
      }
      const response = await fetch(origin + url.pathname + url.search, {
        method: request.method(),
        redirect: 'error',
        headers: { 'Content-Type': 'application/json' },
        body: request.postData() ?? undefined,
      });
      await route.fulfill({
        status: response.status,
        contentType: response.headers.get('Content-Type') ?? 'application/json',
        body: Buffer.from(await response.arrayBuffer()),
      });
    });
    try {
      if (preset === 'order-live-report') await page.clock.install();
      await page.goto(`/flights/orders/${id}`);
      await page.getByRole('button', { name: 'Демо вход' }).click();
      if (preset === 'order-live-ticket') {
        await expect(page.getByRole('heading', { name: 'Заказ подтверждён', exact: true })).toBeVisible();
        await expect(page.getByText('DEMO-TKT-LIVE', { exact: true })).toBeVisible();
      } else if (preset === 'order-live-report') {
        await expect(page.getByText('DEMO-TKT-LIVE', { exact: true })).toBeVisible();
        // The already-ticketed page must still observe a report beyond the old 190s window.
        await page.clock.fastForward(195_000);
        await expect(page.getByText('Фактический исход требует проверки', { exact: false })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Получить условия отмены' })).toHaveCount(0);
      } else {
        await expect(page.getByText('Демо: возврат отправлен', { exact: true })).toBeVisible();
        await expect(page.getByText('Зачисление клиенту не подтверждено.', { exact: false })).toBeVisible();
        failRefund = true;
        await page.getByRole('button', { name: 'Обновить состояние возврата' }).click();
        await expect(page.getByText('Сейчас не удалось прочитать состояние возврата.', { exact: false })).toBeVisible();
        await expect(page.getByText('Демо: возврат отправлен', { exact: true })).toBeVisible();
        failRefund = false;
      }
      expect(streams).toBeGreaterThan(0);
      await page.reload();
      await page.getByRole('button', { name: 'Демо вход' }).click();
      if (preset === 'order-live-ticket') await expect(page.getByText('DEMO-TKT-LIVE', { exact: true })).toBeVisible();
      if (preset === 'order-live-report')
        await expect(page.getByText('Фактический исход требует проверки', { exact: false })).toBeVisible();
      if (preset === 'order-live-refund')
        await expect(page.getByText('Демо: возврат отправлен', { exact: true })).toBeVisible();
      expect(writes).toEqual([]);
    } finally {
      await page.goto('about:blank');
      await new Promise<void>((resolve) => server.close(() => resolve()));
    }
  });
}

test('offline gap restores a missed fictional ticket through GET without replay', async ({ page, context }) => {
  // eslint-disable-next-line @nx/enforce-module-boundaries
  const { createDemoServer } = await import('../../../tools/demo/flights-search-api.mjs');
  // eslint-disable-next-line @nx/enforce-module-boundaries
  const { fictionalOrderLiveId } = await import('../../../tools/demo/flights-order-events.mjs');
  const server = createDemoServer({ orderEventsPreset: 'order-live-ticket', orderEventsDelayMs: 2000 }) as Server;
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Fictional listener unavailable');
  const origin = `http://127.0.0.1:${address.port}`,
    id = fictionalOrderLiveId('order-live-ticket');
  let streams = 0,
    writes = 0;
  await page.route('**/events/**', async (route) => {
    streams++;
    await route.continue({ url: origin + new URL(route.request().url()).pathname });
  });
  await page.route('**/api/**', async (route) => {
    if (!isFictionalApiRequest(route.request().url(), route.request().method(), route.request().headers()))
      throw new Error('Fictional guard refused request');
    if (route.request().method() !== 'GET') writes++;
    const response = await fetch(origin + new URL(route.request().url()).pathname);
    await route.fulfill({
      status: response.status,
      contentType: 'application/json',
      body: Buffer.from(await response.arrayBuffer()),
    });
  });
  try {
    await page.goto(`/flights/orders/${id}`);
    await page.getByRole('button', { name: 'Демо вход' }).click();
    await expect.poll(() => streams).toBeGreaterThan(0);
    await expect(page.getByRole('heading', { name: 'Заказ подтверждён', exact: true })).toBeVisible();
    await context.setOffline(true);
    await new Promise((resolve) => setTimeout(resolve, 2300));
    expect((await (await fetch(origin + `/api/flights/orders/${id}`)).json()).status).toBe('Ticketed');
    await context.setOffline(false);
    await expect(page.getByText('DEMO-TKT-LIVE', { exact: true })).toBeVisible();
    expect(writes).toBe(0);
  } finally {
    await context.setOffline(false);
    await page.goto('about:blank');
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
});
