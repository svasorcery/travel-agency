import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { basename, join, resolve, sep } from 'node:path';
import test from 'node:test';
import {
  verifyInstalledMinimatch,
  verifyMinimatchArtifact,
  verifyMinimatchLock,
} from '../security/minimatch-artifact.mjs';

const root = resolve('.');
const proof = await verifyMinimatchArtifact(root);
const lock = JSON.parse(await readFile('package-lock.json', 'utf8'));

test('installed proof verifies the local backport and the exact glob registry copy', async () => {
  assert.equal((await verifyInstalledMinimatch(proof, lock)).patchRevision, 1);
});

test('backport lock gate refuses substituted archives, unverified proof and extra copies', () => {
  assert.throws(() => verifyMinimatchLock(lock, {}), /proof/);
  for (const mutate of [
    (copy) => {
      copy.packages['node_modules/minimatch'].resolved = 'file:other.tgz';
    },
    (copy) => {
      copy.packages['node_modules/minimatch'].integrity = 'sha512-YQ==';
    },
    (copy) => {
      copy.packages['node_modules/minimatch'].version = '11.0.3';
    },
    (copy) => {
      copy.packages['node_modules/glob/node_modules/minimatch'].version = '11.0.0';
    },
    (copy) => {
      copy.packages['node_modules/consumer/node_modules/minimatch'] = copy.packages['node_modules/minimatch'];
    },
  ]) {
    const copy = structuredClone(lock);
    mutate(copy);
    assert.throws(() => verifyMinimatchLock(copy, proof), /drift|copy/);
  }
});

for (const target of [
  'tools/vendor/minimatch-10.2.5/dist/commonjs/index.js',
  'tools/vendor/minimatch-10.2.5-travel.1.tgz',
]) {
  test(`artifact gate refuses changed bytes in ${target}`, async () => {
    await mkdir(resolve('TestResults'), { recursive: true });
    const base = await realpath(resolve('TestResults'));
    const temp = await mkdtemp(join(base, 'minimatch-proof-'));
    try {
      for (const path of [
        'tools/vendor/minimatch-10.2.5',
        'tools/vendor/minimatch-10.2.5-upstream.tgz',
        'tools/vendor/minimatch-10.2.5-travel.1.tgz',
        'tools/security/minimatch-provenance.json',
      ]) {
        await mkdir(join(temp, path, '..'), { recursive: true });
        await cp(join(root, path), join(temp, path), { recursive: true });
      }
      const bytes = await readFile(join(temp, target));
      bytes[0] ^= 1;
      await writeFile(join(temp, target), bytes);
      await assert.rejects(verifyMinimatchArtifact(temp), /drift/);
    } finally {
      const actual = await realpath(temp);
      assert.ok(actual.startsWith(base + sep) && basename(actual).startsWith('minimatch-proof-'));
      await rm(actual, { recursive: true, force: true });
    }
  });
}
