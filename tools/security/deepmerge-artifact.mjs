import { createHash } from 'node:crypto';
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, resolve } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { assertPhysical } from '../openspec/files.mjs';

const VERSION = '4.3.1';
const ADVISORY = 'https://github.com/advisories/GHSA-gcqx-w48m-w2h4';
const BASE_INTEGRITY =
  'sha512-3sUqbMEc77XqpdNO7FRyRog+eW3ph+GYCbj+rK+uYyRMuwsVy0rMiVtPn+QJlKFvWP/1PYpapqYn0Me2knFn+A==';
const SOURCE = 'tools/vendor/deepmerge-4.3.1';
const BASE = 'tools/vendor/deepmerge-4.3.1-upstream.tgz';
const ARTIFACT = 'tools/vendor/deepmerge-4.3.1-travel.1.tgz';
const BASE_FILES = [
  '.editorconfig',
  '.eslintcache',
  'changelog.md',
  'dist/cjs.js',
  'dist/umd.js',
  'index.d.ts',
  'index.js',
  'license.txt',
  'package.json',
  'readme.md',
  'rollup.config.js',
];
const PATCH_FILES = [...BASE_FILES, 'PATCH-NOTICE.md'];
const CHANGED = ['dist/cjs.js', 'dist/umd.js', 'index.js', 'package.json', 'PATCH-NOTICE.md'];
const proofs = new WeakSet();
const hash = (bytes) => createHash('sha256').update(bytes).digest('hex');
const integrity = (bytes) => `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
const keysEqual = (a, b) => JSON.stringify([...a].sort()) === JSON.stringify([...b].sort());
function freeze(value) {
  if (value && typeof value === 'object') {
    for (const child of Object.values(value)) freeze(child);
    Object.freeze(value);
  }
  return value;
}

// Small fixed-inventory npm archives only. Never extract or execute an install hook.
export function readDeepmergeArchive(compressed) {
  if (compressed.length > 64 * 1024) throw new Error('Backport archive size drift');
  const tar = gunzipSync(compressed, { maxOutputLength: 128 * 1024 });
  const files = new Map();
  let offset = 0;
  while (offset + 512 <= tar.length) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every((v) => v === 0)) {
      if (tar.length - offset < 1024 || !tar.subarray(offset).every((v) => v === 0))
        throw new Error('Archive terminator drift');
      return files;
    }
    const text = (start, length) =>
      header
        .subarray(start, start + length)
        .toString('utf8')
        .replace(/\0.*$/s, '');
    const octal = (start, length) => {
      const n = text(start, length).trim();
      if (!/^[0-7]+$/.test(n)) throw new Error('Archive numeric field drift');
      return Number.parseInt(n, 8);
    };
    const checksum = [...header].reduce((sum, v, i) => sum + (i >= 148 && i < 156 ? 32 : v), 0);
    const path = text(0, 100);
    if (
      checksum !== octal(148, 8) ||
      !path.startsWith('package/') ||
      text(345, 155) ||
      ![0, 48].includes(header[156]) ||
      text(157, 100)
    )
      throw new Error('Archive type/path/checksum drift');
    const name = path.slice(8),
      size = octal(124, 12);
    if (!PATCH_FILES.includes(name) || files.has(name) || offset + 512 + size > tar.length)
      throw new Error('Archive inventory drift');
    files.set(name, tar.subarray(offset + 512, offset + 512 + size));
    offset += 512 + Math.ceil(size / 512) * 512;
  }
  throw new Error('Archive terminator missing');
}
function verifyFiles(files, expected, names) {
  if (!keysEqual(files.keys(), names) || !keysEqual(Object.keys(expected), names))
    throw new Error('Backport file inventory drift');
  for (const [name, bytes] of files)
    if (!/^[a-f0-9]{64}$/.test(expected[name]) || hash(bytes) !== expected[name])
      throw new Error(`Backport file hash drift: ${name}`);
}
async function sourceFiles(root, directory) {
  const files = new Map();
  async function walk(path) {
    await assertPhysical(root, path);
    for (const e of await readdir(path, { withFileTypes: true })) {
      const full = await assertPhysical(root, join(path, e.name));
      if (e.isDirectory()) await walk(full);
      else if (e.isFile()) files.set(relative(directory, full).replaceAll('\\', '/'), await readFile(full));
      else throw new Error('Backport files must be physical');
    }
  }
  await walk(directory);
  return files;
}
export async function verifyDeepmergeArtifact(root) {
  root = resolve(root);
  const p = JSON.parse(
    await readFile(await assertPhysical(root, join(root, 'tools/security/deepmerge-provenance.json')), 'utf8'),
  );
  if (
    p.schemaVersion !== 1 ||
    p.name !== 'deepmerge' ||
    p.version !== VERSION ||
    p.patchRevision !== 1 ||
    p.advisory !== ADVISORY ||
    p.upstream.archive !== BASE ||
    p.upstream.tarball !== 'https://registry.npmjs.org/deepmerge/-/deepmerge-4.3.1.tgz' ||
    p.upstream.integrity !== BASE_INTEGRITY ||
    p.upstream.gitHead !== '5b87756a5671635679001cbac72aa42f23472c81' ||
    p.upstream.license !== 'MIT' ||
    p.source !== SOURCE ||
    p.artifact.path !== ARTIFACT ||
    p.scope !== 'root' ||
    p.resolved !== `file:${ARTIFACT}` ||
    !keysEqual(p.review.changedFiles, CHANGED)
  )
    throw new Error('Backport provenance drift');
  const baseBytes = await readFile(await assertPhysical(root, join(root, BASE)));
  const patchedBytes = await readFile(await assertPhysical(root, join(root, ARTIFACT)));
  if (integrity(baseBytes) !== BASE_INTEGRITY || integrity(patchedBytes) !== p.artifact.integrity)
    throw new Error('Backport archive integrity drift');
  const base = readDeepmergeArchive(baseBytes),
    patched = readDeepmergeArchive(patchedBytes);
  verifyFiles(base, p.upstream.files, BASE_FILES);
  verifyFiles(patched, p.artifact.files, PATCH_FILES);
  verifyFiles(await sourceFiles(root, join(root, SOURCE)), p.artifact.files, PATCH_FILES);
  for (const name of BASE_FILES)
    if (!CHANGED.includes(name) && !base.get(name).equals(patched.get(name)))
      throw new Error('Unapproved backport file change');
  const original = JSON.parse(base.get('package.json')),
    meta = JSON.parse(patched.get('package.json'));
  if (
    meta.name !== 'deepmerge' ||
    meta.version !== VERSION ||
    meta.license !== 'MIT' ||
    meta.travelSecurityPatch?.revision !== 1 ||
    meta.travelSecurityPatch.advisory !== 'GHSA-gcqx-w48m-w2h4' ||
    JSON.stringify(meta.scripts) !== JSON.stringify(original.scripts)
  )
    throw new Error('Backport package metadata drift');
  const proof = freeze({ root, provenance: p });
  proofs.add(proof);
  return proof;
}
export function requireDeepmergeProof(proof) {
  if (!proofs.has(proof)) throw new Error('Verified deepmerge artifact proof required');
}
export function expectedDeepmergeLock(proof, scope = 'root') {
  requireDeepmergeProof(proof);
  if (scope !== 'root') throw new Error('Deepmerge backport scope drift');
  return {
    node: 'node_modules/deepmerge',
    version: VERSION,
    resolved: proof.provenance.resolved,
    integrity: proof.provenance.artifact.integrity,
  };
}
export function verifyDeepmergeManifest(manifest, proof) {
  const e = expectedDeepmergeLock(proof);
  if (manifest.devDependencies?.deepmerge !== e.resolved || manifest.overrides?.deepmerge !== '$deepmerge')
    throw new Error('Canonical deepmerge dependency/override drift');
}
export function verifyDeepmergeLock(lock, proof) {
  const e = expectedDeepmergeLock(proof);
  const copies = Object.entries(lock.packages ?? {}).filter(
    ([name, p]) => /(^|\/)node_modules\/deepmerge$/i.test(name) || p.name === 'deepmerge',
  );
  if (copies.length !== 1 || copies[0][0] !== e.node) throw new Error('Missing or extra locked deepmerge copy');
  const p = copies[0][1];
  if (p.link || p.version !== e.version || p.resolved !== e.resolved || p.integrity !== e.integrity)
    throw new Error('Locked deepmerge artifact drift');
  return e;
}
export async function verifyInstalledDeepmerge(proof, lock) {
  const expected = verifyDeepmergeLock(lock, proof),
    root = proof.root;
  verifyDeepmergeManifest(JSON.parse(await readFile(join(root, 'package.json'), 'utf8')), proof);
  const anchor = await assertPhysical(root, join(root, 'node_modules')),
    copies = new Set();
  const pending = [{ directory: anchor, context: 'modules', packageRoot: false }];
  while (pending.length) {
    const { directory, context, packageRoot, unscopedDeepmerge } = pending.pop();
    if (unscopedDeepmerge) copies.add(directory);
    const entries = await readdir(directory, { withFileTypes: true });
    for (const e of entries) {
      if (context === 'modules' && e.name === '.bin') continue;
      if (e.isSymbolicLink() || (!e.isDirectory() && !e.isFile()))
        throw new Error('Installed dependency entry must be physical');
      if (context === 'modules' && /^deepmerge\.(?:js|json|node)$/i.test(e.name))
        throw new Error('Installed deepmerge file shadow');
      if (!e.isDirectory()) continue;
      const moduleChild = context === 'modules',
        scoped = moduleChild && e.name.startsWith('@');
      pending.push({
        directory: join(directory, e.name),
        context: scoped ? 'scope' : e.name.toLowerCase() === 'node_modules' ? 'modules' : 'content',
        packageRoot: (moduleChild && !scoped) || context === 'scope',
        unscopedDeepmerge: moduleChild && e.name.toLowerCase() === 'deepmerge',
      });
    }
    if (packageRoot)
      for (const e of entries.filter((e) => e.name.toLowerCase() === 'package.json')) {
        if (!e.isFile()) throw new Error('Installed package metadata must be physical');
        if (JSON.parse(await readFile(join(directory, e.name), 'utf8')).name === 'deepmerge') copies.add(directory);
      }
  }
  const paths = [...copies];
  if (paths.length !== 1 || relative(root, paths[0]).replaceAll('\\', '/') !== expected.node)
    throw new Error('Missing, extra or aliased installed deepmerge copy');
  verifyFiles(await sourceFiles(root, paths[0]), proof.provenance.artifact.files, PATCH_FILES);
  return { scope: 'root', status: 'verified-installed-backport', copies: 1, patchRevision: 1 };
}
