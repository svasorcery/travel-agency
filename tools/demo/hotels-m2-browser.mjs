import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

const output = new URL('../../TestResults/hotels-m2/browser/', import.meta.url);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const errors = [];
const posts = [];
const quoteResources = [];
const consoleMessages = [];
const requestUrls = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (message) => consoleMessages.push(message.text()));
page.on('request', (r) => {
  requestUrls.push(r.url());
  if (r.method() === 'POST' && r.url().endsWith('/purchase'))
    posts.push({ url: r.url(), adults: r.postDataJSON().party.guests.length });
  if (r.method() === 'PUT' && r.url().endsWith('/quote')) quoteResources.push(r.url());
});
const stats = async () => (await fetch('http://127.0.0.1:5111/__owned-fixture/stats')).json();
async function choose(source) {
  const card = page.locator('section[aria-labelledby="rates-title"] article').filter({ hasText: source });
  await page.locator('section[aria-labelledby="rates-title"] article').first().waitFor();
  if (!(await card.count()))
    throw new Error(
      JSON.stringify({
        expectedSource: source,
        visibleRates: await page.locator('section[aria-labelledby="rates-title"]').innerText(),
      }),
    );
  await card.getByRole('button', { name: 'Проверить актуальные условия', exact: true }).click();
  await page.getByLabel(/^Имя гостя\s*1$/).waitFor();
  await page.getByText(`Поставщик: ${source}`, { exact: true }).waitFor();
}
async function scenario(source, adults, { lost = false, unknown = false, replace = false } = {}) {
  await fetch('http://127.0.0.1:5111/__owned-fixture/scenario', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ next: unknown ? 'unknown' : 'confirmed' }),
  });
  const before = await stats();
  const count = posts.length;
  await page.goto('http://127.0.0.1:4202/hotels');
  await page.locator('select[name="area"]').selectOption('fictional');
  await page
    .locator('select[name="adults"]')
    .selectOption({ label: `${adults} ${adults === 1 ? 'взрослый' : 'взрослых'}` });
  await page.getByRole('button', { name: 'Найти проживание', exact: true }).click();
  await page.locator('[data-testid="hotel-property-group"]').waitFor();
  assert.equal(await page.locator('[data-testid="hotel-property-group"]').count(), 1);
  if (source === 'Hotelbeds' && adults === 1 && replace)
    await page.screenshot({
      path: new URL('grouped-search.png', output).pathname.replace(/^\/(\w:)/, '$1'),
      fullPage: true,
    });
  await page.getByRole('button', { name: 'Выбрать номер и тариф', exact: true }).click();
  const quoteBefore = quoteResources.length;
  await choose(source);
  if (replace) {
    await page.locator('input[name="consent"]').check();
    await choose(source === 'Hotelbeds' ? 'Duffel Stays' : 'Hotelbeds');
    assert.equal(await page.locator('input[name="consent"]').isChecked(), false);
    await choose(source);
    assert.equal(new Set(quoteResources.slice(quoteBefore)).size, 1);
  }
  for (let i = 0; i < adults; i++) {
    await page.getByLabel(new RegExp(`^Имя гостя\\s*${i + 1}$`)).fill('Fictional');
    await page.getByRole('textbox', { name: 'Фамилия', exact: true }).nth(i).fill('Traveller');
  }
  await page.locator('input[name="email"]').fill('fictional@example.invalid');
  await page.locator('input[name="phone"]').fill('+44000000000');
  for (const name of ['adultsConfirmed', 'checkInConfirmed', 'consent'])
    await page.locator(`input[name="${name}"]`).check();
  if (lost)
    await page.route(
      '**/api/hotels/reservations/*/purchase',
      async (route) => {
        await route.fetch();
        await route.abort('failed');
      },
      { times: 1 },
    );
  const submit = page.getByRole('button', { name: 'Забронировать в тестовом режиме', exact: true });
  await page.waitForFunction(() => document.querySelector('.reservation button[type="submit"]')?.disabled === false);
  if (!(await submit.isEnabled()))
    throw new Error(
      JSON.stringify({
        submitDisabled: true,
        controls: await page.locator('.reservation input').evaluateAll((inputs) =>
          inputs.map((input) => ({
            name: input.name,
            type: input.type,
            valid: input.checkValidity(),
            checked: input.checked,
            length: input.value.length,
          })),
        ),
      }),
    );
  await submit.press('Enter');
  await page
    .getByRole('heading', { name: unknown ? 'Результат пока неизвестен' : 'Бронь подтверждена', exact: true })
    .waitFor();
  await page.reload();
  await page
    .getByRole('heading', { name: unknown ? 'Результат пока неизвестен' : 'Бронь подтверждена', exact: true })
    .waitFor();
  assert.equal(posts.length, count + 1);
  const after = await stats();
  const key = source === 'Hotelbeds' ? 'hotelbedsCreates' : 'duffelCreates';
  const other = source === 'Hotelbeds' ? 'duffelCreates' : 'hotelbedsCreates';
  assert.equal(after[key] - before[key], 1);
  assert.equal(after[other], before[other]);
  assert.equal(after.database, false);
  assert.match(await page.locator('.reservation').innerText(), /Сумма поставщику за всё проживание/);
  assert.match(await page.locator('.reservation').innerText(), new RegExp(`Поставщик: ${source}`));
  const storage = await page.evaluate(() =>
    JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage }, cookie: document.cookie }),
  );
  assert.doesNotMatch(storage, /fictional@example|Traveller|givenName|rateKey|owned-hbx/);
  await page.screenshot({
    path: new URL(
      `${source === 'Hotelbeds' ? 'hbx' : 'duffel'}-${adults}-${unknown ? 'unknown' : lost ? 'lost' : 'confirmed'}.png`,
      output,
    ).pathname.replace(/^\/(\w:)/, '$1'),
    fullPage: true,
  });
  return {
    source,
    adults,
    lostReply: lost,
    unknownWithoutReference: unknown,
    sourceReplacementSameResource: replace,
    selectedCreates: 1,
    otherCreates: 0,
  };
}
try {
  const receipts = [];
  receipts.push(await scenario('Hotelbeds', 1, { replace: true }));
  receipts.push(await scenario('Hotelbeds', 2, { lost: true }));
  receipts.push(await scenario('Hotelbeds', 1, { unknown: true }));
  await page.setViewportSize({ width: 390, height: 844 });
  receipts.push(await scenario('Duffel Stays', 1));
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).waitFor();
  assert.equal(await page.locator('.reservation').count(), 0);
  assert.deepEqual(errors, []);
  const privatePattern = /fictional@example\.invalid|Traveller|givenName|rateKey|owned-hbx/;
  assert.equal(
    consoleMessages.some((message) => privatePattern.test(message)),
    false,
    'Private fixture data appeared in the browser console.',
  );
  assert.equal(
    requestUrls.some((url) => privatePattern.test(decodeURIComponent(url))),
    false,
    'Private fixture data appeared in a request URL.',
  );
  const ssr = await (await page.request.get('http://127.0.0.1:4202/hotels')).text();
  assert.equal(
    /fictional@example\.invalid|Traveller|owned-hbx/.test(ssr),
    false,
    'Private fixture data appeared in SSR HTML.',
  );
  await writeFile(
    new URL('receipt.json', output),
    JSON.stringify(
      {
        surface: 'local headless Microsoft Edge',
        fixture:
          'explicit m2-memory Kestrel, actual Hotels endpoint methods/Core/Application/two adapters, in-memory CAS/AES, owned HttpMessageHandlers; no DB/migrations/real Host/supplier network',
        receipts,
        purchasePosts: posts.length,
        pageErrors: errors,
        keyboardSubmit: true,
        mobileWidth: 390,
        logoutClearedPrivateView: true,
        privacy: {
          consoleEntries: consoleMessages.length,
          urlsExamined: requestUrls.length,
          consolePrivateData: false,
          urlPrivateData: false,
          ssrPrivateData: false,
          storagePrivateData: false,
        },
      },
      null,
      2,
    ),
  );
  console.log(
    JSON.stringify({
      passed: receipts.length,
      purchasePosts: posts.length,
      pageErrors: errors.length,
      database: false,
      supplierNetwork: false,
    }),
  );
} catch (error) {
  console.error(
    JSON.stringify({
      currentUrl: page.url(),
      posts: posts.length,
      message: await page
        .locator('[aria-live="polite"]')
        .innerText()
        .catch(() => ''),
      headings: await page.getByRole('heading').allTextContents(),
    }),
  );
  throw error;
} finally {
  await context.close();
  await browser.close();
}
