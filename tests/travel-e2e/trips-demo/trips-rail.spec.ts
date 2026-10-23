import { createHash, randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
import { expect, type Page, test } from '@playwright/test';
import type { RailSearchResult } from '@travel/api-client';
import { decodeTripPlan, tripTotals, tripWrite } from '@travel/api-client/trips-decoder';
import { atomicJson, browserArtifacts, create, isolated, type Plan, recordReceipt } from './trips-fixture';

test.afterEach(async ({ page }, info) => recordReceipt(page, info));
type Fixture = Awaited<ReturnType<typeof isolated>>;
const names = (page: Page) => page.getByLabel('Название блока', { exact: true });
const save = (page: Page) => page.getByRole('button', { name: 'Сохранить', exact: true });
const picker = (page: Page) => page.locator('app-rail-search');
const choose = (page: Page) => page.getByRole('button', { name: 'Добавить намерение в план', exact: true }).first();
const puts = (f: Fixture) => f.calls.filter((x) => x.method === 'PUT');
const railCalls = (f: Fixture) => f.calls.filter((x) => x.path.startsWith('/api/rail/'));
function required<T>(value: T | null | undefined): T {
  if (value === null || value === undefined) throw new Error('Required fictional fixture value is absent.');
  return value;
}
const saved = (f: Fixture, id: string) => required(f.plans.get(id)).plan;
async function setup(page: Page, walking = false) {
  await page.clock.install({ time: new Date('2026-10-21T00:01:00Z') });
  const fixture = await isolated(page, walking, true);
  const id = await create(page, walking);
  return { fixture, id };
}
async function open(page: Page) {
  await page.locator('#open-rail').click();
  await expect(page.locator('#rail-origin')).toBeFocused();
}
async function stations(page: Page, mode = 'train') {
  await page.locator('#rail-origin').fill('Север');
  await expect(page.getByRole('option', { name: /Учебная Северная/ })).toBeVisible();
  await page.locator('#rail-origin').press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.locator('#rail-destination').fill('Южная');
  await expect(page.getByRole('option', { name: /Учебная Южная/ })).toBeVisible();
  await page.locator('#rail-destination').press('Enter');
  await page.getByLabel('Дата отправления', { exact: true }).fill('2026-10-21');
  await picker(page).locator('select[name="transport"]').selectOption(mode);
}
async function search(page: Page) {
  await open(page);
  await stations(page);
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(picker(page).getByRole('heading', { name: 'Результаты расписания' })).toBeVisible();
}
async function stage(page: Page, date = '2026-11-14') {
  await page.locator('#trip-rail-day').selectOption(date);
  await choose(page).click();
  await expect(picker(page)).toHaveCount(0);
}
async function draft(page: Page) {
  return page
    .locator('form')
    .first()
    .locator('input,select,textarea')
    .evaluateAll((fields) => fields.map((field) => ({ id: field.id, value: (field as HTMLInputElement).value })));
}
function block(note: string | null = 'FICTIONAL prior note'): Plan['days'][number]['blocks'][number] {
  return {
    blockId: randomUUID(),
    title: 'FICTIONAL prior intention',
    note,
    startTime: '08:00',
    endTime: '09:00',
    estimate: { amount: '12.34', currency: 'RUB' },
  };
}

for (const width of [1280, 390]) {
  test(`RAIL01 FICTIONAL walking Save then Rail stage edit Save reload at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    const { fixture, id } = await setup(page, true);
    await page.getByRole('button', { name: 'Предложить прогулочный маршрут' }).click();
    await page.getByRole('button', { name: 'Перенести в редактор' }).click();
    await expect(names(page)).toHaveCount(2);
    await save(page).click();
    await expect(page.locator('#open-rail')).toBeEnabled();
    const prior = structuredClone(saved(fixture, id)),
      beforeCalls = fixture.calls.length;
    await open(page);
    expect(railCalls(fixture)).toHaveLength(0);
    await expect(page.locator('#trip-rail-day')).toHaveValue('');
    await expect(page.getByLabel('Дата отправления', { exact: true })).toHaveValue('');
    expect(
      await picker(page).evaluate((el) => ({
        parentForm: !!el.closest('form'),
        nested: el.querySelectorAll('main,h1').length,
      })),
    ).toEqual({ parentForm: false, nested: 0 });
    await expect(page.locator('h1')).toHaveCount(1);
    const actualDocument = await page.request.get(page.url(), { maxRedirects: 0 });
    expect(actualDocument.status()).toBe(200);
    atomicJson(resolve(browserArtifacts, `rail-document-policy-${width}.json`), {
      url: actualDocument.url(),
      status: actualDocument.status(),
      headers: actualDocument.headers(),
      sha256: createHash('sha256')
        .update(await actualDocument.body())
        .digest('hex'),
      policies: await page
        .locator('meta[http-equiv="Content-Security-Policy"]')
        .evaluateAll((elements) => elements.map((el) => el.getAttribute('content'))),
      boundary: 'Actual local Angular document and active client meta; no external source followed.',
    });
    await stations(page);
    await page.getByLabel('Дата отправления', { exact: true }).press('Enter');
    await expect(page.getByTestId('rail-price')).toHaveText('9007199254740993.05 RUB');
    await expect(page.getByTestId('rail-price').locator('..')).toContainText('Учебная цена');
    await expect(page.getByTestId('rail-price').locator('..')).not.toContainText('по данным Яндекс');
    await expect(page.getByTestId('rail-fictional')).toBeVisible();
    await expect(picker(page)).toContainText('IANA неизвестны');
    await expect(picker(page)).toContainText('Время обновления поставщиком неизвестно');
    await expect(picker(page)).toContainText('Наличие мест неизвестно');
    await expect(picker(page)).toContainText('не является суммой на всех пассажиров');
    await expect(picker(page)).toContainText('2026-10-21 10:00:00 UTC+03:00');
    await expect(picker(page)).toContainText('Справочник получен');
    await expect(picker(page)).toContainText('Расписание истекает');
    await expect(page.locator('iframe')).toHaveAttribute('sandbox', '');
    await expect(page.locator('iframe')).toHaveAttribute('referrerpolicy', 'no-referrer');
    await expect(page.frameLocator('iframe').getByText('RAIL · Учебная демонстрация', { exact: true })).toBeVisible();
    await page.locator('iframe').scrollIntoViewIfNeeded();
    await expect(
      page.frameLocator('iframe').getByText('RAIL · Учебная демонстрация', { exact: true }),
    ).toBeInViewport();
    await page.screenshot({ path: resolve(browserArtifacts, `rail-picker-${width}.png`), fullPage: true });
    await page
      .locator('.rail-picker')
      .screenshot({ path: resolve(browserArtifacts, `rail-picker-detail-${width}.png`) });
    await choose(page).click();
    await expect(page.locator('#trip-rail-status')).toContainText('Выберите день');
    await page.locator('#trip-rail-day').selectOption('2026-10-22');
    await expect(page.getByText('Есть несохранённые изменения')).toHaveCount(0);
    await expect(page.getByLabel('Дата отправления', { exact: true })).toHaveValue('2026-10-21');
    await choose(page).evaluate((el: HTMLButtonElement) => {
      el.click();
      el.click();
    });
    await expect(names(page)).toHaveCount(3);
    await expect(names(page).last()).toBeFocused();
    await expect(names(page).last()).toHaveValue('Поезд 001 (демо)');
    expect(fixture.calls.slice(beforeCalls).filter((x) => x.path.startsWith('/api/trips/'))).toEqual([]);
    const note = page.getByLabel('Заметка (до 500 символов)').last();
    await expect(note).toHaveValue(/Вымышленное демо; историческое намерение/);
    await expect(page.getByLabel('Начало (местное)').last()).toHaveValue('');
    await expect(page.getByLabel('Конец в тот же день').last()).toHaveValue('');
    await expect(page.getByLabel('Ваша оценка, RUB').last()).toHaveValue('');
    await names(page).last().fill('Поезд 001 (демо) — мой переезд');
    await page.screenshot({ path: resolve(browserArtifacts, `rail-staged-${width}.png`), fullPage: true });
    await page
      .locator('fieldset.block')
      .last()
      .screenshot({ path: resolve(browserArtifacts, `rail-staged-detail-${width}.png`) });
    await save(page).click();
    await expect(page.locator('#open-rail')).toBeEnabled();
    const result = structuredClone(saved(fixture, id)),
      added = required(result.days[0].blocks.at(-1));
    expect(result.days[0].blocks.slice(0, -1)).toEqual(prior.days[0].blocks);
    expect(result.brief).toEqual(prior.brief);
    expect(result.title).toBe(prior.title);
    expect(added).toMatchObject({ startTime: null, endTime: null, estimate: null });
    expect(added.note).toContain('Получено=2026-10-21T00:01:00.000Z; истекает=2026-10-21T00:06:00.000Z.');
    expect(added.note).not.toContain('9007199254740993.05');
    expect(puts(fixture)).toHaveLength(2);
    expect(JSON.parse(required(puts(fixture)[1].body))).toEqual({
      title: result.title,
      brief: result.brief,
      days: result.days,
    });
    await page.reload();
    await expect(names(page).last()).toHaveValue(added.title);
    await expect(note).toHaveValue(required(added.note));
    await expect(picker(page)).toHaveCount(0);
    await expect(page.locator('iframe')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: resolve(browserArtifacts, `rail-reloaded-${width}.png`), fullPage: true });
    await page
      .locator('fieldset.block')
      .last()
      .screenshot({ path: resolve(browserArtifacts, `rail-reloaded-detail-${width}.png`) });
    for (const call of railCalls(fixture)) {
      expect(call.owner).toBeUndefined();
      expect(`${call.path}${call.body ?? ''}`).not.toMatch(
        /Fictional private plan|15000|architecture|adults|interests/,
      );
    }
  });
}

test('RAIL02 FICTIONAL nonempty other-city fourteen-day plan preserves manual bounds and walking empty-only guard', async ({
  page,
}) => {
  const { fixture, id } = await setup(page, true),
    p = saved(fixture, id);
  p.brief.destinationId = 'kaliningrad';
  p.destination = {
    destinationId: 'kaliningrad',
    displayName: 'Калининград',
    timeZone: 'Europe/Kaliningrad',
    catalogueVersion: '1',
  };
  p.brief.endDate = '2026-11-04';
  p.days = Array.from({ length: 14 }, (_, i) => ({
    date: new Date(Date.parse('2026-10-22') + i * 86400000).toISOString().slice(0, 10),
    blocks: i === 0 ? [block()] : [],
  }));
  p.unknownEstimateCount = 0;
  p.knownUserEstimateSubtotal = '12.34';
  await page.reload();
  await expect(names(page)).toHaveCount(1);
  await expect(page.getByRole('button', { name: 'Предложить прогулочный маршрут' })).toBeDisabled();
  const before = structuredClone(p);
  await search(page);
  await stage(page, '2026-11-04');
  await save(page).click();
  await expect(page.locator('#open-rail')).toBeEnabled();
  expect(saved(fixture, id).days.slice(0, -1)).toEqual(before.days.slice(0, -1));
  expect(saved(fixture, id).brief).toEqual(before.brief);
  expect(saved(fixture, id).days[13].blocks).toHaveLength(1);
});

for (const fault of ['full-day', 'note-scalars', 'utf8-body'] as const) {
  test(`RAIL03 FICTIONAL ${fault} refusal leaves exact draft unchanged`, async ({ page }) => {
    const { fixture, id } = await setup(page),
      p = saved(fixture, id);
    if (fault === 'full-day') p.days[0].blocks = Array.from({ length: 8 }, () => block());
    if (fault === 'utf8-body') {
      p.brief.endDate = '2026-11-27';
      p.days = Array.from({ length: 14 }, (_, i) => ({
        date: new Date(Date.parse('2026-11-14') + i * 86400000).toISOString().slice(0, 10),
        blocks: Array.from({ length: i === 13 ? 7 : 8 }, () => ({
          ...block('界'.repeat(500)),
        })),
      }));
    }
    if (fault === 'note-scalars') {
      fixture.rail.reference = (r) => {
        r.suggestions.forEach((s) => {
          s.reference.code = 's' + (s.reference.code === 's1' ? '1' : '2').repeat(127);
        });
        return r;
      };
      fixture.rail.result = (r) => {
        r.items.forEach((j) => {
          j.threadReference = 't'.repeat(100);
        });
        return r;
      };
    }
    const totals = tripTotals(p as unknown as Parameters<typeof tripTotals>[0]);
    p.knownUserEstimateSubtotal = totals.subtotal;
    p.unknownEstimateCount = totals.unknown;
    p.warnings = totals.warnings;
    if (fault === 'utf8-body') {
      let fits = false;
      for (let length = 500; length >= 0; length--) {
        try {
          decodeTripPlan(p);
          fits = true;
          break;
        } catch {
          for (const day of p.days) for (const b of day.blocks) b.note = required(b.note).slice(0, -1);
        }
      }
      expect(fits).toBe(true);
      // Fill the sub-step using the actual detail codec, rather than assuming write/detail overheads.
      const first = p.days[0].blocks[0];
      for (let remaining = 500 - Array.from(required(first.note)).length; remaining > 0; remaining--) {
        first.note += '界';
        try {
          decodeTripPlan(p);
        } catch {
          first.note = required(first.note).slice(0, -1);
          break;
        }
      }
      expect(131072 - Buffer.byteLength(JSON.stringify(p))).toBeLessThan(3);
    }
    tripWrite(decodeTripPlan(p));
    await page.reload();
    await expect(page.locator('#open-rail')).toBeEnabled();
    const before = await draft(page);
    await search(page);
    await page.locator('#trip-rail-day').selectOption(fault === 'utf8-body' ? '2026-11-27' : '2026-11-14');
    await choose(page).click();
    await expect(page.locator('#trip-rail-status')).toContainText(
      fault === 'full-day' ? '8 блоков' : fault === 'note-scalars' ? '500 символов' : '128 КиБ',
    );
    expect(await draft(page)).toEqual(before);
    expect(puts(fixture)).toHaveLength(0);
  });
}

const variants: [string, (r: RailSearchResult) => void, boolean][] = [
  [
    'null-ttl',
    (r) => {
      r.expiresAt = null;
    },
    false,
  ],
  [
    'damaged-tickets',
    (r) => {
      r.items[0].tickets.damaged = true;
      r.items[0].tickets.faresState = 'limited';
      r.items[0].tickets.reasons = ['invalid-fare'];
      r.expiresAt = '2026-10-21T00:01:30.000Z';
    },
    false,
  ],
  [
    'missing-tickets',
    (r) => {
      delete (r.items[0] as Partial<(typeof r.items)[0]>).tickets;
    },
    false,
  ],
  [
    'missing-mode',
    (r) => {
      r.modes = [];
    },
    false,
  ],
  [
    'unsafe-limit',
    (r) => {
      r.kind = 'limited';
      r.reasons = ['unrecognized-limit'];
      r.modes[0].kind = 'limited';
      r.modes[0].reasons = ['unrecognized-limit'];
    },
    false,
  ],
  [
    'safe-page-limit',
    (r) => {
      r.kind = 'limited';
      r.reasons = ['page-limit'];
      r.modes[0].kind = 'limited';
      r.modes[0].reasons = ['page-limit'];
    },
    true,
  ],
  [
    'safe-interval-limit',
    (r) => {
      r.kind = 'limited';
      r.reasons = ['interval-service-unsupported'];
      r.modes[0].kind = 'limited';
      r.modes[0].reasons = ['interval-service-unsupported'];
    },
    true,
  ],
  [
    'profile-mismatch',
    (r) => {
      r.profileEpoch = 'c'.repeat(32);
    },
    false,
  ],
  [
    'generation-mismatch',
    (r) => {
      r.request.catalogueGeneration = 'c'.repeat(32);
    },
    false,
  ],
  [
    'number-fallback',
    (r) => {
      r.items[0].number = '9'.repeat(110);
    },
    true,
  ],
];
for (const [fault, mutate, allowed] of variants) {
  test(`RAIL04 FICTIONAL ${fault} ${allowed ? 'allows one ordinary intention' : 'cannot stage'}`, async ({ page }) => {
    const { fixture } = await setup(page);
    fixture.rail.result = (r) => {
      mutate(r);
      return r;
    };
    await open(page);
    await stations(page);
    const response = page.waitForResponse((r) => new URL(r.url()).pathname === '/api/rail/search');
    await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
    await response;
    await expect(picker(page).getByRole('status')).not.toHaveText('');
    if (allowed) {
      await expect(choose(page)).toBeEnabled();
      await stage(page);
      await expect(names(page)).toHaveCount(1);
      if (fault === 'number-fallback') await expect(names(page)).toHaveValue('Поезд (демо)');
    } else {
      await expect.poll(async () => (await choose(page).count()) === 0 || (await choose(page).isDisabled())).toBe(true);
      await expect(names(page)).toHaveCount(0);
    }
    expect(puts(fixture)).toHaveLength(0);
  });
}

for (const preset of ['empty', 'interval', 'failed'] as const) {
  test(`RAIL05 FICTIONAL ${preset} remains distinct without invented selectable rows`, async ({ page }) => {
    const { fixture } = await setup(page);
    fixture.rail.preset = preset;
    await search(page);
    await expect(picker(page)).toContainText(
      preset === 'empty'
        ? 'Поездок в выбранном расписании не найдено'
        : preset === 'interval'
          ? 'Получена ограниченная часть расписания'
          : 'Не удалось получить расписание',
    );
    await expect(choose(page)).toHaveCount(0);
    expect(puts(fixture)).toHaveLength(0);
  });
}

test('RAIL06 FICTIONAL mixed skipped mode warning retains safe selected train', async ({ page }) => {
  const { fixture } = await setup(page);
  fixture.rail.result = (r) => {
    r.kind = 'limited';
    r.reasons = ['quota'];
    r.modes[1] = {
      transport: 'suburban',
      kind: 'skipped',
      reasons: ['quota'],
      originalCount: 0,
      normalizedCount: 0,
      receivedAt: null,
    };
    r.items = r.items.slice(0, 1);
    r.normalizedCount = 1;
    return r;
  };
  await open(page);
  await stations(page, 'all');
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(picker(page)).toContainText('не запущены');
  await stage(page);
  await expect(names(page)).toHaveCount(1);
  expect(puts(fixture)).toHaveLength(0);
});

for (const failure of ['frame-error', 'enforced-csp', 'report-csp', 'actual-csp'] as const) {
  test(`RAIL07 FICTIONAL actual handler ${failure} controls staging`, async ({ page }) => {
    const { fixture } = await setup(page);
    await search(page);
    await page.locator('#trip-rail-day').selectOption('2026-11-14');
    if (failure === 'actual-csp')
      await page.evaluate(() => {
        const p = document.createElement('meta');
        p.httpEquiv = 'Content-Security-Policy';
        p.content = "frame-src 'none'";
        document.head.append(p);
        const f = document.querySelector('iframe');
        if (!f) throw new Error('Expected actual Rail frame');
        f.setAttribute('src', f.src);
      });
    else if (failure === 'frame-error') await page.locator('iframe').dispatchEvent('error');
    else
      await page.evaluate(
        (report) =>
          document.dispatchEvent(
            new SecurityPolicyViolationEvent('securitypolicyviolation', {
              effectiveDirective: 'frame-src',
              disposition: report ? 'report' : 'enforce',
              blockedURI: new URL('/rail-fictional-banner.html', location.href).href,
            }),
          ),
        failure === 'report-csp',
      );
    if (failure === 'report-csp') {
      await expect(choose(page)).toBeEnabled();
      await stage(page);
      await expect(names(page)).toHaveCount(1);
    } else {
      await expect(picker(page)).toContainText('Данные очищены');
      await expect(choose(page)).toHaveCount(0);
      await expect(page.locator('iframe')).toHaveCount(0);
      await expect(names(page)).toHaveCount(0);
    }
    expect(puts(fixture)).toHaveLength(0);
  });
}

test('RAIL08 SYNTHETIC real profile under induced enforcing self CSP cannot stage persist or fall back', async ({
  page,
}) => {
  const { fixture } = await setup(page);
  fixture.rail.reference = (r) => {
    r.profile = 'real';
    required(r.attribution).frameUrl = 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html';
    return r;
  };
  await open(page);
  await expect(page.locator('meta[data-travel-frame-policy]')).toHaveAttribute(
    'content',
    /https:\/\/yandex.st\/rasp\/media\/apicc\/copyright_vert_mono.html/,
  );
  // Extra enforcing policy intersects the actual shared policy; it never permits an external frame.
  await page.evaluate(() => {
    const policy = document.createElement('meta');
    policy.httpEquiv = 'Content-Security-Policy';
    policy.content = "frame-src 'self'";
    document.head.append(policy);
  });
  await page.locator('#rail-origin').fill('Север');
  await expect(picker(page)).toContainText('Данные очищены');
  await expect(page.locator('iframe')).toHaveCount(0);
  await expect(choose(page)).toHaveCount(0);
  await expect(names(page)).toHaveCount(0);
  expect(railCalls(fixture)).toHaveLength(1);
  expect(puts(fixture)).toHaveLength(0);
});

test('RAIL13 FICTIONAL keyboard Escape close reopen and late station A B A preserve current focus and choice', async ({
  page,
}) => {
  const { fixture } = await setup(page);
  await page.locator('#open-rail').focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#rail-origin')).toBeFocused();
  const held = fixture.hold('rail-stations');
  fixture.rail.reference = (r) => {
    r.suggestions.forEach((s) => {
      s.name = 'FICTIONAL obsolete North';
    });
    return r;
  };
  await page.locator('#rail-origin').fill('Север');
  await held.started;
  held.detach();
  fixture.rail.reference = (r) => {
    r.suggestions.forEach((s) => {
      s.name = 'FICTIONAL current North';
    });
    return r;
  };
  await page.locator('#rail-origin').fill('Северн');
  await expect(picker(page).getByRole('option', { name: /FICTIONAL current North/ })).toBeVisible();
  await page.locator('#rail-origin').fill('Север');
  await expect.poll(() => railCalls(fixture).length).toBe(3);
  await expect(picker(page).getByRole('option', { name: /FICTIONAL current North/ })).toBeVisible();
  await page.locator('#rail-origin').press('Escape');
  await expect(picker(page).locator('[role="listbox"] [role="option"]')).toHaveCount(0);
  await page.getByRole('button', { name: 'Закрыть поиск поезда' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('#open-rail')).toBeFocused();
  held.release();
  await held.settled;
  await expect(picker(page)).toHaveCount(0);
  await expect(page.locator('#open-rail')).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#rail-origin')).toBeFocused();
  await expect(page.locator('#rail-origin')).toHaveValue('');
  expect(railCalls(fixture)).toHaveLength(3);
  expect(puts(fixture)).toHaveLength(0);
});

for (const fault of ['null-reference-ttl', 'changed-reference-generation', 'changed-reference-profile'] as const) {
  test(`RAIL14 FICTIONAL ${fault} requires explicit station reselection and never stages`, async ({ page }) => {
    const { fixture } = await setup(page);
    await search(page);
    fixture.rail.reference = (r) => {
      if (fault === 'null-reference-ttl') r.expiresAt = null;
      if (fault === 'changed-reference-generation') r.catalogueGeneration = 'c'.repeat(32);
      if (fault === 'changed-reference-profile') r.profileEpoch = 'c'.repeat(32);
      return r;
    };
    const response = page.waitForResponse((r) => new URL(r.url()).pathname === '/api/rail/stations');
    await page.locator('#rail-origin').fill('Северн');
    await response;
    await expect(choose(page)).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Найти расписание', exact: true })).toBeDisabled();
    await expect(names(page)).toHaveCount(0);
    expect(puts(fixture)).toHaveLength(0);
  });
}

test('RAIL15 FICTIONAL mixed failed mode retains independent train and warning', async ({ page }) => {
  const { fixture } = await setup(page);
  fixture.rail.result = (r) => {
    r.kind = 'limited';
    r.reasons = ['supplier-http'];
    r.modes[1] = {
      transport: 'suburban',
      kind: 'failed',
      reasons: ['supplier-http'],
      originalCount: 0,
      normalizedCount: 0,
      receivedAt: null,
    };
    r.items = r.items.slice(0, 1);
    r.normalizedCount = 1;
    return r;
  };
  await open(page);
  await stations(page, 'all');
  await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
  await expect(picker(page)).toContainText('ошибка источника');
  await expect(choose(page)).toBeEnabled();
  await stage(page);
  expect(puts(fixture)).toHaveLength(0);
});

test('RAIL16 FICTIONAL all skipped is distinct from failed and successful empty', async ({ page }) => {
  const { fixture } = await setup(page);
  fixture.rail.result = (r) => {
    r.kind = 'skipped';
    r.reasons = ['quota'];
    r.items = [];
    r.normalizedCount = 0;
    r.receivedAt = null;
    r.expiresAt = null;
    r.attribution = null;
    r.modes = r.modes.map((m) => ({
      ...m,
      kind: 'skipped',
      reasons: ['quota'],
      originalCount: 0,
      normalizedCount: 0,
      receivedAt: null,
    }));
    return r;
  };
  await search(page);
  await expect(picker(page)).toContainText('Поиск не запущен: источник выключен или ограничен квотой');
  await expect(choose(page)).toHaveCount(0);
  expect(puts(fixture)).toHaveLength(0);
});

test('RAIL09 FICTIONAL expired at click before delayed timer refuses stage', async ({ page }) => {
  const { fixture } = await setup(page);
  await search(page);
  await page.locator('#trip-rail-day').selectOption('2026-11-14');
  await page.clock.setSystemTime(new Date('2026-10-21T00:07:00Z'));
  await choose(page).evaluate((el: HTMLButtonElement) => el.click());
  await expect(names(page)).toHaveCount(0);
  expect(puts(fixture)).toHaveLength(0);
});

test('RAIL10 FICTIONAL criteria A B A and latest failed reference revoke previous result without retry', async ({
  page,
}) => {
  const { fixture } = await setup(page);
  await search(page);
  await page.getByLabel('Дата отправления', { exact: true }).fill('2026-10-22');
  await page.getByLabel('Дата отправления', { exact: true }).fill('2026-10-21');
  await expect(choose(page)).toHaveCount(0);
  expect(railCalls(fixture).filter((c) => c.path.endsWith('/search'))).toHaveLength(1);
  fixture.rail.stationStatus = 503;
  await page.locator('#rail-origin').fill('Северная');
  await expect.poll(() => railCalls(fixture).length).toBe(4);
  await expect(picker(page).locator('[role="listbox"] [role="option"]')).toHaveCount(0);
  await expect(names(page)).toHaveCount(0);
  expect(puts(fixture)).toHaveLength(0);
});

for (const action of [
  'edit-title',
  'dirty-brief',
  'close',
  'refresh',
  'save',
  'logout',
  'owner-switch',
  'new-plan',
  'delete',
] as const) {
  test(`RAIL11 FICTIONAL pending reply after ${action} cannot reopen overwrite stage or steal focus`, async ({
    page,
  }) => {
    const { fixture } = await setup(page);
    await open(page);
    await stations(page);
    const gate = fixture.hold('rail-search');
    await page.getByRole('button', { name: 'Найти расписание', exact: true }).click();
    await gate.started;
    if (action === 'edit-title') await page.getByLabel('Название плана', { exact: true }).fill('FICTIONAL newer edit');
    if (action === 'dirty-brief') await page.getByLabel('Последний день').fill('2026-11-16');
    if (action === 'close') await page.getByRole('button', { name: 'Закрыть поиск поезда' }).click();
    if (action === 'refresh') await page.getByRole('button', { name: 'Перечитать план', exact: true }).click();
    if (action === 'save') {
      // Clean Save is intentionally a no-op. An actual mutation requires a local edit first.
      await page.getByLabel('Название плана', { exact: true }).fill('FICTIONAL explicit Save draft');
      const reply = page.waitForResponse((r) => r.request().method() === 'PUT');
      await save(page).click();
      await reply;
    }
    if (action === 'logout' || action === 'owner-switch') {
      await page.getByRole('button', { name: 'Выйти', exact: true }).click();
      if (action === 'owner-switch') await page.getByRole('button', { name: 'Войти', exact: true }).click();
    }
    if (action === 'new-plan') await page.getByRole('button', { name: 'Новый план', exact: true }).click();
    if (action === 'delete') {
      await page.getByRole('button', { name: 'Удалить план', exact: true }).click();
      await page.getByRole('button', { name: 'Подтвердить удаление' }).click();
    }
    await expect(picker(page)).toHaveCount(0);
    const focused = await page.evaluate(() => document.activeElement?.id);
    gate.release();
    await gate.settled;
    await page.evaluate(() => new Promise<void>((r) => requestAnimationFrame(() => requestAnimationFrame(() => r()))));
    await expect(picker(page)).toHaveCount(0);
    await expect(choose(page)).toHaveCount(0);
    expect(await page.evaluate(() => document.activeElement?.id)).toBe(focused);
    expect(puts(fixture)).toHaveLength(action === 'save' ? 1 : 0);
    if (action === 'edit-title')
      await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('FICTIONAL newer edit');
    if (action === 'dirty-brief') await expect(page.getByLabel('Последний день')).toHaveValue('2026-11-16');
  });
}

for (const outcome of ['conflict', 'lost-put', 'pending-save-edit'] as const) {
  test(`RAIL12 FICTIONAL staged ${outcome} keeps M1 draft and sends no automatic retry`, async ({ page }) => {
    const { fixture, id } = await setup(page);
    await search(page);
    await stage(page);
    const idBefore = await names(page).getAttribute('id');
    if (outcome === 'conflict') saved(fixture, id).revision = '2';
    if (outcome === 'lost-put') fixture.loseNextSave();
    const held = outcome === 'pending-save-edit' ? fixture.hold('put') : null;
    await save(page).click();
    if (held) {
      await held.started;
      await page.getByLabel('Последний день').fill('2026-11-16');
      held.release();
      await held.settled;
      await expect(page.getByLabel('Последний день')).toHaveValue('2026-11-16');
    } else {
      await expect(
        page.getByRole('heading', {
          name: outcome === 'conflict' ? 'Нужна сверка с сервером' : 'Результат действия неизвестен',
        }),
      ).toBeVisible();
      await expect(save(page)).toBeDisabled();
    }
    await expect(names(page)).toHaveCount(1);
    expect(await names(page).getAttribute('id')).toBe(idBefore);
    expect(puts(fixture)).toHaveLength(1);
    expect(puts(fixture)[0].ifMatch).toBe('"1"');
    await expect(page.locator('#open-rail')).toBeDisabled();
  });
}
