import { randomUUID } from 'node:crypto';
import { expect, type Page, test } from '@playwright/test';

const catalogue = {
  catalogueVersion: '1',
  items: [
    { destinationId: 'moscow', displayName: 'Москва', timeZone: 'Europe/Moscow', catalogueVersion: '1' },
    {
      destinationId: 'saint-petersburg',
      displayName: 'Санкт-Петербург',
      timeZone: 'Europe/Moscow',
      catalogueVersion: '1',
    },
    { destinationId: 'kaliningrad', displayName: 'Калининград', timeZone: 'Europe/Kaliningrad', catalogueVersion: '1' },
  ],
};
type Plan = {
  tripId: string;
  revision: string;
  title: string;
  brief: {
    destinationId: string;
    originId: string | null;
    startDate: string;
    endDate: string;
    adults: number;
    currency: string;
    totalBudget: string | null;
    interests: string[];
    pace: string;
  };
  destination: (typeof catalogue.items)[number];
  createdAt: string;
  updatedAt: string;
  days: {
    date: string;
    blocks: {
      blockId: string;
      title: string;
      note: string | null;
      startTime: string | null;
      endTime: string | null;
      estimate: { amount: string; currency: string } | null;
    }[];
  }[];
  knownUserEstimateSubtotal: string;
  unknownEstimateCount: number;
  warnings: string[];
};
async function isolated(page: Page) {
  const plans = new Map<string, { owner: string; plan: Plan }>();
  const calls: { method: string; path: string }[] = [];
  let lostPut = false;
  let delayedRead: Promise<void> | null = null;
  const minor = (amount: string) => {
    const [w, f = '00'] = amount.split('.');
    return BigInt(w) * 100n + BigInt(f);
  };
  const amount = (m: bigint) => `${m / 100n}.${(m % 100n).toString().padStart(2, '0')}`;
  function normalize(plan: Plan): Plan {
    let sum = 0n,
      unknown = 0;
    for (const day of plan.days)
      for (const b of day.blocks) {
        b.note ??= null;
        b.startTime ??= null;
        b.endTime ??= null;
        b.estimate ??= null;
        if (b.estimate) {
          b.estimate.amount = amount(minor(b.estimate.amount));
          sum += minor(b.estimate.amount);
        } else unknown++;
      }
    plan.brief.originId ??= null;
    plan.brief.totalBudget = plan.brief.totalBudget === null ? null : amount(minor(plan.brief.totalBudget));
    plan.knownUserEstimateSubtotal = amount(sum);
    plan.unknownEstimateCount = unknown;
    plan.warnings =
      plan.brief.totalBudget !== null && sum > minor(plan.brief.totalBudget) ? ['KnownEstimateExceedsBudget'] : [];
    return plan;
  }
  await page.route('**/*', async (route) => {
    const req = route.request(),
      url = new URL(req.url());
    expect(url.origin).toBe('http://127.0.0.1:4203');
    if (!url.pathname.startsWith('/api/')) {
      await route.continue();
      return;
    }
    expect(url.pathname).toMatch(/^\/api\/trips(?:\/destinations|\/[0-9a-f-]{36})?$/);
    expect(['GET', 'POST', 'PUT', 'DELETE']).toContain(req.method());
    calls.push({ method: req.method(), path: url.pathname });
    const owner = req.headers()['authorization'];
    const send = async (status: number, body: unknown, headers: Record<string, string> = {}) =>
      route.fulfill({
        status,
        headers: { 'Cache-Control': 'no-store', ...headers },
        contentType: 'application/json',
        body: status === 204 ? '' : JSON.stringify(body),
      });
    if (!owner) return send(401, { type: 'Trips.Unauthorized' });
    expect(['Bearer fictional-trips-owner-a', 'Bearer fictional-trips-owner-b']).toContain(owner);
    if (url.pathname === '/api/trips/destinations') return send(200, catalogue);
    if (url.pathname === '/api/trips' && req.method() === 'GET') {
      const items = [...plans.values()]
        .filter((x) => x.owner === owner)
        .map(({ plan: p }) => ({
          tripId: p.tripId,
          revision: p.revision,
          title: p.title,
          destinationId: p.brief.destinationId,
          displayName: p.destination.displayName,
          startDate: p.brief.startDate,
          endDate: p.brief.endDate,
          updatedAt: p.updatedAt,
        }));
      return send(200, { items, nextCursor: null });
    }
    if (url.pathname === '/api/trips' && req.method() === 'POST') {
      const b = req.postDataJSON();
      expect(Object.keys(b).sort()).toEqual(['brief', 'title']);
      const tripId = randomUUID();
      const dates = [];
      for (
        let d = Date.parse(b.brief.startDate + 'T00:00:00Z');
        d <= Date.parse(b.brief.endDate + 'T00:00:00Z');
        d += 86400000
      )
        dates.push({ date: new Date(d).toISOString().slice(0, 10), blocks: [] });
      const p = normalize({
        tripId,
        revision: '1',
        ...b,
        destination: catalogue.items.find((x) => x.destinationId === b.brief.destinationId)!,
        createdAt: '2026-10-20T09:00:00Z',
        updatedAt: '2026-10-20T09:00:00Z',
        days: dates,
        knownUserEstimateSubtotal: '0.00',
        unknownEstimateCount: 0,
        warnings: [],
      });
      plans.set(tripId, { owner, plan: p });
      return send(201, p, { ETag: '"1"', Location: '/api/trips/' + tripId });
    }
    const id = url.pathname.split('/')[3],
      entry = plans.get(id);
    if (!entry || entry.owner !== owner) return send(404, { type: 'about:blank', status: 404, code: 'Trips.NotFound' });
    const p = structuredClone(entry.plan);
    if (req.method() === 'GET') {
      if (delayedRead) await delayedRead;
      return send(200, p, { ETag: '"' + p.revision + '"' });
    }
    if (req.headers()['if-match'] !== '"' + p.revision + '"')
      return send(412, { type: 'about:blank', status: 412, code: 'Trips.RevisionMismatch' });
    if (req.method() === 'DELETE') {
      plans.delete(id);
      return send(204, null);
    }
    const b = req.postDataJSON();
    expect(Object.keys(b).sort()).toEqual(['brief', 'days', 'title']);
    const populated = p.days.some((day) => day.blocks.length > 0);
    if (populated && (b.brief.destinationId !== p.brief.destinationId || b.brief.currency !== p.brief.currency))
      return send(409, { type: 'about:blank', status: 409, code: 'Trips.BriefChangeRequiresEmptyPlan' });
    if (p.days.some((day) => day.blocks.length > 0 && (day.date < b.brief.startDate || day.date > b.brief.endDate)))
      return send(409, { type: 'about:blank', status: 409, code: 'Trips.DateRangeWouldDropBlocks' });
    if (
      b.days.some((day: Plan['days'][number]) => day.blocks.length > 0 && !p.days.some((old) => old.date === day.date))
    )
      return send(400, { type: 'about:blank', status: 400, code: 'Trips.InvalidInput' });
    entry.plan = normalize({
      ...p,
      ...b,
      destination:
        p.brief.destinationId === b.brief.destinationId
          ? p.destination
          : catalogue.items.find((city) => city.destinationId === b.brief.destinationId)!,
      revision: (BigInt(p.revision) + 1n).toString(),
      updatedAt: '2026-10-20T09:05:00Z',
    });
    if (lostPut) {
      lostPut = false;
      await route.abort('failed');
      return;
    }
    return send(200, entry.plan, { ETag: '"' + entry.plan.revision + '"' });
  });
  return {
    plans,
    calls,
    loseNextSave: () => {
      lostPut = true;
    },
    delayReads: (wait: Promise<void> | null) => {
      delayedRead = wait;
    },
  };
}
async function create(page: Page) {
  await page.goto('/trips');
  await expect(page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
  await page.getByLabel('Название плана', { exact: true }).fill('Fictional private plan');
  await page.getByLabel('Первый день').fill('2026-11-14');
  await page.getByLabel('Последний день').fill('2026-11-15');
  await page.getByLabel('Общий бюджет (необязательно)').fill('15000.00');
  const loaded = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' &&
      /\/api\/trips\/[0-9a-f-]{36}$/.test(new URL(response.url()).pathname) &&
      response.status() === 200,
  );
  await page.getByRole('button', { name: 'Создать основу плана' }).click();
  await loaded;
  await expect(page.getByRole('heading', { name: 'Ваш черновик' })).toBeVisible();
  return page.url().split('/').at(-1)!;
}
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
