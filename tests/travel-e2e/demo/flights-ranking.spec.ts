import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import type { FlightRanking, FlightSearchResponse } from '@travel/api-client';

// Shared HTTP examples are test data, not an importable Nx source project.
const fixtures = JSON.parse(readFileSync(resolve(__dirname, '../../fixtures/flights-search.json'), 'utf8')) as {
  rankingFxFailure: { response: FlightSearchResponse & { ranking: FlightRanking } };
};

test('fictional ranking explains ties with a keyboard disclosure at desktop and mobile widths', async ({ page }) => {
  const unexpected: string[] = [];
  await page.route('**/*', async (route) => {
    if (new URL(route.request().url()).origin !== 'http://127.0.0.1:4201') {
      unexpected.push(route.request().url());
      await route.abort();
    } else await route.continue();
  });
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(
    page.getByText('Сначала цена; при равной цене — длительность и пересадки.', { exact: false }),
  ).toBeVisible();
  const partner = page.getByRole('article', { name: 'Предложение travelpayouts' });
  const disclosure = partner.locator('summary');
  await disclosure.focus();
  await page.keyboard.press('Enter');
  await expect(partner.getByText('Длительность неизвестна', { exact: false })).toBeVisible();
  await expect(partner.getByText('Пересадки неизвестны', { exact: false })).toBeVisible();
  await page.screenshot({ path: 'test-results/m2-ranking-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 360, height: 800 });
  await expect(disclosure).toBeFocused();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/m2-ranking-mobile.png', fullPage: true });
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({
    local: 0,
    session: 0,
  });
  expect(unexpected).toEqual([]);
});

test('FX failure is grouped honestly and malformed ranking cannot supply an explanation', async ({ page }) => {
  let malformed = false;
  await page.route('**/api/flights/search?*', async (route) => {
    const response = structuredClone(fixtures.rankingFxFailure.response);
    if (malformed) response.ranking.entries[0].offerId = 'different-offer';
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(response) });
  });
  await page.goto('/flights');
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByRole('heading', { name: 'Предложения в RUB' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Предложения в EUR' })).toBeVisible();
  const bookable = page.getByRole('article', { name: 'Предложение duffel' });
  await bookable.locator('summary').click();
  await expect(bookable.getByText('Курс недоступен: сравнение только внутри EUR.', { exact: false })).toBeVisible();
  malformed = true;
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.locator('app-flight-offer')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Повторить поиск' })).toBeVisible();
  malformed = false;
  await page.getByRole('button', { name: 'Повторить поиск' }).click();
  await expect(page.getByRole('heading', { name: 'Предложения в EUR' })).toBeVisible();
});
