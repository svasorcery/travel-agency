import { createHash } from 'node:crypto';
import { lstat, readdir, readFile, realpath } from 'node:fs/promises';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
export const sha256 = (value) => createHash('sha256').update(value).digest('hex');
export const normalizeText = (value) => value.replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n');
export async function readJson(path) {
  return JSON.parse(normalizeText(await readFile(path, 'utf8')));
}
export function within(root, path) {
  const r = relative(resolve(root), resolve(path));
  return r === '' || (!isAbsolute(r) && r !== '..' && !r.startsWith('..' + sep));
}
export async function assertPhysical(root, target) {
  const base = await realpath(root);
  const full = resolve(target);
  if (!within(base, full)) throw new Error('Artifact path escapes repository');
  const segments = relative(base, full).split(sep).filter(Boolean);
  let cursor = base;
  for (const part of segments) {
    cursor = join(cursor, part);
    const stat = await lstat(cursor);
    if (stat.isSymbolicLink()) throw new Error('Symbolic links are not allowed in pinned tooling');
  }
  const actual = await realpath(full);
  if (!within(base, actual)) throw new Error('Physical artifact escapes repository');
  return actual;
}
export async function treeDigest(root, { skip = new Set(), skipPaths = new Set() } = {}) {
  const rows = [];
  async function walk(dir) {
    for (const entry of (await readdir(dir, { withFileTypes: true })).sort((a, b) =>
      a.name.localeCompare(b.name, 'en'),
    )) {
      const full = join(dir, entry.name);
      const relativePath = relative(root, full).replaceAll('\\', '/');
      if (
        skipPaths.has(relativePath) ||
        (skip.has(entry.name) && (entry.name !== '.travel-read-config' || resolve(dir) === resolve(root)))
      )
        continue;
      const stat = await lstat(full);
      if (stat.isSymbolicLink()) throw new Error('Runtime symbolic link is not allowed');
      if (stat.isDirectory()) await walk(full);
      else if (stat.isFile()) rows.push([relative(root, full).replaceAll('\\', '/'), sha256(await readFile(full))]);
      else throw new Error('Runtime artifact must be a physical file');
    }
  }
  await walk(root);
  rows.sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0));
  return sha256(rows.map(([p, h]) => p + '\0' + h + '\n').join(''));
}
