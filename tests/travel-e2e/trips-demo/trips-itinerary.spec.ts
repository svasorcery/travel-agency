import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, type Page, test } from '@playwright/test';
import { atomicJson, browserArtifacts, create, type GateKind, isolated, recordReceipt, vector } from './trips-fixture';

test.afterEach(async ({ page }, info) => {
  await recordReceipt(page, info);
});

const generate = (page: Page) => page.getByRole('button', { name: 'Предложить прогулочный маршрут' });
const adopt = (page: Page) => page.getByRole('button', { name: 'Перенести в редактор' });
const preview = (page: Page) => page.locator('#itinerary-preview');
const title = (page: Page) => page.getByLabel('Название плана', { exact: true });
const blocks = (page: Page) => page.getByLabel('Название блока', { exact: true });
const save = (page: Page) => page.getByRole('button', { name: 'Сохранить', exact: true });
const puts = (f: Awaited<ReturnType<typeof isolated>>) => f.calls.filter((c) => c.method === 'PUT');
const candidates = (f: Awaited<ReturnType<typeof isolated>>) =>
  f.calls.filter((c) => c.path.endsWith('/itinerary-candidate'));
function savedPlan(fixture: Awaited<ReturnType<typeof isolated>>, id: string) {
  const entry = fixture.plans.get(id);
  if (!entry) throw new Error('Expected fictional saved plan');
  return entry.plan;
}
const availabilityCalls = (f: Awaited<ReturnType<typeof isolated>>) =>
  f.calls.filter((c) => c.path === '/api/trips/itinerary-availability');
async function setup(page: Page) {
  // Explicit fictional clock preserves frozen production API vectors; no real route/Q evaluation.
  await page.clock.install({ time: new Date('2026-10-21T00:01:00Z') });
  const fixture = await isolated(page, true);
  const id = await create(page, true);
  await expect(generate(page)).toBeEnabled();
  return { fixture, id };
}
async function propose(page: Page) {
  await generate(page).click();
  await expect(preview(page)).toBeVisible();
  await expect(adopt(page)).toBeEnabled();
}
async function noPreviewWrites(page: Page, fixture: Awaited<ReturnType<typeof isolated>>) {
  await expect(preview(page)).toHaveCount(0);
  await expect(blocks(page)).toHaveCount(0);
  expect(puts(fixture)).toHaveLength(0);
  expect(candidates(fixture)).toHaveLength(1);
}

test('FICTIONAL M2 partial preview keyboard sources mobile explicit transfer edit Save reload and public static bytes', async ({
  page,
  request,
}) => {
  const { fixture, id } = await setup(page);
  const initial = structuredClone(savedPlan(fixture, id));
  await generate(page).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#itinerary-preview-title')).toBeFocused();
  await expect(preview(page)).toContainText('Заполнено частично');
  await expect(preview(page)).toContainText('Не покрыты: Еда');
  await expect(preview(page)).toContainText('Часы доступа неизвестны');
  await expect(preview(page)).toContainText('Калибровка не проводилась');
  await expect(preview(page).locator('.preview-summary')).toContainText('Демонстрационная оценка по OSM');
  await expect(preview(page)).toContainText('качество маршрута людьми не оценивалось');
  await expect(preview(page)).toContainText('12–22 мин. Без полевой проверки');
  await expect(page.locator('#itinerary-status')).toHaveAttribute('aria-live', 'polite');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Закрыть предложение' })).toBeFocused();
  await page.keyboard.press('Tab');
  const summary = preview(page).locator('summary').first();
  await expect(summary).toBeFocused();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Tab');
  await expect(preview(page).getByRole('link', { name: 'www.wikidata.org' }).first()).toBeFocused();
  await expect(preview(page)).toContainText('FICTIONAL reference; no actual observation or acceptance');
  const dataDetails = preview(page).locator('.preview-sources details');
  await expect(dataDetails).not.toHaveAttribute('open');
  await dataDetails.locator('summary').click();
  await expect(dataDetails).toContainText('Программная подготовка: fictional-builder-v1');
  await expect(dataDetails).toContainText('75/45 м/мин');
  await dataDetails.locator('summary').click();
  const links = await page
    .locator('a[target="_blank"]')
    .evaluateAll((els) => els.map((e) => ({ href: e.getAttribute('href'), rel: e.getAttribute('rel') })));
  expect(links.length).toBeGreaterThan(4);
  for (const link of links) {
    expect(link.href).toMatch(/^https:\/\//);
    expect(link.rel).toBe('noopener noreferrer');
  }
  await expect(page.locator('iframe,img')).toHaveCount(0);
  expect(fixture.requests.some((r) => r.url.includes('/trips-data/'))).toBe(false);
  expect(fixture.requests.filter((r) => new URL(r.url).origin !== 'http://127.0.0.1:4203')).toEqual([]);
  await preview(page).screenshot({ path: resolve(browserArtifacts, 'walking-preview-desktop.png') });
  await page.screenshot({ path: resolve(browserArtifacts, '01-fictional-preview-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(adopt(page)).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await preview(page).screenshot({ path: resolve(browserArtifacts, 'walking-preview-mobile.png') });
  await page.screenshot({ path: resolve(browserArtifacts, '02-fictional-preview-mobile.png'), fullPage: true });
  await page.setViewportSize({ width: 1280, height: 800 });

  // APIRequestContext makes exactly six SAME-ORIGIN public GETs to the actual Angular assets.
  // Page routing NEVER fulfils these files. No external URL in a manifest/source is followed.
  const root = resolve('modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/moscow-outdoor/1');
  const manifest = JSON.parse(readFileSync(resolve(root, 'moscow-v1.manifest.json'), 'utf8'));
  const names = ['moscow-v1.manifest.json', ...Object.keys(manifest.files)];
  expect(names).toHaveLength(6);
  const staticLedger = [];
  for (const name of names) {
    const path = `/trips-data/moscow-outdoor/1/${name}`;
    const response = await request.get(`http://127.0.0.1:4203${path}`, { maxRedirects: 0 });
    expect(response.status()).toBe(200);
    const actual = await response.body(),
      canonical = readFileSync(resolve(root, name));
    expect(actual.equals(canonical)).toBe(true);
    const sha256 = createHash('sha256').update(actual).digest('hex');
    if (name !== names[0]) expect(sha256).toBe(manifest.files[name]);
    staticLedger.push({
      method: 'GET',
      url: response.url(),
      status: response.status(),
      bytes: actual.length,
      sha256,
      canonicalSha256: createHash('sha256').update(canonical).digest('hex'),
    });
  }
  atomicJson(resolve(browserArtifacts, 'actual-public-assets.json'), {
    fictionalBrowser: true,
    actualLocalPublicDeliveryOnly: true,
    calls: staticLedger,
  });

  const readsBefore = fixture.calls.filter((c) => c.method === 'GET' && c.path === `/api/trips/${id}`).length;
  const availabilityBefore = availabilityCalls(fixture).length;
  const owner = fixture.hold('owner'),
    available = fixture.hold('availability');
  await adopt(page).dblclick();
  await Promise.all([owner.started, available.started]);
  await expect(adopt(page)).toBeDisabled();
  expect(fixture.calls.filter((c) => c.method === 'GET' && c.path === `/api/trips/${id}`)).toHaveLength(
    readsBefore + 1,
  );
  expect(availabilityCalls(fixture)).toHaveLength(availabilityBefore + 1);
  owner.release();
  available.release();
  await expect(blocks(page)).toHaveCount(2);
  await expect(blocks(page).first()).toBeFocused();
  await expect(preview(page)).toHaveCount(0);
  const ids = await blocks(page).evaluateAll((els) => els.map((e) => e.id.slice(6)));
  expect(ids).toEqual(vector('partial').days[0].blocks.map((b: { proposedBlockId: string }) => b.proposedBlockId));
  expect(puts(fixture)).toHaveLength(0);
  expect(savedPlan(fixture, id)).toEqual(initial);
  await expect(title(page)).toHaveValue(initial.title);
  await expect(page.getByLabel('Начало (местное)').first()).toHaveValue('');
  await expect(page.getByLabel('Ваша оценка, RUB').first()).toHaveValue('');
  await expect(page.locator('#itinerary-data-credit')).toContainText('OpenStreetMap contributors');
  await expect(page.locator('#itinerary-data-credit')).toContainText('ODbL');
  await page.screenshot({ path: resolve(browserArtifacts, '03-fictional-staged-intentions.png'), fullPage: true });
  await blocks(page).first().fill('<img src=x onerror=alert(1)> ordinary intention');
  await page
    .getByLabel('Заметка (до 500 символов)')
    .first()
    .fill('Источник часов доступа: verified 24h <b>source-looking user note</b>');
  expect(puts(fixture)).toHaveLength(0);
  await save(page).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  expect(puts(fixture)).toHaveLength(1);
  expect(puts(fixture)[0].ifMatch).toBe('"1"');
  const saved = savedPlan(fixture, id);
  expect(saved.brief).toEqual(initial.brief);
  expect(saved.days[0].blocks.map((b) => b.blockId)).toEqual(ids);
  expect(saved.days[0].blocks.every((b) => b.startTime === null && b.endTime === null && b.estimate === null)).toBe(
    true,
  );
  expect(saved.days[0].blocks[1].note).toContain('Historical proposal: fictional-moscow/fictional-v2');
  expect(Object.keys(JSON.parse(puts(fixture)[0].body ?? 'null')).sort()).toEqual(['brief', 'days', 'title']);
  const response = await page.reload();
  if (!response) throw new Error('Expected local Angular document');
  const html = await response.text();
  expect(html).not.toContain('source-looking user note');
  expect(html).not.toContain('FICTIONAL place p0');
  await expect(blocks(page).first()).toHaveValue('<img src=x onerror=alert(1)> ordinary intention');
  await expect(page.getByLabel('Заметка (до 500 символов)').first()).toHaveValue(
    'Источник часов доступа: verified 24h <b>source-looking user note</b>',
  );
  await expect(preview(page)).toHaveCount(0);
  await expect(page.locator('img,b').filter({ hasText: 'source-looking user note' })).toHaveCount(0);
  await expect(page.getByText('Источник часов доступа', { exact: true })).toHaveCount(0);
  await expect(page.locator('#itinerary-data-credit')).toContainText('отделены от редактируемых заметок');
  const stores = await page.evaluate(async () => ({
    local: localStorage.length,
    session: sessionStorage.length,
    indexed: (await indexedDB.databases()).length,
    workers: (await navigator.serviceWorker.getRegistrations()).length,
  }));
  expect(stores).toEqual({ local: 0, session: 0, indexed: 0, workers: 0 });
  await expect(generate(page)).toBeDisabled(); // saved nonempty refusal; no deletion or new generation
  expect(candidates(fixture)).toHaveLength(1);
});

test('FICTIONAL M2 disabled preserves manual M1 Save and never requests a candidate', async ({ page }) => {
  const fixture = await isolated(page);
  await create(page);
  await expect(page.locator('#itinerary-status')).toContainText('выключены');
  await expect(generate(page)).toHaveCount(0);
  await page.getByRole('button', { name: 'Добавить блок' }).first().click();
  await blocks(page).first().fill('Manual while M2 disabled');
  await save(page).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  expect(candidates(fixture)).toHaveLength(0);
  expect(puts(fixture)).toHaveLength(1);
});

for (const kind of ['package-switch', 'policy-switch', 'withdrawal', 'flag-off', 'read-failure'] as const) {
  test(`FICTIONAL adoption refuses ${kind} at unchanged plan revision without retry or buffer write`, async ({
    page,
  }) => {
    const { fixture, id } = await setup(page);
    await propose(page);
    const before = structuredClone(savedPlan(fixture, id));
    if (kind === 'package-switch') {
      fixture.terminal.availability.datasetHash = 'c'.repeat(64);
      fixture.terminal.availability.datasetVersion = 'fictional-v3';
    }
    if (kind === 'policy-switch') fixture.terminal.availability.policyVersion = 'unknown-policy-v2';
    if (kind === 'withdrawal') fixture.terminal.availabilityStatus = 503;
    if (kind === 'read-failure') fixture.terminal.availabilityStatus = 0;
    if (kind === 'flag-off') fixture.terminal.enabled = false;
    await adopt(page).click();
    await noPreviewWrites(page, fixture);
    await expect(page.locator('#itinerary-status')).toContainText(
      kind === 'package-switch' ? 'устарело' : kind === 'flag-off' ? 'выключены' : 'недоступно',
    );
    expect(savedPlan(fixture, id)).toEqual(before);
    expect(availabilityCalls(fixture)).toHaveLength(3); // new form, saved route, one explicit adoption
  });
}

test('FICTIONAL stale generation revision is refused without retry', async ({ page }) => {
  const { fixture, id } = await setup(page);
  savedPlan(fixture, id).revision = '2';
  await generate(page).click();
  await expect(page.locator('#itinerary-status')).toContainText('устарело');
  await noPreviewWrites(page, fixture);
});

for (const revert of [false, true]) {
  test(`FICTIONAL delayed generation cannot replace form-only edit${revert ? ' even after revert' : ''}`, async ({
    page,
  }) => {
    const { fixture } = await setup(page);
    const gate = fixture.hold('candidate');
    await generate(page).click();
    await gate.started;
    await page.getByLabel('Последний день').fill('2026-10-23');
    if (revert) await page.getByLabel('Последний день').fill('2026-10-22');
    await page.getByLabel('Последний день').focus();
    gate.release();
    await gate.settled;
    await page.evaluate(() => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))));
    await noPreviewWrites(page, fixture);
    await expect(page.getByLabel('Последний день')).toHaveValue(revert ? '2026-10-22' : '2026-10-23');
    await expect(page.getByLabel('Последний день')).toBeFocused();
  });
}

for (const pending of ['both', 'owner', 'availability'] as const) {
  for (const action of ['manual-edit', 'logout', 'plan-change', 'close'] as const) {
    test(`FICTIONAL adoption ${pending} pending then ${action} rejects late replies and preserves focus/content`, async ({
      page,
    }) => {
      const { fixture } = await setup(page);
      await propose(page);
      const kinds: GateKind[] = pending === 'both' ? ['owner', 'availability'] : [pending];
      const held = kinds.map((kind) => fixture.hold(kind));
      await adopt(page).click();
      await Promise.all(held.map((g) => g.started));
      const availableBefore = availabilityCalls(fixture).length;
      if (action === 'manual-edit') await title(page).fill('Edited during mandatory read');
      if (action === 'logout') await page.getByRole('button', { name: 'Выйти', exact: true }).click();
      if (action === 'plan-change') await page.getByRole('button', { name: 'Новый план', exact: true }).click();
      if (action === 'close') {
        const close = page.getByRole('button', { name: 'Закрыть предложение' });
        await expect(close).toBeEnabled();
        await close.click();
        await expect(generate(page)).toBeFocused();
      }
      held.forEach((g) => {
        g.release();
      });
      await Promise.all(held.map((g) => g.settled));
      await page.evaluate(
        () => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))),
      );
      await noPreviewWrites(page, fixture);
      if (action === 'manual-edit') await expect(title(page)).toHaveValue('Edited during mandatory read');
      if (action === 'logout') {
        await expect(title(page)).toHaveCount(0);
        await expect(page.locator('body')).not.toContainText('FICTIONAL place');
      }
      if (action === 'plan-change') {
        await expect(page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
        await expect(title(page)).toHaveValue('');
      }
      if (action === 'close') await expect(generate(page)).toBeFocused();
      // New-plan activation may perform its own availability GET; canceled adoption never retries.
      expect(availabilityCalls(fixture).length).toBeLessThanOrEqual(
        availableBefore + (action === 'plan-change' ? 1 : 0),
      );
    });
  }
}

test('FICTIONAL owner A preview is cleared before owner B access and does not return on owner A login', async ({
  page,
}) => {
  const { fixture } = await setup(page);
  await propose(page);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await expect(preview(page)).toHaveCount(0);
  await expect(title(page)).toHaveCount(0);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('недоступен');
  await expect(title(page)).toHaveCount(0);
  await expect(preview(page)).toHaveCount(0);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(title(page)).toHaveValue('Fictional private plan');
  await noPreviewWrites(page, fixture);
});

for (const outcome of ['candidate', 'empty', 'unsupported'] as const) {
  test(`FICTIONAL frozen ${outcome} wire branch has distinct browser presentation`, async ({ page }) => {
    const { fixture, id } = await setup(page);
    fixture.terminal.outcome = outcome;
    const plan = savedPlan(fixture, id);
    plan.brief.interests = ['architecture'];
    if (outcome === 'unsupported') {
      plan.brief.destinationId = 'saint-petersburg';
      plan.destination = {
        destinationId: 'saint-petersburg',
        displayName: 'Санкт-Петербург',
        timeZone: 'Europe/Moscow',
        catalogueVersion: '1',
      };
    }
    await page.reload();
    await expect(generate(page)).toBeEnabled();
    await generate(page).click();
    await expect(preview(page)).toBeVisible();
    await expect(preview(page)).toContainText(
      outcome === 'candidate'
        ? 'Остановки заполнены'
        : outcome === 'empty'
          ? 'Подходящих остановок нет'
          : 'Основа не поддерживается',
    );
    if (outcome !== 'candidate') await expect(adopt(page)).toHaveCount(0);
    expect(puts(fixture)).toHaveLength(0);
  });
}

test('FICTIONAL transferred intentions retain M1 412 CAS conflict with no automatic rebase', async ({ page }) => {
  const { fixture, id } = await setup(page);
  await propose(page);
  await adopt(page).click();
  await expect(blocks(page)).toHaveCount(2);
  savedPlan(fixture, id).revision = '2';
  await save(page).click();
  await expect(page.getByRole('heading', { name: 'Нужна сверка с сервером' })).toBeVisible();
  await expect(blocks(page)).toHaveCount(2);
  expect(puts(fixture)).toHaveLength(1);
  expect(puts(fixture)[0].ifMatch).toBe('"1"');
  await expect(save(page)).toBeDisabled();
});

test('FICTIONAL transferred lost PUT remains uncertain and later local form edit survives reread', async ({ page }) => {
  const { fixture } = await setup(page);
  await propose(page);
  await adopt(page).click();
  await expect(blocks(page)).toHaveCount(2);
  fixture.loseNextSave();
  await save(page).click();
  await expect(page.getByRole('heading', { name: 'Результат действия неизвестен' })).toBeVisible();
  const owner = fixture.hold('owner');
  await page.getByRole('button', { name: 'Перечитать текущее состояние', exact: true }).click();
  await owner.started;
  await page.getByLabel('Последний день').fill('2026-10-23');
  owner.release();
  await expect(page.getByRole('button', { name: 'Заменить редактор сохранённым планом' })).toBeVisible();
  await expect(page.getByLabel('Последний день')).toHaveValue('2026-10-23');
  await expect(blocks(page)).toHaveCount(2);
  await expect(save(page)).toBeDisabled();
  expect(puts(fixture)).toHaveLength(1);
});

test('FICTIONAL mobile disclosure Close regenerate transfer and explicit Save are operable at 390px', async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { fixture } = await setup(page);
  await propose(page);
  const details = preview(page).locator('summary').first();
  await details.click();
  await expect(preview(page).getByRole('link', { name: 'www.wikidata.org' }).first()).toBeVisible();
  await page.getByRole('button', { name: 'Закрыть предложение' }).click();
  await expect(generate(page)).toBeFocused();
  await propose(page);
  await adopt(page).click();
  await expect(blocks(page)).toHaveCount(2);
  await blocks(page).first().fill('FICTIONAL mobile intention');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(puts(fixture)).toHaveLength(0);
  await save(page).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  expect(candidates(fixture)).toHaveLength(2);
  expect(puts(fixture)).toHaveLength(1);
});

test('FICTIONAL transferred Save reply preserves a newer form-only edit and never sends it automatically', async ({
  page,
}) => {
  const { fixture, id } = await setup(page);
  await propose(page);
  await adopt(page).click();
  await expect(blocks(page)).toHaveCount(2);
  const reply = fixture.hold('put');
  await save(page).click();
  await reply.started;
  await page.getByLabel('Последний день').fill('2026-10-23');
  reply.release();
  await reply.settled;
  await expect(page.getByRole('status').first()).toContainText('Более поздние правки ещё не сохранены');
  await expect(page.getByLabel('Последний день')).toHaveValue('2026-10-23');
  await expect(page.getByText('Есть несохранённые изменения')).toBeVisible();
  expect(savedPlan(fixture, id).brief.endDate).toBe('2026-10-22');
  expect(puts(fixture)).toHaveLength(1);
});
