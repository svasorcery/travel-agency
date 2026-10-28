import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { join, resolve, sep } from 'node:path';
import test from 'node:test';
import {
  expectedDeepmergeLock,
  requireDeepmergeProof,
  verifyDeepmergeArtifact,
  verifyDeepmergeLock,
  verifyDeepmergeManifest,
  verifyInstalledDeepmerge,
} from './deepmerge-artifact.mjs';

const source = resolve('.');
async function fixture(action) {
  await mkdir(resolve('TestResults/deepmerge-artifact'), { recursive: true });
  const base = await realpath(resolve('TestResults/deepmerge-artifact'));
  const root = await mkdtemp(join(base, 'deepmerge-proof-'));
  try {
    for (const file of [
      'tools/vendor/deepmerge-4.3.1',
      'tools/vendor/deepmerge-4.3.1-upstream.tgz',
      'tools/vendor/deepmerge-4.3.1-travel.1.tgz',
      'tools/security/deepmerge-provenance.json',
    ]) {
      await mkdir(resolve(root, file, '..'), { recursive: true });
      await cp(join(source, file), join(root, file), { recursive: true });
    }
    const proof = await verifyDeepmergeArtifact(root);
    const expected = expectedDeepmergeLock(proof);
    const manifest = { devDependencies: { deepmerge: expected.resolved }, overrides: { deepmerge: '$deepmerge' } };
    const lock = {
      packages: {
        'node_modules/deepmerge': {
          version: expected.version,
          resolved: expected.resolved,
          integrity: expected.integrity,
        },
      },
    };
    await writeFile(join(root, 'package.json'), JSON.stringify(manifest));
    await action({ root, proof, manifest, lock });
  } finally {
    assert.ok((await realpath(root)).startsWith(base + sep));
    await rm(root, { recursive: true, force: true });
  }
}

test('exact archive/source/provenance produces an unforgeable root-only proof', () =>
  fixture(async ({ proof, manifest, lock }) => {
    assert.equal(expectedDeepmergeLock(proof).version, '4.3.1');
    assert.throws(() => requireDeepmergeProof({ ...proof }), /proof/i);
    assert.throws(() => expectedDeepmergeLock(proof, 'openspec'), /scope/i);
    verifyDeepmergeManifest(manifest, proof);
    verifyDeepmergeLock(lock, proof);
  }));
for (const file of [
  'tools/vendor/deepmerge-4.3.1/dist/cjs.js',
  'tools/vendor/deepmerge-4.3.1-travel.1.tgz',
  'tools/vendor/deepmerge-4.3.1-upstream.tgz',
])
  test(`artifact gate rejects modified ${file}`, () =>
    fixture(async ({ root }) => {
      const path = join(root, file);
      await writeFile(path, Buffer.concat([await readFile(path), Buffer.from('changed')]));
      await assert.rejects(verifyDeepmergeArtifact(root), /hash|integrity|drift/i);
    }));

test('manifest and lock reject registry fallback, extra copy, wrong integrity and overrides', () =>
  fixture(async ({ proof, manifest, lock }) => {
    for (const bad of [
      {
        packages: {
          'node_modules/deepmerge': {
            ...lock.packages['node_modules/deepmerge'],
            resolved: 'https://registry.npmjs.org/deepmerge/-/deepmerge-4.3.1.tgz',
          },
        },
      },
      {
        packages: {
          ...lock.packages,
          'node_modules/parent/node_modules/deepmerge': lock.packages['node_modules/deepmerge'],
        },
      },
      {
        packages: {
          'node_modules/deepmerge': { ...lock.packages['node_modules/deepmerge'], integrity: 'sha512-changed' },
        },
      },
    ])
      assert.throws(() => verifyDeepmergeLock(bad, proof), /copy|drift/i);
    assert.throws(
      () => verifyDeepmergeManifest({ ...manifest, overrides: { deepmerge: '4.3.1' } }, proof),
      /override/i,
    );
  }));

test('installed proof rejects edited, nested and file-shadow copies', () =>
  fixture(async ({ root, proof, lock }) => {
    const installed = join(root, 'node_modules/deepmerge');
    await cp(join(root, 'tools/vendor/deepmerge-4.3.1'), installed, { recursive: true });
    assert.equal((await verifyInstalledDeepmerge(proof, lock)).copies, 1);
    const file = join(installed, 'dist/cjs.js');
    const original = await readFile(file);
    await writeFile(file, Buffer.concat([original, Buffer.from('changed')]));
    await assert.rejects(verifyInstalledDeepmerge(proof, lock), /hash|drift/i);
    await writeFile(file, original);
    const nested = join(root, 'node_modules/parent/lib/node_modules/deepmerge');
    await mkdir(nested, { recursive: true });
    await writeFile(join(nested, 'package.json'), '{"name":"deepmerge"}');
    await assert.rejects(verifyInstalledDeepmerge(proof, lock), /extra|copy/i);
    await rm(nested, { recursive: true });
    await writeFile(join(root, 'node_modules/deepmerge.js'), 'module.exports={};');
    await assert.rejects(verifyInstalledDeepmerge(proof, lock), /shadow/i);
  }));
