import { expect, type Page, test } from '@playwright/test';
import type { ConfirmedTripHotelLink, TripHotelLinkStatus } from '@travel/api-client';
import { create, isolated, recordReceipt } from './trips-fixture';

const confirmed: ConfirmedTripHotelLink = {
  state: 'confirmed',
  revision: '1',
  reservationId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
  propertyName: 'Гостевой дом <b>Белые ночи</b>',
  checkIn: '2027-06-13',
  checkOut: '2027-06-15',
};
const arrivalDay = '2027-06-13';
const middleDay = '2027-06-14';
const departureDay = '2027-06-15';
const hotelsPath = `/hotels/reservations/${confirmed.reservationId}`;
const calendarDay = (page: Page, date: string) => page.getByRole('region', { name: new RegExp(`День [0-9]+ ${date}`) });
const section = (page: Page) => page.getByRole('region', { name: 'Заказ Hotels', exact: true });

async function setup(page: Page) {
  const fixture = await isolated(page, false, false, true);
  const tripId = await create(page);
  const { plan, owner } = fixture.plans.get(tripId)!;
  const flightOrder = [...fixture.flightLinks.orders.values()].find((entry) => entry.owner === owner)!;
  flightOrder.facts.route = [
    { origin: 'KGD', destination: 'LED', departAt: '2027-06-13T07:00:00+02:00', arriveAt: '2027-06-13T10:00:00+03:00' },
    { origin: 'LED', destination: 'KGD', departAt: '2027-06-15T19:40:00+03:00', arriveAt: '2027-06-15T20:10:00+02:00' },
  ];
  plan.brief = { ...plan.brief, startDate: arrivalDay, endDate: departureDay };
  plan.manualStay = { placeName: 'Ручная запись', checkInDate: arrivalDay, checkOutDate: middleDay };
  plan.days = [arrivalDay, middleDay, departureDay].map((date, i) => ({
    date,
    blocks:
      i === 2
        ? []
        : [
            {
              blockId: i === 0 ? '11111111-1111-4111-8111-111111111111' : '22222222-2222-4222-8222-222222222222',
              title: i === 0 ? 'Прогулка по центру' : 'Экскурсия в музей',
              note: null,
              startTime: '11:00',
              endTime: '14:00',
              estimate: { amount: '0.00', currency: 'RUB' },
            },
          ],
  }));
  const link = {
    status: confirmed as TripHotelLinkStatus,
    hasLink: true,
    serverError: false,
    calls: [] as string[],
  };
  const path = `/api/trips/${tripId}/hotel-link`;
  await page.route(
    (url) =>
      url.pathname === '/api/trips/hotel-link/availability' ||
      url.pathname === path ||
      url.pathname === `${path}/status`,
    async (route) => {
      const request = route.request();
      const pathname = new URL(request.url()).pathname;
      const method = request.method();
      expect(request.headers()['authorization']).toBe(owner);
      link.calls.push(`${method} ${pathname}`);
      const send = (json: unknown, extra: Record<string, string> = {}, status = 200) =>
        route.fulfill({
          status,
          json,
          headers: { 'Cache-Control': 'no-store', ...extra },
        });
      if (pathname === '/api/trips/hotel-link/availability' && method === 'GET')
        return send({ enabled: true, canAttach: false });
      if (pathname === `${path}/status` && method === 'GET') {
        expect(request.headers()['if-none-match']).toBeUndefined();
        return link.serverError ? send({ code: 'Trips.Unavailable' }, {}, 503) : send(link.status);
      }
      expect(pathname).toBe(path);
      if (method === 'PATCH') {
        expect(request.headers()['if-match']).toBe('"hl-1"');
        expect(request.postDataJSON()).toEqual({ op: 'remove' });
        link.hasLink = false;
      } else expect(method).toBe('GET');
      const revision = link.hasLink ? '1' : '2';
      return send({ hasLink: link.hasLink, revision }, { ETag: `"hl-${revision}"` });
    },
  );
  await page.reload();
  await expect(section(page).getByRole('link', { name: 'Открыть заказ Hotels' })).toHaveAttribute('href', hotelsPath);
  return { fixture, link, tripId };
}

test.afterEach(async ({ page }, info) => recordReceipt(page, info));

for (const timezoneId of ['America/Los_Angeles', 'Asia/Tokyo']) {
  test.describe(timezoneId, () => {
    test.use({ timezoneId });
    test(`places hotel dates beside a manual stay, excursions and flights in ${timezoneId} without calendar reads`, async ({
      page,
    }) => {
      const { link } = await setup(page);
      const flights = page.getByRole('region', { name: 'Авиазаказы этой поездки' });
      await flights.getByRole('button', { name: 'Выбрать авиазаказ' }).click();
      await flights
        .getByRole('article', { name: 'Заказ в списке 1', exact: true })
        .getByRole('button', { name: 'Прикрепить' })
        .click();
      await expect(calendarDay(page, arrivalDay).getByRole('article', { name: 'KGD → LED' })).toBeVisible();
      await expect(calendarDay(page, departureDay).getByRole('article', { name: 'LED → KGD' })).toBeVisible();
      for (const [date, label] of [
        [arrivalDay, 'Заезд'],
        [middleDay, 'Проживание'],
        [departureDay, 'Выезд'],
      ]) {
        const day = calendarDay(page, date);
        await expect(day.getByRole('link', { name: confirmed.propertyName })).toHaveAttribute('href', hotelsPath);
        await expect(day.locator('.hotel-stay-day')).toContainText(`${label} · ${confirmed.propertyName} · Hotels`);
        await expect(day.locator('.hotel-stay-day b')).toHaveCount(0);
      }
      await expect(calendarDay(page, middleDay)).toContainText('Выезд · Ручная запись');
      await expect(calendarDay(page, middleDay).getByLabel('Название блока')).toHaveValue('Экскурсия в музей');
      await expect(calendarDay(page, middleDay)).toContainText('1/8 блоков');
      const calls = link.calls.length;
      await page.locator('#trip-end').fill(middleDay);
      await page.getByRole('button', { name: 'Применить параметры к календарю' }).click();
      await expect(calendarDay(page, departureDay)).toHaveCount(0);
      await expect(calendarDay(page, middleDay).locator('.hotel-stay-day')).toContainText('Проживание');
      await expect(section(page).locator(`time[datetime="${departureDay}"]`)).toHaveText(departureDay);
      expect(link.calls).toHaveLength(calls);
      await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
      await expect(page.locator('#trip-status')).toContainText('План сохранён');
      await page.reload();
      await expect(section(page).getByRole('link', { name: 'Открыть заказ Hotels' })).toHaveAttribute(
        'href',
        hotelsPath,
      );
      await expect(calendarDay(page, departureDay)).toHaveCount(0);
      await expect(calendarDay(page, middleDay).locator('.hotel-stay-day')).toContainText('Проживание');
      await expect(calendarDay(page, middleDay)).toContainText('Выезд · Ручная запись');
    });
  });
}

test('discards former private hotel facts on review, cancellation and service failure and keeps unlink available', async ({
  page,
}) => {
  const { link } = await setup(page);
  const refresh = section(page).getByRole('button', { name: 'Обновить сведения Hotels' });
  const order = page.locator(`a[href="${hotelsPath}"]`);
  for (const [state, message] of [
    ['reviewRequired', 'Заказ требует проверки в Hotels.'],
    ['cancelled', 'Заказ отменён.'],
    ['unavailable', 'Заказ недоступен.'],
  ] as const) {
    link.status = { state, revision: '1' };
    await refresh.press('Enter');
    await expect(section(page)).toContainText(message);
    await expect(order).toHaveCount(0);
    await expect(page.getByText(confirmed.propertyName, { exact: true })).toHaveCount(0);
    link.status = confirmed;
    await refresh.press('Enter');
    await expect(order).toHaveCount(4);
  }
  link.serverError = true;
  await refresh.press('Enter');
  await expect(section(page)).toContainText('Заказ недоступен.');
  await expect(order).toHaveCount(0);
  const start = link.calls.length;
  await section(page).getByRole('button', { name: 'Снять связь с заказом' }).press('Enter');
  const confirm = section(page).getByRole('button', { name: 'Подтвердить снятие связи' });
  await expect(confirm).toBeFocused();
  await confirm.press('Enter');
  await expect(section(page)).toContainText('Связь с заказом снята.');
  await expect(section(page).getByRole('heading')).toBeFocused();
  expect(link.calls.slice(start).map((call) => call.split(' ')[0])).toEqual(['PATCH']);
});

test('shows full dates and a range warning when no booking day is visible', async ({ page }) => {
  const { link } = await setup(page);
  link.status = { ...confirmed, checkIn: '2027-06-16', checkOut: '2027-06-18' };
  await page.getByRole('button', { name: 'Перечитать план', exact: true }).click();
  await expect(section(page)).toContainText('Даты заказа вне плана');
  await expect(section(page).locator('time')).toHaveText(['2027-06-16', '2027-06-18']);
  await expect(page.locator('.calendar').locator(`a[href="${hotelsPath}"]`)).toHaveCount(0);
});

test('drops owner-only booking facts on logout and makes no hotel-link reads for the foreign trip', async ({
  page,
}) => {
  const { link } = await setup(page);
  const reads = link.calls.length;
  await page.getByRole('button', { name: 'Выйти', exact: true }).click();
  await expect(section(page)).toHaveCount(0);
  await expect(page.getByText(confirmed.propertyName, { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await expect(page.getByRole('status').first()).toContainText('недоступен');
  await expect(page.locator(`a[href="${hotelsPath}"]`)).toHaveCount(0);
  expect(link.calls).toHaveLength(reads);
});
