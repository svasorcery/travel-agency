import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('./hotels-source-manifest.mjs', import.meta.url));
const base = 'TestResults/hotels-m2-memory-load-acceptance';
const profile = 'openspec/changes/hotels-m2-memory-load-acceptance/load-plan.md';

function fixture(t) {
  const parent = mkdtempSync(path.join(tmpdir(), 'hotels-manifest-test-'));
  const root = path.join(parent, 'repo');
  mkdirSync(root);
  execFileSync('git', ['init', '--quiet', root], { windowsHide: true });
  function put(name, content) {
    const filename = path.join(root, name);
    mkdirSync(path.dirname(filename), { recursive: true });
    writeFileSync(filename, content);
  }
  put('.gitignore', 'TestResults/\n');
  put('source.cs', 'fictional source\n');
  put(profile, '# fixed fictional profile\n');
  put('TestResults/hotels-m1/l1-profile.md', '# historical profile\n');
  put('TestResults/hotels-m1/l1-memory-diagnostic-profile.md', '# historical diagnostic\n');
  put('TestResults/hotels-m1/source-manifest.json', 'HISTORICAL-MANIFEST\n');
  mkdirSync(path.join(root, 'tools/demo'), { recursive: true });
  copyFileSync(source, path.join(root, 'tools/demo/hotels-source-manifest.mjs'));
  t.after(() => {
    assert.equal(path.dirname(parent), path.resolve(tmpdir()));
    assert.equal(path.basename(parent).startsWith('hotels-manifest-test-'), true);
    rmSync(parent, { recursive: true, force: true });
  });
  const run = (...args) =>
    spawnSync(process.execPath, [path.join(root, 'tools/demo/hotels-source-manifest.mjs'), ...args], {
      cwd: root,
      encoding: 'utf8',
      windowsHide: true,
      timeout: 10000,
    });
  return { parent, root, put, run };
}

test('M2 manifest includes the exact profile and preserves historical bytes', (t) => {
  const f = fixture(t);
  const output = `${base}/run-a/source-manifest.json`;
  const result = f.run('--m2', '--profile', profile, '--output', output);
  assert.equal(result.status, 0, result.stderr);
  assert.equal(
    readFileSync(path.join(f.root, 'TestResults/hotels-m1/source-manifest.json'), 'utf8'),
    'HISTORICAL-MANIFEST\n',
  );
  const manifest = JSON.parse(readFileSync(path.join(f.root, output), 'utf8'));
  assert.equal(manifest['source.cs'], createHash('sha256').update('fictional source\n').digest('hex').toUpperCase());
  assert.equal(
    manifest[profile],
    createHash('sha256').update('# fixed fictional profile\n').digest('hex').toUpperCase(),
  );
  assert.equal(
    Object.keys(manifest).some((name) => name.startsWith('TestResults/hotels-m1/')),
    false,
  );
});

test('M2 refuses existing output without replacing it or the historical manifest', (t) => {
  const f = fixture(t);
  const output = `${base}/run-a/source-manifest.json`;
  f.put(output, 'FIRST-RECEIPT\n');
  const result = f.run('--m2', '--profile', profile, '--output', output);
  assert.notEqual(result.status, 0);
  assert.equal(readFileSync(path.join(f.root, output), 'utf8'), 'FIRST-RECEIPT\n');
  assert.equal(
    readFileSync(path.join(f.root, 'TestResults/hotels-m1/source-manifest.json'), 'utf8'),
    'HISTORICAL-MANIFEST\n',
  );
});

test('M2 refuses historical and outside output paths before writing', (t) => {
  for (const output of ['TestResults/hotels-m1/source-manifest.json', '../outside.json']) {
    const f = fixture(t);
    const result = f.run('--m2', '--profile', profile, '--output', output);
    assert.notEqual(result.status, 0, output);
    assert.equal(
      readFileSync(path.join(f.root, 'TestResults/hotels-m1/source-manifest.json'), 'utf8'),
      'HISTORICAL-MANIFEST\n',
    );
    assert.equal(existsSync(path.join(f.parent, 'outside.json')), false);
  }
});

test('M2 refuses an output junction escaping its own evidence tree', (t) => {
  const f = fixture(t);
  const outside = path.join(f.parent, 'outside');
  mkdirSync(outside);
  mkdirSync(path.join(f.root, base), { recursive: true });
  symlinkSync(outside, path.join(f.root, base, 'escaped'), process.platform === 'win32' ? 'junction' : 'dir');
  const result = f.run('--m2', '--profile', profile, '--output', `${base}/escaped/source-manifest.json`);
  assert.notEqual(result.status, 0);
  assert.equal(existsSync(path.join(outside, 'source-manifest.json')), false);
  assert.equal(
    readFileSync(path.join(f.root, 'TestResults/hotels-m1/source-manifest.json'), 'utf8'),
    'HISTORICAL-MANIFEST\n',
  );
});

const archivedProfile = 'openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance/load-plan.md';
test('M2 accepts the exact archived profile with its unchanged content hash', (t) => {
  const f = fixture(t);
  f.put(archivedProfile, '# archived profile\n');
  const output = base + '/archive/source-manifest.json';
  const result = f.run('--m2', '--profile', archivedProfile, '--output', output);
  assert.equal(result.status, 0, result.stderr);
  const manifest = JSON.parse(readFileSync(path.join(f.root, output), 'utf8'));
  assert.equal(
    manifest[archivedProfile],
    createHash('sha256').update('# archived profile\n').digest('hex').toUpperCase(),
  );
});

test('M2 refuses unrelated archives and an archived profile root redirected to M1', (t) => {
  const f = fixture(t);
  const other = 'openspec/changes/archive/2026-11-01-other/load-plan.md';
  f.put(other, '# unrelated\n');
  const output = base + '/denied/source-manifest.json';
  assert.notEqual(f.run('--m2', '--profile', other, '--output', output).status, 0);
  const archiveRoot = path.dirname(path.join(f.root, archivedProfile));
  mkdirSync(path.dirname(archiveRoot), { recursive: true });
  symlinkSync(
    path.join(f.root, 'TestResults/hotels-m1'),
    archiveRoot,
    process.platform === 'win32' ? 'junction' : 'dir',
  );
  assert.notEqual(
    f.run('--m2', '--profile', archiveRoot.replace(f.root + path.sep, '') + '/l1-profile.md', '--output', output)
      .status,
    0,
  );
  assert.equal(existsSync(path.join(f.root, output)), false);
});
