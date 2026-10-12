import { expect, test } from '@playwright/test';
import { isFictionalApiRequest } from './fictional-route-guard';

test('explicit fictional organized re-quote shows rejected-plan reason and keeps deterministic facts', async ({
  page,
}) => {
  const writes: { path: string; body: Record<string, unknown> }[] = [];
  await page.route('**/*', async (route) => {
    const request = route.request(),
      url = new URL(request.url());
    if (
      url.origin !== 'http://127.0.0.1:4201' ||
      (url.pathname.startsWith('/api/') && !isFictionalApiRequest(request.url(), request.method(), request.headers()))
    )
      throw new Error('Unexpected non-demo route');
    if (url.pathname.startsWith('/api/') && request.method() !== 'GET')
      writes.push({ path: url.pathname, body: request.postDataJSON() });
    await route.continue();
  });
  await page.goto('/flights/travelers');
  await page.locator('[data-action="traveler-login"]').click();
  await expect(page.locator('[data-action="traveler-create"]')).toBeEnabled();
  await page.getByRole('link', { name: 'Поиск перелётов', exact: true }).click();
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.locator('#flight-departure').fill('2037-08-20');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await page.locator('[data-action="quote"]').first().click();
  const mode = page.getByLabel('Подача условий при следующем обновлении цены');
  await expect(mode).toBeVisible();
  const before = writes.length;
  await mode.selectOption('organized');
  expect(writes.length).toBe(before);
  await page.locator('[data-action="requote"]').click();
  const summary = page.locator('app-flight-fare-summary');
  await expect(summary.locator('[data-fare-ai-status]')).toHaveText(
    'Результат группировки с AI не прошёл проверку. Показаны структурированные условия.',
  );
  await expect(summary).toContainText('сбор 0.00 RUB');
  expect(writes.filter((write) => write.path.endsWith('/quote'))).toHaveLength(2);
  expect(writes.at(-1)?.body['explanationMode']).toBe('organized');
  const after = writes.length;
  await summary.locator('summary').click();
  expect(writes.length).toBe(after);
  expect(writes.some((write) => /hold|confirm|cancel|refund/.test(write.path))).toBe(false);
});

test('fictional fare details disclose exact scoped facts with no explanation requests or financial actions', async ({
  page,
}) => {
  const requests: string[] = [];
  await page.route('**/*', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (
      url.origin !== 'http://127.0.0.1:4201' ||
      (url.pathname.startsWith('/api/') && !isFictionalApiRequest(request.url(), request.method(), request.headers()))
    )
      throw new Error('Unexpected non-demo route');
    if (url.pathname.startsWith('/api/')) requests.push(`${request.method()} ${url.pathname}`);
    await route.continue();
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/flights');
  await page.locator('input[value="multiLeg"]').check();
  const rows = page.getByTestId('flight-leg-row');
  await expect(rows).toHaveCount(2);
  for (const count of [3, 4]) {
    await page.locator('[data-action="add-leg"]').click();
    await expect(rows).toHaveCount(count);
  }
  const legs = [
    ['LED', 'DME', '2037-07-21'],
    ['VKO', 'KZN', '2037-07-23'],
    ['KZN', 'SVO', '2037-07-25'],
    ['SVO', 'LED', '2037-07-28'],
  ];
  for (const [leg, values] of legs.entries())
    for (const [field, value] of ['origin', 'destination', 'departureDate'].map((field, index) => [
      field,
      values[index],
    ]))
      await page.locator(`#flight-leg-${leg}-${field}`).fill(value);
  await page.locator('#flight-passenger-count').selectOption('9');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await page.locator('[data-action="quote"]').first().click();
  const summary = page.locator('app-flight-fare-summary');
  await expect(summary).toContainText('сбор 0.00 RUB');
  await expect(summary).toContainText('Возврат до вылета: не предоставлено; сбор не предоставлен');
  const before = requests.length;
  const disclosure = summary.locator('summary');
  await disclosure.focus();
  await page.keyboard.press('Enter');
  await expect(summary).toContainText('Fictional Basic');
  await expect(summary).toContainText('Участок 4');
  await expect(summary).toContainText('взрослые 1, 3, 5, 7, 9');
  expect(requests.length).toBe(before);
  expect(requests.some((r) => /hold|confirm|cancel|refund/.test(r))).toBe(false);
  expect(await summary.evaluate((el) => el.scrollWidth <= el.clientWidth)).toBe(true);
});
