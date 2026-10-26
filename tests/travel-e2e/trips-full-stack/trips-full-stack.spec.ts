import { createHash } from 'node:crypto';
import { lstat, mkdir, readFile, realpath, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import {
  type APIResponse,
  type Browser,
  type BrowserContext,
  expect,
  type Page,
  type Response,
  test,
} from '@playwright/test';
import type { ReplaceTrip, TripInterest, TripItineraryComputed, TripPlan } from '@travel/api-client';
import { decodeTripItineraryCandidateV2 } from '@travel/api-client/trips-itinerary-decoder';
import { observeCandidateResponse } from './candidate-response';
import { observeResponse } from './response-observation';
import { type SafeFailure, type Stage, safeFailure } from './safe-reporter';

const editorSaveStatusSelector = '.section-heading > .save-status';
const guid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const editedBlockTitle = 'Fictional edited outdoor stop';
const editedNote = 'Fictional note: exact amount and local time retained after restart.';
const interestLabels: Record<TripInterest, string> = {
  architecture: 'Архитектура',
  culture: 'Культура',
  food: 'Еда',
  nature: 'Природа',
  history: 'История',
};
type Credential = { username: string; password: string; subject: string };
type OwnedState = {
  id: string;
  mode: string;
  stateDir: string;
  webPort: number;
  oidcPort: number;
  applicationAsOfUtc: string;
  e2eProfile?: string;
};
type DatabaseSnapshot = {
  tripId: string;
  ownerIssuer: string;
  ownerSubject: string;
  revision: string | number;
  createdAt: string;
  updatedAt: string;
  payloadLength: number;
  payloadSha256: string;
  keyFileHashes: Record<string, string>;
};
type ProcessSnapshot = {
  service: string;
  containerId: string;
  pid: number;
  startedAt: string;
  running: boolean;
  mounts: unknown;
};
type RestartReceipt = {
  before: ProcessSnapshot[];
  stopped: ProcessSnapshot[];
  after: ProcessSnapshot[];
  storagePreserved: boolean;
};
type Session = {
  context: BrowserContext;
  page: Page;
  token: string | null;
  externalRequests: number;
  writeMethods: string[];
  candidateRequests: number;
  auth: { codePkce: number; codeExchange: number; tokenSuccess: number; forbiddenGrant: number };
};
type ApplicationDiagnostic = {
  saveStatusPresent: boolean;
  saveStatusMatchCount: number;
  displayedRevision: string | null;
  revisionHasSeparatingWhitespace: boolean;
  clean: boolean;
  dirty: boolean;
};

function fingerprint(value: unknown): string {
  return createHash('sha256').update(JSON.stringify(value)).digest('hex');
}

async function readCredentials(state: OwnedState): Promise<Credential[]> {
  const privateDir = resolve(state.stateDir, 'private');
  const path = resolve(privateDir, 'credentials.json');
  for (const item of [privateDir, path]) {
    expect((await lstat(item)).isSymbolicLink()).toBe(false);
    expect(await realpath(item)).toBe(item);
  }
  expect((await lstat(path)).size).toBeLessThan(4096);
  const credentials: Credential[] = JSON.parse(await readFile(path, 'utf8'));
  expect(credentials).toHaveLength(2);
  for (const credential of credentials) {
    // Assert booleans so even an unexpected credentials file cannot enter a diff.
    expect(
      typeof credential.username === 'string' && credential.username.length > 0 && credential.username.length <= 128,
    ).toBe(true);
    expect(
      typeof credential.password === 'string' && credential.password.length >= 16 && credential.password.length <= 256,
    ).toBe(true);
    expect(typeof credential.subject === 'string' && guid.test(credential.subject)).toBe(true);
  }
  expect(credentials[0].subject !== credentials[1].subject).toBe(true);
  return credentials;
}

async function freshSession(browser: Browser, state: OwnedState): Promise<Session> {
  const context = await browser.newContext({
    baseURL: `http://127.0.0.1:${state.webPort}`,
    viewport: { width: 1440, height: 1000 },
    timezoneId: 'Europe/Moscow',
    serviceWorkers: 'block',
  });
  const page = await context.newPage();
  // Date only: native timers and the real Keycloak/JWT service clocks keep running.
  await page.clock.setFixedTime(new Date(state.applicationAsOfUtc));
  const session: Session = {
    context,
    page,
    token: null,
    externalRequests: 0,
    writeMethods: [],
    candidateRequests: 0,
    auth: { codePkce: 0, codeExchange: 0, tokenSuccess: 0, forbiddenGrant: 0 },
  };
  const webOrigin = `http://127.0.0.1:${state.webPort}`;
  const oidcOrigin = `http://127.0.0.1:${state.oidcPort}`;
  context.on('request', (request) => {
    const url = new URL(request.url());
    if (!['http:', 'https:'].includes(url.protocol)) return;
    if (url.origin !== webOrigin && url.origin !== oidcOrigin) {
      session.externalRequests++;
      return;
    }
    if (url.origin === webOrigin && url.pathname.startsWith('/api/trips')) {
      const authorization = request.headers()['authorization'];
      if (authorization?.startsWith('Bearer ')) session.token = authorization.slice(7);
      if (['POST', 'PUT', 'DELETE'].includes(request.method()) && !url.pathname.endsWith('/itinerary-candidate'))
        session.writeMethods.push(request.method());
      if (request.method() === 'POST' && url.pathname.endsWith('/itinerary-candidate')) session.candidateRequests++;
    }
    if (url.origin === oidcOrigin && url.pathname.endsWith('/protocol/openid-connect/auth')) {
      if (
        url.searchParams.get('response_type') === 'code' &&
        url.searchParams.get('code_challenge_method') === 'S256' &&
        /^[A-Za-z0-9_-]{43,128}$/.test(url.searchParams.get('code_challenge') ?? '')
      )
        session.auth.codePkce++;
    }
    if (url.origin === oidcOrigin && url.pathname.endsWith('/protocol/openid-connect/token')) {
      const form = new URLSearchParams(request.postData() ?? '');
      if (form.get('grant_type') === 'authorization_code' && form.has('code_verifier') && form.has('code'))
        session.auth.codeExchange++;
      else if (form.get('grant_type') !== 'refresh_token') session.auth.forbiddenGrant++;
    }
  });
  context.on('response', (response) => {
    const url = new URL(response.url());
    if (
      url.origin === oidcOrigin &&
      url.pathname.endsWith('/protocol/openid-connect/token') &&
      response.status() === 200
    )
      session.auth.tokenSuccess++;
  });
  return session;
}

async function login(session: Session, state: OwnedState, credential: Credential, path = '/trips'): Promise<void> {
  const page = session.page;
  await page.goto(path);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await page.waitForURL(
    (url) =>
      url.origin === `http://127.0.0.1:${state.oidcPort}` && url.pathname.endsWith('/protocol/openid-connect/auth'),
  );
  await page.locator('#username').fill(credential.username);
  await page.locator('#password').fill(credential.password);
  await page.locator('#kc-login').click();
  await expect(page.getByRole('button', { name: 'Выйти', exact: true })).toBeVisible();
  await expect.poll(() => session.token !== null).toBe(true);
  await expect
    .poll(() => {
      const url = new URL(page.url());
      return url.origin === `http://127.0.0.1:${state.webPort}` && url.pathname === path && !url.search && !url.hash;
    })
    .toBe(true);
  expect(session.auth.codePkce).toBeGreaterThan(0);
  expect(session.auth.codeExchange).toBe(1);
  expect(session.auth.tokenSuccess).toBeGreaterThan(0);
  expect(session.auth.forbiddenGrant).toBe(0);
  // Inspect only in memory. Real Host requests independently validate this token.
  const claims: { iat?: unknown } = JSON.parse(
    Buffer.from(session.token?.split('.')[1] ?? '', 'base64url').toString('utf8'),
  );
  expect(
    typeof claims.iat === 'number' &&
      Number.isSafeInteger(claims.iat) &&
      Math.abs(Date.now() - claims.iat * 1000) <= 120_000,
  ).toBe(true);
}

function apiResponse(page: Page, method: string, path: string): Promise<Response> {
  return page.waitForResponse(
    (response) => response.request().method() === method && new URL(response.url()).pathname === path,
  );
}

async function ownerRead(session: Session, tripId: string): Promise<TripPlan> {
  expect(session.token !== null).toBe(true);
  const response = await session.context.request.get(`/api/trips/${tripId}`, {
    headers: { Authorization: `Bearer ${session.token}` },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(200);
  return response.json();
}

async function applicationDiagnostic(page: Page, state: OwnedState): Promise<ApplicationDiagnostic | null> {
  if (page.isClosed()) return null;
  try {
    return await page.evaluate(
      ({ allowedOrigin, statusSelector }) => {
        // Never inspect a login document, OIDC callback, foreign origin or dirty URL.
        if (
          location.origin !== allowedOrigin ||
          location.search ||
          location.hash ||
          !/^\/trips\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(location.pathname)
        )
          return null;
        const app = document.querySelector('main.trips-page');
        const loggedIn = Array.from(app?.querySelectorAll('button') ?? []).some(
          (button) => button.textContent?.trim() === 'Выйти' && button.getClientRects().length > 0,
        );
        if (!loggedIn) return null;
        const matches = app?.querySelectorAll(statusSelector);
        const matchCount = matches?.length ?? 0;
        const status = matchCount === 1 ? matches?.[0] : null;
        const text = (status?.textContent ?? '').slice(0, 512);
        // Only bounded digits and booleans leave the browser. No raw text or URL.
        return {
          saveStatusPresent: status !== null && status !== undefined,
          saveStatusMatchCount: Math.min(matchCount, 100),
          displayedRevision: /версия\s*([1-9]\d{0,18})\s*$/u.exec(text)?.[1] ?? null,
          revisionHasSeparatingWhitespace: /версия\s+[1-9]\d{0,18}\s*$/u.test(text),
          clean: /Без\s+несохранённых\s+изменений/u.test(text),
          dirty: /Есть\s+несохранённые\s+изменения/u.test(text),
        };
      },
      { allowedOrigin: `http://127.0.0.1:${state.webPort}`, statusSelector: editorSaveStatusSelector },
    );
  } catch {
    return null;
  }
}

async function assertEditor(page: Page, plan: TripPlan): Promise<void> {
  await expect(page.locator('#trip-title')).toHaveValue(plan.title);
  await expect(page.locator('#trip-destination')).toHaveValue(plan.brief.destinationId);
  await expect(page.locator('#trip-origin')).toHaveValue(plan.brief.originId ?? '0: null');
  await expect(page.locator('#trip-start')).toHaveValue(plan.brief.startDate);
  await expect(page.locator('#trip-end')).toHaveValue(plan.brief.endDate);
  await expect(page.locator('#trip-adults')).toHaveValue(String(plan.brief.adults));
  await expect(page.locator('#trip-currency')).toHaveValue(plan.brief.currency);
  await expect(page.locator('#trip-budget')).toHaveValue(plan.brief.totalBudget ?? '');
  await expect(page.locator('#trip-pace')).toHaveValue(plan.brief.pace);
  for (const interest of Object.keys(interestLabels) as TripInterest[])
    await expect(page.getByLabel(interestLabels[interest], { exact: true })).toBeChecked({
      checked: plan.brief.interests.includes(interest),
    });
  await expect(page.locator('.calendar .day')).toHaveCount(plan.days.length);
  for (let index = 0; index < plan.days.length; index++) {
    const day = plan.days[index];
    const section = page.locator('.calendar .day').nth(index);
    await expect(section).toHaveAttribute('aria-label', `День ${index + 1} ${day.date}`);
    expect(
      await section
        .locator('input[id^="block-"]')
        .evaluateAll((elements) => elements.map((element) => element.id.slice(6))),
    ).toEqual(day.blocks.map((block) => block.blockId));
    for (const block of day.blocks) {
      await expect(page.locator(`#block-${block.blockId}`)).toHaveValue(block.title);
      await expect(page.locator(`#note-${block.blockId}`)).toHaveValue(block.note ?? '');
      await expect(page.locator(`#start-${block.blockId}`)).toHaveValue(block.startTime ?? '');
      await expect(page.locator(`#end-${block.blockId}`)).toHaveValue(block.endTime ?? '');
      await expect(page.locator(`#estimate-${block.blockId}`)).toHaveValue(block.estimate?.amount ?? '');
      await expect(page.locator(`#move-${block.blockId}`)).toHaveValue(day.date);
    }
  }
  expect(/^[1-9]\d*$/.test(plan.revision)).toBe(true);
  const saveStatus = page.locator(editorSaveStatusSelector);
  await expect(saveStatus).toHaveCount(1);
  await expect(saveStatus).toContainText(new RegExp(`версия\\s*${plan.revision}\\s*$`));
  await expect(saveStatus).toContainText('Без несохранённых изменений');
  await expect(page.locator('.estimates strong')).toHaveText(
    new RegExp(`^\\s*${plan.knownUserEstimateSubtotal.replace('.', '\\.')}\\s*${plan.brief.currency}\\s*$`),
  );
  await expect(page.locator('.estimates')).toContainText(
    new RegExp(`Блоков\\s+без\\s+оценки:\\s*${plan.unknownEstimateCount}\\s*\\.`),
  );
}

async function assertRefusal(response: APIResponse, status: number): Promise<void> {
  expect(response.status()).toBe(status);
}

test('real Trips PKCE, explicit save, full restart, private ownership and stale revision', async ({
  browser,
  playwright,
}, info) => {
  const startedAtUtc = new Date().toISOString();
  const stageResults: { name: Stage; outcome: 'running' | 'passed' | 'failed' }[] = [];
  const sessions: Session[] = [];
  let currentStage: Stage = 'validate-owned-state';
  let evidenceDir: string | undefined;
  let state: OwnedState | undefined;
  let saved: TripPlan | undefined;
  let historical: { plan: TripPlan; replacement: ReplaceTrip; snapshot: DatabaseSnapshot } | undefined;
  let savedSnapshot: DatabaseSnapshot | undefined;
  let restart: RestartReceipt | undefined;
  let failed = false;
  let failureLine: number | null = null;
  let failure: SafeFailure | null = null;
  let applicationFailure: ApplicationDiagnostic | null = null;

  async function stage<T>(name: Stage, action: () => Promise<T>): Promise<T> {
    currentStage = name;
    info.annotations.push({ type: 'trips-safe-stage', description: name });
    const result: (typeof stageResults)[number] = { name, outcome: 'running' };
    stageResults.push(result);
    try {
      const value = await action();
      result.outcome = 'passed';
      return value;
    } catch (error) {
      result.outcome = 'failed';
      failure = safeFailure(error);
      const location = error instanceof Error ? /trips-full-stack\.spec\.ts:(\d+):\d+/.exec(error.stack ?? '') : null;
      failureLine = location ? Number(location[1]) : null;
      if (state) {
        for (const session of [...sessions].reverse()) {
          applicationFailure = await applicationDiagnostic(session.page, state);
          if (applicationFailure) break;
        }
      }
      // Never propagate Playwright's call log: it can contain fill(password) or OIDC query strings.
      throw new Error(`Trips full-stack stage failed: ${name}. Private details suppressed.`);
    }
  }

  try {
    // Keep the Node ESM controller native when Playwright transpiles this test to CommonJS.
    // Test-only owned stack lifecycle tool outside Nx projects, following the existing E2E driver boundary.
    // eslint-disable-next-line @nx/enforce-module-boundaries
    const { captureDatabaseSnapshot, readState, restartStack, e2eProfile } = await import(
      '../../../tools/trips/local-stack.mjs'
    );
    const { credentials, ownedState, artifactDir } = await stage('validate-owned-state', async () => {
      const statePath = process.env['TRIPS_STACK_STATE'];
      expect(typeof statePath === 'string' && statePath.length > 0).toBe(true);
      const validated: OwnedState = await readState(statePath);
      state = validated;
      expect(validated.mode).toBe('e2e');
      expect(validated.webPort).toBe(4206);
      expect(validated.oidcPort).toBe(8186);
      expect(validated.applicationAsOfUtc).toBe(e2eProfile(validated.e2eProfile).instant);
      const directory = resolve(validated.stateDir, 'evidence');
      expect(process.env['TRIPS_STACK_EVIDENCE'] === directory).toBe(true);
      await mkdir(directory, { recursive: true });
      expect((await lstat(directory)).isSymbolicLink()).toBe(false);
      expect(await realpath(directory)).toBe(directory);
      evidenceDir = directory;
      return { credentials: await readCredentials(validated), ownedState: validated, artifactDir: directory };
    });
    const profile = e2eProfile(ownedState.e2eProfile);
    const fixedInstant = profile.instant;
    const editedTitle = `Fictional ${profile.destination} — saved after browser edits`;
    await stage('anonymous-refusal', async () => {
      const anonymous = await playwright.request.newContext({ baseURL: `http://127.0.0.1:${ownedState.webPort}` });
      try {
        await assertRefusal(await anonymous.get('/api/trips', { maxRedirects: 0 }), 401);
      } finally {
        await anonymous.dispose();
      }
    });
    const owner = await stage('owner-login', async () => {
      const session = await freshSession(browser, ownedState);
      sessions.push(session);
      await login(session, ownedState, credentials[0]);
      await expect(session.page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
      return session;
    });
    const page = owner.page;
    const { foundation, initialSnapshot } = await stage('create-foundation', async () => {
      await page.locator('#trip-title').fill(`Fictional ${profile.destination} foundation`);
      await page.locator('#trip-destination').selectOption(profile.destination);
      await page.locator('#trip-origin').selectOption(profile.origin);
      await page.locator('#trip-start').fill(profile.dates[0]);
      await page.locator('#trip-end').fill(profile.dates[2]);
      await page.locator('#trip-adults').fill('2');
      await page.locator('#trip-currency').selectOption('RUB');
      await page.locator('#trip-budget').fill('15000.00');
      await page.locator('#trip-pace').selectOption('balanced');
      await page.getByLabel(interestLabels.architecture, { exact: true }).check();
      await page.getByLabel(interestLabels.nature, { exact: true }).check();
      const created = await observeResponse(apiResponse(page, 'POST', '/api/trips'), () =>
        page.getByRole('button', { name: 'Создать основу плана', exact: true }).click(),
      );
      expect(created.status()).toBe(201);
      expect(created.headers()['etag']).toBe('"1"');
      const location = created.headers()['location'];
      expect(location).toMatch(/^\/api\/trips\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i);
      const tripId = location.slice('/api/trips/'.length);
      // The UI decodes the real POST body and navigates. Read persisted state using
      // owner GET: Chromium can discard its inspector resource during that navigation.
      const plan = await ownerRead(owner, tripId);
      expect(plan.tripId).toBe(tripId);
      expect(guid.test(plan.tripId)).toBe(true);
      await expect(page).toHaveURL(`http://127.0.0.1:${ownedState.webPort}/trips/${tripId}`);
      expect(Date.parse(plan.createdAt)).toBe(Date.parse(fixedInstant));
      expect(Date.parse(plan.updatedAt)).toBe(Date.parse(fixedInstant));
      expect(plan.revision).toBe('1');
      expect(plan.title).toBe(`Fictional ${profile.destination} foundation`);
      expect(plan.brief).toEqual({
        destinationId: profile.destination,
        originId: profile.origin,
        startDate: profile.dates[0],
        endDate: profile.dates[2],
        adults: 2,
        currency: 'RUB',
        totalBudget: '15000.00',
        interests: ['architecture', 'nature'],
        pace: 'balanced',
      });
      expect(plan.days.map((day) => day.date)).toEqual(profile.dates);
      expect(plan.days.every((day) => day.blocks.length === 0)).toBe(true);
      await expect(page.getByRole('heading', { name: 'Ваш черновик' })).toBeVisible();
      await assertEditor(page, plan);
      expect(owner.writeMethods).toEqual(['POST']);
      const snapshot = (await captureDatabaseSnapshot(ownedState, plan.tripId)) as DatabaseSnapshot;
      expect(snapshot.ownerSubject).toBe(credentials[0].subject);
      expect(snapshot.ownerIssuer).toBe(`http://127.0.0.1:${ownedState.oidcPort}/realms/travel`);
      return { foundation: plan, initialSnapshot: snapshot };
    });
    const candidate = await stage('generate-without-write', async () => {
      const { response: generated, body: proposed } = await observeCandidateResponse<TripItineraryComputed>(
        page,
        `http://127.0.0.1:${ownedState.webPort}/api/trips/${foundation.tripId}/itinerary-candidate`,
        () => page.getByRole('button', { name: 'Предложить прогулочный маршрут', exact: true }).click(),
        30_000,
        (bytes) => {
          const result = decodeTripItineraryCandidateV2(bytes);
          if (result.outcome === 'unsupported') throw new Error('Expected computed candidate');
          return result;
        },
      );
      expect(generated.status()).toBe(200);
      expect(generated.request().headers()['x-trips-itinerary-version']).toBe('2');
      expect(proposed.candidateVersion).toBe(2);
      expect(proposed.destinationId).toBe(profile.destination);
      expect(Date.parse(proposed.generatedAt)).toBe(Date.parse(fixedInstant));
      expect(proposed.outcome).toBe('candidate');
      expect(proposed.baseRevision).toBe(foundation.revision);
      expect(proposed.dataMode).toBe('osm-modelled-demo');
      expect(proposed.humanQuality).toBe('not-evaluated');
      expect(proposed.days.map((day) => day.blocks.length)).toEqual([3, 3, 3]);
      expect(new Set(proposed.days.flatMap((day) => day.blocks.map((block) => block.proposedBlockId))).size).toBe(9);
      await expect(page.locator('#itinerary-preview')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Перенести в редактор', exact: true })).toBeEnabled();
      expect(owner.writeMethods).toEqual(['POST']);
      expect(await ownerRead(owner, foundation.tripId)).toEqual(foundation);
      expect(await captureDatabaseSnapshot(ownedState, foundation.tripId)).toEqual(initialSnapshot);
      expect(owner.candidateRequests).toBe(1);
      return proposed;
    });
    const expected: ReplaceTrip = {
      title: editedTitle,
      brief: structuredClone(foundation.brief),
      days: candidate.days.map((day) => ({
        date: day.date,
        blocks: day.blocks.map((block) => ({
          blockId: block.proposedBlockId,
          title: block.title,
          note: block.note,
          startTime: null,
          endTime: null,
          estimate: null,
        })),
      })),
    };
    await stage('adopt-without-write', async () => {
      await page.getByRole('button', { name: 'Перенести в редактор', exact: true }).click();
      await expect(page.getByLabel('Название блока', { exact: true })).toHaveCount(9);
      await expect(page.locator('#itinerary-preview')).toHaveCount(0);
      expect(
        await page
          .getByLabel('Название блока', { exact: true })
          .evaluateAll((elements) => elements.map((element) => element.id.slice(6))),
      ).toEqual(expected.days.flatMap((day) => day.blocks.map((block) => block.blockId)));
      expect(owner.writeMethods).toEqual(['POST']);
      expect(await ownerRead(owner, foundation.tripId)).toEqual(foundation);
      expect(await captureDatabaseSnapshot(ownedState, foundation.tripId)).toEqual(initialSnapshot);
    });
    saved = await stage('edit-and-save', async () => {
      const edited = expected.days[0].blocks[0];
      const moved = expected.days[0].blocks[2];
      edited.title = editedBlockTitle;
      edited.note = editedNote;
      edited.startTime = '10:05';
      edited.endTime = '11:10';
      edited.estimate = { amount: '1234.56', currency: 'RUB' };
      await page.locator('#trip-title').fill(editedTitle);
      await page.locator(`#block-${edited.blockId}`).fill(edited.title);
      await page.locator(`#note-${edited.blockId}`).fill(edited.note);
      await page.locator(`#start-${edited.blockId}`).fill(edited.startTime);
      await page.locator(`#end-${edited.blockId}`).fill(edited.endTime);
      await page.locator(`#estimate-${edited.blockId}`).fill(edited.estimate.amount);
      const fieldset = page.locator('fieldset.block').filter({ has: page.locator(`#block-${edited.blockId}`) });
      await fieldset.getByRole('button', { name: 'Ниже', exact: true }).click();
      [expected.days[0].blocks[0], expected.days[0].blocks[1]] = [
        expected.days[0].blocks[1],
        expected.days[0].blocks[0],
      ];
      await page.locator(`#move-${moved.blockId}`).selectOption(profile.dates[1]);
      expected.days[0].blocks.splice(2, 1);
      expected.days[1].blocks.push(moved);
      expect(await ownerRead(owner, foundation.tripId)).toEqual(foundation);
      expect(owner.writeMethods).toEqual(['POST']);
      const response = apiResponse(page, 'PUT', `/api/trips/${foundation.tripId}`);
      await page.getByRole('button', { name: 'Сохранить', exact: true }).click();
      const accepted = await response;
      expect(accepted.status()).toBe(200);
      expect(accepted.request().headers()['if-match']).toBe('"1"');
      expect(accepted.request().postDataJSON()).toEqual(expected);
      // The real UI decodes the PUT response; owner GET is authoritative and does not
      // depend on Chromium retaining that response body in its debugging-protocol cache.
      const plan = await ownerRead(owner, foundation.tripId);
      expect({ title: plan.title, brief: plan.brief, days: plan.days }).toEqual(expected);
      expect(plan.tripId).toBe(foundation.tripId);
      expect(plan.revision).toBe('2');
      expect(plan.knownUserEstimateSubtotal).toBe('1234.56');
      expect(plan.unknownEstimateCount).toBe(8);
      expect(plan.warnings).toEqual([]);
      expect(owner.writeMethods).toEqual(['POST', 'PUT']);
      await assertEditor(page, plan);
      expect(await ownerRead(owner, plan.tripId)).toEqual(plan);
      return plan;
    });
    const accepted = saved;
    await stage('list-and-reopen', async () => {
      const response = apiResponse(page, 'GET', '/api/trips');
      await page.getByRole('button', { name: 'Обновить список', exact: true }).click();
      expect((await response).status()).toBe(200);
      await page.getByRole('button', { name: 'Новый план', exact: true }).click();
      await expect(page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
      const read = await observeResponse(apiResponse(page, 'GET', `/api/trips/${accepted.tripId}`), () =>
        page.locator(`aside a[href="/trips/${accepted.tripId}"]`).click(),
      );
      expect(read.status()).toBe(200);
      expect(read.headers()['etag']).toBe(`"${accepted.revision}"`);
      await expect(page).toHaveURL(`http://127.0.0.1:${ownedState.webPort}/trips/${accepted.tripId}`);
      expect(await ownerRead(owner, accepted.tripId)).toEqual(accepted);
      await assertEditor(page, accepted);
      savedSnapshot = (await captureDatabaseSnapshot(ownedState, accepted.tripId)) as DatabaseSnapshot;
      expect(String(savedSnapshot.revision)).toBe(accepted.revision);
      expect(savedSnapshot.ownerSubject).toBe(credentials[0].subject);
      expect(savedSnapshot.payloadLength).toBeGreaterThan(0);
      expect(savedSnapshot.payloadSha256).toMatch(/^[0-9a-f]{64}$/i);
      expect(savedSnapshot.payloadSha256).not.toBe(initialSnapshot.payloadSha256);
      expect(Object.keys(savedSnapshot.keyFileHashes).length).toBeGreaterThan(0);
      expect(savedSnapshot.keyFileHashes).toEqual(initialSnapshot.keyFileHashes);
      await page.screenshot({ path: resolve(artifactDir, 'saved-plan.png'), fullPage: true });
    });
    if (profile.destination === 'saint-petersburg')
      await stage('historical-moscow-save', async () => {
        const body = {
          title: 'FICTIONAL historical Moscow manual plan',
          brief: {
            destinationId: 'moscow',
            originId: 'saint-petersburg',
            startDate: '2026-10-25',
            endDate: '2026-10-27',
            adults: 2,
            currency: 'RUB',
            totalBudget: '15000.00',
            interests: ['architecture', 'nature'],
            pace: 'balanced',
          },
        };
        const headers = { Authorization: `Bearer ${owner.token}` };
        const created = await owner.context.request.post('/api/trips', { headers, data: body, maxRedirects: 0 });
        expect(created.status()).toBe(201);
        expect(created.headers()['etag']).toBe('"1"');
        const initial: TripPlan = await created.json();
        const replacement: ReplaceTrip = {
          title: body.title,
          brief: initial.brief,
          days: initial.days.map((day, index) => ({
            date: day.date,
            blocks:
              index === 0
                ? [
                    {
                      blockId: 'bb511111-2222-4333-8444-555555555555',
                      title: 'FICTIONAL historical Moscow intention',
                      note: 'Historical manual intention; no current access or price claim.',
                      startTime: '09:15',
                      endTime: '10:00',
                      estimate: { amount: '123.45', currency: 'RUB' },
                    },
                  ]
                : [],
          })),
        };
        const savedResponse = await owner.context.request.put(`/api/trips/${initial.tripId}`, {
          headers: { ...headers, 'If-Match': '"1"' },
          data: replacement,
          maxRedirects: 0,
        });
        expect(savedResponse.status()).toBe(200);
        expect(savedResponse.headers()['etag']).toBe('"2"');
        const plan = await ownerRead(owner, initial.tripId);
        expect({ title: plan.title, brief: plan.brief, days: plan.days }).toEqual(replacement);
        expect(plan.destination.catalogueVersion).toBe('1');
        expect(plan.destination.destinationId).toBe('moscow');
        historical = { plan, replacement, snapshot: await captureDatabaseSnapshot(ownedState, plan.tripId) };
        expect(historical.snapshot.keyFileHashes).toEqual(savedSnapshot?.keyFileHashes);
      });
    await stage('complete-service-restart', async () => {
      await owner.context.close();
      owner.token = null;
      restart = (await restartStack(ownedState)) as RestartReceipt;
      expect(restart.storagePreserved).toBe(true);
      expect(restart.before).toHaveLength(3);
      expect(restart.stopped).toHaveLength(3);
      expect(restart.after).toHaveLength(3);
      expect(restart.before.map((entry) => entry.service).sort()).toEqual(['host', 'keycloak', 'postgres']);
      for (const before of restart.before) {
        const stopped = restart.stopped.find((entry) => entry.service === before.service);
        const after = restart.after.find((entry) => entry.service === before.service);
        if (!stopped || !after) throw new Error('Missing required process restart evidence.');
        expect(before.running).toBe(true);
        expect(before.pid).toBeGreaterThan(0);
        expect(stopped.running).toBe(false);
        expect(stopped.pid).toBe(0);
        expect(after.running).toBe(true);
        expect(after.pid).toBeGreaterThan(0);
        expect(after.pid).not.toBe(before.pid);
        expect(after.startedAt).not.toBe(before.startedAt);
        expect(after.containerId).toBe(before.containerId);
        expect(after.mounts).toEqual(before.mounts);
      }
    });
    const reopenedOwner = await stage('fresh-login-and-compare', async () => {
      const session = await freshSession(browser, ownedState);
      sessions.push(session);
      // A new context has no cookies or saved state from the previous login.
      expect(await session.context.cookies()).toEqual([]);
      await login(session, ownedState, credentials[0], `/trips/${accepted.tripId}`);
      expect(await ownerRead(session, accepted.tripId)).toEqual(accepted);
      await assertEditor(session.page, accepted);
      expect(await captureDatabaseSnapshot(ownedState, accepted.tripId)).toEqual(savedSnapshot);
      await session.page.screenshot({ path: resolve(artifactDir, 'reopened-after-restart.png'), fullPage: true });
      if (historical) {
        expect(await ownerRead(session, historical.plan.tripId)).toEqual(historical.plan);
        expect(await captureDatabaseSnapshot(ownedState, historical.plan.tripId)).toEqual(historical.snapshot);
        // Use the ordinary SPA links: a document reload intentionally discards the
        // in-memory access token and would require another explicit login.
        await session.page.locator(`aside a[href="/trips/${historical.plan.tripId}"]`).click();
        await assertEditor(session.page, historical.plan);
        await session.page.locator(`aside a[href="/trips/${accepted.tripId}"]`).click();
        await assertEditor(session.page, accepted);
      }
      return session;
    });
    await stage('foreign-owner-refusals', async () => {
      const foreign = await freshSession(browser, ownedState);
      sessions.push(foreign);
      await login(foreign, ownedState, credentials[1]);
      const headers = { Authorization: `Bearer ${foreign.token}`, 'If-Match': `"${accepted.revision}"` };
      const list = await foreign.context.request.get('/api/trips', { headers, maxRedirects: 0 });
      expect(list.status()).toBe(200);
      const listed = await list.json();
      expect(listed.items).toEqual([]);
      expect(listed.nextCursor).toBeNull();
      await expect(foreign.page.locator('aside .plan-link')).toHaveCount(0);
      await assertRefusal(
        await foreign.context.request.get(`/api/trips/${accepted.tripId}`, { headers, maxRedirects: 0 }),
        404,
      );
      await assertRefusal(
        await foreign.context.request.put(`/api/trips/${accepted.tripId}`, {
          headers,
          data: expected,
          maxRedirects: 0,
        }),
        404,
      );
      await assertRefusal(
        await foreign.context.request.delete(`/api/trips/${accepted.tripId}`, { headers, maxRedirects: 0 }),
        404,
      );
      if (historical) {
        const historicHeaders = { ...headers, 'If-Match': `"${historical.plan.revision}"` };
        const path = `/api/trips/${historical.plan.tripId}`;
        await assertRefusal(
          await foreign.context.request.get(path, { headers: historicHeaders, maxRedirects: 0 }),
          404,
        );
        await assertRefusal(
          await foreign.context.request.put(path, {
            headers: historicHeaders,
            data: historical.replacement,
            maxRedirects: 0,
          }),
          404,
        );
        await assertRefusal(
          await foreign.context.request.delete(path, { headers: historicHeaders, maxRedirects: 0 }),
          404,
        );
        expect(await ownerRead(reopenedOwner, historical.plan.tripId)).toEqual(historical.plan);
        expect(await captureDatabaseSnapshot(ownedState, historical.plan.tripId)).toEqual(historical.snapshot);
      }
      expect(await ownerRead(reopenedOwner, accepted.tripId)).toEqual(accepted);
      expect(await captureDatabaseSnapshot(ownedState, accepted.tripId)).toEqual(savedSnapshot);
    });
    await stage('stale-revision-refusal', async () => {
      const response = await reopenedOwner.context.request.put(`/api/trips/${accepted.tripId}`, {
        headers: { Authorization: `Bearer ${reopenedOwner.token}`, 'If-Match': `"${foundation.revision}"` },
        data: { ...expected, title: 'Fictional stale write must never persist' },
        maxRedirects: 0,
      });
      await assertRefusal(response, 412);
      if (historical) {
        await assertRefusal(
          await reopenedOwner.context.request.put(`/api/trips/${historical.plan.tripId}`, {
            headers: { Authorization: `Bearer ${reopenedOwner.token}`, 'If-Match': '"1"' },
            data: { ...historical.replacement, title: 'FICTIONAL stale historical edit' },
            maxRedirects: 0,
          }),
          412,
        );
        expect(await ownerRead(reopenedOwner, historical.plan.tripId)).toEqual(historical.plan);
        expect(await captureDatabaseSnapshot(ownedState, historical.plan.tripId)).toEqual(historical.snapshot);
      }
      expect(await ownerRead(reopenedOwner, accepted.tripId)).toEqual(accepted);
      expect(await captureDatabaseSnapshot(ownedState, accepted.tripId)).toEqual(savedSnapshot);
      await assertEditor(reopenedOwner.page, accepted);
    });
    await stage('private-evidence', async () => {
      expect(sessions.every((session) => session.externalRequests === 0)).toBe(true);
      expect(sessions.every((session) => session.auth.forbiddenGrant === 0)).toBe(true);
      expect(sessions).toHaveLength(3);
      expect(sessions.every((session) => session.auth.codeExchange === 1)).toBe(true);
      expect(sessions.reduce((total, session) => total + session.candidateRequests, 0)).toBe(1);
    });
  } catch (error) {
    failed = true;
    failure ??= safeFailure(error);
  } finally {
    for (const session of sessions) {
      session.token = null;
      try {
        await session.context.close();
      } catch (error) {
        failed = true;
        failure ??= safeFailure(error);
      }
    }
    if (evidenceDir && state) {
      try {
        // Intentional allowlist: never serialize credentials, API bodies, errors, URLs,
        // request headers, authentication state, ciphertext or protection key material.
        await writeFile(
          resolve(evidenceDir, 'browser-receipt.json'),
          `${JSON.stringify(
            {
              schemaVersion: 1,
              stackId: state.id,
              status: failed ? 'failed' : 'passed',
              startedAtUtc,
              completedAtUtc: new Date().toISOString(),
              applicationAsOfUtc: state.applicationAsOfUtc,
              e2eProfile: state.e2eProfile ?? 'moscow',
              fixedBrowserDateOnly: true,
              realIdentityClock: true,
              stages: stageResults,
              failureLine,
              errorClass: failure?.errorClass ?? null,
              failureCategory: failure?.failureCategory ?? null,
              responseFailure: failure?.responseFailure ?? null,
              applicationFailure,
              acceptedPlanSha256: saved ? fingerprint(saved) : null,
              historicalMoscowPlanSha256: historical ? fingerprint(historical.plan) : null,
              historicalMoscowRowSha256: historical ? fingerprint(historical.snapshot) : null,
              protectedRowSha256: savedSnapshot ? fingerprint(savedSnapshot) : null,
              protectionKeyFingerprint: savedSnapshot ? fingerprint(savedSnapshot.keyFileHashes) : null,
              restart: restart
                ? {
                    serviceCount: restart.before.length,
                    allExited: restart.stopped.every((process) => process.pid === 0 && !process.running),
                    storagePreserved: restart.storagePreserved,
                  }
                : null,
              loginCount: sessions.reduce((total, session) => total + session.auth.codeExchange, 0),
              candidateRequests: sessions.reduce((total, session) => total + session.candidateRequests, 0),
              externalRequests: sessions.reduce((total, session) => total + session.externalRequests, 0),
            },
            null,
            2,
          )}\n`,
          { flag: 'wx' },
        );
      } catch {
        failed = true;
      }
    }
  }
  if (failed) throw new Error(`Trips full-stack failed during ${currentStage}. Private browser details suppressed.`);
});
