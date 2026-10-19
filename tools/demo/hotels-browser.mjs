import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

if (process.argv.includes('--m2-memory')) {
  await import('./hotels-m2-browser.mjs');
  process.exit(0);
}

const output = new URL('../../TestResults/hotels-m1/browser/', import.meta.url);
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
const failures = [];
page.on('pageerror', (error) => failures.push(error.message));
const purchases = [];
page.on('request', (request) => {
  if (request.method() === 'POST' && request.url().endsWith('/purchase')) {
    const body = request.postDataJSON();
    assert.equal(body.party.email, 'fictional@example.invalid');
    assert.equal(body.party.guests[0].givenName, 'Fictional');
    purchases.push({ guests: body.party.guests.length, consent: body.consent });
  }
});
const receipts = [];
async function purchase(adults, lostReply = false) {
  await page.goto('http://127.0.0.1:4202/hotels');
  await page
    .locator('select[name="adults"]')
    .selectOption({ label: `${adults} ${adults === 1 ? 'взрослый' : 'взрослых'}` });
  await page.getByRole('button', { name: 'Найти проживание', exact: true }).click();
  await page.getByRole('button', { name: 'Выбрать номер и тариф' }).click();
  await page.getByRole('button', { name: 'Проверить актуальные условия' }).click();
  await page.getByLabel(/^Имя гостя\s*1$/).waitFor();
  assert.equal(await page.getByRole('textbox', { name: /^Имя гостя/ }).count(), adults);
  await page.getByLabel(/^Имя гостя\s*1$/).fill('FictionalDraft');
  await page.getByLabel('Email основного гостя', { exact: true }).fill('fictional@example.invalid');
  await page.locator('input[name="consent"]').check();
  await page.getByRole('button', { name: 'Обновить условия', exact: true }).click();
  await page.getByRole('alert').filter({ hasText: 'Условия изменились' }).waitFor();
  assert.equal(await page.getByLabel(/^Имя гостя\s*1$/).inputValue(), '');
  assert.equal(await page.getByLabel('Email основного гостя', { exact: true }).inputValue(), '');
  assert.equal(await page.locator('input[name="consent"]').isChecked(), false);
  await page.screenshot({
    path: new URL(`review-${adults}.png`, output).pathname.replace(/^\/(\w:)/, '$1'),
    fullPage: true,
  });
  for (let index = 0; index < adults; index++) {
    await page.getByLabel(new RegExp(`^Имя гостя\\s*${index + 1}$`)).fill('Fictional');
    await page
      .getByRole('textbox', { name: 'Фамилия', exact: true })
      .nth(index)
      .fill(index === 0 ? 'First' : 'Second');
  }
  await page.locator('input[name="email"]').fill('fictional@example.invalid');
  await page.locator('input[name="phone"]').fill('+44000000000');
  for (const name of ['adultsConfirmed', 'checkInConfirmed', 'consent'])
    await page.locator(`input[name="${name}"]`).check();
  if (lostReply)
    await page.route(
      '**/api/hotels/reservations/*/purchase',
      async (route) => {
        await route.fetch();
        await route.abort('failed');
      },
      { times: 1 },
    );
  const before = purchases.length;
  const formDiagnostic = await page.evaluate(() => {
    const form = document.querySelector('.reservation form');
    const component = window.ng?.getComponent(document.querySelector('app-hotels-page'));
    const ngForm = window.ng?.getDirectives(form).find((value) => 'controls' in value);
    return {
      nativeValid: form?.checkValidity(),
      formStatus: ngForm?.status,
      busy: component?.busy(),
      consent: component?.consent,
      expired: component?.expired(component?.reservation()?.validUntil),
      inputs: [...(form?.querySelectorAll('input') ?? [])].map((input) => ({
        name: input.getAttribute('name'),
        type: input.type,
        valueLength: input.value.length,
        checked: input.checked,
        valid: input.validity.valid,
        patternMismatch: input.validity.patternMismatch,
        missing: input.validity.valueMissing,
      })),
      controls: Object.entries(ngForm?.controls ?? {}).map(([name, control]) => ({
        name,
        status: control.status,
        errorKeys: Object.keys(control.errors ?? {}),
      })),
    };
  });
  await writeFile(new URL(`form-${adults}.json`, output), JSON.stringify(formDiagnostic, null, 2));
  // Keyboard APIs do not perform click's actionability checks; wait for the template-driven form's render.
  await page.getByRole('button', { name: 'Забронировать в тестовом режиме' }).click({ trial: true });
  await page.getByRole('button', { name: 'Забронировать в тестовом режиме' }).focus();
  await page.keyboard.press('Enter');
  await page.waitForURL(/\/hotels\/reservations\/[a-f0-9-]{36}$/);
  await page.getByRole('heading', { name: 'Бронь подтверждена', exact: true }).waitFor();
  assert.equal(purchases.length, before + 1);
  const id = page.url().split('/').at(-1);
  await page.reload();
  await page.getByRole('heading', { name: 'Бронь подтверждена', exact: true }).waitFor();
  assert.equal(purchases.length, before + 1);
  const storage = await page.evaluate(() => ({
    local: Object.keys(localStorage),
    session: Object.keys(sessionStorage),
    url: location.href,
  }));
  assert(!JSON.stringify(storage).includes('fictional@example.invalid'));
  assert(!storage.url.includes('token'));
  await page.screenshot({
    path: new URL(`confirmed-${adults}${lostReply ? '-lost-client-reply' : ''}.png`, output).pathname.replace(
      /^\/(\w:)/,
      '$1',
    ),
    fullPage: true,
  });
  receipts.push({
    adults,
    id,
    lostClientReply: lostReply,
    changedTermsAtSamePriceClearedPartyAndConsent: true,
    exactlyOneBrowserPost: true,
    reloadedFromLocalDetail: true,
    privateStorageKeys: storage,
  });
}
try {
  await purchase(1);
  await page.getByRole('link', { name: 'Поиск', exact: true }).click();
  await page.waitForURL('**/hotels');
  await purchase(2);
  await page.setViewportSize({ width: 390, height: 844 });
  await purchase(1, true);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).waitFor();
  assert.equal(await page.locator('input[name="email"]').count(), 0);
  assert.equal(await page.getByRole('heading', { name: 'Бронь подтверждена', exact: true }).count(), 0);
  assert.deepEqual(failures, []);
  await writeFile(
    new URL('receipt.json', output),
    JSON.stringify(
      {
        surface: 'installed Microsoft Edge, local headless fresh scoped context',
        actualApplication:
          'Angular hotels-demo -> real Program.cs Kestrel -> owned PostgreSQL/outbox -> fake supplier HttpMessageHandler',
        receipts,
        purchases,
        pageErrors: failures,
        mobileWidth: 390,
        keyboardSubmit: true,
        logoutClearedPrivateView: true,
      },
      null,
      2,
    ),
  );
  console.log(JSON.stringify({ passed: receipts.length, posts: purchases.length, pageErrors: failures.length }));
} finally {
  await context.close();
  await browser.close();
}
