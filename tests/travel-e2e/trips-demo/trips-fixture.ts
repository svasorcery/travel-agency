import { createHash, randomUUID } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readFileSync, realpathSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path';
import { expect, type Page, type TestInfo } from '@playwright/test';
import type { RailSearchResult, RailSuggestions } from '@travel/api-client';

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
export type Plan = {
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
export type GateKind = 'candidate' | 'availability' | 'owner' | 'put' | 'rail-stations' | 'rail-search';
export const vector = (name: string, city = 'moscow') =>
  JSON.parse(
    readFileSync(
      resolve(
        name === 'error' ? 'tests/fixtures/trips-itinerary-v1' : 'tests/fixtures/trips-itinerary-v2',
        `${name === 'error' || name === 'unsupported' ? name : name + '-' + city}.json`,
      ),
      'utf8',
    ),
  );
export const browserArtifacts = resolve(process.env['TRIPS_DEMO_ARTIFACTS_DIR'] ?? 'TestResults/trips-m2/browser');
if (process.env['TRIPS_DEMO_ARTIFACTS_DIR']) {
  const base = realpathSync('TestResults');
  const inside = relative(base, browserArtifacts);
  if (
    !inside ||
    isAbsolute(inside) ||
    inside.startsWith(`..${sep}`) ||
    inside === '..' ||
    resolve(base, inside) !== browserArtifacts
  )
    throw new Error('Trips artifact path must be inside this worktree TestResults.');
  for (let path = browserArtifacts; path !== dirname(path); path = dirname(path))
    if (existsSync(path) && (lstatSync(path).isSymbolicLink() || realpathSync(path) !== path))
      throw new Error('Trips artifact path cannot escape through a link.');
}
export function atomicJson(path: string, value: unknown) {
  if (process.env['TRIPS_DEMO_ARTIFACTS_DIR'] && existsSync(path)) throw new Error(`Receipt already exists: ${path}`);
  mkdirSync(resolve(path, '..'), { recursive: true });
  const tmp = `${path}.${randomUUID()}.tmp`;
  writeFileSync(tmp, `${JSON.stringify(value, null, 2)}\n`);
  renameSync(tmp, path);
}
const receipts = new WeakMap<
  Page,
  { requests: { method: string; url: string }[]; failures: string[]; calls: unknown[] }
>();
export async function recordReceipt(page: Page, info: TestInfo) {
  const receipt = receipts.get(page);
  if (!receipt) return;
  atomicJson(
    resolve(browserArtifacts, `ledger-${createHash('sha256').update(info.title).digest('hex').slice(0, 16)}.json`),
    {
      fictional: true,
      test: info.title,
      testId: info.testId,
      file: info.file,
      retry: info.retry,
      repeatEachIndex: info.repeatEachIndex,
      status: info.status,
      ...receipt,
    },
  );
  expect(receipt.failures, 'No unknown API methods/paths or external requests').toEqual([]);
  expect(receipt.requests.every((r) => new URL(r.url).origin === 'http://127.0.0.1:4203')).toBe(true);
  expect(page.context().pages()).toHaveLength(1);
}
/** FICTIONAL terminal route fixture: all API effects terminate in memory; no provider/model/Host. */
export async function isolated(page: Page, m2 = false, railEnabled = false) {
  const { fictionalRailSearch, fictionalRailSuggestions } = await import('@travel/demo/rail-search');
  const plans = new Map<string, { owner: string; plan: Plan }>();
  const calls: {
    method: string;
    path: string;
    owner?: string;
    ifMatch?: string;
    body?: string | null;
    status?: number;
  }[] = [];
  const requests: { method: string; url: string }[] = [],
    failures: string[] = [];
  receipts.set(page, { requests, failures, calls });
  page.on('request', (req) => requests.push({ method: req.method(), url: req.url() }));
  const terminal = {
    enabled: m2,
    availability: vector('ready'),
    availabilityStatus: 200,
    outcome: 'partial',
    candidateStatus: 200,
  };
  const rail = {
    now: Date.parse('2026-10-21T00:01:00Z'),
    preset: 'normal',
    stationStatus: 200,
    searchStatus: 200,
    reference: (value: RailSuggestions) => value,
    result: (value: RailSearchResult) => value,
  };
  const gates = new Map<GateKind, { wait: Promise<void>; arrived: () => void; returned: () => void }>();
  const wait = async (kind: GateKind) => {
    const gate = gates.get(kind);
    if (gate) {
      gate.arrived();
      await gate.wait;
    }
  };
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
    const kind: GateKind | null =
      url.pathname === '/api/rail/stations'
        ? 'rail-stations'
        : url.pathname === '/api/rail/search'
          ? 'rail-search'
          : url.pathname.endsWith('/itinerary-candidate')
            ? 'candidate'
            : url.pathname === '/api/trips/itinerary-availability'
              ? 'availability'
              : /^\/api\/trips\/[0-9a-f-]{36}$/.test(url.pathname) && req.method() === 'GET'
                ? 'owner'
                : req.method() === 'PUT'
                  ? 'put'
                  : null;
    const held = kind ? gates.get(kind) : undefined;
    try {
      await (async () => {
        if (url.origin !== 'http://127.0.0.1:4203') {
          failures.push(`external ${req.method()} ${req.url()}`);
          return route.abort('blockedbyclient');
        }
        if (!url.pathname.startsWith('/api/')) {
          await route.continue();
          return;
        }
        const method = req.method(),
          path = url.pathname;
        if (railEnabled && path.startsWith('/api/rail/')) {
          const station =
            path === '/api/rail/stations' &&
            method === 'GET' &&
            [...url.searchParams.keys()].join(',') === 'q' &&
            (url.searchParams.get('q')?.length ?? 0) >= 3 &&
            (url.searchParams.get('q')?.length ?? 0) <= 80;
          const search = path === '/api/rail/search' && method === 'POST' && !url.search;
          if ((!station && !search) || req.headers()['authorization']) {
            failures.push(`unknown or authorized Rail request ${method} ${req.url()}`);
            return route.abort('blockedbyclient');
          }
          const call = { method, path, body: req.postData(), status: station ? rail.stationStatus : rail.searchStatus };
          calls.push(call);
          const body = station
            ? rail.reference(
                structuredClone(fictionalRailSuggestions(url.searchParams.get('q') ?? '', rail.now, rail.preset)),
              )
            : rail.result(structuredClone(fictionalRailSearch(req.postDataJSON(), rail.now, rail.preset)));
          if (search)
            expect(Object.keys(req.postDataJSON()).sort()).toEqual([
              'catalogueGeneration',
              'departureDate',
              'from',
              'to',
              'transport',
            ]);
          await wait(station ? 'rail-stations' : 'rail-search');
          return route.fulfill({
            status: call.status,
            contentType: 'application/json',
            headers: { 'Cache-Control': 'no-store' },
            body: JSON.stringify(body),
          });
        }
        const allowed =
          (path === '/api/trips/destinations' && method === 'GET') ||
          (path === '/api/trips' && ['GET', 'POST'].includes(method)) ||
          (/^\/api\/trips\/[0-9a-f-]{36}$/.test(path) && ['GET', 'PUT', 'DELETE'].includes(method)) ||
          (path === '/api/trips/itinerary-availability' && method === 'GET') ||
          (/^\/api\/trips\/[0-9a-f-]{36}\/itinerary-candidate$/.test(path) && method === 'POST');
        const cityQuery =
          path === '/api/trips/itinerary-availability' &&
          method === 'GET' &&
          [...url.searchParams.keys()].length === 1 &&
          url.searchParams.has('destinationId') &&
          ['moscow', 'saint-petersburg', 'kaliningrad'].includes(url.searchParams.get('destinationId') ?? '');
        if (!allowed || (url.search && path !== '/api/trips' && !cityQuery)) {
          failures.push(`unknown ${method} ${req.url()}`);
          return route.abort('blockedbyclient');
        }
        const owner = req.headers()['authorization'];
        const call = { method, path, owner, ifMatch: req.headers()['if-match'], body: req.postData(), status: 0 };
        calls.push(call);
        const send = async (status: number, body: unknown, headers: Record<string, string> = {}) => {
          call.status = status;
          return route.fulfill({
            status,
            headers: { 'Cache-Control': 'no-store', ...headers },
            contentType: 'application/json',
            body: status === 204 ? '' : JSON.stringify(body),
          });
        };
        const problem = (status: number, code: string) =>
          send(status, { ...vector('error'), title: 'FICTIONAL terminal outcome', status, code });
        if (path === '/api/trips/itinerary-availability' && !terminal.enabled) return problem(404, 'Trips.NotFound');
        if (!owner) return send(401, { type: 'Trips.Unauthorized' });
        expect(['Bearer fictional-trips-owner-a', 'Bearer fictional-trips-owner-b']).toContain(owner);
        if (path === '/api/trips/itinerary-availability') {
          expect(req.headers()['x-trips-itinerary-version']).toBe('2');
          expect([...url.searchParams.keys()]).toEqual(['destinationId']);
          if (url.searchParams.get('destinationId') === 'kaliningrad')
            return send(200, {
              candidateVersion: 2,
              status: 'unsupported',
              destinationId: 'kaliningrad',
              reason: 'destinationUnsupported',
            });
          const snapshot = structuredClone(terminal.availability),
            status = terminal.availabilityStatus;
          await wait('availability');
          if (status === 0) return route.abort('failed');
          return status === 200
            ? send(200, snapshot)
            : problem(status, status === 404 ? 'Trips.NotFound' : 'Trips.ItineraryUnavailable');
        }
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
        if (!entry || entry.owner !== owner)
          return send(404, { type: 'about:blank', status: 404, code: 'Trips.NotFound' });
        const p = structuredClone(entry.plan);
        if (path.endsWith('/itinerary-candidate')) {
          if (!terminal.enabled) return problem(404, 'Trips.NotFound');
          expect(req.headers()['x-trips-itinerary-version']).toBe('2');
          expect(req.postData()).toBe('{}');
          expect(req.headers()['if-match']).toMatch(/^"[1-9][0-9]*"$/);
          const reply = vector(terminal.outcome, p.destination.destinationId),
            status = terminal.candidateStatus;
          // Only revision/date metadata bind the actual frozen vector to this fictional saved foundation.
          reply.baseRevision = p.revision;
          reply.destinationId = p.destination.destinationId;
          reply.timeZone = p.destination.timeZone;
          reply.catalogueVersion = p.destination.catalogueVersion;
          if (reply.days) {
            expect(p.days).toHaveLength(reply.days.length);
            reply.days.forEach((day: { date: string; blocks: { date: string }[] }, i: number) => {
              day.date = p.days[i].date;
              day.blocks.forEach((b) => {
                b.date = day.date;
              });
            });
          }
          const finalStatus =
            req.headers()['if-match'] !== `"${entry.plan.revision}"`
              ? 412
              : p.days.some((day) => day.blocks.length > 0)
                ? 409
                : status;
          await wait('candidate');
          if (finalStatus !== 200)
            return problem(
              finalStatus,
              finalStatus === 412
                ? 'Trips.RevisionMismatch'
                : finalStatus === 409
                  ? 'Trips.ItineraryRequiresEmptyPlan'
                  : 'Trips.ItineraryUnavailable',
            );
          return send(200, reply);
        }
        if (req.method() === 'GET') {
          await wait('owner');
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
          b.days.some(
            (day: Plan['days'][number]) => day.blocks.length > 0 && !p.days.some((old) => old.date === day.date),
          )
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
        const savedReply = structuredClone(entry.plan);
        await wait('put');
        return send(200, savedReply, { ETag: `"${savedReply.revision}"` });
      })();
    } finally {
      held?.returned();
    }
  });
  return {
    plans,
    calls,
    requests,
    failures,
    terminal,
    rail,
    hold(kind: GateKind) {
      let release: () => void = () => undefined,
        arrived: () => void = () => undefined,
        returned: () => void = () => undefined;
      const wait = new Promise<void>((resolve) => {
        release = resolve;
      });
      const started = new Promise<void>((resolve) => {
        arrived = resolve;
      });
      const settled = new Promise<void>((resolve) => {
        returned = resolve;
      });
      gates.set(kind, { wait, arrived, returned });
      return {
        started,
        settled,
        detach: () => {
          gates.delete(kind);
        },
        release: () => {
          gates.delete(kind);
          release();
        },
      };
    },
    loseNextSave: () => {
      lostPut = true;
    },
    delayReads: (wait: Promise<void> | null) => {
      delayedRead = wait;
    },
  };
}
export async function create(page: Page, moscow = false) {
  await page.goto('/trips');
  await expect(page.getByRole('heading', { name: 'Основа нового плана' })).toBeVisible();
  await page.getByLabel('Название плана', { exact: true }).fill('Fictional private plan');
  await page.getByLabel('Первый день').fill(moscow ? '2026-10-22' : '2026-11-14');
  await page.getByLabel('Последний день').fill(moscow ? '2026-10-22' : '2026-11-15');
  if (moscow) {
    await page.getByLabel('Город поездки', { exact: true }).selectOption('moscow');
    await page.getByLabel('Откуда (необязательно)').selectOption('saint-petersburg');
    await page.getByLabel('Темп', { exact: true }).selectOption('relaxed');
    await page.getByLabel('Архитектура', { exact: true }).check();
    await page.getByLabel('Еда', { exact: true }).check();
  }
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
