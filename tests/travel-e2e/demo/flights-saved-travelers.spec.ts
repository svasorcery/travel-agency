import { randomUUID } from 'node:crypto';
import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

const details = {
  title: 'mr',
  givenName: 'SavedFiction',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'male',
  email: 'saved-fiction@example.test',
  phone: '+79001234567',
};
type Receipt = { id: string; revision: string };
const action = (page: Page, value: string) => page.locator(`[data-action="${value}"]`);
const profileRow = (page: Page, id: string) => page.locator(`[data-traveler-id="${id}"]`);
const passengerRows = (page: Page) => page.locator('fieldset[data-passenger-id]');
async function seed(request: APIRequestContext, values = details, owner = 'demo-only'): Promise<Receipt> {
  const id = randomUUID();
  const response = await request.put(`/api/flights/travelers/${id}`, {
    data: values,
    headers: { 'If-None-Match': '*', 'X-Travel-Demo-Owner': owner },
  });
  expect(response.status()).toBe(201);
  const receipt = await response.json();
  expect(response.headers()['etag']).toBe(`"${receipt.revision}"`);
  return receipt;
}
async function edit(request: APIRequestContext, receipt: Receipt, values = details) {
  const response = await request.put(`/api/flights/travelers/${receipt.id}`, {
    data: values,
    headers: { 'If-Match': `"${receipt.revision}"` },
  });
  expect(response.status()).toBe(200);
  return response.json() as Promise<Receipt>;
}
async function isolate(page: Page) {
  await page.route('**/*', async (route) => {
    const url = new URL(route.request().url());
    expect(url.origin).toBe('http://127.0.0.1:4201');
    expect(url.pathname.startsWith('/events/')).toBe(false);
    if (url.pathname.startsWith('/api/'))
      expect(url.pathname).toMatch(
        /^\/api\/flights\/(?:search|travelers(?:\/[0-9a-f-]{36})?|orders(?:\/(?:quote|hold|confirm|[0-9a-f-]{36}(?:\/cancel)?))?)$/i,
      );
    expect(route.request().headers()['authorization']).toBeUndefined();
    await route.continue();
  });
}
async function login(page: Page) {
  await page.goto('/flights/travelers');
  await action(page, 'traveler-login').click();
  await expect(action(page, 'traveler-create')).toBeEnabled();
}
async function fillProfile(page: Page, values = details) {
  for (const [field, value] of Object.entries(values)) {
    const control = page.locator(`#traveler-${field}`);
    if (field === 'title' || field === 'gender') await control.selectOption(value);
    else await control.fill(value);
  }
}
async function privacy(page: Page, messages: string[]) {
  const persisted = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, history: history.state }),
  );
  for (const sentinel of [details.givenName, details.email, details.phone, 'ManualFiction']) {
    expect(persisted).not.toContain(sentinel);
    expect(page.url()).not.toContain(sentinel);
    expect(messages.join('\n')).not.toContain(sentinel);
  }
}
async function checkout(page: Page, day: number, count = 2) {
  await page.getByRole('link', { name: 'Поиск перелётов', exact: true }).click();
  await page.getByRole('button', { name: 'Подставить пример' }).click();
  await page.locator('#flight-departure').fill(`2030-07-${String(day).padStart(2, '0')}`);
  await page.locator('#flight-passenger-count').selectOption(String(count));
  await page.getByRole('button', { name: /Найти рейсы/ }).click();
  await expect(page.getByText('SU101')).toBeVisible();
  await action(page, 'quote').click();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await expect(page.getByText(/Цена и маршрут обновлены после входа/)).toBeVisible();
  await action(page, 'accept-quote').click();
  await action(page, 'start-booking').click();
  await expect(passengerRows(page)).toHaveCount(count);
}

test('fictional profile CRUD has explicit owner isolation, keyboard labels, confirmation and mobile privacy', async ({
  page,
}) => {
  await isolate(page);
  const messages: string[] = [];
  page.on('console', (message) => messages.push(message.text()));
  await login(page);
  await action(page, 'traveler-create').click();
  await page.locator('#traveler-title').focus();
  await page.keyboard.press('Tab');
  await expect(page.getByLabel('Имя', { exact: true })).toBeFocused();
  await fillProfile(page);
  const createdResponse = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.status() === 201,
  );
  await action(page, 'traveler-save').click();
  const created: Receipt = await (await createdResponse).json();
  await expect(profileRow(page, created.id)).toContainText(details.givenName);
  await profileRow(page, created.id).locator('[data-action="traveler-edit"]').click();
  await page.locator('#traveler-givenName').fill('EditedFiction');
  await action(page, 'traveler-save').click();
  await expect(profileRow(page, created.id)).toContainText('EditedFiction');
  await action(page, 'traveler-demo-owner').selectOption('demo-other');
  await expect(profileRow(page, created.id)).toHaveCount(0);
  const foreign = await page.request.get(`/api/flights/travelers/${created.id}`, {
    headers: { 'X-Travel-Demo-Owner': 'demo-other' },
  });
  expect(foreign.status()).toBe(404);
  await action(page, 'traveler-demo-owner').selectOption('demo-only');
  await expect(profileRow(page, created.id)).toBeVisible();
  await page.setViewportSize({ width: 360, height: 780 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  await page.screenshot({ path: 'test-results/m24-profiles-mobile.png', fullPage: true });
  await profileRow(page, created.id).locator('[data-action="traveler-delete"]').click();
  await expect(action(page, 'traveler-delete-confirm')).toBeVisible();
  expect((await page.request.get(`/api/flights/travelers/${created.id}`)).status()).toBe(200);
  await action(page, 'traveler-delete-cancel').click();
  await expect(profileRow(page, created.id)).toBeVisible();
  await profileRow(page, created.id).locator('[data-action="traveler-delete"]').click();
  await action(page, 'traveler-delete-confirm').click();
  await expect(profileRow(page, created.id)).toHaveCount(0);
  expect((await page.request.get(`/api/flights/travelers/${created.id}`)).status()).toBe(404);
  await privacy(page, messages);
});

test('stale profile update requires a fresh explicit read and review before a new conditional write', async ({
  page,
}) => {
  await isolate(page);
  const created = await seed(page.request);
  await login(page);
  await profileRow(page, created.id).locator('[data-action="traveler-edit"]').click();
  await expect(page.locator('#traveler-givenName')).toHaveValue(details.givenName);
  const changed = await edit(page.request, created, { ...details, givenName: 'ConcurrentFiction' });
  await page.locator('#traveler-givenName').fill('StaleFiction');
  const refusal = page.waitForResponse(
    (response) => response.request().method() === 'PUT' && response.status() === 412,
  );
  await action(page, 'traveler-save').click();
  await refusal;
  await expect(action(page, 'traveler-save')).toBeDisabled();
  expect((await (await page.request.get(`/api/flights/travelers/${created.id}`)).json()).revision).toBe(
    changed.revision,
  );
  await action(page, 'traveler-review').click();
  await expect(page.locator('#traveler-givenName')).toHaveValue('ConcurrentFiction');
  await expect(action(page, 'traveler-save')).toBeDisabled();
  await action(page, 'traveler-accept-review').click();
  await page.locator('#traveler-givenName').fill('ReviewedFiction');
  await action(page, 'traveler-save').click();
  await expect(profileRow(page, created.id)).toContainText('ReviewedFiction');
});

for (const failure of ['lost-create', 'malformed-update', 'lost-delete'] as const) {
  test(`${failure} proves the server effect before hiding the receipt and never automatically retries`, async ({
    page,
  }) => {
    await isolate(page);
    const receipt = failure === 'lost-create' ? null : await seed(page.request);
    await login(page);
    let writes = 0;
    let affectedId = receipt?.id ?? '';
    let effectProven = false;
    await page.route('**/api/flights/travelers/*', async (route) => {
      const request = route.request();
      if (request.method() !== (failure === 'lost-delete' ? 'DELETE' : 'PUT')) return route.fallback();
      writes++;
      affectedId = new URL(request.url()).pathname.split('/').at(-1)!;
      const response = await route.fetch();
      expect(response.status()).toBe(failure === 'lost-create' ? 201 : failure === 'lost-delete' ? 204 : 200);
      const current = await page.request.get(`/api/flights/travelers/${affectedId}`);
      if (failure === 'lost-delete') expect(current.status()).toBe(404);
      else {
        expect(current.status()).toBe(200);
        const observed = await current.json();
        expect(observed.details.givenName).toBe(failure === 'lost-create' ? details.givenName : 'MalformedFiction');
        expect(observed.revision).toBe((await response.json()).revision);
      }
      effectProven = true;
      if (failure === 'malformed-update') await route.fulfill({ response, body: '{"id":"broken"}' });
      else await route.abort('failed');
    });
    if (failure === 'lost-create') {
      await action(page, 'traveler-create').click();
      await fillProfile(page);
      await action(page, 'traveler-save').click();
    } else if (failure === 'lost-delete') {
      await profileRow(page, receipt!.id).locator('[data-action="traveler-delete"]').click();
      await action(page, 'traveler-delete-confirm').click();
    } else {
      await profileRow(page, receipt!.id).locator('[data-action="traveler-edit"]').click();
      await page.locator('#traveler-givenName').fill('MalformedFiction');
      await action(page, 'traveler-save').click();
    }
    await expect(page.getByText(/Результат операции неизвестен/)).toBeVisible();
    expect(effectProven).toBe(true);
    expect(writes).toBe(1);
    await expect(action(page, 'traveler-create')).toBeDisabled();
    await action(page, 'traveler-review').click();
    await expect(action(page, 'traveler-accept-review')).toBeVisible();
    await expect(page.getByText(/Результат прежней операции остаётся неизвестным/)).toBeVisible();
    expect(writes).toBe(1);
    expect(affectedId).toMatch(/^[0-9a-f-]{36}$/);
  });
}

test('unavailable protected profile list clears PII and leaves ordinary booking available', async ({ page }) => {
  await isolate(page);
  await seed(page.request);
  await login(page);
  await expect(page.locator('[data-traveler-id]').first()).toBeVisible();
  await page.route('**/api/flights/travelers?offset=0', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ type: 'https://travel.local/errors/Flights.PiiProtectionUnavailable', status: 503 }),
    }),
  );
  await action(page, 'traveler-refresh').click();
  await expect(page.getByText(/Профили недоступны/)).toBeVisible();
  await expect(page.locator('[data-traveler-id]')).toHaveCount(0);
  await checkout(page, 21);
  await expect(passengerRows(page)).toHaveCount(2);
});

test('two profiles explicitly copy into quote-local slots; later edit/delete preserve drafts and held snapshot', async ({
  page,
}) => {
  await isolate(page);
  const first = await seed(page.request);
  const secondDetails = {
    ...details,
    title: 'ms',
    gender: 'female',
    givenName: 'SecondFiction',
    email: 'second-fiction@example.test',
  };
  const second = await seed(page.request, secondDetails);
  const messages: string[] = [];
  page.on('console', (message) => messages.push(message.text()));
  let held: { aggregateId: string; passengers: (typeof details & { bookingPassengerId: string })[] } | null = null;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/orders/hold') held = structuredClone(request.postDataJSON());
  });
  await login(page);
  await checkout(page, 22);
  await action(page, 'traveler-load').click();
  const rows = passengerRows(page);
  const ids = await rows.evaluateAll((elements) =>
    elements.map((element) => element.getAttribute('data-passenger-id')),
  );
  for (const [index, receipt, values] of [
    [0, first, details],
    [1, second, secondDetails],
  ] as const) {
    const row = rows.nth(index);
    await row.locator('[data-action="traveler-select"]').selectOption(receipt.id);
    await expect(row.locator('[formControlName="givenName"]')).toHaveValue('');
    await row.locator('[data-action="traveler-fill"]').click();
    await expect(row.locator('[formControlName="givenName"]')).toHaveValue(values.givenName);
    for (const [field, value] of Object.entries(values))
      await expect(row.locator(`[formControlName="${field}"]`)).toHaveValue(value);
  }
  expect(
    await rows.evaluateAll((elements) => elements.map((element) => element.getAttribute('data-passenger-id'))),
  ).toEqual(ids);
  await edit(page.request, first, { ...details, givenName: 'LaterFiction' });
  expect(
    (
      await page.request.delete(`/api/flights/travelers/${second.id}`, {
        headers: { 'If-Match': `"${second.revision}"` },
      })
    ).status(),
  ).toBe(204);
  await action(page, 'traveler-load').click();
  await expect(rows.nth(0).locator('[formControlName="givenName"]')).toHaveValue(details.givenName);
  await expect(rows.nth(1).locator('[formControlName="givenName"]')).toHaveValue(secondDetails.givenName);
  await page.screenshot({ path: 'test-results/m24-two-profile-desktop.png', fullPage: true });
  await page.setViewportSize({ width: 360, height: 780 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(360);
  await page.screenshot({ path: 'test-results/m24-two-profile-mobile.png', fullPage: true });
  await action(page, 'hold').click();
  await expect(page.getByText('Предложение удержано', { exact: true })).toBeVisible();
  const snapshot = held as {
    aggregateId: string;
    passengers: (typeof details & { bookingPassengerId: string })[];
  } | null;
  expect(snapshot).not.toBeNull();
  expect(snapshot!.passengers.map((passenger) => passenger.bookingPassengerId)).toEqual(ids);
  expect(snapshot!.passengers.map((passenger) => passenger.givenName)).toEqual([
    details.givenName,
    secondDetails.givenName,
  ]);
  const current = await (await page.request.get(`/api/flights/travelers/${first.id}`)).json();
  expect(
    (
      await page.request.delete(`/api/flights/travelers/${first.id}`, {
        headers: { 'If-Match': `"${current.revision}"` },
      })
    ).status(),
  ).toBe(204);
  const order = await (await page.request.get(`/api/flights/orders/${snapshot!.aggregateId}`)).json();
  expect(order.status).toBe('Held');
  expect(order.passengerCount).toBe(2);
  expect(order.totalAmount).toBe(21420);
  expect(snapshot!.passengers.map((passenger) => passenger.givenName)).toEqual([
    details.givenName,
    secondDetails.givenName,
  ]);
  await expect(action(page, 'traveler-fill')).toHaveCount(0);
  await privacy(page, messages);
});

test('future-adult profile is saved from an explicit row action while booking still blocks a departure minor', async ({
  page,
}) => {
  await isolate(page);
  const futureAdult = await seed(page.request, { ...details, dateOfBirth: '2020-01-01' });
  await login(page);
  await checkout(page, 23, 1);
  await action(page, 'traveler-load').click();
  const row = passengerRows(page).first();
  await row.locator('[data-action="traveler-select"]').selectOption(futureAdult.id);
  await row.locator('[data-action="traveler-fill"]').click();
  await expect(row.locator('[formControlName="dateOfBirth"]')).toHaveValue('2020-01-01');
  const saved = page.waitForResponse((response) => response.request().method() === 'PUT' && response.status() === 201);
  await row.locator('[data-action="traveler-save-row"]').click();
  const receipt = await (await saved).json();
  expect(receipt.id).not.toBe(futureAdult.id);
  expect((await (await page.request.get(`/api/flights/travelers/${receipt.id}`)).json()).details.dateOfBirth).toBe(
    '2020-01-01',
  );
  let holdCount = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/flights/orders/hold') holdCount++;
  });
  await action(page, 'hold').click();
  await expect(row.locator('[formControlName="dateOfBirth"]')).toHaveAttribute('aria-invalid', 'true');
  expect(holdCount).toBe(0);
});

test('late profile fill cannot replace a newer manual edit; authentication refusal clears profile PII', async ({
  page,
}) => {
  await isolate(page);
  const receipt = await seed(page.request);
  await login(page);
  await checkout(page, 24, 1);
  await action(page, 'traveler-load').click();
  const row = passengerRows(page).first();
  await row.locator('[data-action="traveler-select"]').selectOption(receipt.id);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let started!: () => void;
  const incoming = new Promise<void>((resolve) => (started = resolve));
  await page.route(`**/api/flights/travelers/${receipt.id}`, async (route) => {
    const response = await route.fetch();
    started();
    await gate;
    await route.fulfill({ response });
  });
  await row.locator('[data-action="traveler-fill"]').click();
  await incoming;
  await row.locator('[formControlName="givenName"]').fill('ManualFiction');
  release();
  await expect(row.locator('[data-action="traveler-fill"]')).toBeEnabled();
  await expect(row.locator('[formControlName="givenName"]')).toHaveValue('ManualFiction');
  await page.unroute(`**/api/flights/travelers/${receipt.id}`);
  await page.route(`**/api/flights/travelers/${receipt.id}`, (route) =>
    route.fulfill({ status: 401, contentType: 'application/problem+json', body: '{"status":401}' }),
  );
  await row.locator('[data-action="traveler-fill"]').click();
  await expect(page.getByText(/Войдите снова/)).toBeVisible();
  await expect(row.locator('[data-action="traveler-select"] option')).toHaveCount(1);
  await expect(row.locator('[formControlName="givenName"]')).toHaveValue('');
});

for (const boundary of ['quote-slots', 'logout', 'navigation'] as const) {
  test(`a delayed fictional profile read cannot refill after ${boundary} changes`, async ({ page }) => {
    await isolate(page);
    const receipt = await seed(page.request);
    await login(page);
    await checkout(page, boundary === 'quote-slots' ? 25 : boundary === 'logout' ? 26 : 27, 1);
    await action(page, 'traveler-load').click();
    const row = passengerRows(page).first();
    await row.locator('[data-action="traveler-select"]').selectOption(receipt.id);
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    let started!: () => void;
    const incoming = new Promise<void>((resolve) => (started = resolve));
    await page.route(`**/api/flights/travelers/${receipt.id}`, async (route) => {
      const response = await route.fetch();
      started();
      await gate;
      await route.fulfill({ response });
    });
    await row.locator('[data-action="traveler-fill"]').click();
    await incoming;
    if (boundary === 'quote-slots') {
      await page.route('**/api/flights/orders/quote', async (route) => {
        const response = await route.fetch();
        const quote = await response.json();
        quote.binding.slots[0].bookingPassengerId = randomUUID();
        await route.fulfill({ response, json: quote });
      });
      const quoted = page.waitForResponse((response) => new URL(response.url()).pathname.endsWith('/quote'));
      await action(page, 'refresh-quote').click();
      await quoted;
      await expect(action(page, 'hold')).toBeDisabled();
    } else if (boundary === 'logout') {
      // The fictional build intentionally has no logout button. Exercise the existing handler
      // through Angular's development debug API; this is lifecycle proof, not production UX proof.
      await page.evaluate(() => {
        const element = document.querySelector('app-flights-page');
        const debug = (window as unknown as { ng: { getComponent(element: Element): { logout(): void } } }).ng;
        if (element === null || debug === undefined) throw new Error('Fictional development component unavailable');
        debug.getComponent(element).logout();
      });
    } else await page.getByRole('link', { name: 'Мои заказы', exact: true }).first().click();
    release();
    if (boundary === 'quote-slots') {
      await expect(passengerRows(page).first().locator('[formControlName="givenName"]')).toHaveValue('');
      await expect(passengerRows(page).first().locator('[data-action="traveler-select"]')).toHaveValue('');
    } else if (boundary === 'logout') {
      await expect(action(page, 'logout')).toHaveCount(0);
      await expect(page.locator('[formControlName="givenName"]')).toHaveCount(0);
    } else {
      await expect(page).toHaveURL(/\/flights\/orders$/);
      await expect(page.locator('[formControlName="givenName"]')).toHaveCount(0);
    }
    await expect(page.getByText(details.givenName, { exact: true })).toHaveCount(0);
    await privacy(page, []);
  });
}

test('changing the fictional owner invalidates a pending profile editor read and erases editor PII', async ({
  page,
}) => {
  await isolate(page);
  const receipt = await seed(page.request);
  await login(page);
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let started!: () => void;
  const incoming = new Promise<void>((resolve) => (started = resolve));
  await page.route(`**/api/flights/travelers/${receipt.id}`, async (route) => {
    const response = await route.fetch();
    started();
    await gate;
    await route.fulfill({ response });
  });
  await profileRow(page, receipt.id).locator('[data-action="traveler-edit"]').click();
  await incoming;
  await action(page, 'traveler-demo-owner').selectOption('demo-other');
  release();
  await expect(profileRow(page, receipt.id)).toHaveCount(0);
  await expect(page.locator('#traveler-givenName')).toHaveCount(0);
  await privacy(page, []);
});
