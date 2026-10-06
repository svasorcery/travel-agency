import { expect, type Page, test } from '@playwright/test';

async function isolate(page: Page) {
  await page.route('**/*', (route) => {
    if (new URL(route.request().url()).origin !== 'http://127.0.0.1:4201')
      throw new Error('Unexpected external request');
    return route.continue();
  });
}
async function seed(page: Page, day: number) {
  const quote = await page.request.post('/api/flights/orders/quote', {
    data: {
      provider: 'duffel',
      providerOfferRef: 'off_fixture_ow_2035-06-' + String(day).padStart(2, '0') + '_p2',
      passengerCount: 2,
    },
  });
  expect(quote.ok()).toBeTruthy();
  const { aggregateId, binding } = await quote.json();
  const hold = await page.request.post('/api/flights/orders/hold', {
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    data: {
      aggregateId,
      quoteRevision: binding.revision,
      passengers: binding.slots.map((slot: { bookingPassengerId: string }, index: number) => ({
        bookingPassengerId: slot.bookingPassengerId,
        title: 'mr',
        givenName: index === 0 ? 'Fictional' : 'Demo',
        familyName: 'Traveler',
        dateOfBirth: '1990-01-01',
        gender: 'male',
        email: 'fictional' + index + '@example.test',
        phone: '+79001234567',
      })),
    },
  });
  expect(hold.ok()).toBeTruthy();
  return aggregateId as string;
}
async function enter(page: Page, id: string) {
  await page.goto('/flights/orders/' + id);
  await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Получить условия отмены' })).toBeVisible();
}
test('whole-party exact consent works at360px with keyboard', async ({ page }) => {
  await isolate(page);
  const id = await seed(page, 11);
  await enter(page, id);
  await page.setViewportSize({ width: 360, height: 780 });
  await page.getByRole('button', { name: 'Получить условия отмены' }).click();
  await expect(page.getByRole('heading', { name: 'Проверьте условия всего заказа' })).toBeFocused();
  await expect(page.getByText('17.25 USD', { exact: true })).toBeVisible();
  await expect(page.getByText('Выплата клиенту оформляется отдельно.', { exact: false })).toBeVisible();
  await expect(page.getByText('Отмена всех перелётов и всех взрослых пассажиров: 2.')).toBeVisible();
  const consent = page.getByRole('button', { name: 'Согласовать и отменить' });
  await expect(consent).toBeDisabled();
  await page.screenshot({ path: 'test-results/flights-m3-terms-mobile.png', fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  const checkbox = page.getByRole('checkbox');
  await checkbox.focus();
  await checkbox.press('Space');
  await expect(consent).toBeEnabled();
  await consent.click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён', exact: true })).toBeVisible();
  await expect(page.getByText('Отмена подтверждена поставщиком.', { exact: true })).toBeVisible();
});
test('lost consent restores saved result after reload without resend', async ({ page }) => {
  await isolate(page);
  const id = await seed(page, 12);
  let writes = 0;
  await page.route('**/api/flights/cancellations/consent', async (route) => {
    writes++;
    await route.fetch();
    await route.abort('failed');
  });
  await enter(page, id);
  await page.getByRole('button', { name: 'Получить условия отмены' }).click();
  await page.getByRole('checkbox').check();
  await page.getByRole('button', { name: 'Согласовать и отменить' }).click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён', exact: true })).toBeVisible();
  expect(writes).toBe(1);
  await page.reload();
  await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Заказ отменён', exact: true })).toBeVisible();
  expect(writes).toBe(1);
  expect(
    await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } })),
  ).not.toMatch(/termsHash|operationId|fictional@|Traveler|Bearer|Idempotency/i);
});
