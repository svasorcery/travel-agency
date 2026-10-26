import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import {
  acquire,
  buildPair,
  CURRENT_MANIFEST_SHA,
  inspect,
  newSourceBytes,
  outputPath,
  prepare,
  publication,
  readCurrent,
  SOURCE_URL,
} from './moscow-refresh.mjs';

const at = new Date('2026-10-24T12:00:00Z');
const current = await readCurrent();
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const response = (date = 'Sun, 18 Oct 2026 03:55:13 GMT', body = null, length = current.input.bytes) =>
  new Response(body, {
    status: 200,
    headers: { 'Last-Modified': date, 'Content-Length': String(length) },
  });
async function workspace(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'travel-moscow-refresh-test-')));
  await mkdir(join(root, 'TestResults'));
  t.after(() => rm(root, { recursive: true, force: true }));
  return root;
}

test('a later publication with identical bytes cannot renew the source or become a candidate', () => {
  const input = { ...current.input, snapshotPublishedAtUtc: '2026-10-23T03:55:13Z', downloadedAtUtc: at.toISOString() };
  assert.equal(newSourceBytes(input, current.input, at), false);
  assert.equal(newSourceBytes({ ...input, sha256: 'a'.repeat(64) }, current.input, at), true);
  assert.throws(() => newSourceBytes({ ...input, snapshotPublishedAtUtc: '2026-10-18T03:55:13Z' }, current.input, at));
  assert.throws(() => newSourceBytes(input, current.input, new Date('2026-11-23T03:55:13Z')));
  assert.throws(() => newSourceBytes({ ...input, downloadedAtUtc: '2026-10-25T00:00:00Z' }, current.input, at));
});

test('a controlled new-source candidate requires two matching builds and explicit validation, without activation', async (t) => {
  const root = await workspace(t),
    output = join(root, 'TestResults', 'candidate');
  const bytes = Buffer.from('FICTIONAL newer map bytes'),
    date = 'Fri, 23 Oct 2026 03:55:13 GMT';
  let builds = 0,
    validations = 0;
  let clock = at,
    expireDuringValidation = false;
  const deps = {
    root,
    now: () => clock,
    fetchImpl: async (_url, { method }) => response(date, method === 'HEAD' ? null : bytes, bytes.length),
    build: async (args) => {
      builds++;
      const files = { ...current.files };
      const payload = JSON.parse(files['moscow-v1.json']);
      payload.version = '2';
      files['moscow-v1.json'] = Buffer.from(JSON.stringify(payload));
      files['input-manifest.json'] = await readFile(args.inputPath);
      files['moscow-v1.manifest.json'] = Buffer.from(
        JSON.stringify({
          datasetId: 'moscow-outdoor',
          version: '2',
          files: Object.fromEntries(
            Object.entries(files)
              .filter(([name]) => name !== 'moscow-v1.manifest.json')
              .map(([name, content]) => [name, sha(content)]),
          ),
        }),
      );
      await mkdir(args.output);
      for (const [name, content] of Object.entries(files)) await writeFile(join(args.output, name), content);
      return { status: 'built', pois: 20, paths: 90, elapsedMilliseconds: 1, peakWorkingSetBytes: 100 };
    },
    validate: async ({ manifestSha256, version }) => {
      validations++;
      if (expireDuringValidation) clock = new Date(current.payload.releaseValidUntil);
      return {
        ok: true,
        status: 'validated-not-released',
        datasetId: 'moscow-outdoor',
        version,
        manifestSha256,
        releaseValidUntil: current.payload.releaseValidUntil,
      };
    },
  };
  const result = await prepare({ version: '2', output }, deps);
  assert.equal(result.status, 'candidate-needs-review');
  assert.equal(builds, 2);
  assert.equal(validations, 1);
  assert.equal(result.input.sha256, sha(bytes));
  assert.equal(result.input.snapshotPublishedAtUtc, '2026-10-23T03:55:13.000Z');
  assert.equal((await readCurrent()).manifestSha256, CURRENT_MANIFEST_SHA);
  assert.equal(JSON.parse(await readFile(join(output, 'receipt.json'))).candidatePrepared, true);
  expireDuringValidation = true;
  const expiredOutput = join(root, 'TestResults', 'expired-during-build');
  await assert.rejects(prepare({ version: '2', output: expiredOutput }, deps), /package-expired-before-completion/);
  const failed = JSON.parse(await readFile(join(expiredOutput, 'receipt.json')));
  assert.equal(failed.status, 'failed');
  assert.equal(failed.error, 'package-expired-before-completion');
});

test('v1 remains bound to the exact reviewed manifest and actual descriptor', async () => {
  assert.equal(current.version, '1');
  assert.equal(current.manifestSha256, CURRENT_MANIFEST_SHA);
  assert.equal(current.payload.releaseValidUntil, '2026-11-17T03:55:13+00:00');
  const code = await readFile(
    'modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/CuratedItineraryRelease.cs',
    'utf8',
  );
  assert(code.includes(CURRENT_MANIFEST_SHA));
});

test('inspection reports publication separately from actual current expiry and sends only a fixed anonymous HEAD', async () => {
  const calls = [];
  const result = await inspect({
    now: () => at,
    fetchImpl: async (url, options) => {
      calls.push({ url, options });
      return response();
    },
  });
  assert.equal(result.publicationStatus, 'unchanged');
  assert.equal(result.currentStatus, 'valid');
  assert.equal(result.candidatePrepared, false);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, SOURCE_URL);
  assert.equal(calls[0].options.method, 'HEAD');
  assert.equal(calls[0].options.redirect, 'error');
  assert.equal(calls[0].options.credentials, 'omit');
  assert(calls[0].options.signal instanceof AbortSignal);
  const expired = await inspect({ now: () => new Date('2026-11-17T03:55:13Z'), fetchImpl: async () => response() });
  assert.equal(expired.currentStatus, 'expired');
  const prebuild = await inspect({ now: () => new Date('2026-10-20T12:00:00Z'), fetchImpl: async () => response() });
  assert.equal(prebuild.currentStatus, 'not-yet-valid');
});

test('versions cannot overwrite the baseline and corrupt current bytes cannot be silently repinned', async (t) => {
  const root = await workspace(t),
    directory = join(root, 'copy');
  for (const version of ['1', '0', '02', '2.0', '-2', '2/../1']) {
    await assert.rejects(
      prepare(
        { version, output: join(root, 'TestResults', 'out') },
        { root, fetchImpl: async () => assert.fail('must reject before network') },
      ),
      /new-version-required/,
    );
  }
  await mkdir(directory);
  for (const [name, bytes] of Object.entries(current.files)) await writeFile(join(directory, name), bytes);
  await writeFile(join(directory, 'ATTRIBUTION.txt'), 'changed');
  await assert.rejects(readCurrent(directory), /package-digest-mismatch/);
});

test('new-source preparation propagates a build failure and preserves a failed receipt without leaking child details', async (t) => {
  const root = await workspace(t),
    output = join(root, 'TestResults', 'failure');
  const bytes = Buffer.from('FICTIONAL next source'),
    date = 'Fri, 23 Oct 2026 03:55:13 GMT';
  const calls = [];
  await assert.rejects(
    prepare(
      { version: '2', output },
      {
        root,
        now: () => at,
        fetchImpl: async (_url, { method }) => {
          calls.push(method);
          return response(date, method === 'HEAD' ? null : bytes, bytes.length);
        },
        build: async () => {
          throw new Error('private child details');
        },
      },
    ),
  );
  assert.deepEqual(calls, ['HEAD', 'GET']);
  const receipt = await readFile(join(output, 'receipt.json'), 'utf8');
  assert.equal(JSON.parse(receipt).status, 'failed');
  assert(!receipt.includes('private child details'));
  assert.equal((await readCurrent()).manifestSha256, CURRENT_MANIFEST_SHA);
});

test('newer/older publication hints never claim accepted new bytes', async () => {
  for (const [date, status] of [
    ['Fri, 23 Oct 2026 03:55:13 GMT', 'newer'],
    ['Sat, 17 Oct 2026 03:55:13 GMT', 'older'],
  ]) {
    const result = await inspect({ now: () => at, fetchImpl: async () => response(date) });
    assert.equal(result.publicationStatus, status);
    assert.equal(result.candidatePrepared, false);
  }
});

test('inspection binds the observation clock after the HEAD response arrives', async () => {
  let now = new Date('2026-10-24T12:00:00Z');
  const result = await inspect({
    now: () => now,
    fetchImpl: async () => {
      now = new Date('2026-10-24T12:00:02Z');
      return response('Sat, 24 Oct 2026 12:00:01 GMT');
    },
  });
  assert.equal(result.publicationStatus, 'newer');
  assert.equal(result.checkedAtUtc, now.toISOString());
});

test('missing, malformed, future, redirect, status and oversized metadata fail instead of reporting unchanged', async () => {
  for (const r of [
    new Response(),
    response('bad'),
    response('Sun, 25 Oct 2026 03:55:13 GMT'),
    response(undefined, null, 200 * 1024 * 1024 + 1),
    response(undefined, null, -1),
    new Response(null, { status: 302 }),
    new Response(null, { status: 503 }),
  ]) {
    assert.throws(() => publication(r, at));
  }
  let calls = 0;
  await assert.rejects(
    inspect({
      now: () => at,
      fetchImpl: async () => {
        calls++;
        throw new Error('private network detail');
      },
    }),
    /source-request-failed/,
  );
  assert.equal(calls, 1);
});

test('unchanged publication produces no output, GET or build', async (t) => {
  const root = await workspace(t),
    output = join(root, 'TestResults', 'no-update');
  let calls = 0;
  const result = await prepare(
    { version: '2', output },
    {
      root,
      now: () => at,
      fetchImpl: async () => {
        calls++;
        return response();
      },
      build: async () => assert.fail('must not build'),
    },
  );
  assert.equal(result.status, 'no-new-publication');
  assert.equal(result.candidatePrepared, false);
  assert.equal(calls, 1);
  await assert.rejects(readFile(join(output, 'receipt.json')), { code: 'ENOENT' });
});

test('output refuses existing, foreign, relative and reparse targets before writes', async (t) => {
  const root = await workspace(t),
    existing = join(root, 'TestResults', 'existing');
  await mkdir(existing);
  await writeFile(join(existing, 'sentinel'), 'keep');
  for (const path of [existing, root, join(root, 'outside'), 'relative']) await assert.rejects(outputPath(path, root));
  await symlink(existing, join(root, 'TestResults', 'alias'), process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(outputPath(join(root, 'TestResults', 'alias', 'child'), root));
  assert.equal(await readFile(join(existing, 'sentinel'), 'utf8'), 'keep');
});

test('download binds one actual GET and refuses changed publication, length and invalid bytes', async (t) => {
  const root = await workspace(t),
    bytes = Buffer.from('FICTIONAL source bytes');
  const date = 'Fri, 23 Oct 2026 03:55:13 GMT',
    head = publication(response(date, null, bytes.length), at);
  const acquired = await acquire(response(date, bytes, bytes.length), head, join(root, 'source.gz'), at);
  assert.equal(acquired.sha256, sha(bytes));
  assert.equal(acquired.bytes, bytes.length);
  assert.equal(acquired.snapshotPublishedAtUtc, '2026-10-23T03:55:13.000Z');
  assert.equal(acquired.downloadedAtUtc, at.toISOString());
  await assert.rejects(
    acquire(response('Thu, 22 Oct 2026 03:55:13 GMT', bytes, bytes.length), head, join(root, 'wrong.gz'), at),
  );
  await assert.rejects(acquire(response(date, bytes.subarray(1), bytes.length), head, join(root, 'short.gz'), at));
  await assert.rejects(acquire(response(date, bytes, bytes.length - 1), head, join(root, 'long.gz'), at));
});

test('refused GET releases its stream and acquisition never replaces an existing file', async (t) => {
  const root = await workspace(t),
    destination = join(root, 'existing.gz');
  await writeFile(destination, 'keep');
  let cancelled = false;
  const r = new Response(
    new ReadableStream({
      cancel() {
        cancelled = true;
      },
    }),
    { headers: { 'Last-Modified': 'bad', 'Content-Length': '1' } },
  );
  const head = publication(response(), at);
  await assert.rejects(acquire(r, head, join(root, 'bad.gz'), at));
  assert.equal(cancelled, true);
  const valid = publication(response(undefined, null, 1), at);
  await assert.rejects(acquire(response(undefined, Buffer.from('x'), 1), valid, destination, at));
  assert.equal(await readFile(destination, 'utf8'), 'keep');
});

test('two builds compare exact bytes, propagate validator failure and bind input identities', async (t) => {
  const root = await workspace(t),
    source = join(root, 'source.gz');
  await writeFile(source, 'FICTIONAL');
  const files = current.files;
  const build = async ({ output }) => {
    await mkdir(output);
    for (const [name, bytes] of Object.entries(files)) await writeFile(join(output, name), bytes);
    return { status: 'built', pois: 20, paths: 90, elapsedMs: 1, peakWorkingSetBytes: 100 };
  };
  let validations = 0;
  const options = {
    output: join(root, 'proof'),
    osm: source,
    inventory: current.files['inventory.json'],
    inputManifest: current.files['input-manifest.json'],
    asOf: current.payload.buildAsOfUtc,
    version: '1',
    expectedFiles: files,
  };
  const helperPath = join(root, 'fixture-builder.cs');
  await writeFile(helperPath, 'FICTIONAL unchanged helper');
  const result = await buildPair(options, {
    build,
    validate: async () => {
      validations++;
      return {
        ok: true,
        status: 'validated-not-released',
        datasetId: 'moscow-outdoor',
        version: '1',
        manifestSha256: CURRENT_MANIFEST_SHA,
        pois: 20,
        edges: 90,
      };
    },
  });
  assert.equal(validations, 1);
  assert.equal(result.manifestSha256, CURRENT_MANIFEST_SHA);
  await assert.rejects(
    buildPair(
      { ...options, output: join(root, 'changed-helper'), helperPath },
      {
        build: async (args) => {
          const stats = await build(args);
          await writeFile(helperPath, 'changed helper');
          return stats;
        },
        validate: async () => assert.fail('must not validate changed build source'),
      },
    ),
    /helper-changed-during-build/,
  );
  await assert.rejects(
    buildPair(
      { ...options, output: join(root, 'bad-reader') },
      {
        build,
        validate: async () => {
          throw new Error('invalid');
        },
      },
    ),
  );
  let count = 0;
  await assert.rejects(
    buildPair(
      { ...options, output: join(root, 'different') },
      {
        build: async (args) => {
          const stats = await build(args);
          if (++count === 2) await writeFile(join(args.output, 'ATTRIBUTION.txt'), 'changed');
          return stats;
        },
        validate: async () => assert.fail('must not validate mismatched bytes'),
      },
    ),
  );
});
