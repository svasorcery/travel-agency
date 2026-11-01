import { createHash } from 'node:crypto';
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, resolve } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { assertPhysical } from '../openspec/files.mjs';

const VERSION = '10.2.5';
const ADVISORY = 'https://github.com/advisories/GHSA-g3hv-75wg-m249';
const BASE_INTEGRITY =
  'sha512-MULkVLfKGYDFYejP07QOurDLLQpcjk7Fw+7jXS2R2czRQzR56yHRveU5NDJEOviH+hETZKSkIk5c+T23GjFUMg==';
const SOURCE = 'tools/vendor/minimatch-10.2.5';
const BASE = 'tools/vendor/minimatch-10.2.5-upstream.tgz';
const ARTIFACT = 'tools/vendor/minimatch-10.2.5-travel.1.tgz';
const BASE_FILES = [
  'LICENSE.md',
  'README.md',
  'dist/commonjs/assert-valid-pattern.d.ts',
  'dist/commonjs/assert-valid-pattern.d.ts.map',
  'dist/commonjs/assert-valid-pattern.js',
  'dist/commonjs/assert-valid-pattern.js.map',
  'dist/commonjs/ast.d.ts',
  'dist/commonjs/ast.d.ts.map',
  'dist/commonjs/ast.js',
  'dist/commonjs/ast.js.map',
  'dist/commonjs/brace-expressions.d.ts',
  'dist/commonjs/brace-expressions.d.ts.map',
  'dist/commonjs/brace-expressions.js',
  'dist/commonjs/brace-expressions.js.map',
  'dist/commonjs/escape.d.ts',
  'dist/commonjs/escape.d.ts.map',
  'dist/commonjs/escape.js',
  'dist/commonjs/escape.js.map',
  'dist/commonjs/index.d.ts',
  'dist/commonjs/index.d.ts.map',
  'dist/commonjs/index.js',
  'dist/commonjs/index.js.map',
  'dist/commonjs/package.json',
  'dist/commonjs/unescape.d.ts',
  'dist/commonjs/unescape.d.ts.map',
  'dist/commonjs/unescape.js',
  'dist/commonjs/unescape.js.map',
  'dist/esm/assert-valid-pattern.d.ts',
  'dist/esm/assert-valid-pattern.d.ts.map',
  'dist/esm/assert-valid-pattern.js',
  'dist/esm/assert-valid-pattern.js.map',
  'dist/esm/ast.d.ts',
  'dist/esm/ast.d.ts.map',
  'dist/esm/ast.js',
  'dist/esm/ast.js.map',
  'dist/esm/brace-expressions.d.ts',
  'dist/esm/brace-expressions.d.ts.map',
  'dist/esm/brace-expressions.js',
  'dist/esm/brace-expressions.js.map',
  'dist/esm/escape.d.ts',
  'dist/esm/escape.d.ts.map',
  'dist/esm/escape.js',
  'dist/esm/escape.js.map',
  'dist/esm/index.d.ts',
  'dist/esm/index.d.ts.map',
  'dist/esm/index.js',
  'dist/esm/index.js.map',
  'dist/esm/package.json',
  'dist/esm/unescape.d.ts',
  'dist/esm/unescape.d.ts.map',
  'dist/esm/unescape.js',
  'dist/esm/unescape.js.map',
  'package.json',
];
const PATCH_FILES = [...BASE_FILES, 'PATCH-NOTICE.md'];
const CHANGED = [
  'dist/commonjs/index.js',
  'dist/esm/index.js',
  'dist/commonjs/index.d.ts',
  'dist/esm/index.d.ts',
  'package.json',
  'PATCH-NOTICE.md',
];
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
export function readMinimatchArchive(compressed) {
  if (compressed.length > 256 * 1024) throw new Error('Backport archive size drift');
  const tar = gunzipSync(compressed, { maxOutputLength: 2 * 1024 * 1024 });
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
export async function verifyMinimatchArtifact(root) {
  root = resolve(root);
  const p = JSON.parse(
    await readFile(await assertPhysical(root, join(root, 'tools/security/minimatch-provenance.json')), 'utf8'),
  );
  if (
    p.schemaVersion !== 1 ||
    p.name !== 'minimatch' ||
    p.version !== VERSION ||
    p.patchRevision !== 1 ||
    p.advisory !== ADVISORY ||
    p.upstream.archive !== BASE ||
    p.upstream.tarball !== 'https://registry.npmjs.org/minimatch/-/minimatch-10.2.5.tgz' ||
    p.upstream.integrity !== BASE_INTEGRITY ||
    p.upstream.license !== 'BlueOak-1.0.0' ||
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
  const base = readMinimatchArchive(baseBytes),
    patched = readMinimatchArchive(patchedBytes);
  verifyFiles(base, p.upstream.files, BASE_FILES);
  verifyFiles(patched, p.artifact.files, PATCH_FILES);
  verifyFiles(await sourceFiles(root, join(root, SOURCE)), p.artifact.files, PATCH_FILES);
  for (const name of BASE_FILES)
    if (!CHANGED.includes(name) && !base.get(name).equals(patched.get(name)))
      throw new Error('Unapproved backport file change');
  const original = JSON.parse(base.get('package.json')),
    meta = JSON.parse(patched.get('package.json'));
  if (
    meta.name !== 'minimatch' ||
    meta.version !== VERSION ||
    meta.license !== 'BlueOak-1.0.0' ||
    meta.travelSecurityPatch?.revision !== 1 ||
    meta.travelSecurityPatch.advisory !== 'GHSA-g3hv-75wg-m249' ||
    JSON.stringify(meta.scripts) !== JSON.stringify(original.scripts)
  )
    throw new Error('Backport package metadata drift');
  const proof = freeze({ root, provenance: p });
  proofs.add(proof);
  return proof;
}
export function requireMinimatchProof(proof) {
  if (!proofs.has(proof)) throw new Error('Verified minimatch artifact proof required');
}
export function expectedMinimatchLock(proof, scope = 'root') {
  requireMinimatchProof(proof);
  if (scope !== 'root') throw new Error('Minimatch backport scope drift');
  return {
    node: 'node_modules/minimatch',
    version: VERSION,
    resolved: proof.provenance.resolved,
    integrity: proof.provenance.artifact.integrity,
  };
}
export function verifyMinimatchManifest(manifest, proof) {
  const e = expectedMinimatchLock(proof);
  if (manifest.devDependencies?.minimatch !== e.resolved || manifest.overrides?.minimatch !== '$minimatch')
    throw new Error('Canonical minimatch dependency/override drift');
}
export function expectedRegistryMinimatchLock(proof) {
  requireMinimatchProof(proof);
  return {
    node: 'node_modules/glob/node_modules/minimatch',
    version: '11.0.3',
    resolved: 'https://registry.npmjs.org/minimatch/-/minimatch-11.0.3.tgz',
    integrity: 'sha512-iny/40PEGkNJQ5/Pv6z3nm6FbRAby32GeZRjCUJk2WnNDbgPSTcdI+DDvLqsgBdD4cD5sMtMheqPPnehCCa8Nw==',
  };
}
export function verifyMinimatchLock(lock, proof) {
  const e = expectedMinimatchLock(proof);
  const copies = Object.entries(lock.packages ?? {}).filter(
    ([name, p]) => /(^|\/)node_modules\/minimatch$/i.test(name) || p.name === 'minimatch',
  );
  const fixed = expectedRegistryMinimatchLock(proof);
  if (
    !keysEqual(
      copies.map(([path]) => path),
      [e.node, fixed.node],
    )
  )
    throw new Error('Missing or extra locked minimatch copy');
  const registry = lock.packages[fixed.node];
  if (
    registry.link ||
    registry.version !== fixed.version ||
    registry.resolved !== fixed.resolved ||
    registry.integrity !== fixed.integrity
  )
    throw new Error('Registry minimatch copy drift');
  const p = lock.packages[e.node];
  if (p.link || p.version !== e.version || p.resolved !== e.resolved || p.integrity !== e.integrity)
    throw new Error('Locked minimatch artifact drift');
  return e;
}
export async function verifyInstalledMinimatch(proof, lock) {
  const expected = verifyMinimatchLock(lock, proof),
    root = proof.root;
  verifyMinimatchManifest(JSON.parse(await readFile(join(root, 'package.json'), 'utf8')), proof);
  const anchor = await assertPhysical(root, join(root, 'node_modules')),
    copies = new Set();
  const pending = [{ directory: anchor, context: 'modules', packageRoot: false }];
  while (pending.length) {
    const { directory, context, packageRoot, unscopedMinimatch } = pending.pop();
    if (unscopedMinimatch) copies.add(directory);
    const entries = await readdir(directory, { withFileTypes: true });
    for (const e of entries) {
      if (context === 'modules' && e.name === '.bin') continue;
      if (e.isSymbolicLink() || (!e.isDirectory() && !e.isFile()))
        throw new Error('Installed dependency entry must be physical');
      if (context === 'modules' && /^minimatch\.(?:js|json|node)$/i.test(e.name))
        throw new Error('Installed minimatch file shadow');
      if (!e.isDirectory()) continue;
      const moduleChild = context === 'modules',
        scoped = moduleChild && e.name.startsWith('@');
      pending.push({
        directory: join(directory, e.name),
        context: scoped ? 'scope' : e.name.toLowerCase() === 'node_modules' ? 'modules' : 'content',
        packageRoot: (moduleChild && !scoped) || context === 'scope',
        unscopedMinimatch: moduleChild && e.name.toLowerCase() === 'minimatch',
      });
    }
    if (packageRoot)
      for (const e of entries.filter((e) => e.name.toLowerCase() === 'package.json')) {
        if (!e.isFile()) throw new Error('Installed package metadata must be physical');
        if (JSON.parse(await readFile(join(directory, e.name), 'utf8')).name === 'minimatch') copies.add(directory);
      }
  }
  const paths = [...copies];
  const registry = expectedRegistryMinimatchLock(proof);
  if (
    !keysEqual(
      paths.map((path) => relative(root, path).replaceAll('\\', '/')),
      [expected.node, registry.node],
    )
  )
    throw new Error('Missing, extra or aliased installed minimatch copy');
  verifyFiles(await sourceFiles(root, join(root, expected.node)), proof.provenance.artifact.files, PATCH_FILES);
  const registryMeta = JSON.parse(
    await readFile(await assertPhysical(root, join(root, registry.node, 'package.json')), 'utf8'),
  );
  if (registryMeta.name !== 'minimatch' || registryMeta.version !== registry.version)
    throw new Error('Installed registry minimatch drift');

  return { scope: 'root', status: 'verified-installed-backport', copies: 2, patchRevision: 1 };
}
