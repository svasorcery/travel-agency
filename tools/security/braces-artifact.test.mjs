import assert from 'node:assert/strict';
import { cp, mkdir, mkdtemp, readFile, rm, symlink, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { basename, dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { within } from '../openspec/files.mjs';
import {
  expectedBracesLock,
  requireBracesProof,
  verifyBracesArtifact,
  verifyBracesLock,
  verifyBracesManifest,
  verifyInstalledBraces,
} from './braces-artifact.mjs';

const repository = fileURLToPath(new URL('../..', import.meta.url));
const original = await verifyBracesArtifact(repository);
async function fixture(action, scope = 'root') {
  const base = join(repository, 'TestResults/braces-remediation-2026-10-16');
  await mkdir(base, { recursive: true });
  const root = await mkdtemp(join(base, 'artifact-fixture-'));
  try {
    for (const path of [
      'tools/security/braces-provenance.json',
      original.provenance.source,
      original.provenance.artifact.path,
      original.provenance.upstream.archive,
    ]) {
      await mkdir(dirname(join(root, path)), { recursive: true });
      await cp(join(repository, path), join(root, path), { recursive: true });
    }
    const proof = await verifyBracesArtifact(root);
    const expected = expectedBracesLock(proof, scope);
    const lock = {
      lockfileVersion: 3,
      packages: {
        '': {},
        [expected.node]: { version: expected.version, resolved: expected.resolved, integrity: expected.integrity },
      },
    };
    const cwd = scope === 'root' ? root : join(root, 'tools/openspec');
    await mkdir(cwd, { recursive: true });
    await writeFile(
      join(cwd, 'package.json'),
      JSON.stringify({ dependencies: { braces: expected.resolved }, overrides: { braces: '$braces' } }),
    );
    const installed = join(cwd, 'node_modules/braces');
    await mkdir(dirname(installed), { recursive: true });
    await cp(join(root, original.provenance.source), installed, { recursive: true });
    await action({ root, cwd, proof, lock, installed });
  } finally {
    assert.ok(within(base, root) && basename(root).startsWith('artifact-fixture-'));
    await rm(root, { recursive: true, force: true });
  }
}

// These exercise the verifier's ownership boundary: ordinary Node resolution
// from a package source file can use node_modules below that package root.
for (const scope of ['root', 'openspec']) {
  for (const source of [
    'consumer/lib',
    '@scope/consumer/dist/runtime/chunks',
    'consumer/.bin/deeper',
    '.package-lock.json/source',
  ])
    for (const metadata of ['missing', 'renamed'])
      test(`${scope}: rejects the actual Node-resolved source copy in ${source} with ${metadata} metadata`, async () =>
        fixture(async ({ cwd, proof, lock }) => {
          const directory = join(cwd, 'node_modules', source);
          const extra = join(directory, 'node_modules/braces');
          await mkdir(extra, { recursive: true });
          await writeFile(join(extra, 'index.js'), "module.exports = 'ordinary-private-copy-fixture';\n");
          if (metadata === 'renamed')
            await writeFile(join(extra, 'package.json'), JSON.stringify({ name: 'renamed', version: '3.0.3' }));
          const req = createRequire(join(directory, 'index.cjs'));
          assert.equal(req.resolve('braces'), join(extra, 'index.js'));
          assert.equal(req('braces'), 'ordinary-private-copy-fixture');
          await assert.rejects(verifyInstalledBraces(proof, scope, lock), /extra|copy|braces/i);
        }, scope));

  test(`${scope}: case-insensitive module-directory discovery includes deep BrAcEs copies`, async () =>
    fixture(async ({ cwd, proof, lock }) => {
      const directory = join(cwd, 'node_modules/consumer/lib/runtime');
      const extra = join(directory, 'NoDe_MoDuLeS/BrAcEs');
      await mkdir(extra, { recursive: true });
      await writeFile(join(extra, 'index.js'), "module.exports = 'case-private-copy';\n");
      if (process.platform === 'win32') {
        const req = createRequire(join(directory, 'index.cjs'));
        assert.equal(req('braces'), 'case-private-copy');
      }
      await assert.rejects(verifyInstalledBraces(proof, scope, lock), /extra|copy|braces/i);
    }, scope));

  for (const source of ['consumer/lib', '@scope/consumer/dist/deeper'])
    test(`${scope}: rejects an actual Node file shadow below ${source}`, async () =>
      fixture(async ({ cwd, proof, lock }) => {
        const directory = join(cwd, 'node_modules', source);
        const modules = join(directory, 'node_modules');
        await mkdir(modules, { recursive: true });
        await writeFile(join(modules, 'braces.js'), "module.exports = 'ordinary-shadow-fixture';\n");
        const req = createRequire(join(directory, 'index.cjs'));
        assert.equal(req.resolve('braces'), join(modules, 'braces.js'));
        assert.equal(req('braces'), 'ordinary-shadow-fixture');
        await assert.rejects(verifyInstalledBraces(proof, scope, lock), /shadow|copy/i);
      }, scope));

  test(`${scope}: ordinary source folders and files named braces are not npm packages`, async () =>
    fixture(async ({ cwd, proof, lock }) => {
      const source = join(cwd, 'node_modules/consumer/lib');
      await mkdir(join(source, 'braces'), { recursive: true });
      await mkdir(join(source, 'dist'), { recursive: true });
      await writeFile(join(source, 'braces/package.json'), JSON.stringify({ name: 'braces' }));
      await writeFile(join(source, 'braces.js'), 'ordinary source file\n');
      await writeFile(join(source, 'dist/BRACES.JSON'), '{}\n');
      // A generic source subdirectory's metadata is not npm package metadata.
      await writeFile(join(source, 'package.json'), JSON.stringify({ name: 'braces' }));
      assert.equal((await verifyInstalledBraces(proof, scope, lock)).copies, 1);
    }, scope));

  test(`${scope}: a linked source subtree is rejected instead of traversed`, async () =>
    fixture(async ({ root, cwd, proof, lock }) => {
      const source = join(cwd, 'node_modules/consumer/lib');
      const target = join(root, 'link-target');
      await mkdir(source, { recursive: true });
      await mkdir(target, { recursive: true });
      await symlink(target, join(source, 'linked'), 'junction');
      await assert.rejects(verifyInstalledBraces(proof, scope, lock), /link|unsafe|physical/i);
    }, scope));
}

test('artifact-only verification and mandatory installed verification are different phases', async () =>
  fixture(async ({ proof, lock, installed }) => {
    assert.equal(requireBracesProof(proof).patchRevision, 1);
    assert.equal((await verifyInstalledBraces(proof, 'root', lock)).copies, 1);
    await rm(installed, { recursive: true });
    await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /missing|copy|installed/i);
  }));
for (const physicalName of ['braces', 'BrAcEs'])
  for (const metadata of ['renamed', 'missing'])
    test(`extra nested physical ${physicalName} copy is rejected with ${metadata} package metadata`, async () =>
      fixture(async ({ root, proof, lock }) => {
        const extra = join(root, `node_modules/consumer/node_modules/${physicalName}`);
        await mkdir(extra, { recursive: true });
        await writeFile(join(root, 'node_modules/consumer/package.json'), JSON.stringify({ name: 'consumer' }));
        await writeFile(join(extra, 'index.js'), 'module.exports = {};\n');
        if (metadata === 'renamed')
          await writeFile(join(extra, 'package.json'), JSON.stringify({ name: 'renamed', version: '3.0.3' }));
        await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /extra|copy|braces/i);
      }));
test('renamed canonical metadata fails byte verification', async () =>
  fixture(async ({ proof, lock, installed }) => {
    await writeFile(join(installed, 'package.json'), JSON.stringify({ name: 'renamed', version: '3.0.3' }));
    await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /copy|hash|inventory/i);
  }));
test('an additional installed file fails the complete inventory', async () =>
  fixture(async ({ proof, lock, installed }) => {
    await writeFile(join(installed, 'unexpected.js'), 'module.exports = {};\n');
    await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /inventory/i);
  }));
test('removing the installed depth guard fails the required complete inventory', async () =>
  fixture(async ({ proof, lock, installed }) => {
    await rm(join(installed, 'lib/depth.js'));
    await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /inventory/i);
  }));
test('editing an installed compile byte fails the exact patched source hashes', async () =>
  fixture(async ({ proof, lock, installed }) => {
    const path = join(installed, 'lib/compile.js');
    const bytes = await readFile(path);
    bytes[20] ^= 1;
    await writeFile(path, bytes);
    await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /hash/i);
  }));
for (const shadow of ['braces.js', 'BRACES.JS'])
  test(`a Node file-resolution shadow ${shadow} beside the installed package is rejected`, async () =>
    fixture(async ({ proof, lock, installed }) => {
      await writeFile(join(dirname(installed), shadow), 'module.exports = {};\n');
      await assert.rejects(verifyInstalledBraces(proof, 'root', lock), /shadow|copy/i);
    }));
test('artifact integrity/source drift is rejected before installation', async () =>
  fixture(async ({ root }) => {
    const path = join(root, original.provenance.artifact.path);
    const bytes = await readFile(path);
    bytes[20] ^= 1;
    await writeFile(path, bytes);
    await assert.rejects(verifyBracesArtifact(root), /integrity/i);
  }));
test('editing guard source cannot be approved by a matching archive alone', async () =>
  fixture(async ({ root }) => {
    await writeFile(join(root, original.provenance.source, 'lib/depth.js'), 'module.exports = {};\n');
    await assert.rejects(verifyBracesArtifact(root), /hash/i);
  }));
test('proof cannot be forged and locks cannot use registry, links or another local archive', () => {
  assert.throws(() => requireBracesProof({ provenance: original.provenance }), /proof/i);
  const expected = expectedBracesLock(original, 'root');
  for (const change of [
    { resolved: original.provenance.upstream.tarball },
    { resolved: 'file:tools/vendor/other.tgz' },
    { link: true },
    { integrity: original.provenance.upstream.integrity },
  ]) {
    const lock = { packages: { [expected.node]: { ...expected, ...change } } };
    assert.throws(() => verifyBracesLock(lock, original, 'root'), /drift/i);
  }
  assert.throws(
    () =>
      verifyBracesManifest(
        { dependencies: { braces: expected.resolved }, overrides: { braces: expected.resolved } },
        original,
        'root',
      ),
    /override/i,
  );
});
