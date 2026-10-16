import { createHash } from 'node:crypto';
import { lstat, readdir, readFile } from 'node:fs/promises';
import { join, relative, resolve } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { assertPhysical } from '../openspec/files.mjs';

const VERSION = '3.0.3';
const ADVISORY = 'https://github.com/advisories/GHSA-vfj7-8cjw-p6xm';
const BASE_INTEGRITY =
  'sha512-yQbXgO/OSZVD2IsiLlro+7Hf6Q18EJrKSEsdoMzKePKXct3gvD8oLcOQdIzGupr5Fj+EDe8gO/lxc1BzfMpxvA==';
const SOURCE = 'tools/vendor/braces-3.0.3';
const ARTIFACT = 'tools/vendor/braces-3.0.3-travel.1.tgz';
const BASE = 'tools/vendor/braces-3.0.3-upstream.tgz';
const BASE_FILES = [
  'LICENSE',
  'README.md',
  'index.js',
  'package.json',
  ...['compile', 'constants', 'expand', 'parse', 'stringify', 'utils'].map((name) => `lib/${name}.js`),
];
const PATCH_FILES = [...BASE_FILES, 'lib/depth.js', 'PATCH-NOTICE.md'];
const proofs = new WeakSet();
const hash = (bytes, algorithm = 'sha256') =>
  createHash(algorithm)
    .update(bytes)
    .digest(algorithm === 'sha512' ? 'base64' : 'hex');
const integrity = (bytes) => `sha512-${hash(bytes, 'sha512')}`;
const equalKeys = (actual, expected) => JSON.stringify([...actual].sort()) === JSON.stringify([...expected].sort());
function freeze(value) {
  if (value && typeof value === 'object') {
    for (const child of Object.values(value)) freeze(child);
    Object.freeze(value);
  }
  return value;
}

// Accept only this package's small regular-file npm archive format. Never extract
// paths or invoke install scripts while checking the pre-install artifact gate.
export function readPackageArchive(compressed) {
  if (compressed.length > 256 * 1024) throw new Error('Backport archive exceeds size bound');
  const tar = gunzipSync(compressed, { maxOutputLength: 2 * 1024 * 1024 });
  const files = new Map();
  let offset = 0;
  while (offset + 512 <= tar.length) {
    const header = tar.subarray(offset, offset + 512);
    if (header.every((value) => value === 0)) {
      if (tar.length - offset < 1024 || !tar.subarray(offset).every((value) => value === 0))
        throw new Error('Invalid archive terminator');
      return files;
    }
    const text = (start, length) =>
      header
        .subarray(start, start + length)
        .toString('utf8')
        .replace(/\0.*$/s, '');
    const octal = (start, length) => {
      const value = text(start, length).trim();
      if (!/^[0-7]+$/.test(value)) throw new Error('Invalid archive numeric field');
      return Number.parseInt(value, 8);
    };
    const checksum = [...header].reduce((sum, value, index) => sum + (index >= 148 && index < 156 ? 32 : value), 0);
    if (checksum !== octal(148, 8)) throw new Error('Archive checksum mismatch');
    const path = text(0, 100);
    if (!path.startsWith('package/') || text(345, 155) || ![0, 48].includes(header[156]) || text(157, 100))
      throw new Error('Unapproved archive entry type/path');
    const name = path.slice(8);
    if (!PATCH_FILES.includes(name) || files.has(name)) throw new Error('Unexpected or duplicate archive entry');
    const size = octal(124, 12);
    if (offset + 512 + size > tar.length) throw new Error('Truncated archive');
    files.set(name, tar.subarray(offset + 512, offset + 512 + size));
    offset += 512 + Math.ceil(size / 512) * 512;
  }
  throw new Error('Missing archive terminator');
}
function verifyFiles(files, expected, names) {
  if (!equalKeys(files.keys(), names) || !equalKeys(Object.keys(expected), names))
    throw new Error('Backport file inventory drift');
  for (const [name, bytes] of files) {
    if (!/^[a-f0-9]{64}$/.test(expected[name]) || hash(bytes) !== expected[name])
      throw new Error(`Backport file hash drift: ${name}`);
    if (bytes.includes(13)) throw new Error(`Backport source must use LF: ${name}`);
  }
}
async function sourceFiles(root, directory) {
  const files = new Map();
  async function walk(path) {
    await assertPhysical(root, path);
    for (const entry of await readdir(path, { withFileTypes: true })) {
      const full = await assertPhysical(root, join(path, entry.name));
      if (entry.isDirectory()) await walk(full);
      else if (entry.isFile()) files.set(relative(directory, full).replaceAll('\\', '/'), await readFile(full));
      else throw new Error('Backport artifacts must be regular physical files');
    }
  }
  await walk(directory);
  return files;
}
export async function verifyBracesArtifact(root) {
  root = resolve(root);
  const json = async (path) => JSON.parse(await readFile(await assertPhysical(root, join(root, path)), 'utf8'));
  const p = await json('tools/security/braces-provenance.json');
  if (
    p.schemaVersion !== 1 ||
    p.name !== 'braces' ||
    p.version !== VERSION ||
    p.patchRevision !== 1 ||
    p.advisory !== ADVISORY ||
    p.source !== SOURCE ||
    p.artifact.path !== ARTIFACT ||
    p.upstream.archive !== BASE ||
    p.upstream.license !== 'MIT' ||
    p.upstream.gitHead !== '74b2db2938fad48a2ea54a9c8bf27a37a62c350d' ||
    p.upstream.tarball !== 'https://registry.npmjs.org/braces/-/braces-3.0.3.tgz' ||
    p.upstream.integrity !== BASE_INTEGRITY ||
    p.scopes.root.resolved !== `file:${ARTIFACT}` ||
    p.scopes.openspec.resolved !== 'file:../vendor/braces-3.0.3-travel.1.tgz'
  )
    throw new Error('Backport provenance identity drift');
  const readArchive = async (path, sri) => {
    const physical = await assertPhysical(root, join(root, path));
    if ((await lstat(physical)).size > 256 * 1024) throw new Error('Backport archive exceeds size bound');
    const bytes = await readFile(physical);
    if (integrity(bytes) !== sri) throw new Error(`Backport artifact integrity drift: ${path}`);
    return readPackageArchive(bytes);
  };
  const original = await readArchive(BASE, BASE_INTEGRITY);
  const archived = await readArchive(ARTIFACT, p.artifact.integrity);
  verifyFiles(original, p.upstream.files, BASE_FILES);
  verifyFiles(archived, p.artifact.files, PATCH_FILES);
  const source = await sourceFiles(root, join(root, SOURCE));
  verifyFiles(source, p.artifact.files, PATCH_FILES);
  for (const name of ['LICENSE', 'README.md', 'index.js', 'lib/constants.js'])
    if (!source.get(name).equals(original.get(name))) throw new Error(`Untouched upstream file drift: ${name}`);
  const pkg = JSON.parse(archived.get('package.json'));
  if (
    pkg.name !== 'braces' ||
    pkg.version !== VERSION ||
    pkg.license !== 'MIT' ||
    pkg['x-travel-security-patch']?.revision !== 1 ||
    pkg['x-travel-security-patch'].advisory !== 'GHSA-vfj7-8cjw-p6xm'
  )
    throw new Error('Backport package identity drift');
  const proof = freeze({ root, provenance: p });
  proofs.add(proof);
  return proof;
}
export function requireBracesProof(proof) {
  if (!proofs.has(proof)) throw new Error('Verified backport artifact proof is required');
  return proof.provenance;
}
export function expectedBracesLock(proof, scope) {
  const p = requireBracesProof(proof);
  if (!['root', 'openspec'].includes(scope)) throw new Error('Unknown backport scope');
  return {
    node: 'node_modules/braces',
    version: VERSION,
    resolved: p.scopes[scope].resolved,
    integrity: p.artifact.integrity,
  };
}
export function verifyBracesLock(lock, proof, scope) {
  const expected = expectedBracesLock(proof, scope);
  const copies = Object.entries(lock.packages ?? {}).filter(
    ([node, entry]) => /(^|\/)node_modules\/braces$/i.test(node) || entry.name === 'braces',
  );
  if (copies.length !== 1 || copies[0][0] !== expected.node) throw new Error('Missing or new locked braces copy');
  const entry = copies[0][1];
  if (
    entry.link ||
    entry.version !== expected.version ||
    entry.resolved !== expected.resolved ||
    entry.integrity !== expected.integrity
  )
    throw new Error('Locked backport artifact drift');
  return expected;
}
export function verifyBracesManifest(manifest, proof, scope) {
  const expected = expectedBracesLock(proof, scope);
  if (manifest.dependencies?.braces !== expected.resolved || manifest.overrides?.braces !== '$braces')
    throw new Error('Canonical direct backport dependency/override is missing or changed');
}
export async function verifyInstalledBraces(proof, scope, lock) {
  const expected = verifyBracesLock(lock, proof, scope);
  const cwd = scope === 'root' ? proof.root : join(proof.root, 'tools/openspec');
  const manifest = JSON.parse(await readFile(join(cwd, 'package.json'), 'utf8'));
  verifyBracesManifest(manifest, proof, scope);
  const copies = new Set();
  let anchor;
  try {
    anchor = await assertPhysical(proof.root, join(cwd, 'node_modules'));
  } catch (error) {
    throw new Error(`Installed dependency tree is missing or unsafe: ${scope}`, { cause: error });
  }
  // Anchor once, then follow only physical directories returned by readdir.
  // Every descendant entry is checked without following links; repeating the
  // entire ancestor lstat/realpath chain for every installed file is unnecessary.
  const pending = [{ directory: anchor, context: 'modules', packageRoot: false }];
  while (pending.length) {
    const { directory, context, packageRoot, unscopedBraces } = pending.pop();
    if (unscopedBraces) copies.add(directory);
    const entries = await readdir(directory, { withFileTypes: true });
    for (const entry of entries) {
      // npm's generated executable links are excluded only at a module root.
      // An ordinary source .bin directory still needs recursive discovery.
      if (context === 'modules' && entry.name === '.bin') continue;
      if (entry.isSymbolicLink()) throw new Error(`Installed dependency symbolic link is not allowed: ${scope}`);
      if (!entry.isDirectory() && !entry.isFile())
        throw new Error(`Installed dependency entry is not physical: ${scope}`);
      // LOAD_AS_FILE runs before LOAD_AS_DIRECTORY for a bare require. An
      // adjacent braces.js/json/node would otherwise shadow verified bytes.
      if (context === 'modules' && /^braces\.(?:js|json|node)$/i.test(entry.name))
        throw new Error(`Installed braces file-resolution shadow: ${scope}`);
      if (!entry.isDirectory()) continue;
      const moduleChild = context === 'modules';
      const scopedDirectory = moduleChild && entry.name.startsWith('@');
      pending.push({
        directory: join(directory, entry.name),
        context: scopedDirectory ? 'scope' : entry.name.toLowerCase() === 'node_modules' ? 'modules' : 'content',
        packageRoot: (moduleChild && !scopedDirectory) || context === 'scope',
        unscopedBraces: moduleChild && entry.name.toLowerCase() === 'braces',
      });
    }
    // Only npm package roots own package metadata. A source lib/package.json
    // or lib/braces folder is not itself another installed npm package.
    if (packageRoot) {
      for (const entry of entries.filter((entry) => entry.name.toLowerCase() === 'package.json')) {
        if (!entry.isFile()) throw new Error(`Installed package metadata is not a physical file: ${scope}`);
        const data = JSON.parse(await readFile(join(directory, entry.name), 'utf8'));
        if (data.name === 'braces') copies.add(directory);
      }
    }
  }
  const paths = [...copies];
  if (paths.length !== 1 || relative(cwd, paths[0]).replaceAll('\\', '/') !== expected.node)
    throw new Error(`Missing, extra or aliased installed braces copy: ${scope}`);
  verifyFiles(await sourceFiles(proof.root, paths[0]), proof.provenance.artifact.files, PATCH_FILES);
  return { scope, status: 'verified-installed-backport', copies: 1, patchRevision: 1 };
}
