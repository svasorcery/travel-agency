import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { link, lstat, mkdir, open, readdir, readFile, realpath, unlink, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export const SOURCE_URL = 'https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz';
export const CURRENT_MANIFEST_SHA = '8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43';
const COMMON_CAPS = {
  'ATTRIBUTION.txt': 8192,
  'input-manifest.json': 4096,
  'inventory.json': 131072,
  'walking-evidence.json': 4194304,
};
function city(destination = 'moscow') {
  need(destination === 'moscow' || destination === 'saint-petersburg', 'unsupported-destination');
  const spb = destination === 'saint-petersburg';
  const datasetId = `${destination}-outdoor`,
    manifest = `${destination}-v1.manifest.json`,
    payload = `${destination}-v1.json`;
  return {
    destination,
    datasetId,
    manifest,
    payload,
    sourceUrl: spb ? 'https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz' : SOURCE_URL,
    pin: spb ? '3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d' : CURRENT_MANIFEST_SHA,
    directory: join(ROOT, 'modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data', datasetId, '1'),
    caps: {
      ...COMMON_CAPS,
      [manifest]: 2048,
      [payload]: 262144,
      ...(spb ? { 'preparation-profile.json': 65536 } : {}),
    },
  };
}
const MAX_SOURCE = 200 * 1024 * 1024;
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const json = (value) => `${JSON.stringify(value, null, 2)}\n`;
class Refusal extends Error {
  constructor(code) {
    super(code);
    this.code = code;
  }
}
const need = (condition, code) => {
  if (!condition) throw new Refusal(code);
};
const plain = (value) => value && typeof value === 'object' && !Array.isArray(value);
const nowFrom = (deps) => (deps.now ?? (() => new Date()))();

async function boundedFiles(directory, profile = city()) {
  const CAPS = profile.caps,
    MANIFEST = profile.manifest;
  const info = await lstat(directory);
  need(info.isDirectory() && !info.isSymbolicLink(), 'unsafe-package-directory');
  const names = (await readdir(directory)).sort();
  need(JSON.stringify(names) === JSON.stringify(Object.keys(CAPS).sort()), 'unexpected-package-files');
  const files = {};
  for (const name of names) {
    const path = join(directory, name),
      stat = await lstat(path);
    need(stat.isFile() && !stat.isSymbolicLink() && stat.size > 0 && stat.size <= CAPS[name], 'unsafe-package-file');
    files[name] = await readFile(path);
    need(files[name].length <= CAPS[name], 'package-file-too-large');
  }
  const manifest = JSON.parse(files[MANIFEST]);
  need(
    plain(manifest.files) &&
      JSON.stringify(Object.keys(manifest.files).sort()) === JSON.stringify(names.filter((name) => name !== MANIFEST)),
    'invalid-manifest',
  );
  for (const name of names.filter((name) => name !== MANIFEST))
    need(manifest.files[name] === sha(files[name]), 'package-digest-mismatch');
  return { files, manifest, manifestSha256: sha(files[MANIFEST]) };
}

export async function readCurrent(directory, destination = 'moscow') {
  const profile = city(destination);
  const result = await boundedFiles(directory ?? profile.directory, profile);
  need(
    result.manifestSha256 === profile.pin &&
      result.manifest.datasetId === profile.datasetId &&
      result.manifest.version === '1',
    'reviewed-v1-pin-mismatch',
  );
  const input = JSON.parse(result.files['input-manifest.json']),
    payload = JSON.parse(result.files[profile.payload]);
  need(input.sourceUrl === profile.sourceUrl && payload.version === '1', 'invalid-reviewed-source');
  return { ...result, version: '1', input, payload };
}

export function publication(response, now) {
  need(response.status === 200 && !response.redirected, 'source-status-refused');
  const date = response.headers.get('last-modified'),
    length = response.headers.get('content-length');
  const millis = Date.parse(date ?? '');
  need(
    Number.isFinite(millis) && new Date(millis).toUTCString() === date && millis <= now.getTime(),
    'invalid-source-publication',
  );
  need(
    typeof length === 'string' &&
      /^[1-9]\d*$/.test(length) &&
      Number.isSafeInteger(Number(length)) &&
      Number(length) <= MAX_SOURCE,
    'invalid-source-size',
  );
  return { publishedAtUtc: new Date(millis).toISOString(), bytes: Number(length) };
}

async function request(method, deps) {
  try {
    return await (deps.fetchImpl ?? fetch)(city(deps.destination).sourceUrl, {
      method,
      redirect: 'error',
      credentials: 'omit',
      signal: AbortSignal.timeout(method === 'HEAD' ? 15000 : 600000),
    });
  } catch {
    throw new Refusal('source-request-failed');
  }
}

export async function inspect(deps = {}) {
  const profile = city(deps.destination);
  const current = await (deps.currentRelease ?? (() => readCurrent(undefined, profile.destination)))();
  const response = await request('HEAD', deps);
  const now = nowFrom(deps);
  const upstream = publication(response, now);
  if (!current)
    return {
      status: 'inspected',
      destinationId: profile.destination,
      currentStatus: 'not-installed',
      current: null,
      publicationStatus: 'no-baseline',
      candidatePrepared: false,
      checkedAtUtc: now.toISOString(),
      upstream,
    };
  const difference = Date.parse(upstream.publishedAtUtc) - Date.parse(current.input.snapshotPublishedAtUtc);
  return {
    status: 'inspected',
    destinationId: profile.destination,
    publicationStatus: difference > 0 ? 'newer' : difference < 0 ? 'older' : 'unchanged',
    candidatePrepared: false,
    checkedAtUtc: now.toISOString(),
    currentStatus:
      now.getTime() < Date.parse(current.payload.buildAsOfUtc)
        ? 'not-yet-valid'
        : now.getTime() < Date.parse(current.payload.releaseValidUntil)
          ? 'valid'
          : 'expired',
    current: {
      version: current.version,
      manifestSha256: current.manifestSha256,
      sourceSha256: current.input.sha256,
      publishedAtUtc: current.input.snapshotPublishedAtUtc,
      releaseValidUntil: current.payload.releaseValidUntil,
    },
    upstream,
  };
}

export async function outputPath(output, root = ROOT) {
  need(typeof output === 'string' && isAbsolute(output), 'absolute-output-required');
  const actualRoot = await realpath(root),
    target = resolve(output),
    area = join(actualRoot, 'TestResults');
  need(target.startsWith(area + sep), 'output-outside-owned-area');
  for (let item = target; ; item = dirname(item)) {
    let info;
    try {
      info = await lstat(item);
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
    if (info) {
      need(!info.isSymbolicLink() && info.isDirectory(), 'output-crosses-nondirectory-or-reparse');
      need(item !== target, 'output-already-exists');
    }
    if (item === actualRoot) break;
    need(item !== dirname(item), 'output-outside-owned-area');
  }
  return target;
}

async function createOutput(target, root) {
  await outputPath(target, root);
  await mkdir(dirname(target), { recursive: true });
  await outputPath(target, root);
  await mkdir(target);
}

export async function acquire(response, head, destination, now, destinationId = 'moscow') {
  try {
    return await acquireBound(response, head, destination, now, city(destinationId));
  } catch (error) {
    if (response.body && !response.body.locked) await response.body.cancel().catch(() => {});
    throw error;
  }
}

async function acquireBound(response, head, destination, now, profile) {
  const clock = typeof now === 'function' ? now : () => now;
  const observed = publication(response, clock());
  need(
    observed.publishedAtUtc === head.publishedAtUtc && observed.bytes === head.bytes,
    'source-changed-during-acquisition',
  );
  need(response.body !== null, 'missing-source-body');
  const file = await open(destination + '.partial', 'wx');
  const hash = createHash('sha256');
  let bytes = 0;
  try {
    for await (const chunk of response.body) {
      bytes += chunk.length;
      need(bytes <= observed.bytes && bytes <= MAX_SOURCE, 'source-body-too-large');
      hash.update(chunk);
      let offset = 0;
      while (offset < chunk.length) offset += (await file.write(chunk, offset, chunk.length - offset)).bytesWritten;
    }
    need(bytes === observed.bytes, 'source-body-truncated');
  } finally {
    await file.close();
  }
  // Same-directory hard link is exclusive: publication cannot replace an existing file.
  await link(destination + '.partial', destination);
  await unlink(destination + '.partial');
  return {
    sourceUrl: profile.sourceUrl,
    sha256: hash.digest('hex'),
    bytes,
    snapshotPublishedAtUtc: observed.publishedAtUtc,
    downloadedAtUtc: clock().toISOString(),
  };
}

function command(args) {
  const result = spawnSync('dotnet', args, {
    cwd: ROOT,
    encoding: 'utf8',
    windowsHide: true,
    timeout: 420000,
    maxBuffer: 1024 * 1024,
  });
  need(!result.error && result.status === 0, 'package-command-failed');
  const line = result.stdout
    .trim()
    .split(/\r?\n/)
    .findLast((value) => value.startsWith('{'));
  need(line, 'missing-package-command-receipt');
  return JSON.parse(line);
}

function buildDefault({ osm, inventoryPath, inputPath, profilePath, destination = 'moscow', asOf, version, output }) {
  const result = command([
    'run',
    '--file',
    'tools/trips/build-moscow.cs',
    '--verbosity',
    'quiet',
    '--',
    '--osm',
    osm,
    '--inventory',
    inventoryPath,
    '--input-manifest',
    inputPath,
    '--as-of',
    asOf,
    '--dataset-id',
    city(destination).datasetId,
    '--destination',
    destination,
    ...(profilePath ? ['--preparation-profile', profilePath] : []),
    '--version',
    version,
    '--output',
    output,
  ]);
  need(result.status === 'built', 'builder-not-successful');
  return result;
}

function validateDefault({ directory, version, manifestSha256, asOf, destination = 'moscow' }) {
  return command([
    'run',
    '--project',
    'tools/trips/Travel.Trips.LocalStack/Travel.Trips.LocalStack.csproj',
    '--verbosity',
    'quiet',
    '--',
    'package-check',
    '--destination',
    destination,
    '--directory',
    directory,
    '--version',
    version,
    '--manifest-sha',
    manifestSha256,
    '--as-of',
    asOf,
  ]);
}

export async function buildPair(options, deps = {}) {
  const profile = city(options.destination);
  const helperPath = options.helperPath ?? join(ROOT, 'tools/trips/build-moscow.cs');
  const helperSha256 = sha(await readFile(helperPath));
  await mkdir(options.output);
  const inventoryPath = join(options.output, 'inventory.json'),
    inputPath = join(options.output, 'input-manifest.json');
  await writeFile(inventoryPath, options.inventory, { flag: 'wx' });
  await writeFile(inputPath, options.inputManifest, { flag: 'wx' });
  const profilePath = options.preparationProfile ? join(options.output, 'preparation-profile.json') : undefined;
  need(profile.destination === 'saint-petersburg' ? !!profilePath : !profilePath, 'preparation-profile-city-mismatch');
  if (profilePath) await writeFile(profilePath, options.preparationProfile, { flag: 'wx' });
  const stats = [],
    results = [];
  for (const name of ['first', 'second']) {
    const output = join(options.output, name);
    stats.push(
      await (deps.build ?? buildDefault)({
        ...options,
        destination: profile.destination,
        inventoryPath,
        inputPath,
        profilePath,
        output,
      }),
    );
    need(sha(await readFile(helperPath)) === helperSha256, 'helper-changed-during-build');
    results.push(await boundedFiles(output, profile));
  }
  for (const name of Object.keys(profile.caps)) {
    need(results[0].files[name].equals(results[1].files[name]), 'builds-disagree');
    if (options.expectedFiles)
      need(results[0].files[name].equals(options.expectedFiles[name]), 'current-reproduction-disagrees');
  }
  const { manifest, manifestSha256 } = results[0];
  need(manifest.datasetId === profile.datasetId && manifest.version === options.version, 'output-identity-mismatch');
  const validation = await (deps.validate ?? validateDefault)({
    directory: join(options.output, 'first'),
    version: options.version,
    manifestSha256,
    asOf: options.asOf,
    destination: profile.destination,
  });
  need(
    validation.ok === true &&
      validation.datasetId === profile.datasetId &&
      validation.status === 'validated-not-released' &&
      validation.version === options.version &&
      validation.manifestSha256 === manifestSha256,
    'strict-reader-refused',
  );
  return {
    helperSha256,
    manifestSha256,
    files: Object.fromEntries(Object.entries(results[0].files).map(([name, bytes]) => [name, sha(bytes)])),
    builds: stats,
    validation,
  };
}

async function withReceipt(target, deps, operation) {
  const startedAtUtc = nowFrom(deps).toISOString();
  try {
    const result = await operation();
    const finished = nowFrom(deps);
    if (result.status === 'candidate-needs-review' || result.status === 'reproduced-current')
      need(Date.parse(result.validation.releaseValidUntil) > finished.getTime(), 'package-expired-before-completion');
    const receipt = { schemaVersion: 1, startedAtUtc, finishedAtUtc: finished.toISOString(), ...result };
    await writeFile(join(target, 'receipt.json'), json(receipt), { flag: 'wx' });
    return receipt;
  } catch (error) {
    await writeFile(
      join(target, 'receipt.json'),
      json({
        schemaVersion: 1,
        status: 'failed',
        startedAtUtc,
        finishedAtUtc: nowFrom(deps).toISOString(),
        error: error instanceof Refusal ? error.code : 'operation-failed',
      }),
      { flag: 'wx' },
    );
    throw error;
  }
}

export function newSourceBytes(input, previous, at, destination = 'moscow') {
  const published = Date.parse(input.snapshotPublishedAtUtc),
    downloaded = Date.parse(input.downloadedAtUtc);
  need(
    input.sourceUrl === city(destination).sourceUrl && /^[a-f0-9]{64}$/.test(input.sha256),
    'invalid-downloaded-identity',
  );
  need(
    published > Date.parse(previous.snapshotPublishedAtUtc) && published <= downloaded && downloaded <= at.getTime(),
    'invalid-downloaded-time-binding',
  );
  need(published + 30 * 86400000 > at.getTime(), 'new-source-already-expired');
  return input.sha256 !== previous.sha256;
}

export async function prepare({ version, output, destination = 'moscow', preparationProfile }, deps = {}) {
  const profile = city(destination);
  deps = { ...deps, destination };
  need(typeof version === 'string' && /^[1-9]\d{0,8}$/.test(version) && Number(version) > 1, 'new-version-required');
  const target = await outputPath(output, deps.root ?? ROOT),
    current = await readCurrent(undefined, destination);
  const profileBytes = destination === 'saint-petersburg' ? await boundedInput(preparationProfile, 65536) : undefined;
  need(destination === 'saint-petersburg' || preparationProfile === undefined, 'preparation-profile-city-mismatch');
  const observed = await inspect(deps);
  if (observed.publicationStatus !== 'newer') return { ...observed, status: 'no-new-publication' };
  need(
    Date.parse(observed.upstream.publishedAtUtc) + 30 * 86400000 > nowFrom(deps).getTime(),
    'new-source-already-expired',
  );
  await createOutput(target, deps.root ?? ROOT);
  return withReceipt(target, deps, async () => {
    const input = await acquire(
      await request('GET', deps),
      observed.upstream,
      join(target, 'source.osm.gz'),
      () => nowFrom(deps),
      destination,
    );
    if (!newSourceBytes(input, current.input, nowFrom(deps), destination))
      return {
        status: 'unchanged-source-bytes',
        candidatePrepared: false,
        input,
        currentManifestSha256: current.manifestSha256,
      };
    const asOf = nowFrom(deps).toISOString();
    need(Date.parse(input.snapshotPublishedAtUtc) + 30 * 86400000 > Date.parse(asOf), 'new-source-already-expired');
    const proof = await buildPair(
      {
        output: join(target, 'builds'),
        osm: join(target, 'source.osm.gz'),
        inventory: current.files['inventory.json'],
        inputManifest: Buffer.from(JSON.stringify(input)),
        asOf,
        version,
        destination: profile.destination,
        preparationProfile: profileBytes,
      },
      deps,
    );
    return {
      status: 'candidate-needs-review',
      candidatePrepared: true,
      version,
      destinationId: destination,
      asOfUtc: asOf,
      input,
      inventorySha256: sha(current.files['inventory.json']),
      currentManifestSha256: current.manifestSha256,
      ...proof,
    };
  });
}

async function sourceDigest(file) {
  const info = await lstat(file);
  need(info.isFile() && !info.isSymbolicLink() && info.size > 0 && info.size <= MAX_SOURCE, 'invalid-local-source');
  const hash = createHash('sha256');
  let bytes = 0;
  for await (const chunk of createReadStream(file)) {
    bytes += chunk.length;
    need(bytes <= MAX_SOURCE, 'source-body-too-large');
    hash.update(chunk);
  }
  return { sha256: hash.digest('hex'), bytes };
}

async function boundedInput(file, maximum) {
  need(typeof file === 'string' && isAbsolute(file), 'absolute-reviewed-input-required');
  const info = await lstat(file);
  need(info.isFile() && !info.isSymbolicLink() && info.size > 0 && info.size <= maximum, 'unsafe-reviewed-input');
  const bytes = await readFile(file);
  need(bytes.length <= maximum, 'reviewed-input-too-large');
  return bytes;
}

export async function prepareInitial({ destination, version, output, inventory, preparationProfile }, deps = {}) {
  const profile = city(destination);
  need(destination === 'saint-petersburg' && version === '1', 'initial-city-version-required');
  // Current descriptors are source-owned. Missing or corrupt installed files must fail,
  // never turn a reviewed release into permission to bootstrap over its immutable pin.
  const current = await (deps.currentRelease ?? (() => readCurrent(undefined, destination)))();
  need(current === null, 'initial-already-reviewed');
  deps = { ...deps, destination };
  const target = await outputPath(output, deps.root ?? ROOT);
  const inventoryBytes = await boundedInput(inventory, 131072),
    profileBytes = await boundedInput(preparationProfile, 65536);
  const upstream = publication(await request('HEAD', deps), nowFrom(deps));
  need(Date.parse(upstream.publishedAtUtc) + 30 * 86400000 > nowFrom(deps).getTime(), 'new-source-already-expired');
  await createOutput(target, deps.root ?? ROOT);
  return withReceipt(target, deps, async () => {
    const input = await acquire(
      await request('GET', deps),
      upstream,
      join(target, 'source.osm.gz'),
      () => nowFrom(deps),
      destination,
    );
    const asOf = nowFrom(deps).toISOString();
    const proof = await buildPair(
      {
        output: join(target, 'builds'),
        osm: join(target, 'source.osm.gz'),
        inventory: inventoryBytes,
        preparationProfile: profileBytes,
        inputManifest: Buffer.from(JSON.stringify(input)),
        destination: profile.destination,
        version,
        asOf,
      },
      deps,
    );
    return {
      status: 'candidate-needs-review',
      candidatePrepared: true,
      destinationId: destination,
      version,
      asOfUtc: asOf,
      input,
      inventorySha256: sha(inventoryBytes),
      currentManifestSha256: null,
      ...proof,
    };
  });
}

export async function reproduceCurrent({ osm, output, destination = 'moscow' }, deps = {}) {
  city(destination);
  need(isAbsolute(osm), 'absolute-input-required');
  const target = await outputPath(output, deps.root ?? ROOT),
    current = await readCurrent(undefined, destination);
  const digest = await sourceDigest(osm);
  need(digest.sha256 === current.input.sha256 && digest.bytes === current.input.bytes, 'current-source-mismatch');
  need(nowFrom(deps).getTime() < Date.parse(current.payload.releaseValidUntil), 'current-source-expired');
  await createOutput(target, deps.root ?? ROOT);
  return withReceipt(target, deps, async () => ({
    status: 'reproduced-current',
    candidatePrepared: false,
    destinationId: destination,
    version: '1',
    asOfUtc: current.payload.buildAsOfUtc,
    input: current.input,
    inventorySha256: sha(current.files['inventory.json']),
    ...(await buildPair(
      {
        output: join(target, 'builds'),
        osm,
        inventory: current.files['inventory.json'],
        inputManifest: current.files['input-manifest.json'],
        asOf: current.payload.buildAsOfUtc,
        version: '1',
        destination,
        preparationProfile: current.files['preparation-profile.json'],
        expectedFiles: current.files,
      },
      deps,
    )),
  }));
}

async function main(args) {
  const [action, ...rest] = args;
  const expected =
    action === 'check'
      ? []
      : action === 'prepare'
        ? ['--version', '--output']
        : action === 'prepare-initial'
          ? ['--destination', '--version', '--output', '--inventory', '--preparation-profile']
          : action === 'reproduce-current'
            ? ['--osm', '--output']
            : null;
  need(expected && rest.length % 2 === 0, 'invalid-command');
  const allowed = [...(expected ?? []), '--destination', ...(action === 'prepare' ? ['--preparation-profile'] : [])];
  const options = {};
  for (let index = 0; index < rest.length; index += 2) {
    need(allowed.includes(rest[index]) && !Object.hasOwn(options, rest[index].slice(2)), 'invalid-command');
    options[rest[index].slice(2)] = rest[index + 1];
  }
  need(
    expected.every((key) => Object.hasOwn(options, key.slice(2))),
    'invalid-command',
  );
  options.preparationProfile = options['preparation-profile'];
  return action === 'check'
    ? inspect(options)
    : action === 'prepare'
      ? prepare(options)
      : action === 'prepare-initial'
        ? prepareInitial(options)
        : reproduceCurrent(options);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.stdout.write(json(await main(process.argv.slice(2))));
  } catch (error) {
    process.stderr.write(json({ status: 'failed', error: error instanceof Refusal ? error.code : 'operation-failed' }));
    process.exitCode = 1;
  }
}
