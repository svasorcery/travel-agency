import { randomUUID } from 'node:crypto';
import { expect, type Page, test } from '@playwright/test';
import { fetchFictionalApi, isFictionalApiRequest } from './fictional-route-guard';

type Leg = { origin: string; destination: string; departureDate: string };
const action = (page: Page, value: string) => page.locator(`[data-action="${value}"]`);
const passengerRows = (page: Page) => page.locator('fieldset[data-passenger-id]');
const openJaw = (day: number): Leg[] => [
  { origin: 'LED', destination: 'DME', departureDate: `2030-09-${String(day).padStart(2, '0')}` },
  { origin: 'VKO', destination: 'LED', departureDate: `2030-09-${String(day + 7).padStart(2, '0')}` },
];
const fourLeg = (day: number): Leg[] => [
  openJaw(day)[0],
  { origin: 'VKO', destination: 'KZN', departureDate: `2030-09-${String(day + 2).padStart(2, '0')}` },
  { origin: 'KZN', destination: 'SVO', departureDate: `2030-09-${String(day + 4).padStart(2, '0')}` },
  { origin: 'SVO', destination: 'LED', departureDate: `2030-09-${String(day + 7).padStart(2, '0')}` },
];
const profileDetails = (index: number) => ({
  title: index ? 'ms' : 'mr',
  givenName: index ? 'FictionBeta' : 'FictionAlpha',
  familyName: 'DemoTraveler',
  dateOfBirth: '1990-04-12',
  gender: index ? 'female' : 'male',
  email: `multileg${index}@example.test`,
  phone: `+7900123456${index}`,
});
async function isolate(page: Page) {
  await page.route('**/*', async (route) => {
    const request = route.request(),
      url = new URL(request.url());
    if (
      url.origin !== 'http://127.0.0.1:4201' ||
      url.pathname.startsWith('/events/') ||
      (url.pathname.startsWith('/api/') && !isFictionalApiRequest(request.url(), request.method(), request.headers()))
    ) {
      await route.abort('blockedbyclient');
      throw new Error('Unexpected route outside the fictional multi-leg boundary');
    }
    expect(request.headers().authorization).toBeUndefined();
    await route.continue();
  });
}
async function login(page: Page) {
  await page.goto('/flights/travelers');
  await action(page, 'traveler-login').click();
  await expect(action(page, 'traveler-create')).toBeEnabled();
  await page.getByRole('link', { name: 'Поиск перелётов', exact: true }).click();
}
async function criteria(page: Page, legs: Leg[]) {
  await page.locator('input[value="multiLeg"]').check();
  const rows = page.getByTestId('flight-leg-row');
  await expect(rows.nth(1)).toBeVisible();
  while ((await rows.count()) < legs.length) {
    const count = await rows.count();
    await action(page, 'add-leg').click();
    await expect(rows).toHaveCount(count + 1);
  }
  for (const [index, leg] of legs.entries())
    for (const [field, value] of Object.entries(leg)) await page.locator(`#flight-leg-${index}-${field}`).fill(value);
  await page.locator('#flight-passenger-count').selectOption('2');
}
async function search(page: Page, legs: Leg[]) {
  await criteria(page, legs);
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101', { exact: false }).first()).toBeVisible();
  await expect(action(page, 'quote')).toHaveCount(1);
}
async function checkout(page: Page, legs: Leg[]) {
  await search(page, legs);
  await action(page, 'quote').click();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await expect(page.getByText(/Цена и маршрут обновлены после входа/)).toBeVisible();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await expect(passengerRows(page)).toHaveCount(2);
}
async function fillManualParty(page: Page) {
  for (const index of [0, 1])
    for (const [field, value] of Object.entries(profileDetails(index))) {
      const control = passengerRows(page).nth(index).locator(`[formControlName="${field}"]`);
      if (field === 'title' || field === 'gender') await control.selectOption(value);
      else await control.fill(value);
    }
}
async function privacy(page: Page, messages: string[]) {
  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, history: history.state }),
  );
  for (const sentinel of ['FictionAlpha', 'FictionBeta', 'DemoTraveler', 'multileg0@example.test', '+79001234560']) {
    expect(persisted).not.toContain(sentinel);
    expect(page.url()).not.toContain(sentinel);
    expect(messages.join('\n')).not.toContain(sentinel);
  }
  expect(persisted).not.toMatch(/quoteRevision|bookingPassengerId|off_fixture_|Idempotency-Key/);
}
for (const [name, legs] of [
  ['open-jaw', openJaw(1)],
  ['four-leg', fourLeg(2)],
] as const) {
  test(`two saved fictional adults complete ${name} as one order and reopen every local-time leg`, async ({ page }) => {
    await isolate(page);
    const messages: string[] = [],
      writes: { path: string; body: { aggregateId: string; passengers: { bookingPassengerId: string }[] } }[] = [];
    page.on('console', (message) => messages.push(message.text()));
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (request.method() === 'POST' && ['/api/flights/orders/hold', '/api/flights/orders/confirm'].includes(path))
        writes.push({ path, body: request.postDataJSON() });
    });
    const profileIds: string[] = [];
    for (const index of [0, 1]) {
      const id = randomUUID();
      const response = await page.request.put(`/api/flights/travelers/${id}`, {
        data: profileDetails(index),
        headers: { 'If-None-Match': '*' },
      });
      expect(response.status()).toBe(201);
      profileIds.push(id);
    }
    let sent: unknown;
    await page.route('**/api/flights/search/v2?currency=RUB', async (route) => {
      sent = route.request().postDataJSON();
      const response = await fetchFictionalApi(route);
      expect(response.ok()).toBe(true);
      await route.fulfill({ response });
    });
    await login(page);
    await checkout(page, [...legs]);
    expect(sent).toEqual({ legs, passengerCount: 2, cabinClass: 'economy' });
    await expect(page.getByText(/Самостоятельное перемещение DME → VKO не входит в билет/).first()).toBeVisible();
    await action(page, 'traveler-load').click();
    const ids = await passengerRows(page).evaluateAll((rows) =>
      rows.map((row) => row.getAttribute('data-passenger-id')),
    );
    for (const index of [0, 1]) {
      const row = passengerRows(page).nth(index);
      await row.locator('[data-action="traveler-select"]').selectOption(profileIds[index]);
      await row.locator('[data-action="traveler-fill"]').click();
      await expect(row.locator('[formControlName="givenName"]')).toHaveValue(profileDetails(index).givenName);
    }
    expect(
      await passengerRows(page).evaluateAll((rows) => rows.map((row) => row.getAttribute('data-passenger-id'))),
    ).toEqual(ids);
    await passengerRows(page).first().locator('[formControlName="title"]').focus();
    await page.keyboard.press('Tab');
    await expect(passengerRows(page).first().locator('[formControlName="givenName"]')).toBeFocused();
    await page.screenshot({ path: `test-results/m25-${name}-desktop.png`, fullPage: true });
    await page.setViewportSize({ width: 360, height: 780 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
    await page.screenshot({ path: `test-results/m25-${name}-mobile.png`, fullPage: true });
    const before = await (await page.request.get('/api/flights/orders?limit=200')).json();
    await action(page, 'hold').click();
    await expect(page.getByText('Предложение удержано', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(1);
    const id = writes[0].body.aggregateId;
    expect(writes[0].body.passengers.map((p) => p.bookingPassengerId)).toEqual(ids);
    await action(page, 'confirm').click();
    await expect(page.getByRole('heading', { name: 'Билет выписан' })).toBeVisible({ timeout: 15_000 });
    expect(writes.map((write) => write.path)).toEqual(['/api/flights/orders/hold', '/api/flights/orders/confirm']);
    const after = await (await page.request.get('/api/flights/orders?limit=200')).json();
    expect(after.items.length).toBe(before.items.length + 1);
    const order = after.items.find((item: { aggregateId: string }) => item.aggregateId === id);
    expect(order.passengerCount).toBe(2);
    expect(order.itinerary.journeyKind).toBe('multi-leg');
    expect(order.itinerary.slices.map((l: Leg) => [l.origin, l.destination])).toEqual(
      legs.map((l) => [l.origin, l.destination]),
    );
    await page.getByRole('link', { name: 'К списку заказов' }).click();
    const row = page.locator(`[data-order-id="${id}"]`);
    await expect(row).toBeVisible();
    for (const leg of legs) await expect(row).toContainText(`${leg.origin} → ${leg.destination}`);
    await expect(row).toContainText('Самостоятельное перемещение DME → VKO не входит в билет');
    await expect(row).toContainText('UTC+03:00');
    await row.locator(`[data-order-link="${id}"]`).click();
    for (const index of legs.keys()) await expect(page.getByText(`SU${101 + index}`, { exact: false })).toBeVisible();
    await expect(page.getByText(/Самостоятельное перемещение DME → VKO не входит в билет/)).toBeVisible();
    await expect(page.getByText(/UTC\+03:00/).first()).toBeVisible();
    await page.screenshot({ path: `test-results/m25-${name}-order-mobile.png`, fullPage: true });
    await privacy(page, messages);
  });
}
for (const change of ['route', 'cabin'] as const) {
  test(`fourth-leg ${change}-only quote refresh keeps price but needs visible fresh acceptance`, async ({ page }) => {
    await isolate(page);
    await login(page);
    await checkout(page, fourLeg(change === 'route' ? 4 : 5));
    await fillManualParty(page);
    let oldAmount = 0,
      newAmount = 0;
    await page.route('**/api/flights/orders/quote', async (route) => {
      const response = await fetchFictionalApi(route),
        quote = await response.json();
      oldAmount = quote.offer.totalAmount;
      if (change === 'route') quote.offer.itinerary.slices[3].segments[0].flightNumber = 'SU909';
      else quote.offer.itinerary.slices[3].segments[0].cabinClass = 'business';
      newAmount = quote.offer.totalAmount;
      await route.fulfill({ response, json: quote });
    });
    await action(page, 'refresh-quote').click();
    await expect(action(page, 'accept-quote')).toBeVisible();
    expect(newAmount).toBe(oldAmount);
    expect(newAmount).toBeGreaterThan(0);
    await expect(action(page, 'hold')).toBeDisabled();
    await expect(action(page, 'start-booking')).toHaveCount(0);
    await expect(page.getByRole('heading', { name: 'Маршрут в поиске' })).toBeVisible();
    await expect(page.locator('app-flight-quote-panel')).toContainText(change === 'route' ? 'SU909' : 'Бизнес');
    await action(page, 'accept-quote').click();
    await expect(passengerRows(page)).toHaveCount(2);
    await fillManualParty(page);
    await expect(action(page, 'hold')).toBeEnabled();
  });
}
test('lost multi-leg hold survives a new four-leg search and blocks a second write', async ({ page }) => {
  await isolate(page);
  await login(page);
  await checkout(page, openJaw(6));
  await fillManualParty(page);
  let id = '',
    writes = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/orders/hold') {
      id = request.postDataJSON().aggregateId;
      writes++;
    }
  });
  let finished!: () => void;
  const serverEffect = new Promise<void>((resolve) => {
    finished = resolve;
  });
  await page.route('**/api/flights/orders/hold', async (route) => {
    try {
      const response = await fetchFictionalApi(route);
      expect(response.ok()).toBe(true);
      await route.abort('failed');
    } finally {
      finished();
    }
  });
  await action(page, 'hold').click();
  await expect(page.getByText('Исход удержания неизвестен.', { exact: false })).toBeVisible();
  await serverEffect;
  const order = await (await page.request.get(`/api/flights/orders/${id}`)).json();
  expect(order.itinerary.slices).toHaveLength(2);
  await page.getByRole('link', { name: 'Мои заказы' }).click();
  await page.locator(`[data-order-link="${id}"]`).click();
  await expect(action(page, 'confirm-order')).toHaveCount(0);
  await page.getByRole('link', { name: 'Новый поиск' }).click();
  await search(page, fourLeg(7));
  await action(page, 'quote').click();
  await expect(
    page.getByText('Исход предыдущей операции бронирования требует проверки.', { exact: false }),
  ).toBeVisible();
  expect(writes).toBe(1);
  await expect(action(page, 'hold')).toHaveCount(0);
  await privacy(page, []);
});
test('multi-leg numbered rows have bounded add/remove and keyboard-labelled exact-airport errors', async ({ page }) => {
  await isolate(page);
  await page.goto('/flights');
  await criteria(page, fourLeg(8));
  await expect(page.getByTestId('flight-leg-row')).toHaveCount(4);
  await expect(action(page, 'add-leg')).toBeDisabled();
  await page.locator('#flight-leg-3-origin').focus();
  await page.keyboard.press('Tab');
  await expect(page.locator('#flight-leg-3-destination')).toBeFocused();
  let searches = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/search/v2') searches++;
  });
  await page.locator('#flight-leg-3-destination').fill('LED1');
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(
    page
      .getByTestId('flight-leg-row')
      .nth(3)
      .getByText(/трёх латинских букв/),
  ).toBeVisible();
  expect(searches).toBe(0);
  await page.getByTestId('flight-leg-row').nth(3).locator('[data-action="remove-leg"]').click();
  await expect(page.getByTestId('flight-leg-row')).toHaveCount(3);
  await page.getByTestId('flight-leg-row').nth(2).locator('[data-action="remove-leg"]').click();
  await expect(page.getByTestId('flight-leg-row')).toHaveCount(2);
  await page.setViewportSize({ width: 360, height: 780 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  await page.screenshot({ path: 'test-results/m25-form-mobile.png', fullPage: true });
});
