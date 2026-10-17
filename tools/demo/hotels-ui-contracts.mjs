import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';

// UI-only controlled API boundary tests. Full booking evidence is hotels-browser.mjs against the real Host.
const output = new URL('../../TestResults/hotels-m1/browser/ui-contracts.json', import.meta.url);
await mkdir(new URL('./', output), { recursive: true });
const now = new Date().toISOString();
const until = new Date(Date.now() + 300000).toISOString();
const id = 'a320b78f-ed42-438a-9979-7e029c655454';
const terms = {
  property: {
    id: 'acc_fictional_ui',
    name: 'Fictional UI contract hotel',
    address: null,
    rating: null,
    ratingSource: null,
  },
  room: 'Double',
  rateId: 'rate_fictional_ui',
  dates: { checkIn: '2027-01-03', checkOut: '2027-01-05', nights: 2 },
  occupancy: { adults: 1 },
  total: { amount: '120.25', currency: 'GBP' },
  mandatoryLocalPayments: [],
  board: [],
  cancellation: { kind: 'NonRefundable', rules: [] },
  conditions: [],
  eligible: true,
  paymentTiming: 'ImmediateTest',
};
const offer = {
  provider: 'duffel-stays',
  environment: 'Test',
  selectionKey: 'selection_fictional_ui',
  terms,
  sourceExpiresAt: until,
  purchasable: true,
  purchaseUnavailableReason: null,
};
function search(kind = 'Complete', expired = false) {
  return {
    id,
    result: { kind, items: kind === 'Complete' ? [offer] : [], reason: kind === 'Complete' ? null : 'FictionalReason' },
    statuses: [{ provider: 'duffel-stays', kind, reason: kind === 'Complete' ? null : 'FictionalReason' }],
    expiresAt: expired ? new Date(Date.now() - 1000).toISOString() : until,
  };
}
const quote = {
  id,
  quoteRevision: 1,
  accepted: terms,
  validUntil: until,
  attemptId: null,
  outcome: 'Quoted',
  reviewReasons: [],
  everReserved: false,
  active: false,
  facts: null,
  createdAt: now,
  updatedAt: now,
  privateContentAvailable: true,
};
const browser = await chromium.launch({ headless: true, channel: 'msedge' });
const context = await browser.newContext();
const page = await context.newPage();
let searchResult = search(),
  quoteMode = 'ready',
  heldSearch,
  heldQuote;
let holdSearch = false,
  posts = 0;
const receipts = [];
async function json(route, value, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) });
}
await page.route('**/api/hotels/**', async (route) => {
  const request = route.request(),
    path = new URL(request.url()).pathname;
  if (path.endsWith('/purchase')) {
    posts++;
    return json(route, {}, 500);
  }
  if (path.endsWith('/search')) {
    if (holdSearch)
      await new Promise((resolve) => {
        heldSearch = resolve;
      });
    return json(route, searchResult);
  }
  if (path.endsWith('/rates'))
    return json(route, {
      id,
      searchId: id,
      result: { kind: 'Complete', items: [offer], reason: null },
      expiresAt: until,
    });
  if (path.endsWith('/quote')) {
    if (quoteMode === 'held')
      await new Promise((resolve) => {
        heldQuote = resolve;
      });
    return json(route, quote, quoteMode === 'forbidden' ? 403 : 200);
  }
  return json(route, []);
});
async function open() {
  await page.goto('http://127.0.0.1:4202/hotels');
  await page.getByRole('button', { name: 'Найти проживание', exact: true }).waitFor();
}
async function find() {
  await page.getByRole('button', { name: 'Найти проживание', exact: true }).click();
}
async function openQuote() {
  await open();
  await find();
  await page.getByRole('button', { name: 'Выбрать номер и тариф' }).click();
  await page.getByRole('button', { name: 'Проверить актуальные условия' }).click();
  await page.getByLabel(/^Имя гостя\s*1$/).waitFor();
}
try {
  for (const [kind, text] of [
    ['Unsupported', 'Для выбранных условий поиск недоступен.'],
    ['Failed', 'Поставщик не смог ответить. Попробуйте позже.'],
    ['Limited', 'Показана часть предложений. Некоторые результаты недоступны.'],
  ]) {
    searchResult = search(kind);
    await open();
    await find();
    await page.getByText(text, { exact: true }).waitFor();
    receipts.push({ kind, shown: true });
  }
  searchResult = search();
  searchResult.result.items = [];
  await open();
  await find();
  await page.getByText('По этим условиям ничего не найдено.', { exact: true }).waitFor();
  receipts.push({ kind: 'CompleteEmpty', shown: true });
  searchResult = search('Complete', true);
  await open();
  await find();
  await page.getByText('Предложения устарели. Повторите поиск.', { exact: true }).waitFor();
  assert(await page.getByRole('button', { name: 'Выбрать номер и тариф' }).isDisabled());
  receipts.push({ kind: 'Expired', selectionDisabled: true });
  searchResult = search();
  holdSearch = true;
  await open();
  await find();
  await page.getByText('Загружаем…', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  heldSearch();
  holdSearch = false;
  await page.getByRole('button', { name: 'Войти', exact: true }).waitFor();
  await page.waitForTimeout(100);
  assert.equal(await page.getByRole('heading', { name: 'Результаты поиска', exact: true }).count(), 0);
  receipts.push({ kind: 'StaleSearchAfterLogout', discarded: true });
  await openQuote();
  await page.getByLabel(/^Имя гостя\s*1$/).fill('Fictional');
  await page.locator('input[name="consent"]').check();
  quoteMode = 'forbidden';
  await page.getByRole('button', { name: 'Обновить условия', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).waitFor();
  assert.equal(await page.getByLabel(/^Имя гостя\s*1$/).count(), 0);
  receipts.push({ kind: 'PermissionLoss403', privateViewAndConsentCleared: true });
  quoteMode = 'ready';
  await openQuote();
  await page.getByLabel(/^Имя гостя\s*1$/).fill('Fictional');
  quoteMode = 'held';
  await page.getByRole('button', { name: 'Обновить условия', exact: true }).click();
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  heldQuote();
  await page.getByRole('button', { name: 'Войти', exact: true }).waitFor();
  await page.waitForTimeout(100);
  assert.equal(await page.getByLabel(/^Имя гостя\s*1$/).count(), 0);
  assert.equal(posts, 0);
  receipts.push({ kind: 'StaleQuoteAfterLogout', discarded: true, purchasePosts: 0 });
  await writeFile(
    output,
    JSON.stringify(
      {
        surface: 'installed Edge, fresh headless context',
        scope: 'UI-only mocked normalized responses; no real booking/Host claim from these checks',
        receipts,
      },
      null,
      2,
    ),
  );
  console.log(JSON.stringify({ uiContractCases: receipts.length, purchases: posts }));
} finally {
  await context.close();
  await browser.close();
}
