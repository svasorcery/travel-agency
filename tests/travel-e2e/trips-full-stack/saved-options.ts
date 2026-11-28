import { createHash } from 'node:crypto';
import { type BrowserContext, expect, type Page, type Route } from '@playwright/test';
import type { TripBrief, TripOptionPreview } from '@travel/api-client';
import { observeOptionPreviewResponse } from './candidate-response';
import { observeResponse } from './response-observation';
import type { Stage } from './safe-reporter';

type Session = { page: Page; context: BrowserContext; token: string | null };
type Selection = { tripId: string; selectionToken: string; expiresAtUtc: string };
type Shown = { preview: TripOptionPreview; selection: Selection };
type Details = { version: number; brief: TripBrief; preview: TripOptionPreview; savedAtUtc: string };
type Owned = { mode: string; e2eProfile?: string; id: string; webPort: number; stateDir: string };
const previewPath = '/api/trips/option-preview',
  savePath = '/api/trips/saved-options';
const historyPath = (id: string) => `/api/trips/${id}/saved-option`;
const hash = (value: string) => createHash('sha256').update(value).digest('hex');
const canonical = (value: unknown): string =>
  JSON.stringify(value, (_, x) =>
    x && typeof x === 'object' && !Array.isArray(x)
      ? Object.fromEntries(Object.entries(x).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)))
      : x,
  );
const response = (page: Page, method: string, path: string) =>
  page.waitForResponse((r) => r.request().method() === method && new URL(r.url()).pathname === path);

export async function verifySavedOptions(
  state: Owned,
  directory: string,
  mark: (stage: Stage) => void,
  open: () => Promise<Session>,
) {
  const { savedOptionFixture, switchSavedVerification, captureDatabaseSnapshot, optionBuildFingerprints } =
    // eslint-disable-next-line @nx/enforce-module-boundaries -- This E2E fixture uses the launcher for its owned stack.
    await import('../../../tools/trips/local-stack.mjs');
  const web = `http://127.0.0.1:${state.webPort}`;
  let fixture = await savedOptionFixture(state);
  const builds = await optionBuildFingerprints(state);
  const transitions: Awaited<ReturnType<typeof switchSavedVerification>>[] = [];
  const observations: { kind: string; status: number; http: number; previews: number; packages: number }[] = [];
  let ordinaryPosts = 0,
    previewPosts = 0,
    savePosts = 0;
  const postedTokenHashes: string[] = [];
  async function newLogin() {
    const s = await open();
    s.page.on('request', (r) => {
      const u = new URL(r.url());
      if (u.origin !== web || r.method() !== 'POST') return;
      if (u.pathname === previewPath) previewPosts++;
      if (u.pathname === '/api/trips') ordinaryPosts++;
      if (u.pathname === savePath) {
        savePosts++;
        postedTokenHashes.push(hash(String(r.postDataJSON().selectionToken)));
      }
    });
    return s;
  }
  const auth = (s: Session) => ({ Authorization: `Bearer ${s.token}` });
  async function counts(expected: number, id?: string, present = true) {
    const f = await fixture.command('saved-facts', id);
    if (id)
      expect([f.matchingPlans, f.matchingSnapshots, f.matchingReceipts, f.revision]).toEqual(
        present ? [1, 1, 1, 1] : [0, 0, 0, 0],
      );
    expect([f.plans, f.snapshots, f.receipts]).toEqual([expected, expected, expected]);
    return f;
  }
  async function transition(mode: string, refusals = false) {
    observations.push(...(await fixture.observations()));
    transitions.push(await switchSavedVerification(state, mode, refusals));
    fixture = await savedOptionFixture(state);
    expect(fixture.mode).toBe(mode);
  }
  async function prep(
    s: Session,
    version: 'A' | 'B',
    title = '',
    net = false,
    eur = false,
  ): Promise<{ shown: Shown; brief: TripBrief }> {
    const page = s.page;
    if (new URL(page.url()).pathname !== '/trips')
      await page.getByRole('button', { name: 'Новый план', exact: true }).click();
    await page.locator('#trip-title').fill(title);
    await page.locator('#trip-destination').selectOption('saint-petersburg');
    await page.locator('#trip-origin').selectOption('moscow');
    await page.locator('#trip-start').fill('2026-10-28');
    await page.locator('#trip-end').fill(net ? '2026-10-29' : '2026-10-30');
    await page.locator('#trip-adults').fill('2');
    await page.locator('#trip-currency').selectOption(eur ? 'EUR' : 'RUB');
    await page.locator('#trip-budget').fill(eur ? '1.00' : '');
    await page.locator('#trip-pace').selectOption('relaxed');
    for (const name of ['Культура', 'Еда', 'Природа', 'История'])
      await page.getByLabel(name, { exact: true }).uncheck();
    await page.getByLabel('Архитектура', { exact: true }).check();
    const captured = await observeOptionPreviewResponse<Shown>(page, () =>
      page.locator('#trip-option-preview').click(),
    );
    expect(captured.response.headers()['cache-control']).toContain('no-store');
    expect(await captured.response.request().headerValue('X-Trips-Option-Save')).toBe('1');
    const result = { shown: captured.body, brief: captured.response.request().postDataJSON().brief as TripBrief };
    expect(result.shown.preview.catalogueVersion).toBe(version);
    expect(result.shown.preview.outcome).toBe('option');
    expect(Date.parse(result.shown.selection.expiresAtUtc) - Date.now()).toBeGreaterThan(13 * 60_000);
    expect(Date.parse(result.shown.selection.expiresAtUtc) - Date.now()).toBeLessThanOrEqual(15 * 60_000 + 5000);
    expect(
      await page.evaluate(
        (t) =>
          !location.href.includes(t) &&
          !document.documentElement.innerHTML.includes(t) &&
          [localStorage, sessionStorage].every((storage) =>
            Object.keys(storage).every((k) => !k.includes(t) && !storage.getItem(k)?.includes(t)),
          ),
        result.shown.selection.selectionToken,
      ),
    ).toBe(true);
    await expect(page.locator('#trip-option-save')).toBeVisible();
    return result;
  }
  async function stored(s: Session, id: string) {
    const got = await s.context.request.get(historyPath(id), { headers: auth(s) });
    expect(got.status()).toBe(200);
    expect(got.headers()['cache-control']).toContain('no-store');
    return (await got.json()).savedOption as Details;
  }
  async function opened(s: Session, id: string) {
    await expect(s.page).toHaveURL(web + '/trips/' + id);
    await expect(s.page.locator('#trip-saved-option')).toBeVisible();
    await expect(s.page.locator(`aside a[href="/trips/${id}"]`)).toHaveCount(1);
  }
  async function chooseFromList(s: Session, id: string) {
    const read = await observeResponse(response(s.page, 'GET', historyPath(id)), () =>
      s.page.locator(`aside a[href="/trips/${id}"]`).click(),
    );
    expect(read.status()).toBe(200);
    await opened(s, id);
  }
  async function save(s: Session, id: string, keyboard = false) {
    const button = s.page.locator('#trip-option-save');
    const r = await observeResponse(response(s.page, 'POST', savePath), () =>
      keyboard ? button.press('Enter') : button.click(),
    );
    expect(r.status()).toBe(201);
    expect(r.headers()['location']).toBe('/api/trips/' + id);
    await opened(s, id);
  }
  async function factsMatch(s: Session, choice: { shown: Shown; brief: TripBrief }) {
    const detail = await stored(s, choice.shown.selection.tripId);
    expect(detail.version).toBe(1);
    expect(detail.brief).toEqual(choice.brief);
    expect(detail.preview).toEqual(choice.shown.preview);
    expect(Math.abs(Date.parse(detail.savedAtUtc) - Date.now())).toBeLessThan(15 * 60000);
    return hash(canonical(detail));
  }
  async function visibleB(s: Session) {
    const c = s.page.locator('#trip-saved-option');
    await expect(c.locator('[data-option-flight]')).toContainText(/Запланировано.*RT1/s);
    await expect(c.locator('[data-option-hotel]')).toContainText(/Запланировано.*H1.*2 ночи/s);
    for (const dt of ['2026-10-28T20:00', '2026-10-28T21:30', '2026-10-30T08:00', '2026-10-30T11:00'])
      await expect(c.locator(`time[datetime^="${dt}"]`).first()).toContainText(dt.slice(-5));
    const first = c.locator('[data-option-day="2026-10-28"]'),
      middle = c.locator('[data-option-day="2026-10-29"]');
    await expect(first.locator('[data-option-stop]')).toHaveCount(0);
    await expect(middle.locator('[data-option-window]')).toContainText('38 мин');
    expect(
      await middle.locator('[data-option-stop]').evaluateAll((es) => es.map((e) => e.getAttribute('data-option-stop'))),
    ).toEqual(['spb-green-bridge', 'spb-stroganov-palace']);
    await expect(c.locator('[data-option-subtotal]')).toContainText(/30\s?000\s*₽/);
    await expect(c).toContainText('Итоговая стоимость поездки неизвестна');
  }
  async function hiddenResponse(s: Session, id: string, before: boolean) {
    const page = s.page;
    let handled = 0;
    let complete!: (status: number) => void;
    let failed!: (error: Error) => void;
    const upstream = new Promise<number>((ok, no) => {
      complete = ok;
      failed = no;
    });
    // An independent upstream connection forwards the actual request once. Losing the downstream
    // response (or the UI timing out) must not abort the database operation under test.
    const intercept = async (route: Route) => {
      if (++handled !== 1) {
        await route.continue();
        return;
      }
      try {
        const original = await route.fetch({ timeout: 125000, maxRetries: 0, maxRedirects: 0 });
        const body = await original.json();
        expect(body.tripId).toBe(id);
        if (before) await route.abort('connectionreset').catch(() => undefined);
        else {
          const committed = await fixture.command('saved-facts', id);
          expect([committed.matchingPlans, committed.matchingSnapshots, committed.matchingReceipts]).toEqual([1, 1, 1]);
          await route.abort('connectionreset');
        }
        complete(original.status());
      } catch (e) {
        failed(e instanceof Error ? e : new Error('Upstream failed'));
      }
    };
    await page.route(web + savePath, intercept);
    if (before) await fixture.command('saved-arm');
    await page.locator('#trip-option-save').click();
    if (before) await fixture.command('saved-held');
    await expect(page.locator('#trip-option-check')).toBeVisible({ timeout: 25000 });
    return { upstream, remove: () => page.unroute(web + savePath, intercept) };
  }
  mark('saved-option-b');
  expect(fixture.mode).toBe('B');
  const empty = await counts(0);
  let s = await newLogin();
  const b = await prep(s, 'B');
  const id = b.shown.selection.tripId;
  expect(b.shown.preview.option).toMatchObject({
    flight: {
      id: 'RT1',
      adults: 2,
      outbound: {
        fromAirport: 'SVO',
        toAirport: 'LED',
        departsAt: '2026-10-28T18:30:00+03:00',
        arrivesAt: '2026-10-28T20:00:00+03:00',
      },
      inbound: {
        fromAirport: 'LED',
        toAirport: 'SVO',
        departsAt: '2026-10-30T11:00:00+03:00',
        arrivesAt: '2026-10-30T12:30:00+03:00',
      },
    },
    hotel: {
      id: 'H1',
      nights: 2,
      adults: 2,
      checkInAt: '2026-10-28T21:30:00+03:00',
      checkOutAt: '2026-10-30T08:00:00+03:00',
    },
    price: {
      knownSubtotal: '30000.00',
      components: expect.arrayContaining([
        expect.objectContaining({ component: 'flightBase', amount: '18000.00', currency: 'RUB' }),
        expect.objectContaining({ component: 'hotelBase', amount: '12000.00', currency: 'RUB' }),
      ]),
      unpricedExpenses: expect.arrayContaining(['meals', 'transfers']),
    },
  });
  await counts(0, id, false);
  await save(s, id, true);
  await expect(s.page.locator('#trip-title')).toHaveValue(/Санкт-Петербург.*28.*30.*2026/);
  const digest = await factsMatch(s, b);
  await visibleB(s);
  const bStored = await counts(1, id);
  const foundation = await captureDatabaseSnapshot(state, id);
  expect(String(foundation.revision)).toBe('1');
  await s.context.close();
  mark('saved-option-cold');
  await transition('history', true);
  s = await newLogin();
  const firstHistorySession = s;
  expect((await s.context.request.get(previewPath + '/availability', { headers: auth(s) })).status()).toBe(404);
  const coldDisabled = (await s.context.request.post(savePath, { headers: auth(s), data: {} })).status();
  expect(coldDisabled).toBe(404);
  await chooseFromList(s, id);
  await visibleB(s);
  expect(hash(canonical(await stored(s, id)))).toBe(digest);
  const reopened = await captureDatabaseSnapshot(state, id);
  expect(String(reopened.revision)).toBe('1');
  expect(reopened.keyFileHashes).toEqual(foundation.keyFileHashes);
  expect(reopened.payloadSha256).toBe(foundation.payloadSha256);
  const bCold = await counts(1, id);
  expect(bCold.keysSha256).toBe(empty.keysSha256);
  expect(bCold.protectedHistorySha256).toBe(bStored.protectedHistorySha256);
  await s.page.locator('#trip-saved-option').screenshot({ path: directory + '/cold-saved-b.png' });
  await firstHistorySession.context.close();
  mark('saved-option-catalogue-a');
  await transition('A');
  s = await newLogin();
  await chooseFromList(s, id);
  expect(hash(canonical(await stored(s, id)))).toBe(digest);
  await visibleB(s);
  mark('saved-option-net');
  const n = await prep(s, 'A', 'Fictional one-night option', true);
  await save(s, n.shown.selection.tripId);
  await factsMatch(s, n);
  await expect(s.page.locator('#trip-saved-option [data-price-component="hotelBase"]')).toContainText(
    /поставщик|net|нетто/i,
  );
  await counts(2);
  mark('saved-option-before-commit');
  const e = await prep(s, 'A', 'Fictional EUR option', false, true),
    euroId = e.shown.selection.tripId;
  const startPosts = savePosts;
  const delayed = await hiddenResponse(s, euroId, true);
  const hiddenRows = await counts(2, euroId, false);
  const notFound = await observeResponse(response(s.page, 'GET', historyPath(euroId)), () =>
    s.page.locator('#trip-option-check').click(),
  );
  expect(notFound.status()).toBe(404);
  const refreshed = await observeResponse(response(s.page, 'GET', '/api/trips'), () =>
    s.page.getByRole('button', { name: 'Обновить список', exact: true }).click(),
  );
  expect(refreshed.status()).toBe(200);
  await expect(s.page.locator(`aside a[href="/trips/${euroId}"]`)).toHaveCount(0);
  await expect(s.page.getByRole('button', { name: 'Создать основу плана', exact: true })).toBeDisabled();
  expect(savePosts - startPosts).toBe(1);
  expect(ordinaryPosts).toBe(0);
  const retryResponse = response(s.page, 'POST', savePath);
  await s.page.locator('#trip-option-retry').click();
  try {
    await expect
      .poll(async () => (await fixture.command('saved-facts')).waitingForOwner, { timeout: 6500, intervals: [40, 100] })
      .toBeGreaterThan(0);
  } finally {
    await fixture.command('saved-release');
  }
  const retried = await retryResponse;
  expect(retried.status()).toBe(200);
  expect(await delayed.upstream).toBe(201);
  await delayed.remove();
  await opened(s, euroId);
  await factsMatch(s, e);
  expect(savePosts - startPosts).toBe(2);
  expect(postedTokenHashes.at(-1)).toBe(hash(e.shown.selection.selectionToken));
  expect(postedTokenHashes.at(-2)).toBe(postedTokenHashes.at(-1));
  await expect(s.page.locator('#trip-saved-option [data-option-subtotal]')).toContainText(/30\s?000\s*₽/);
  await expect(s.page.locator('#trip-saved-option [data-option-budget]')).toContainText(/1\s*€.*без пересчёта/);
  const retryRows = await counts(3, euroId);
  mark('saved-option-after-commit');
  const a = await prep(s, 'A', 'Fictional early arrival');
  const aid = a.shown.selection.tripId;
  expect(a.shown.preview.option?.days[0].programmeMinutes).toBe(44);
  const lost = await hiddenResponse(s, aid, false);
  expect(await lost.upstream).toBe(201);
  await lost.remove();
  const committedRows = await counts(4, aid);
  await transition('history');
  const oldPosts = savePosts;
  const recoveredList = await observeResponse(response(s.page, 'GET', '/api/trips'), () =>
    s.page.getByRole('button', { name: 'Обновить список', exact: true }).click(),
  );
  expect(recoveredList.status()).toBe(200);
  await expect(s.page.locator('#trip-option-check')).toBeVisible();
  const found = await observeResponse(response(s.page, 'GET', historyPath(aid)), () =>
    s.page.locator('#trip-option-check').click(),
  );
  expect(found.status()).toBe(200);
  await opened(s, aid);
  await factsMatch(s, a);
  expect(savePosts).toBe(oldPosts);
  const recoveryDisabled = (await s.context.request.post(savePath, { headers: auth(s), data: {} })).status();
  expect(recoveryDisabled).toBe(404);
  await s.context.close();
  mark('saved-option-expiry');
  await transition('expired');
  s = await newLogin();
  const unavailable = await s.context.request.post(previewPath, {
    headers: auth(s),
    data: { brief: { ...b.brief, startDate: '2026-11-27', endDate: '2026-11-29' } },
  });
  expect(unavailable.status()).toBe(503);
  await chooseFromList(s, id);
  await visibleB(s);
  expect(hash(canonical(await stored(s, id)))).toBe(digest);
  const final = await counts(4);
  expect(await optionBuildFingerprints(state)).toEqual(builds);
  observations.push(...(await fixture.observations()));
  const history = observations.filter((o) => o.kind === 'historical');
  expect(history.length).toBeGreaterThan(6);
  expect(history.every((o) => o.http === 0 && o.previews === 0 && o.packages === 0)).toBe(true);
  expect(
    observations.some((o) => o.kind === 'preview' && o.status === 200 && o.previews === 1 && o.packages >= 1),
  ).toBe(true);
  expect(observations.every((o) => o.http === 0)).toBe(true);
  expect([ordinaryPosts, previewPosts, savePosts]).toEqual([0, 4, 5]);
  return {
    schemaVersion: 1,
    status: 'passed',
    stackId: state.id,
    profile: state.mode,
    tripId: id,
    revision: '1',
    snapshotSha256: digest,
    builds,
    coldRead: true,
    protectedKeysPreserved: empty.keysSha256 === final.keysSha256,
    keyFingerprint: final.keysSha256,
    historyReads: history.length,
    beforeCommitRows: {
      plans: hiddenRows.matchingPlans,
      snapshots: hiddenRows.matchingSnapshots,
      receipts: hiddenRows.matchingReceipts,
    },
    retryRows: {
      plans: retryRows.matchingPlans,
      snapshots: retryRows.matchingSnapshots,
      receipts: retryRows.matchingReceipts,
      revision: retryRows.revision,
    },
    afterCommitRows: {
      plans: committedRows.matchingPlans,
      snapshots: committedRows.matchingSnapshots,
      receipts: committedRows.matchingReceipts,
      revision: committedRows.revision,
    },
    separatelyProbedDisabledSaveStatuses: [coldDisabled, recoveryDisabled],
    storedCiphertextSha256: bCold.protectedHistorySha256,
    canonicalDetailsMatched: true,
    earlyArrivalMinutes: 44,
    netStayChecked: true,
    foreignBudgetChecked: true,
    expiredPreviewStatus: unavailable.status(),
    beforeCommit: 'same-ticket-replayed',
    beforeCommitGet: notFound.status(),
    retryStatus: retried.status(),
    afterCommit: 'recovered-by-read',
    afterCommitGet: found.status(),
    ordinaryPosts,
    previewPosts,
    savePosts,
    finalPlans: final.plans,
    finalSnapshots: final.snapshots,
    finalReceipts: final.receipts,
    transitions,
    observations,
  };
}
