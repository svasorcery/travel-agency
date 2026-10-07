import { expect, type Page, test } from '@playwright/test';
import { fetchFictionalApi, isFictionalApiRequest } from './fictional-route-guard';

type PartyQuote = {
  offer: { totalAmount: number };
  aggregateId: string;
  binding: { revision: string; slots: { bookingPassengerId: string; kind: 'adult' }[] };
};
const rows = (page: Page) => page.locator('fieldset[data-passenger-id]');
async function isolate(page: Page) {
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    const knownApi = isFictionalApiRequest(route.request().url(), route.request().method(), route.request().headers());
    if (
      url.origin !== 'http://127.0.0.1:4201' ||
      url.pathname.startsWith('/events/') ||
      (url.pathname.startsWith('/api/') && !knownApi)
    )
      throw new Error('Unexpected non-demo route');
    expect(route.request().headers().authorization).toBeUndefined();
    await route.continue();
  });
}
async function search(page: Page, count: number, roundTrip = false) {
  await page.goto('/flights');
  if (roundTrip) await page.getByLabel('Туда и обратно').check();
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  // Each case uses its own aggregate in the shared in-memory server.
  const title = test.info().title;
  const day = title.startsWith('2 fictional')
    ? 10
    : title.startsWith('9 fictional')
      ? 11
      : title.startsWith('same IDs')
        ? 12
        : title.startsWith('underage')
          ? 13
          : title.startsWith('unsupported')
            ? 14
            : title.includes('group hold')
              ? 15
              : 16;
  await page.locator('#flight-departure').fill(`2030-06-${day}`);
  if (roundTrip) await page.locator('#flight-return').fill(`2030-06-${day + 7}`);
  await page.locator('#flight-passenger-count').selectOption(String(count));
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
}
async function checkout(page: Page, count: number, roundTrip = false) {
  await search(page, count, roundTrip);
  await page.locator('[data-action="quote"]').click();
  await page.locator('[data-action="accept-quote"]').click();
  await page.locator('[data-action="start-booking"]').click();
  await expect(page.getByText(/Цена и маршрут обновлены после входа/)).toBeVisible();
  await page.locator('[data-action="accept-quote"]').click();
  await page.locator('[data-action="start-booking"]').click();
  await expect(rows(page)).toHaveCount(count);
}
async function fillParty(page: Page, count: number) {
  for (let i = 0; i < count; i++) {
    const row = rows(page).nth(i);
    await row.locator('[formControlName="title"]').selectOption(['mr', 'ms', 'mrs', 'miss', 'dr'][i % 5]);
    await row.locator('[formControlName="givenName"]').fill(`Demo${String.fromCharCode(65 + i)}`);
    await row.locator('[formControlName="familyName"]').fill('Fictional');
    await row.locator('[formControlName="dateOfBirth"]').fill('1990-04-12');
    await row.locator('[formControlName="gender"]').selectOption(i % 2 ? 'female' : 'male');
    await row.locator('[formControlName="email"]').fill(`group${i}@example.test`);
    await row.locator('[formControlName="phone"]').fill(`+7900123456${i}`);
  }
}
async function privacy(page: Page, messages: string[]) {
  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, history: history.state }),
  );
  for (const sentinel of ['DemoA', 'Fictional', 'group0@example.test', '+79001234560']) {
    expect(persisted).not.toContain(sentinel);
    expect(page.url()).not.toContain(sentinel);
    expect(messages.join('\n')).not.toContain(sentinel);
  }
  expect(persisted).not.toMatch(/quoteRevision|bookingPassengerId|off_fixture_|Idempotency-Key/);
}
for (const [count, roundTrip, searchTotal, quoteTotal] of [
  [2, false, 20750, 21420],
  [9, true, 172900, 178480],
] as const) {
  test(`${count} fictional adults ${roundTrip ? 'return' : 'one-way'} use one whole-party hold and group total`, async ({
    page,
  }) => {
    await isolate(page);
    const messages: string[] = [],
      holds: {
        body: { quoteRevision: string; passengers: { bookingPassengerId: string; title: string; gender: string }[] };
        key: string | undefined;
      }[] = [];
    let latest: PartyQuote | null = null;
    let searchedTotal = 0;
    page.on('console', (m) => messages.push(m.text()));
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/flights/orders/hold')
        holds.push({ body: request.postDataJSON(), key: request.headers()['idempotency-key'] });
    });
    // Read the real fictional server response before delivering it to the browser.
    // Detached response listeners can lose a body when Angular consumes a subsequent quote.
    await page.route('**/api/flights/search*', async (route) => {
      expect(route.request().headers().authorization).toBeUndefined();
      const response = await fetchFictionalApi(route);
      expect(response.ok()).toBe(true);
      searchedTotal = (await response.json()).offers[0].totalAmount;
      await route.fulfill({ response });
    });
    await page.route('**/api/flights/orders/quote', async (route) => {
      expect(route.request().headers().authorization).toBeUndefined();
      const response = await fetchFictionalApi(route);
      expect(response.ok()).toBe(true);
      latest = await response.json();
      await route.fulfill({ response });
    });
    await checkout(page, count, roundTrip);
    await expect(page.getByRole('article', { name: 'Предложение travelpayouts' })).toHaveCount(0);
    await expect(page.getByText(/Некоторые источники не поддерживают/)).toBeVisible();
    expect(searchedTotal).toBe(searchTotal);
    expect((latest as PartyQuote | null)?.offer.totalAmount).toBe(quoteTotal);
    await fillParty(page, count);
    const ids = await rows(page).evaluateAll((elements) => elements.map((e) => e.getAttribute('data-passenger-id')));
    expect(new Set(ids).size).toBe(count);
    const controlIds = await rows(page)
      .locator('input[formControlName],select[formControlName]')
      .evaluateAll((elements) => elements.map((e) => e.id));
    expect(new Set(controlIds).size).toBe(count * 7);
    for (let i = 0; i < count; i++)
      await expect(rows(page).nth(i).locator('legend')).toHaveText(`Пассажир ${i + 1} · взрослый`);
    await rows(page).first().locator('[formControlName="title"]').focus();
    await page.keyboard.press('Tab');
    await expect(rows(page).first().locator('[formControlName="givenName"]')).toBeFocused();
    await page.screenshot({ path: `test-results/m23b-${count}-desktop.png`, fullPage: true });
    await page.setViewportSize({ width: 360, height: 780 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
    await page.screenshot({ path: `test-results/m23b-${count}-mobile.png`, fullPage: true });
    await page.locator('[data-action="hold"]').click();
    await expect(page.getByRole('heading', { name: 'Заказ удержан', exact: true })).toBeVisible();
    expect(holds).toHaveLength(1);
    expect(holds[0].body.quoteRevision).toBe((latest as PartyQuote | null)?.binding.revision);
    expect(holds[0].body.passengers.map((p) => p.bookingPassengerId)).toEqual(ids);
    expect(holds[0].body.passengers.map((p) => p.title)).toEqual(
      Array.from({ length: count }, (_, i) => ['mr', 'ms', 'mrs', 'miss', 'dr'][i % 5]),
    );
    expect(holds[0].body.passengers.map((p) => p.gender)).toEqual(
      Array.from({ length: count }, (_, i) => (i % 2 ? 'female' : 'male')),
    );
    const observed = await (
      await page.request.get(`/api/flights/orders/${(latest as PartyQuote | null)?.aggregateId}`)
    ).json();
    expect(observed.passengerCount).toBe(count);
    expect(observed.totalAmount).toBe(quoteTotal);
    expect(observed.ticketNumbers).toEqual([]);
    await page.locator('[data-action="confirm-order"]').click();
    await expect(page.getByRole('heading', { name: 'Билет выписан' })).toBeVisible({ timeout: 15000 });
    await privacy(page, messages);
    const orders = await (await page.request.get('/api/flights/orders')).json();
    expect(
      orders.items.find(
        (item: { aggregateId: string }) => item.aggregateId === (latest as PartyQuote | null)?.aggregateId,
      ).passengerCount,
    ).toBe(count);
  });
}

test('same IDs preserve each draft after requote; new revision needs acceptance; changed IDs and route clear entries', async ({
  page,
}) => {
  await isolate(page);
  await checkout(page, 2);
  await fillParty(page, 2);
  const originalIds = await rows(page).evaluateAll((elements) =>
    elements.map((e) => e.getAttribute('data-passenger-id')),
  );
  const response = page.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/quote'));
  await page.locator('[data-action="refresh-quote"]').click();
  await response;
  await expect(page.locator('[data-action="hold"]')).toBeDisabled();
  await expect(rows(page).nth(0).locator('[formControlName="givenName"]')).toHaveValue('DemoA');
  await expect(rows(page).nth(1).locator('[formControlName="givenName"]')).toHaveValue('DemoB');
  expect(await rows(page).evaluateAll((elements) => elements.map((e) => e.getAttribute('data-passenger-id')))).toEqual(
    originalIds,
  );
  await page.locator('[data-action="accept-quote"]').click();
  await expect(page.locator('[data-action="hold"]')).toBeEnabled();
  await page.route('**/api/flights/orders/quote', async (route) => {
    const response = await fetchFictionalApi(route),
      q = await response.json();
    q.binding.slots = q.binding.slots.map((s: { kind: string }, i: number) => ({
      ...s,
      bookingPassengerId: `22222222-2222-4222-8222-${String(i + 1).padStart(12, '0')}`,
    }));
    q.offer.itinerary.slices[0].segments[0].flightNumber = 'SU199';
    await route.fulfill({ response, json: q });
  });
  await page.locator('[data-action="refresh-quote"]').click();
  await expect(rows(page).nth(0).locator('[formControlName="givenName"]')).toHaveValue('');
  await expect(rows(page).nth(1).locator('[formControlName="email"]')).toHaveValue('');
  await expect(page.locator('[data-action="hold"]')).toBeDisabled();
  await expect(page.getByText('SU199').last()).toBeVisible();
});

test('underage second adult is focused locally with zero hold requests and blank title/gender stay explicit', async ({
  page,
}) => {
  await isolate(page);
  let writes = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.endsWith('/hold')) writes++;
  });
  await checkout(page, 2);
  await expect(rows(page).nth(1).locator('[formControlName="title"]')).toHaveValue('');
  await expect(rows(page).nth(1).locator('[formControlName="gender"]')).toHaveValue('');
  await fillParty(page, 2);
  const birth = rows(page).nth(1).locator('[formControlName="dateOfBirth"]');
  await birth.fill('2020-01-01');
  await page.locator('[data-action="hold"]').click();
  await expect(birth).toBeFocused();
  await expect(birth).toHaveAttribute('aria-invalid', 'true');
  expect(writes).toBe(0);
});

test('unsupported capability is honest; a mismatched quote count fails closed', async ({ page }) => {
  await isolate(page);
  await page.route('**/api/flights/search*', async (route) => {
    const response = await fetchFictionalApi(route),
      result = await response.json();
    result.offers[0].holdEligible = false;
    result.offers[0].holdIneligibilityReason = 'identity-documents-required';
    await route.fulfill({ response, json: result });
  });
  await search(page, 2);
  await expect(page.locator('[data-action="quote"]')).toBeDisabled();
  await expect(page.getByText(/нужны документы/)).toBeVisible();
  await page.unroute('**/api/flights/search*');
  await search(page, 2);
  await page.route('**/api/flights/orders/quote', async (route) => {
    const response = await fetchFictionalApi(route),
      q = await response.json();
    q.binding.passengerCount = 1;
    await route.fulfill({ response, json: q });
  });
  await page.locator('[data-action="quote"]').click();
  await expect(page.locator('[data-action="start-booking"]')).toHaveCount(0);
  await expect(rows(page)).toHaveCount(0);
  await expect(page.locator('app-flight-quote-panel [role="alert"]')).toBeVisible();
});

for (const operation of ['hold', 'confirm'] as const) {
  test(`lost group ${operation} response recovers only from authoritative persisted facts`, async ({ page }) => {
    await isolate(page);
    await checkout(page, 2);
    await fillParty(page, 2);
    const writes: string[] = [];
    let id = '';
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (request.method() === 'POST' && ['/api/flights/orders/hold', '/api/flights/orders/confirm'].includes(path)) {
        writes.push(path);
        id = request.postDataJSON().aggregateId;
      }
    });
    await page.route(`**/api/flights/orders/${operation}`, async (route) => {
      const response = await fetchFictionalApi(route);
      expect(response.status()).toBe(200);
      await route.abort('failed');
    });
    await page.locator('[data-action="hold"]').click();
    if (operation === 'confirm') {
      await expect(page.getByRole('heading', { name: 'Заказ удержан', exact: true })).toBeVisible();
      await page.locator('[data-action="confirm-order"]').click();
      await expect(page.getByText('Исход подтверждения неизвестен.', { exact: false })).toBeVisible();
    } else {
      await expect(page.getByRole('heading', { name: 'Заказ удержан', exact: true })).toBeVisible();
      await expect(page.locator('[data-action="confirm-order"]')).toBeVisible();
      await page.reload();
      await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
      await expect(page.locator('[data-action="confirm-order"]')).toBeVisible();
    }
    await expect(page).toHaveURL(new RegExp(`/flights/orders/${id}$`));
    if (operation === 'confirm') await expect(page.locator('[data-action="confirm-order"]')).toHaveCount(0);
    expect(writes.filter((p) => p.endsWith('/hold'))).toHaveLength(1);
    if (operation === 'hold') {
      await privacy(page, []);
      return;
    }
    await page.getByRole('link', { name: 'Новый поиск' }).click();
    await page.getByRole('button', { name: 'Подставить пример' }).click();
    await page.getByRole('button', { name: /Найти рейсы/ }).click();
    await page.locator('[data-action="quote"]').click();
    await expect(
      page.getByText('Исход предыдущей операции бронирования требует проверки.', { exact: false }),
    ).toBeVisible();
    expect(writes.filter((p) => p.endsWith('/hold'))).toHaveLength(1);
    expect(writes.filter((p) => p.endsWith('/confirm'))).toHaveLength(operation === 'confirm' ? 1 : 0);
    await privacy(page, []);
    await page.reload();
    await expect(rows(page)).toHaveCount(0);
    await expect(page.locator('app-flight-quote-panel')).toHaveCount(0);
  });
}
