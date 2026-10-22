import { expect, type Page, test } from '@playwright/test';
import type { RailSuggestions } from '@travel/api-client';

async function stationFixture(page: Page): Promise<RailSuggestions> {
  // Read authored test data through the demo's existing loopback HTTP boundary.
  const response = await page.request.get('/api/rail/stations?q=%D0%A3%D1%87%D0%B5%D0%B1%D0%BD%D0%B0%D1%8F');
  expect(response.ok()).toBe(true);
  return response.json();
}

async function select(page: Page) {
  await expect(page.locator('meta[data-travel-frame-policy]')).toHaveAttribute('content', /frame-src 'self'/);
  await page.getByLabel('Откуда', { exact: true }).fill('Север');
  await expect(page.getByRole('option', { name: /Учебная Северная/ })).toBeVisible();
  await page.getByLabel('Откуда', { exact: true }).press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.getByLabel('Куда', { exact: true }).fill('Южная');
  await page.getByRole('option', { name: /Учебная Южная/ }).click();
  await page.getByLabel('Дата отправления', { exact: true }).fill(new Date().toISOString().slice(0, 10));
}
test.beforeEach(async ({ page }) => {
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== 'http://127.0.0.1:4201') throw new Error(`External fictional Rail destination ${url.origin}`);
    const allowedRail =
      (route.request().method() === 'GET' && url.pathname === '/api/rail/stations') ||
      (route.request().method() === 'POST' && ['/api/rail/search', '/api/rail/journey-details'].includes(url.pathname));
    if (
      url.pathname.startsWith('/api/rail/') &&
      Object.keys(route.request().headers()).some((key) => key.toLowerCase() === 'authorization')
    )
      throw new Error('Rail browser must not send provider/user authorization.');
    if (
      url.pathname.startsWith('/api/') &&
      !allowedRail &&
      !(route.request().method() === 'GET' && url.pathname === '/api/flights/travelers')
    )
      throw new Error(`Unexpected Rail request ${url.pathname}`);
    await route.continue();
  });
  await page.request.post('/api/demo/rail/preset', { data: { preset: 'normal' } });
});
test('detected local frame CSP refusal clears source facts and makes no hidden search', async ({ page }) => {
  let searches = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/rail/search') searches++;
  });
  await page.goto('/rail');
  await page.evaluate(() => {
    const policy = document.createElement('meta');
    policy.httpEquiv = 'Content-Security-Policy';
    policy.content = "frame-src 'none'";
    document.head.append(policy);
  });
  await page.getByLabel('Откуда', { exact: true }).fill('Север');
  await expect(
    page.getByText('Не удалось показать сведения об источнике. Данные очищены; повторите ввод вручную.'),
  ).toBeVisible();
  await expect(page.locator('iframe')).toHaveCount(0);
  await expect(page.locator('[role="listbox"] [role="option"]')).toHaveCount(0);
  expect(searches).toBe(0);
});
test('cancelled first A cannot replace the newest A or the independent destination field', async ({ page }) => {
  let first: import('@playwright/test').Route | null = null;
  let calls = 0;
  await page.route('**/api/rail/stations?**', async (route) => {
    calls++;
    const q = new URL(route.request().url()).searchParams.get('q');
    if (q === 'Учебная' && first === null) {
      first = route;
      return;
    }
    const body = await stationFixture(page);
    body.suggestions = body.suggestions.filter(
      (x: { reference: { code: string } }) => x.reference.code === (q === 'Южная' ? 's2' : 's1'),
    );
    if (q === 'Учебная') body.suggestions[0].name = 'Новая Северная';
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: { 'Cache-Control': 'no-store' },
      body: JSON.stringify(body),
    });
  });
  await page.goto('/rail');
  const origin = page.getByLabel('Откуда', { exact: true });
  await origin.fill('Учебная');
  await expect.poll(() => calls).toBe(1);
  await page.getByLabel('Куда', { exact: true }).fill('Южная');
  await expect(page.getByRole('option', { name: /Учебная Южная/ })).toBeVisible();
  await origin.fill('Временная');
  await expect.poll(() => calls).toBe(3);
  await origin.fill('Учебная');
  await expect(page.getByRole('option', { name: /Новая Северная/ })).toBeVisible();
  const stale = await stationFixture(page);
  stale.suggestions[0].name = 'Лишняя старая';
  try {
    await first!.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(stale) });
  } catch {
    /* Browser may already have aborted the old route. */
  }
  await expect(page.getByRole('option', { name: /Лишняя старая/ })).toHaveCount(0);
  await expect(page.getByRole('option', { name: /Новая Северная/ })).toBeVisible();
  await expect(page.getByRole('option', { name: /Учебная Южная/ })).toBeVisible();
});
test('own Rail flow preserves exact fare, unknown inventory, sandbox and document policy on client navigation', async ({
  page,
}, testInfo) => {
  await page.goto('/flights');
  await page.getByRole('link', { name: 'Поезда и электрички', exact: true }).click();
  const navigationWidths = [];
  for (const width of [320, 360]) {
    await page.setViewportSize({ width, height: 780 });
    const bounds = await page.evaluate(() => {
      const box = (selector: string) => {
        const element = document.querySelector(selector);
        if (!element) throw new Error(`Missing navigation element: ${selector}`);
        const { left, right, width, height } = element.getBoundingClientRect();
        return { left, right, width, height, scrollWidth: element.scrollWidth };
      };
      return {
        viewport: window.innerWidth,
        document: document.documentElement.scrollWidth,
        header: box('.app-header'),
        nav: box('.app-header nav'),
        brand: box('.app-header__brand'),
        links: box('.app-header__links'),
        items: [...document.querySelectorAll('.app-header a')].map((link) => {
          const { left, right, width, height } = link.getBoundingClientRect();
          return { text: link.textContent, left, right, width, height };
        }),
      };
    });
    navigationWidths.push(bounds);
    await page.screenshot({ path: testInfo.outputPath(`rail-navigation-${width}.png`) });
  }
  await testInfo.attach('navigation-widths.json', {
    body: JSON.stringify(navigationWidths, null, 2),
    contentType: 'application/json',
  });
  for (const bounds of navigationWidths) {
    expect(bounds.document).toBeLessThanOrEqual(bounds.viewport);
    for (const link of bounds.items) {
      expect(link.left).toBeGreaterThanOrEqual(0);
      expect(link.right).toBeLessThanOrEqual(bounds.viewport);
      expect(link.width).toBeGreaterThan(0);
      expect(link.height).toBeGreaterThan(0);
    }
  }
  for (const width of [320, 360]) {
    await page.setViewportSize({ width, height: 780 });
    const links = await page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link').all();
    await links[0].focus();
    for (const link of links) {
      await expect(link).toBeVisible();
      await expect(link).toBeFocused();
      await page.keyboard.press('Tab');
    }
  }
  await page.setViewportSize({ width: 1280, height: 720 });
  await select(page);
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(page.getByTestId('rail-price').first()).toHaveText('9007199254740993.05 RUB');
  await expect(page.getByTestId('rail-fictional')).toBeVisible();
  await expect(page.locator('iframe')).toHaveAttribute('sandbox', '');
  await expect(page.locator('iframe')).toHaveAttribute('referrerpolicy', 'no-referrer');
  await expect(page.locator('iframe')).toHaveAttribute('src', '/rail-fictional-banner.html');
  await expect(page.frameLocator('iframe').getByText('RAIL · Учебная демонстрация', { exact: true })).toBeVisible();
  await expect(
    page.frameLocator('iframe').getByText('Вымышленные данные. Не официальный баннер Яндекса.', { exact: true }),
  ).toBeVisible();
  await expect(page.locator('meta[data-travel-frame-policy]')).toHaveAttribute(
    'content',
    /frame-src 'self' https:\/\/yandex.st\/rasp\/media\/apicc\/copyright_vert_mono.html/,
  );
  await page.locator('iframe').scrollIntoViewIfNeeded();
  await expect(page.locator('iframe')).toBeInViewport();
  await expect(page.frameLocator('iframe').getByText('RAIL · Учебная демонстрация', { exact: true })).toBeInViewport();
  await page.locator('iframe').screenshot({ path: testInfo.outputPath('rail-banner-desktop.png') });
  await page.screenshot({ path: testInfo.outputPath('rail-attribution-desktop.png') });
  await page.screenshot({ path: testInfo.outputPath('rail-search-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.locator('iframe').scrollIntoViewIfNeeded();
  await expect(page.locator('iframe')).toBeInViewport();
  await expect(page.frameLocator('iframe').getByText('RAIL · Учебная демонстрация', { exact: true })).toBeInViewport();
  await page.locator('iframe').screenshot({ path: testInfo.outputPath('rail-banner-mobile.png') });
  await page.screenshot({ path: testInfo.outputPath('rail-attribution-mobile.png') });
  await page.screenshot({ path: testInfo.outputPath('rail-search-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1280, height: 720 });
  const frameRequest = page.waitForRequest((r) => r.url().endsWith('/rail-fictional-banner.html'));
  await page.reload();
  await select(page);
  const frame = await frameRequest;
  expect(frame.headers()['referer']).toBeUndefined();
});
for (const [preset, text] of [
  ['empty', 'Поездок в выбранном расписании не найдено.'],
  ['interval', 'Получена ограниченная часть расписания'],
  ['failed', 'Не удалось получить расписание'],
  ['partial', 'Билетные сведения неполные'],
  ['stale', 'Каталог истёк'],
]) {
  test(`fictional ${preset} is a distinct outcome`, async ({ page }) => {
    await page.request.post('/api/demo/rail/preset', { data: { preset } });
    await page.goto('/rail');
    await select(page);
    await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
    await expect(page.getByText(text, { exact: false }).first()).toBeVisible();
  });
}
test('clock expiry removes source rows/prices without an automatic search', async ({ page }) => {
  await page.request.post('/api/demo/rail/preset', { data: { preset: 'result-expiry' } });
  await page.goto('/rail');
  await select(page);
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(page.getByTestId('rail-price').first()).toBeVisible();
  await expect(page.getByTestId('rail-price')).toHaveCount(0, { timeout: 5000 });
  await expect(page.getByText('Срок расписания и цен истёк. Обновите поиск вручную.')).toBeVisible();
});
test('local view controls use no HTTP and one explicit accessible route preserves exact fares on mobile', async ({
  page,
}, testInfo) => {
  let searches = 0;
  let details = 0;
  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;
    if (path === '/api/rail/search') searches++;
    if (path === '/api/rail/journey-details') details++;
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/rail');
  await select(page);
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(page.getByTestId('rail-price').first()).toBeVisible();
  const frame = await page.locator('iframe').elementHandle();
  await page.getByLabel('Порядок вариантов').selectOption('duration');
  await page.getByLabel('Время отправления', { exact: true }).selectOption('night');
  await expect(page.getByText('Среди полученных вариантов нет подходящих.', { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Сбросить фильтры' }).click();
  expect(searches).toBe(1);
  expect(details).toBe(0);
  const open = page.getByRole('button', { name: 'Открыть маршрут' }).first();
  await open.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Маршрут выбранной поездки' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Маршрут выбранной поездки' })).toBeFocused();
  await expect(page.getByRole('heading', { name: 'Маршрут выбранной поездки' })).toBeInViewport();
  await expect(page.getByText('Плановый маршрут получен.', { exact: true })).toBeVisible();
  await expect(page.locator('#rail-route li')).toHaveCount(3);
  await expect(page.getByText('Стоянка: 0 с', { exact: false })).toBeVisible();
  await expect(page.getByTestId('rail-price').first()).toHaveText('9007199254740993.05 RUB');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.locator('#rail-route').screenshot({ path: testInfo.outputPath('rail-route-mobile.png') });
  expect(await page.evaluate(() => localStorage.length + sessionStorage.length)).toBe(0);
  expect(details).toBe(1);
  expect(await frame!.evaluate((node) => node.isConnected)).toBe(true);
  await page.getByRole('button', { name: 'Закрыть маршрут' }).click();
  await expect(open).toBeFocused();
  await expect(page.locator('#rail-route')).toHaveCount(0);
});
for (const [preset, text] of [
  ['detail-partial', 'Маршрут содержит неполные сведения.'],
  ['detail-no-match', 'Выбранная поездка не подтверждена'],
  ['detail-recheck-failed', 'Не удалось получить маршрут.'],
  ['detail-route-failed', 'Не удалось получить маршрут.'],
  ['detail-quota', 'Подробности не запущены:'],
]) {
  test(`explicit route ${preset} keeps search data and source notices`, async ({ page }) => {
    await page.request.post('/api/demo/rail/preset', { data: { preset } });
    await page.goto('/rail');
    await select(page);
    await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
    await page.getByRole('button', { name: 'Открыть маршрут' }).first().click();
    await expect(page.getByText(text, { exact: false }).first()).toBeVisible();
    await expect(page.getByTestId('rail-price').first()).toHaveText('9007199254740993.05 RUB');
    await expect(page.getByTestId('rail-fictional')).toBeVisible();
    await expect(page.locator('iframe')).toHaveCount(1);
  });
}
