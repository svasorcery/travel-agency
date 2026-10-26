import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import * as maintenance from './moscow-refresh.mjs';

const at = new Date('2026-10-27T12:00:00Z');
const spbUrl = 'https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz';
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
async function workspace(t) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'travel-city-refresh-')));
  await mkdir(join(root, 'TestResults'));
  t.after(() => rm(root, { recursive: true, force: true }));
  return root;
}
test('city inspection uses its own reviewed package and one allowlisted HEAD', async () => {
  const requests = [];
  const result = await maintenance.inspect({
    destination: 'saint-petersburg',
    now: () => at,
    fetchImpl: async (url, options) => {
      requests.push([url, options.method]);
      return new Response(null, {
        status: 200,
        headers: { 'Last-Modified': 'Sun, 25 Oct 2026 04:08:52 GMT', 'Content-Length': '101559015' },
      });
    },
  });
  assert.equal(result.current.manifestSha256, '3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d');
  assert.equal(result.publicationStatus, 'unchanged');
  assert.equal(result.currentStatus, 'valid');
  assert.deepEqual(requests, [[spbUrl, 'HEAD']]);
  assert.equal(
    (await maintenance.readCurrent(undefined, 'saint-petersburg')).manifest.datasetId,
    'saint-petersburg-outdoor',
  );
});
test('unsupported maintenance city never falls back to Moscow', async () => {
  let calls = 0;
  await assert.rejects(
    () =>
      maintenance.inspect({
        destination: 'kaliningrad',
        fetchImpl: async () => {
          calls++;
          throw Error('unexpected');
        },
      }),
    /unsupported-destination/,
  );
  assert.equal(calls, 0);
});
test('initial preparation refuses an already reviewed city before acquisition', async (t) => {
  const root = await workspace(t);
  let calls = 0;
  await assert.rejects(
    () =>
      maintenance.prepareInitial(
        { destination: 'saint-petersburg', version: '1', output: join(root, 'TestResults', 'initial') },
        {
          root,
          fetchImpl: async () => {
            calls++;
            throw Error('unexpected');
          },
        },
      ),
    /initial-already-reviewed/,
  );
  assert.equal(calls, 0);
});
test('controlled initial preparation binds one city input and two builds without selecting a release', async (t) => {
  // FICTIONAL acquisition/build/validation seams; this does not establish real map quality.
  const root = await workspace(t),
    current = await maintenance.readCurrent(undefined, 'saint-petersburg');
  const raw = Buffer.from('FICTIONAL first SPb snapshot'),
    inventory = join(root, 'inventory.json'),
    profile = join(root, 'profile.json');
  await writeFile(inventory, current.files['inventory.json']);
  const profileObject = JSON.parse(current.files['preparation-profile.json']);
  profileObject.inputSha256 = sha(raw);
  await writeFile(profile, JSON.stringify(profileObject));
  let builds = 0,
    validations = 0;
  const requests = [];
  const result = await maintenance.prepareInitial(
    {
      destination: 'saint-petersburg',
      version: '1',
      output: join(root, 'TestResults', 'initial'),
      inventory,
      preparationProfile: profile,
    },
    {
      root,
      now: () => at,
      currentRelease: async () => null,
      fetchImpl: async (url, { method }) => {
        requests.push([url, method]);
        return new Response(method === 'HEAD' ? null : raw, {
          status: 200,
          headers: { 'Last-Modified': 'Sun, 25 Oct 2026 04:08:52 GMT', 'Content-Length': String(raw.length) },
        });
      },
      build: async (args) => {
        builds++;
        assert.equal(args.destination, 'saint-petersburg');
        assert.ok(args.profilePath);
        const files = { ...current.files };
        files['input-manifest.json'] = await readFile(args.inputPath);
        files['preparation-profile.json'] = await readFile(args.profilePath);
        const manifest = {
          datasetId: 'saint-petersburg-outdoor',
          version: '1',
          files: Object.fromEntries(
            Object.entries(files)
              .filter(([name]) => name !== 'saint-petersburg-v1.manifest.json')
              .map(([name, bytes]) => [name, sha(bytes)]),
          ),
        };
        files['saint-petersburg-v1.manifest.json'] = Buffer.from(JSON.stringify(manifest));
        await mkdir(args.output);
        for (const [name, bytes] of Object.entries(files)) await writeFile(join(args.output, name), bytes);
        return { status: 'built', pois: 20, paths: 62, elapsedMilliseconds: 1, peakWorkingSetBytes: 100 };
      },
      validate: async (args) => {
        validations++;
        assert.equal(args.destination, 'saint-petersburg');
        return {
          ok: true,
          status: 'validated-not-released',
          datasetId: 'saint-petersburg-outdoor',
          version: '1',
          manifestSha256: args.manifestSha256,
          releaseValidUntil: current.payload.releaseValidUntil,
        };
      },
    },
  );
  assert.equal(result.status, 'candidate-needs-review');
  assert.equal(builds, 2);
  assert.equal(validations, 1);
  assert.equal(result.input.sourceUrl, spbUrl);
  assert.equal(result.input.sha256, sha(raw));
  assert.deepEqual(requests, [
    [spbUrl, 'HEAD'],
    [spbUrl, 'GET'],
  ]);
  assert.equal((await maintenance.readCurrent(undefined, 'saint-petersburg')).manifestSha256, current.manifestSha256);
});
