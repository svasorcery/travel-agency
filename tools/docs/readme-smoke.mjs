import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateCatalog } from './readme-examples.mjs';

function dateAfterThirtyDays(now) {
  const day = new Date(now.getTime() + 30 * 24 * 60 * 60 * 1000);
  return day.toISOString().slice(0, 10);
}

async function request(fetchImpl, url, options, timeoutMs) {
  const response = await fetchImpl(url, {
    ...options,
    redirect: 'error',
    signal: AbortSignal.timeout(timeoutMs),
  });
  return response;
}

async function json(response, phase) {
  try {
    return await response.json();
  } catch {
    throw new Error(phase + ' returned malformed JSON');
  }
}

export async function runSmoke({
  baseUrl = 'http://localhost:5099',
  fetchImpl = fetch,
  timeoutMs = 10000,
  now = new Date(),
} = {}) {
  const base = new URL(baseUrl);
  if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password) {
    throw new Error('base URL must be HTTP(S) without credentials');
  }
  const catalog = validateCatalog(
    JSON.parse(await readFile(new URL('../../docs/examples/flights-requests.json', import.meta.url), 'utf8')),
  );
  const url = (path) => new URL(path, base).toString();
  const status = await request(fetchImpl, url('/api/status'), { method: 'GET' }, timeoutMs);
  if (status.status !== 200 || (await json(status, 'status')).db !== 'ok') {
    throw new Error('status must return 200 with db=ok');
  }
  const openapi = await request(fetchImpl, url('/openapi/v1.json'), { method: 'GET' }, timeoutMs);
  if (openapi.status !== 200) throw new Error('OpenAPI must return 200');
  const document = await json(openapi, 'OpenAPI');
  for (const item of catalog) {
    if (!document.paths?.[item.path]?.[item.method.toLowerCase()]) {
      throw new Error('OpenAPI route or method missing for ' + item.id);
    }
  }
  const search = structuredClone(catalog[0].body);
  search.departureDate = dateAfterThirtyDays(now);
  search.origin = 'LE';
  const validation = await request(
    fetchImpl,
    url('/api/flights/search'),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(search),
    },
    timeoutMs,
  );
  if (validation.status !== 400 || !validation.headers.get('content-type')?.startsWith('application/problem+json')) {
    throw new Error('validation must return 400 ProblemDetails');
  }
  const problem = await json(validation, 'ProblemDetails');
  if (
    problem.status !== 400 ||
    !Array.isArray(problem.errors) ||
    !problem.errors.some((error) => error.code === 'IataCode.Length')
  ) {
    throw new Error('ProblemDetails must contain IataCode.Length');
  }
  return ['status', 'openapi', 'validation'];
}

async function main() {
  const args = process.argv.slice(2);
  if (args.length !== 0 && (args.length !== 2 || args[0] !== '--base-url')) {
    throw new Error('usage: node tools/docs/readme-smoke.mjs [--base-url http://localhost:5099]');
  }
  const phases = await runSmoke({ baseUrl: args[1] ?? undefined });
  process.stdout.write('README smoke passed: ' + phases.join(', ') + '\n');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  main().catch((error) => {
    process.stderr.write('README smoke failed: ' + error.message + '\n');
    process.exitCode = 1;
  });
}
