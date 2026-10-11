import { randomUUID } from 'node:crypto';
import type { Server } from 'node:http';
import { expect, test } from '@playwright/test';
import { isFictionalApiRequest } from './fictional-route-guard';

for (const preset of ['refund-sent', 'refund-zero', 'refund-result-lost'] as const) {
  test(preset + ' retains whole-order money across reload and stale reads', async ({ page }, testInfo) => {
    // This API creates only local fictional records. Each scenario owns a separate start.
    // eslint-disable-next-line @nx/enforce-module-boundaries
    const { createDemoServer } = await import('../../../tools/demo/flights-search-api.mjs');
    const server = createDemoServer({ customerRefundPreset: preset }) as Server;
    await new Promise<void>((r) => server.listen(0, '127.0.0.1', r));
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('Fictional listener unavailable');
    const origin = 'http://127.0.0.1:' + address.port;
    const post = async (path: string, body: unknown, key?: string) => {
      const r = await fetch(origin + '/api/flights/' + path, {
        method: 'POST',
        redirect: 'error',
        headers: { 'Content-Type': 'application/json', ...(key ? { 'Idempotency-Key': key } : {}) },
        body: JSON.stringify(body),
      });
      expect(r.status).toBe(200);
      return r.json();
    };
    const writes: string[] = [],
      consoleMessages: string[] = [];
    let stale = false;
    page.on('console', (m) => consoleMessages.push(m.text()));
    await page.addInitScript(() => {
      const captured: string[] = [];
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
          writeText: async (s: string) => {
            captured.push(s);
          },
        },
      });
      (window as unknown as { refundClipboardWrites: string[] }).refundClipboardWrites = captured;
    });
    await page.route('**/*', async (route) => {
      const req = route.request(),
        url = new URL(req.url());
      if (
        url.origin !== 'http://127.0.0.1:4201' ||
        url.username ||
        url.password ||
        Object.keys(req.headers()).some((k) => k.toLowerCase() === 'authorization')
      )
        throw new Error('Unexpected fictional request');
      if (!url.pathname.startsWith('/api/')) {
        await route.continue();
        return;
      }
      if (!isFictionalApiRequest(req.url(), req.method(), req.headers()))
        throw new Error('Fictional route guard refusal');
      const body = req.postData(),
        h = req.headers();
      const r = await fetch(origin + url.pathname + url.search, {
        method: req.method(),
        redirect: 'error',
        headers: {
          ...(h['content-type'] ? { 'Content-Type': h['content-type'] } : {}),
          ...(h['idempotency-key'] ? { 'Idempotency-Key': h['idempotency-key'] } : {}),
        },
        ...(body !== null ? { body } : {}),
      });
      if (req.method() === 'POST') writes.push(url.pathname);
      let text = await r.text();
      if (stale && url.pathname.endsWith('/customer-refund')) {
        const value = JSON.parse(text);
        stale = false;
        text = JSON.stringify({ ...value, bookingVersion: value.bookingVersion - 1, state: 'Unknown', source: 'None' });
      }
      await route.fulfill({
        status: r.status,
        contentType: r.headers.get('content-type') ?? 'application/json',
        headers: { 'Cache-Control': 'no-store', 'X-Travel-Demo': 'fixtures' },
        body: text,
      });
    });
    try {
      const people = preset === 'refund-sent' ? 9 : 1;
      const legs =
        preset === 'refund-sent'
          ? [
              { origin: 'LED', destination: 'DME', departureDate: '2037-07-20' },
              { origin: 'VKO', destination: 'KZN', departureDate: '2037-07-22' },
              { origin: 'KZN', destination: 'SVO', departureDate: '2037-07-24' },
              { origin: 'SVO', destination: 'LED', departureDate: '2037-07-26' },
            ]
          : [{ origin: 'LED', destination: 'DME', departureDate: '2037-07-20' }];
      const search = await post('search/v2?currency=RUB', { legs, passengerCount: people, cabinClass: 'economy' });
      const offer = search.offers.find((o: { provider: string }) => o.provider === 'duffel');
      const request = { provider: 'duffel', providerOfferRef: offer.providerOfferRef, passengerCount: people };
      let quote = await post('orders/quote', request);
      const catalog = await post('orders/ancillaries', {
        aggregateId: quote.aggregateId,
        quoteRevision: quote.binding.revision,
        includeSeats: true,
      });
      const bag = catalog.services.find((s: { kind: string }) => s.kind === 'checked-baggage');
      const seat = catalog.services.find(
        (s: { kind: string; unitPrice: { amount: string } }) => s.kind === 'seat' && s.unitPrice.amount === '0.00',
      );
      quote = await post('orders/quote', {
        ...request,
        aggregateId: quote.aggregateId,
        selections: [
          { selectionKey: bag.selectionKey, quantity: 2 },
          { selectionKey: seat.selectionKey, quantity: 1 },
        ],
      });
      expect(quote.purchase.total).toEqual({ amount: '100.00', currency: 'GBP' });
      const passengers = quote.binding.slots.map((s: { bookingPassengerId: string }) => ({
        bookingPassengerId: s.bookingPassengerId,
        title: 'mr',
        gender: 'male',
        givenName: 'Fictional',
        familyName: 'Traveler',
        dateOfBirth: '1990-01-01',
        email: 'fictional@example.test',
        phone: '+441234567890',
      }));
      await post(
        'orders/hold',
        { aggregateId: quote.aggregateId, quoteRevision: quote.binding.revision, passengers, acceptAncillaries: true },
        randomUUID(),
      );
      await post('orders/confirm', { aggregateId: quote.aggregateId }, randomUUID());
      await page.setViewportSize({ width: 375, height: 812 });
      await page.goto('/flights/orders/' + quote.aggregateId);
      await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
      await expect(page.getByText('Взрослых пассажиров: ' + people + '. Цена за всю группу.')).toBeVisible();
      await expect(page.getByText('Место 12A', { exact: false })).toBeVisible();
      if (people === 9) await expect(page.getByText('Участок 4: SVO → LED', { exact: false })).toBeVisible();
      await page.getByRole('button', { name: 'Получить условия отмены' }).click();
      await page.getByLabel('Согласен на отмену всего заказа на указанных условиях возврата.').check();
      await page.locator('[data-action="consent-cancellation"]').click();
      const card = page.locator('[data-testid="customer-refund"]');
      const caption =
        preset === 'refund-sent'
          ? 'Демо: возврат отправлен'
          : preset === 'refund-zero'
            ? 'По условиям отмены сумма возврата — 0'
            : 'Требуется ручная проверка';
      await expect(card.getByText(caption, { exact: true })).toBeVisible();
      await expect(card.locator('dd')).toHaveText(preset === 'refund-zero' ? '0 GBP' : '80 GBP');
      await expect(card.getByText('Поступление денег от поставщика не подтверждено.')).toBeVisible();
      await expect(card.getByText(/Зачисление клиенту не подтверждено/)).toBeVisible();
      if (preset === 'refund-sent') {
        stale = true;
        await card.locator('[data-action="refresh-refund"]').click();
        await expect(card.getByText(caption, { exact: true })).toBeVisible();
      }
      await page.reload();
      await page.getByRole('button', { name: 'Демо вход', exact: true }).click();
      await expect(card.getByText(caption, { exact: true })).toBeVisible();
      await card.locator('[data-action="refresh-refund"]').click();
      await expect(card.getByText(caption, { exact: true })).toBeVisible();
      expect(writes).toEqual(['/api/flights/cancellations/prepare', '/api/flights/cancellations/consent']);
      expect(
        await page.evaluate(() =>
          JSON.stringify({
            local: { ...localStorage },
            session: { ...sessionStorage },
            clipboard: (window as unknown as { refundClipboardWrites: string[] }).refundClipboardWrites,
          }),
        ),
      ).not.toMatch(
        /fictional@example|Traveler|paymentReference|refundId|receiptReference|Authorization|quoteRevision|Idempotency|bag_|seat_/,
      );
      expect(consoleMessages.join('\n')).not.toMatch(
        /fictional@example|Traveler|paymentReference|refundId|receiptReference|Authorization/,
      );
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await card.screenshot({ path: testInfo.outputPath(preset + '-card.png') });
      expect(page.url()).toMatch(/\/flights\/orders\/[0-9a-f-]{36}$/);
      await expect(card.getByRole('button', { name: /повторить выплату/i })).toHaveCount(0);
    } finally {
      await new Promise<void>((r) => server.close(() => r()));
    }
  });
}
