import { randomUUID } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { create, isolated, recordReceipt } from './trips-fixture';

test.afterEach(async ({ page }, info) => {
  await recordReceipt(page, info);
});

test('fictional owner create, keyboard edit, save, reload, stale conflict, other-owner denial and delete', async ({
  page,
}) => {
  const fixture = await isolated(page);
  const id = await create(page);
  expect(id).toMatch(/^[0-9a-f-]{36}$/);
  await page.getByRole('button', { name: 'Добавить блок' }).first().click();
  const title = page.getByLabel('Название блока', { exact: true });
  await expect(title).toBeFocused();
  await title.fill('<img src=x onerror=alert(1)>');
  await page.getByLabel('Заметка (до 500 символов)').fill('Fictional note');
  await page.getByLabel('Начало (местное)').fill('10:00');
  await page.getByLabel('Конец в тот же день').fill('11:30');
  await page.getByLabel('Ваша оценка, RUB').fill('0.10');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  await page.reload();
  await expect(title).toHaveValue('<img src=x onerror=alert(1)>');
  await expect(page.locator('img')).toHaveCount(0);
  await expect(page.getByText('Сумма известных оценок:')).toContainText('0.10 RUB');
  expect(fixture.plans.get(id)?.plan.days[0].blocks[0].estimate?.amount).toBe('0.10');
  await page.screenshot({ path: '__screenshots__/trips-m1-workspace.png', fullPage: true });
  const entry = fixture.plans.get(id)!;
  entry.plan.revision = '3';
  entry.plan.title = 'Other editor';
  await page.getByLabel('Название плана', { exact: true }).fill('Local retained');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Нужна сверка с сервером' })).toBeVisible();
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Local retained');
  await page.getByRole('button', { name: 'Перечитать текущее состояние', exact: true }).click();
  await page.getByRole('button', { name: 'Оставить мои правки на текущей версии' }).click();
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('недоступен');
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Local retained');
  await page.getByRole('button', { name: 'Удалить план', exact: true }).click();
  await page.getByRole('button', { name: 'Подтвердить удаление', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
  expect(fixture.plans.has(id)).toBe(false);
  expect(fixture.calls.filter((x) => x.method === 'POST')).toHaveLength(1);
  expect(fixture.calls.every((x) => x.path.startsWith('/api/trips'))).toBe(true);
  expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({
    local: 0,
    session: 0,
  });
});
test('lost PUT keeps a buffer, never resubmits, and rereads before explicit reconciliation', async ({ page }) => {
  const fixture = await isolated(page);
  await create(page);
  fixture.loseNextSave();
  await page.getByLabel('Название плана', { exact: true }).fill('Lost save');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Результат действия неизвестен' })).toBeVisible();
  expect(fixture.calls.filter((x) => x.method === 'PUT')).toHaveLength(1);
  await expect(page.getByRole('button', { name: 'Сохранить', exact: true })).toBeDisabled();
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Lost save');
  await page.getByRole('button', { name: 'Перечитать текущее состояние', exact: true }).click();
  await page.getByRole('button', { name: 'Заменить редактор сохранённым планом' }).click();
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Lost save');
  expect(fixture.calls.filter((x) => x.method === 'PUT')).toHaveLength(1);
});

test('a delayed old GET cannot regress an accepted save and private HTML is not server-rendered', async ({ page }) => {
  const fixture = await isolated(page);
  await create(page);
  let release: () => void = () => {
    throw new Error('Deferred test gate not initialized.');
  };
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  fixture.delayReads(gate);
  const count = fixture.calls.filter((c) => c.method === 'GET' && /\/[0-9a-f-]{36}$/.test(c.path)).length;
  await page.getByRole('button', { name: 'Перечитать план', exact: true }).click();
  await expect
    .poll(() => fixture.calls.filter((c) => c.method === 'GET' && /\/[0-9a-f-]{36}$/.test(c.path)).length)
    .toBe(count + 1);
  await page.getByLabel('Название плана', { exact: true }).fill('Newest accepted private title');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  release();
  fixture.delayReads(null);
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Newest accepted private title');
  await expect(page.getByText('Без несохранённых изменений')).toContainText('версия 2');
  const response = await page.reload();
  expect(await response!.text()).not.toContain('Newest accepted private title');
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Newest accepted private title');
});

test('near-limit multibyte fictional detail remains readable and its editor retains all stable block IDs', async ({
  page,
}) => {
  const fixture = await isolated(page);
  const id = await create(page);
  const p = fixture.plans.get(id)!.plan;
  p.brief.endDate = '2026-11-27';
  p.days = Array.from({ length: 14 }, (_, i) => ({
    date: new Date(Date.parse('2026-11-14T00:00:00Z') + i * 86400000).toISOString().slice(0, 10),
    blocks: Array.from({ length: 8 }, () => ({
      blockId: randomUUID(),
      title: 'Фиктивный блок',
      note: '😀'.repeat(190),
      startTime: null,
      endTime: null,
      estimate: null,
    })),
  }));
  p.unknownEstimateCount = 112;
  const size = () => Buffer.byteLength(JSON.stringify(p), 'utf8');
  while (size() < 129000) for (const d of p.days) for (const b of d.blocks) b.note += '😀';
  expect(size()).toBeLessThanOrEqual(131072);
  expect(size()).toBeGreaterThan(128000);
  console.info('Trips fictional near-limit detail UTF8 bytes:', size());
  const ids = p.days.flatMap((d) => d.blocks.map((b) => b.blockId));
  await page.reload();
  await expect(page.getByLabel('Название блока', { exact: true })).toHaveCount(112);
  const rendered = await page
    .getByLabel('Название блока', { exact: true })
    .evaluateAll((elements) => elements.map((e) => e.id.slice('block-'.length)));
  expect(rendered).toEqual(ids);
  await expect(page.getByText('Блоков без оценки: 112.')).toBeVisible();
  const stores = await page.evaluate(async () => ({
    local: localStorage.length,
    session: sessionStorage.length,
    indexed: (await indexedDB.databases()).length,
    serviceWorkers: (await navigator.serviceWorker.getRegistrations()).length,
  }));
  expect(stores).toEqual({ local: 0, session: 0, indexed: 0, serviceWorkers: 0 });
});

test('saved-baseline guards guide explicit empty-day save and block-removal save before a city change', async ({
  page,
}) => {
  const fixture = await isolated(page);
  const id = await create(page);
  await page.getByRole('button', { name: 'Добавить блок' }).first().click();
  await page.getByLabel('Название блока', { exact: true }).fill('Stable fictional block');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  const blockId = fixture.plans.get(id)!.plan.days[0].blocks[0].blockId;
  await page.getByLabel('Последний день').fill('2026-11-16');
  await page.getByRole('button', { name: 'Применить параметры к календарю' }).click();
  await expect(page.getByRole('button', { name: 'Добавить блок' }).nth(2)).toBeDisabled();
  await expect(page.getByLabel('Перенести в день').locator('option[value="2026-11-16"]')).toBeDisabled();
  expect(fixture.plans.get(id)!.plan.days).toHaveLength(2);
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Добавить блок' }).nth(2)).toBeEnabled();
  await page.getByLabel('Перенести в день').selectOption('2026-11-16');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  expect(fixture.plans.get(id)!.plan.days[2].blocks[0].blockId).toBe(blockId);
  await page.getByRole('button', { name: 'Удалить блок', exact: true }).click();
  await page.getByLabel('Город поездки', { exact: true }).selectOption('kaliningrad');
  const writes = fixture.calls.filter((c) => c.method === 'PUT').length;
  await page.getByRole('button', { name: 'Применить параметры к календарю' }).click();
  await expect(page.getByRole('status').first()).toContainText('Сначала сохраните удаление');
  expect(fixture.calls.filter((c) => c.method === 'PUT')).toHaveLength(writes);
  await page.getByRole('button', { name: 'Отменить изменения параметров' }).click();
  await expect(page.getByLabel('Город поездки', { exact: true })).toHaveValue('saint-petersburg');
  await expect(page.getByLabel('Название блока', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  await page.getByLabel('Город поездки', { exact: true }).selectOption('kaliningrad');
  await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('План сохранён');
  expect(fixture.plans.get(id)!.plan.brief.destinationId).toBe('kaliningrad');
  expect(fixture.plans.get(id)!.plan.days.flatMap((d) => d.blocks)).toHaveLength(0);
});
