import { expect, type Page, test } from '@playwright/test';
import type { FlightLinkLeg } from '@travel/api-client';
import { create, isolated, recordReceipt } from './trips-fixture';

test.afterEach(async ({ page }, info) => recordReceipt(page, info));
const section = (page: Page) => page.getByRole('region', { name: 'Авиазаказы этой поездки' });
const openPicker = (page: Page) => section(page).getByRole('button', { name: 'Выбрать авиазаказ' });
async function attach(page: Page, index: number) {
  await openPicker(page).click();
  await section(page)
    .getByRole('article', { name: 'Заказ в списке ' + index, exact: true })
    .getByRole('button', { name: 'Прикрепить' })
    .press('Enter');
  await expect(section(page).getByRole('heading', { name: 'Мои авиазаказы' })).toHaveCount(0);
}

test.describe('automatic calendar placement', () => {
  test.use({ timezoneId: 'America/Los_Angeles' });
  const calendar = (page: Page) => page.locator('.calendar');
  const outbound: FlightLinkLeg = {
    origin: 'KGD',
    destination: 'LED',
    departAt: '2027-06-12T23:40:00+02:00',
    arriveAt: '2027-06-13T02:10:00+03:00',
  };

  async function openPlan(page: Page, endDate: '2027-06-13' | '2027-06-15', routes: FlightLinkLeg[][]) {
    const fixture = await isolated(page, false, false, true);
    const id = await create(page);
    const saved = fixture.plans.get(id)!;
    const firstDay = '2027-06-13';
    const dates = endDate === firstDay ? [firstDay] : [firstDay, '2027-06-14', endDate];
    saved.plan.brief = { ...saved.plan.brief, startDate: firstDay, endDate };
    saved.plan.days = dates.map((date) => ({
      date,
      blocks:
        date === firstDay
          ? [
              {
                blockId: '22222222-2222-4222-8222-222222222222',
                title: 'Экскурсия по центру',
                note: null,
                startTime: '09:00',
                endTime: '11:00',
                estimate: { amount: '0.00', currency: 'RUB' },
              },
            ]
          : [],
    }));
    const ownOrders = [...fixture.flightLinks.orders.values()].filter((entry) => entry.owner === saved.owner);
    for (const [index, route] of routes.entries()) ownOrders[index].facts.route = route;
    await page.reload();
    await expect(calendar(page).getByRole('heading', { name: /2027-06-13/ })).toBeVisible();
    await expect(openPicker(page)).toBeEnabled();
    const start = fixture.calls.length;
    for (let index = 1; index <= routes.length; index++) await attach(page, index);
    return { fixture, id, start, ownOrders };
  }

  test('places overnight arrival next to the excursion and the same order return after the calendar', async ({
    page,
  }) => {
    const returning: FlightLinkLeg = {
      origin: 'LED',
      destination: 'KGD',
      departAt: '2027-06-16T00:40:00+03:00',
      arriveAt: '2027-06-16T01:30:00+02:00',
    };
    const { fixture, id, start, ownOrders } = await openPlan(page, '2027-06-15', [[outbound, returning]]);
    const plan = calendar(page);
    const firstDay = plan.getByRole('region', { name: 'День 1 2027-06-13' });
    const arrival = firstDay.getByRole('article');
    await expect(arrival.locator('time').nth(1)).toHaveAttribute('datetime', outbound.arriveAt);
    await expect(arrival).toContainText('По расписанию из Flights');
    await expect(arrival).toContainText('Местное время LED');
    await expect(arrival).toContainText('UTC+03:00');
    await expect(firstDay.getByLabel('Название блока')).toHaveValue('Экскурсия по центру');
    await expect(firstDay.getByLabel('Начало (местное)')).toHaveValue('09:00');
    await expect(firstDay).toContainText('1/8 блоков');
    const lastSection = plan.getByRole('region', { name: 'После окончания поездки' });
    await expect(lastSection.getByRole('article').locator('time').first()).toHaveAttribute(
      'datetime',
      returning.departAt,
    );
    await expect(plan.getByRole('article')).toHaveCount(2);
    await expect(plan.getByRole('radio')).toHaveCount(0);
    await expect(plan.getByRole('region', { name: 'До начала поездки' })).toHaveCount(0);
    await expect(plan.getByRole('region', { name: 'По обе стороны дат поездки' })).toHaveCount(0);
    const orderPath = '/flights/orders/' + ownOrders[0].facts.orderId;
    const link = arrival.getByRole('link', { name: 'Открыть заказ' });
    await expect(lastSection.getByRole('link')).toHaveAttribute('href', orderPath);
    await link.focus();
    await expect(link).toBeFocused();
    expect(fixture.calls.slice(start).map(({ method, path }) => [method, path])).toEqual([
      ['GET', '/api/trips/' + id + '/flight-order-candidates'],
      ['PATCH', '/api/trips/' + id + '/flight-links'],
      ['GET', '/api/trips/' + id + '/flight-links/status'],
    ]);
    await link.press('Enter');
    await expect(page).toHaveURL(new RegExp(orderPath + '$'));
    expect(
      fixture.calls.slice(start).filter(({ method }) => ['PUT', 'POST', 'PATCH', 'DELETE'].includes(method)),
    ).toHaveLength(1);
  });

  test('keeps before and cross-boundary cards above the only plan day', async ({ page }) => {
    const before = { ...outbound, departAt: '2027-06-12T20:40:00+02:00', arriveAt: '2027-06-12T23:10:00+03:00' };
    const across = { ...outbound, arriveAt: '2027-06-14T02:10:00+03:00' };
    const { fixture, id, start } = await openPlan(page, '2027-06-13', [[before], [across]]);
    const plan = calendar(page);
    const sections = plan.locator(':scope > app-trips-flight-calendar-cards:has(article), :scope > .day');
    await expect(sections).toHaveCount(3);
    await expect(sections.nth(0)).toContainText('До начала поездки');
    await expect(sections.nth(1)).toContainText('По обе стороны дат поездки');
    await expect(sections.nth(2)).toContainText('2027-06-13');
    await expect(sections.nth(1).locator('time').nth(1)).toHaveAttribute('datetime', across.arriveAt);
    await expect(plan.getByRole('region', { name: 'После окончания поездки' })).toHaveCount(0);
    expect(fixture.calls.slice(start).map(({ method, path }) => [method, path])).toEqual(
      Array.from({ length: 2 }, () => [
        ['GET', '/api/trips/' + id + '/flight-order-candidates'],
        ['PATCH', '/api/trips/' + id + '/flight-links'],
        ['GET', '/api/trips/' + id + '/flight-links/status'],
      ]).flat(),
    );
  });
});

test('unlink preserves the remaining observation without requesting statuses', async ({ page }) => {
  const fixture = await isolated(page, false, false, true);
  const id = await create(page);
  await attach(page, 1);
  await attach(page, 2);
  const [remaining, removed] = fixture.flightLinks.collections.get(id)!.links;
  const cards = section(page).getByRole('article');
  await expect(cards.first().getByRole('button')).toBeEnabled();
  const observation = await cards.first().innerText();
  fixture.flightLinks.unavailable.add(remaining.orderId);
  const start = fixture.calls.length;
  await cards.nth(1).getByRole('button', { name: 'Убрать из поездки' }).click();
  await expect(cards).toHaveCount(1);
  await expect(openPicker(page)).toBeEnabled();
  await expect(section(page).getByRole('status')).toHaveText('Связь удалена.');
  expect(fixture.calls.slice(start)).toEqual([
    expect.objectContaining({
      method: 'PATCH',
      path: '/api/trips/' + id + '/flight-links',
      body: JSON.stringify({ op: 'remove', orderId: removed.orderId }),
      status: 200,
    }),
  ]);
  expect(await cards.first().innerText()).toBe(observation);
});

test('unlink clears the old collection after the trip is deleted in another tab', async ({ page, context }) => {
  const fixture = await isolated(page, false, false, true);
  const id = await create(page);
  await attach(page, 1);
  const remove = section(page).getByRole('button', { name: 'Убрать из поездки' });
  await expect(remove).toBeEnabled();
  const other = await context.newPage();
  await other.goto('/trips/' + id);
  await other.getByRole('button', { name: 'Удалить план', exact: true }).click();
  await other.getByRole('button', { name: 'Подтвердить удаление', exact: true }).click();
  await expect(section(other)).toHaveCount(0);
  await other.close();
  expect(fixture.calls).toContainEqual(
    expect.objectContaining({ method: 'DELETE', path: '/api/trips/' + id, status: 204 }),
  );
  const start = fixture.calls.length;
  await remove.click();
  await expect(section(page).getByRole('article')).toHaveCount(0);
  await expect(section(page).getByRole('status')).toHaveText('Поездка или её авиазаказы больше недоступны.');
  await expect(openPicker(page)).toBeDisabled();
  expect(fixture.calls.slice(start)).toEqual([
    expect.objectContaining({ method: 'PATCH', path: '/api/trips/' + id + '/flight-links', status: 404 }),
  ]);
});

test('whole owned flight orders retain all legs, isolate owners and unlink during source loss', async ({ page }) => {
  const fixture = await isolated(page, false, false, true);
  const id = await create(page),
    oldPlan = structuredClone(fixture.plans.get(id)!.plan);
  await openPicker(page).press('Enter');
  await expect(page.getByRole('heading', { name: 'Мои авиазаказы' })).toBeFocused();
  expect(await section(page).evaluate((el) => !!el.closest('form'))).toBe(false);
  await section(page).getByRole('button', { name: 'Закрыть список' }).click();
  for (let index = 1; index <= 4; index++) await attach(page, index);
  await expect(openPicker(page)).toBeDisabled();
  await page.reload();
  const first = section(page).getByRole('article', { name: 'Прикреплённый заказ 1', exact: true });
  await expect(first.getByRole('listitem')).toHaveCount(4);
  await expect(first.getByText('2026-11-11T15:30:00+02:00', { exact: true })).toBeVisible();
  const links = fixture.flightLinks.collections.get(id)!;
  const [a, b, c] = links.links;
  await expect(first.getByRole('link', { name: 'Открыть заказ' })).toHaveAttribute(
    'href',
    '/flights/orders/' + a.orderId,
  );
  await first.getByRole('link').click();
  await expect(page.getByRole('heading', { name: 'Состояние заказа' })).toBeVisible();
  await expect(page).toHaveURL(new URL('/flights/orders/' + a.orderId, page.url()).href);
  await page.goBack();
  await expect(first.getByRole('listitem')).toHaveCount(4);
  const source = fixture.flightLinks.orders.get(a.orderId)!.facts;
  Object.assign(source, { bookingStatus: 'Cancelled', reviewRequired: true, streamVersion: '11' });
  fixture.flightLinks.unavailable.add(b.orderId);
  fixture.flightLinks.orders.delete(c.orderId);
  await section(page).getByRole('button', { name: 'Обновить сведения' }).click();
  await expect(first).toContainText('Отменён');
  await expect(first).toContainText('Требуется проверка');
  const second = section(page).getByRole('article', { name: 'Прикреплённый заказ 2', exact: true });
  await expect(second).toContainText('прежние сведения');
  const third = section(page).getByRole('article', { name: 'Прикреплённый заказ 3', exact: true });
  await expect(third).toContainText('Заказ не найден');
  await expect(third.getByRole('listitem')).toHaveCount(0);
  await second.getByRole('button', { name: 'Убрать из поездки' }).click();
  await expect(section(page)).toContainText('Прикреплено 3 из 4');
  expect(fixture.flightLinks.collections.get(id)?.linksRevision).toBe('5');
  expect(fixture.plans.get(id)!.plan).toEqual(oldPlan);
  await page.getByLabel('Название плана', { exact: true }).fill('Dirty fictional calendar');
  await expect(openPicker(page)).toBeDisabled();
  page.once('dialog', (d) => d.dismiss());
  await first.getByRole('link').click();
  await expect(page).toHaveURL(new RegExp('/trips/' + id + '$'));
  await expect(page.getByLabel('Название плана', { exact: true })).toHaveValue('Dirty fictional calendar');
  await page.getByLabel('Название плана', { exact: true }).fill(oldPlan.title);
  await page.reload();
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('недоступен');
  await expect(section(page)).toHaveCount(0);
  const response = await page.evaluate(
    async (trip) =>
      (
        await fetch('/api/trips/' + trip + '/flight-links', {
          headers: { Authorization: 'Bearer fictional-trips-owner-b' },
        })
      ).status,
    id,
  );
  expect(response).toBe(404);
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(section(page)).toContainText('Прикреплено 3 из 4');
  await page.getByRole('button', { name: 'Удалить план', exact: true }).click();
  await page.getByRole('button', { name: 'Подтвердить удаление', exact: true }).click();
  await expect(section(page)).toHaveCount(0);
  expect(fixture.flightLinks.collections.has(id)).toBe(false);
  expect(fixture.calls.filter((c) => c.method === 'PATCH')).toHaveLength(5);
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
});

test('lost add and remove responses require explicit reads and never replay a PATCH', async ({ page }) => {
  const fixture = await isolated(page, false, false, true);
  await create(page);
  const area = section(page);
  const writes = () => fixture.calls.filter((c) => c.method === 'PATCH').length;
  for (const op of ['add', 'remove'] as const) {
    if (op === 'add') await openPicker(page).click();
    const target = area
      .getByRole('button', { name: op === 'add' ? 'Прикрепить' : 'Убрать из поездки', exact: true })
      .first();
    fixture.flightLinks.loseNextPatch = true;
    await target.evaluate((button: HTMLButtonElement) => {
      button.click();
      button.click();
    });
    await expect(area).toContainText('Результат записи неизвестен');
    expect(writes()).toBe(op === 'add' ? 1 : 2);
    await area.getByRole('button', { name: 'Обновить сведения' }).click();
    await expect(area).toContainText('Прочитан текущий состав');
    await expect(area).toContainText(op === 'add' ? 'Прикреплено 1 из 4' : 'Прикреплено 0 из 4');
    expect(writes()).toBe(op === 'add' ? 1 : 2);
  }
});

test('disabled links make no private requests', async ({ page }) => {
  const fixture = await isolated(page);
  await create(page);
  await expect.poll(() => fixture.calls.some((c) => c.path === '/api/trips/flight-links/availability')).toBe(true);
  await expect(section(page)).toHaveCount(0);
  await expect(page.locator('.flight-calendar-card')).toHaveCount(0);
  expect(fixture.calls.filter((c) => /\/[0-9a-f-]{36}\/flight-/.test(c.path))).toEqual([]);
});

test('fictional owner discovery, checked no-ops and stale revisions use the same collection URI', async ({ page }) => {
  const fixture = await isolated(page, false, false, true),
    id = await create(page);
  await attach(page, 1);
  const own = fixture.flightLinks.collections.get(id)!.links[0].orderId;
  const foreign = [...fixture.flightLinks.orders.keys()].find((order) => order.startsWith('b'))!;
  const result = await page.evaluate(
    async ({ id, own, foreign }) => {
      const path = '/api/trips/' + id + '/flight-links';
      const attempt = async (op: string, orderId: string, revision: string) => {
        const r = await fetch(path, {
          method: 'PATCH',
          headers: {
            Authorization: 'Bearer fictional-trips-owner-a',
            'Content-Type': 'application/json',
            'If-Match': '"fl-' + revision + '"',
          },
          body: JSON.stringify({ op, orderId }),
        });
        return [r.status, r.headers.get('etag')];
      };
      const discovery = await fetch('/api/trips/' + id + '/flight-order-candidates?offset=0', {
        headers: { Authorization: 'Bearer fictional-trips-owner-a' },
      });
      const candidates = await discovery.json();
      return {
        discovered: candidates.items.map((row: { orderId: string }) => row.orderId),
        add: await attempt('add', own, '1'),
        remove: await attempt('remove', foreign, '1'),
        stale: await attempt('add', own, '0'),
        foreign: await attempt('add', foreign, '1'),
      };
    },
    { id, own, foreign },
  );
  expect(result.discovered).toContain(own);
  expect(result.discovered).not.toContain(foreign);
  expect(result.add).toEqual([200, '"fl-1"']);
  expect(result.remove).toEqual([200, '"fl-1"']);
  expect(result.stale[0]).toBe(412);
  expect(result.foreign[0]).toBe(404);
  expect(fixture.flightLinks.collections.get(id)!.links.map((link) => link.orderId)).toEqual([own]);
  expect(fixture.calls.filter((c) => c.method === 'PATCH')).toHaveLength(5);
});
