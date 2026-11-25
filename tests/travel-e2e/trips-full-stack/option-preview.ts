import { expect, type Page } from '@playwright/test';
import type { TripOptionPreview } from '@travel/api-client';
import { observeResponse } from './response-observation';
import type { Stage } from './safe-reporter';

const previewPath = '/api/trips/option-preview';
const referenceUtc = '2026-10-27T12:00:00Z';
const middleStops = ['spb-green-bridge', 'spb-stroganov-palace'];

export async function verifyOptionCard(
  page: Page,
  version: 'A' | 'B',
  directory: string,
  mark: (stage: Stage) => void,
) {
  const early = version === 'A';
  const card = page.locator('#trip-option-result');
  const select = page.getByRole('button', { name: 'Подобрать вариант', exact: true });
  const apiOrigin = new URL(page.url()).origin;
  let previewRequests = 0;
  let previewResponses = 0;
  let writes = 0;
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (
      url.origin !== apiOrigin ||
      !url.pathname.startsWith('/api/') ||
      ['GET', 'HEAD', 'OPTIONS'].includes(request.method())
    )
      return;
    if (url.pathname === previewPath && request.method() === 'POST') previewRequests++;
    else writes++;
  });
  async function clickForPreview(
    startDate: string,
    endDate: string,
    {
      keyboard = false,
      originId = 'moscow',
      adults = 2,
      currency = 'RUB',
      totalBudget = null,
    }: { keyboard?: boolean; originId?: string; adults?: number; currency?: string; totalBudget?: string | null } = {},
  ): Promise<TripOptionPreview> {
    const expectedCount = previewResponses + 1;
    const got = page
      .waitForResponse(
        (response) => response.url() === apiOrigin + previewPath && response.request().method() === 'POST',
      )
      .then(async (response) => {
        previewResponses++;
        expect(response.fromServiceWorker()).toBe(false);
        expect(response.status()).toBe(200);
        expect(response.headers()['cache-control']).toContain('no-store');
        expect(response.request().postDataJSON()).toEqual({
          brief: {
            originId,
            destinationId: 'saint-petersburg',
            adults,
            currency,
            startDate,
            endDate,
            totalBudget,
            interests: ['architecture'],
            pace: 'relaxed',
          },
        });
        return response.json();
      });
    const body: TripOptionPreview = await observeResponse(got, () =>
      keyboard ? select.press('Enter') : select.click(),
    );
    expect(previewRequests).toBe(expectedCount);
    expect(previewResponses).toBe(expectedCount);
    expect(body.catalogueVersion).toBe(version);
    expect(Date.parse(body.asOfUtc)).toBe(Date.parse(referenceUtc));
    return body;
  }

  mark('option-form');
  await expect(page.locator('#trip-title')).toHaveValue('');
  await expect(page.locator('#trip-option-example time')).toHaveAttribute('datetime', /2026-10-27T12:00:00/);
  await expect(select).toHaveAttribute('type', 'button');
  await page.locator('#trip-destination').selectOption('saint-petersburg');
  await page.locator('#trip-origin').selectOption('moscow');
  await page.locator('#trip-start').fill('2026-10-28');
  await page.locator('#trip-end').fill('2026-10-30');
  await page.locator('#trip-adults').fill('2');
  await page.locator('#trip-currency').selectOption('RUB');
  await page.locator('#trip-pace').selectOption('relaxed');
  for (const label of ['Культура', 'Еда', 'Природа', 'История'])
    await page.getByLabel(label, { exact: true }).uncheck();
  await page.getByLabel('Архитектура', { exact: true }).check();
  await page.locator('#trip-budget').fill('');
  mark('option-original-response');
  const original = await clickForPreview('2026-10-28', '2026-10-30', { keyboard: early });
  expect(original.outcome).toBe('option');
  const result = original.option;
  if (!result) throw new Error('Expected an option.');
  const dateTime = (day: number, time: string) => `2026-10-${day}T${time}:00+03:00`;
  const flightTimes = [
    dateTime(28, early ? '07:30' : '18:30'),
    dateTime(28, early ? '09:00' : '20:00'),
    dateTime(30, '11:00'),
    dateTime(30, '12:30'),
  ];
  expect(result.flight).toEqual({
    id: 'RT1',
    adults: 2,
    outbound: { fromAirport: 'SVO', toAirport: 'LED', departsAt: flightTimes[0], arrivesAt: flightTimes[1] },
    inbound: { fromAirport: 'LED', toAirport: 'SVO', departsAt: flightTimes[2], arrivesAt: flightTimes[3] },
  });
  expect(result.hotel).toMatchObject({
    id: 'H1',
    adults: 2,
    nights: 2,
    checkInAt: dateTime(28, early ? '14:00' : '21:30'),
    checkOutAt: dateTime(30, '08:00'),
  });
  expect(result.price).toMatchObject({
    currency: 'RUB',
    knownSubtotal: '30000.00',
    unpricedExpenses: expect.arrayContaining(['meals', 'transfers']),
  });
  expect([
    result.arrivalTransferMinutes,
    result.departureTransferMinutes,
    result.hotelToWalkMinutes,
    result.walkToHotelMinutes,
  ]).toEqual([90, 180, 20, 20]);
  const windows = early
    ? [
        ['14:20', '17:40'],
        ['10:20', '17:40'],
        [null, null],
      ]
    : [
        [null, null],
        ['10:20', '17:40'],
        [null, null],
      ];
  const stops = [early ? ['spb-general-staff', 'spb-pevchesky-bridge'] : [], middleStops, []];
  expect(result.days.map((d) => [d.windowStart, d.windowEnd])).toEqual(windows);
  expect(result.days.map((d) => d.stops.map((s) => s.id))).toEqual(stops);
  const programmeMinutes = result.days.map((d) => d.programmeMinutes);
  expect(programmeMinutes).toEqual(early ? [44, 38, 0] : [0, 38, 0]);
  mark('option-visible-card');
  await expect(card).toBeVisible();
  await expect(card).toContainText(/SVO.*LED.*LED.*SVO/s);
  await expect(card.locator('[data-option-flight]')).toContainText('RT1');
  const hotel = card.locator('[data-option-hotel]');
  await expect(hotel).toContainText('H1');
  await expect(hotel).toContainText(result.hotel.name);
  await expect(hotel).toContainText(/2\s*ноч|ноч\S*\s*:?\s*2/i);
  for (const timestamp of [...flightTimes, result.hotel.checkInAt, result.hotel.checkOutAt]) {
    const value = card.locator(`time[datetime="${timestamp}"]`).first();
    await expect(value).toBeVisible();
    await expect(value).toContainText(timestamp.slice(11, 16));
  }
  await expect(card.locator('[data-option-subtotal]')).toContainText(/30\s?000/);
  await expect(card).toContainText(/питани|еда|еду/i);
  await expect(card).toContainText(/трансфер/i);
  await expect(card).toContainText('Итоговая стоимость поездки неизвестна');
  for (const [index, day] of result.days.entries()) {
    expect(day.date).toBe(`2026-10-${28 + index}`);
    const block = card.locator(`[data-option-day="${day.date}"]`);
    await expect(block).toBeVisible();
    if (day.windowStart && day.windowEnd) {
      await expect(block.locator('[data-option-window]')).toContainText(day.windowStart);
      await expect(block.locator('[data-option-window]')).toContainText(day.windowEnd);
    } else await expect(block.locator('[data-empty-reason]')).toBeVisible();
    const visibleStops = block.locator('[data-option-stop]');
    await expect(visibleStops).toHaveCount(stops[index].length);
    for (const [position, stop] of day.stops.entries()) {
      expect(stop.accessHoursKnown).toBe(false);
      await expect(visibleStops.nth(position)).toContainText(stop.id);
      await expect(visibleStops.nth(position)).toContainText(stop.title);
    }
  }
  const sourceLink = card.locator(`a[href="${original.dataUrl}"]`);
  await expect(sourceLink).toBeVisible();
  const manifest = await page.request.get(original.dataUrl, { maxRedirects: 0 });
  expect(manifest.status()).toBe(200);
  expect(await manifest.json()).toBeTruthy();
  await expect(page.locator('#trip-title')).toHaveValue('');
  await expect(page.locator('#trip-start')).toHaveValue('2026-10-28');
  await expect(page.locator('#trip-end')).toHaveValue('2026-10-30');
  await expect(page).toHaveURL(`${apiOrigin}/trips`);
  await card.screenshot({ path: `${directory}/option-${version.toLowerCase()}.png` });
  mark('option-foreign-currency');
  await page.locator('#trip-currency').selectOption('EUR');
  await page.locator('#trip-budget').fill('1.00');
  const foreign = await clickForPreview('2026-10-28', '2026-10-30', { currency: 'EUR', totalBudget: '1.00' });
  expect(foreign.outcome).toBe('option');
  expect(foreign.option).toMatchObject({
    flight: { id: 'RT1' },
    hotel: { id: 'H1' },
    price: {
      currency: 'EUR',
      knownSubtotal: '0.00',
      budgetStatus: 'indeterminate',
      components: expect.arrayContaining([
        expect.objectContaining({
          component: 'flightBase',
          kind: 'travelerMandatory',
          amount: '18000.00',
          currency: 'RUB',
        }),
        expect.objectContaining({
          component: 'hotelBase',
          kind: 'travelerMandatory',
          amount: '12000.00',
          currency: 'RUB',
        }),
      ]),
    },
  });
  const displayed = card.locator('[data-option-subtotal]');
  await expect(displayed.locator('[data-currency="RUB"]')).toContainText(/30\s?000\s*₽/);
  await expect(displayed.locator('[data-currency="EUR"]')).toHaveCount(0);
  await expect(card).not.toContainText(/(?:^|[^\d])0(?:[.,]00)?\s*€/);
  await expect(card.locator('[data-option-budget]')).toContainText(/1\s*€.*без пересчёта валют нельзя/);
  await expect(card).not.toContainText('Полная цена перелёта и гостиницы неизвестна');
  await card.screenshot({ path: `${directory}/option-eur-${version.toLowerCase()}.png` });
  await page.locator('#trip-budget').fill('');
  await page.locator('#trip-currency').selectOption('RUB');
  mark('option-short-stay');
  await page.locator('#trip-end').fill('2026-10-29');
  await expect(card).toHaveCount(0);
  const short = await clickForPreview('2026-10-28', '2026-10-29');
  expect(short.option?.flight.id).toBe('RT3');
  expect(short.option?.hotel).toMatchObject({ id: 'H3', nights: 1 });
  expect(short.option?.price).toMatchObject({
    knownSubtotal: '18000.00',
    flightAndStayComplete: false,
    components: expect.arrayContaining([expect.objectContaining({ kind: 'supplierNet', component: 'hotelBase' })]),
  });
  await expect(card.locator('[data-option-hotel]')).toContainText('H3');
  await expect(card.locator('[data-option-subtotal]')).toContainText(/18\s?000/);
  await expect(card.locator('[data-price-component="hotelBase"]')).toContainText(/net|нетто|поставщик/i);
  mark('option-missing-room');
  await page.locator('#trip-end').fill('2026-10-30');
  await page.locator('#trip-start').fill('2026-10-29');
  const absent = await clickForPreview('2026-10-29', '2026-10-30');
  expect(absent).toMatchObject({ outcome: 'noMatch', reasons: ['hotelUnavailable'], option: null });
  await expect(card).toContainText(/номер|гостиниц|проживан/i);
  await expect(card.locator('[data-option-flight]')).toHaveCount(0);
  mark('option-mixed-unsupported');
  await page.locator('#trip-start').fill('2026-10-28');
  await page.locator('#trip-end').fill('2026-10-28');
  await page.locator('#trip-origin').selectOption('kaliningrad');
  await page.locator('#trip-adults').fill('3');
  const mixed = await clickForPreview('2026-10-28', '2026-10-28', { originId: 'kaliningrad', adults: 3 });
  const unsupportedReasons = ['directionUnsupported', 'dateRangeUnsupported', 'groupUnsupported'];
  expect(mixed).toMatchObject({ outcome: 'unsupported', option: null, reasons: unsupportedReasons });
  await expect(card.locator('li')).toHaveCount(3);
  await expect(card.locator('[data-option-flight]')).toHaveCount(0);
  expect(writes).toBe(0);
  return {
    referenceUtc,
    previewResponses,
    writes,
    unknownTotal: true,
    roundTripId: result.flight.id,
    hotelId: result.hotel.id,
    nights: result.hotel.nights,
    knownSubtotal: result.price.knownSubtotal,
    arrival: result.flight.outbound.arrivesAt.slice(11, 16),
    outbound: result.flight.outbound,
    inbound: result.flight.inbound,
    checkIn: result.hotel.checkInAt,
    checkOut: result.hotel.checkOutAt,
    days: result.days.map((day) => ({
      date: day.date,
      start: day.windowStart,
      end: day.windowEnd,
      stops: day.stops.map((stop) => stop.id),
    })),
    unpricedExpenses: result.price.unpricedExpenses,
    programmeMinutes,
    shortStay: 'net-only',
    missingRoom: absent.outcome === 'noMatch',
    unsupportedReasons: mixed.reasons,
    foreignBudget: {
      currency: 'EUR',
      amount: '1.00',
      sourceCurrency: 'RUB',
      knownSourceSubtotal: '30000.00',
      status: foreign.option?.price.budgetStatus,
    },
  };
}
