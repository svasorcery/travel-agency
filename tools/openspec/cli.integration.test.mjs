import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { cp, mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { delimiter, join } from 'node:path';
import { test } from 'node:test';
import { promisify } from 'node:util';

const exec = promisify(execFile);

import { SKILLS } from './policy.mjs';
import { repositoryRoot, runPinned } from './run.mjs';

async function loadSetup() {
  const m = await import('./setup.mjs').catch((e) => {
    if (e.code === 'ERR_MODULE_NOT_FOUND' && e.url === new URL('./setup.mjs', import.meta.url).href) return {};
    throw e;
  });
  assert.equal(typeof m.setup, 'function', 'staged setup is not implemented');
  return m.setup;
}
async function cloneFixture(fn, { canonical = true } = {}) {
  const temporary = await mkdtemp(join(tmpdir(), 'travel-openspec integration '));
  const root = canonical ? await realpath(temporary) : temporary;
  try {
    await mkdir(join(root, 'tools'), { recursive: true });
    await cp(join(repositoryRoot, 'tools', 'openspec'), join(root, 'tools', 'openspec'), {
      recursive: true,
      filter: (p) => !p.includes('.travel-read-config'),
    });
    await mkdir(join(root, '.agents', 'skills', 'spec'), { recursive: true });
    await writeFile(join(root, '.agents', 'skills', 'spec', 'SKILL.md'), 'travel-skill-byte-preserved');
    await mkdir(join(root, 'modules', 'flights'), { recursive: true });
    await mkdir(join(root, 'openspec', 'changes', 'example', 'specs', 'example'), { recursive: true });
    await writeFile(join(root, 'openspec', 'changes', 'example', 'proposal.md'), '# Why\nExample fictional change.');
    await writeFile(
      join(root, 'openspec', 'changes', 'example', 'specs', 'example', 'spec.md'),
      '## ADDED Requirements\n### Requirement: Example\nThe system SHALL be fictional.\n#### Scenario: Example\n- **WHEN** read\n- **THEN** nothing is sent.\n',
    );
    await fn(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}
test('staged init and update install exactly six adapted skills without changing Travel or feature corpus', async () => {
  const setup = await loadSetup();
  await cloneFixture(async (root) => {
    const before = await readFile(join(root, 'openspec', 'changes', 'example', 'proposal.md'), 'utf8');
    await setup(root, ['init']);
    const snapshots = [];
    for (const name of SKILLS) {
      const text = await readFile(join(root, '.agents', 'skills', name, 'SKILL.md'), 'utf8');
      assert.ok(text.includes('node tools/openspec/run.mjs'));
      snapshots.push(text);
    }
    await setup(root, ['update']);
    for (let i = 0; i < SKILLS.length; i++)
      assert.equal(await readFile(join(root, '.agents', 'skills', SKILLS[i], 'SKILL.md'), 'utf8'), snapshots[i]);
    assert.equal(
      await readFile(join(root, '.agents', 'skills', 'spec', 'SKILL.md'), 'utf8'),
      'travel-skill-byte-preserved',
    );
    assert.equal(await readFile(join(root, 'openspec', 'changes', 'example', 'proposal.md'), 'utf8'), before);
    assert.equal((await readFile(join(root, '.agents', 'skills', '.openspec-target'), 'utf8')).trim(), 'codex');
  });
});
test('actual pinned skill commands preserve JSON output', async () => {
  const setup = await loadSetup();
  await cloneFixture(async (root) => {
    await setup(root, ['init']);
    const version = await runPinned(['--version'], { repositoryRoot: root });
    assert.equal(version.stdout.trim(), '1.14.0');
    const result = await runPinned(['status', '--change', 'example', '--json'], {
      repositoryRoot: root,
    });
    assert.equal(result.exitCode, 0);
    const status = JSON.parse(result.stdout);
    assert.equal(status.changeName, 'example');
    assert.equal(status.schemaName, 'spec-driven');
    const instructions = await runPinned(['instructions', 'proposal', '--change', 'example', '--json'], {
      repositoryRoot: root,
    });
    assert.equal(instructions.exitCode, 0);
    assert.equal(JSON.parse(instructions.stdout).artifactId, 'proposal');
  });
});
test('unknown tools/profile/force flags cannot broaden generated inventory', async () => {
  const setup = await loadSetup();
  await assert.rejects(setup(repositoryRoot, ['init', '--tools', 'all']), /tools|arguments|profile|unsupported/i);
});

test('setup rejects unknown existing generated children and conflicting selected metadata without overwriting', async () => {
  const setup = await loadSetup();
  await cloneFixture(async (root) => {
    await setup(root, ['init']);
    const extra = join(root, '.agents', 'skills', 'openspec-explore', 'extra.txt');
    await writeFile(extra, 'user file');
    await assert.rejects(setup(root, ['update']), /inventory|extra|children|unexpected/i);
    assert.equal(await readFile(extra, 'utf8'), 'user file');
    await rm(extra);
    const change = join(root, 'openspec', 'changes', 'flights-m3-cancellation');
    await mkdir(change, { recursive: true });
    await writeFile(join(change, '.openspec.yaml'), 'schema: different\n');
    await assert.rejects(setup(root, ['update']), /schema|metadata/i);
    assert.equal(await readFile(join(change, '.openspec.yaml'), 'utf8'), 'schema: different\n');
  });
});

test('incompatible existing pilot metadata is rejected without modifying it', async () => {
  const setup = await loadSetup();
  await cloneFixture(async (root) => {
    const change = join(root, 'openspec', 'changes', 'flights-m3-cancellation');
    await mkdir(change, { recursive: true });
    await writeFile(join(change, '.openspec.yaml'), 'schema: different\n');
    await assert.rejects(setup(root, ['init']), /schema|metadata/i);
    assert.equal(await readFile(join(change, '.openspec.yaml'), 'utf8'), 'schema: different\n');
  });
});

test('public launcher from root and nested process cwd ignores an adversarial global executable', async () => {
  const setup = await loadSetup();
  await cloneFixture(async (root) => {
    await setup(root, ['init']);
    const fake = join(root, 'foreign-bin');
    await mkdir(fake);
    const marker = join(root, 'foreign-called');
    const script = join(fake, 'foreign.mjs');
    await writeFile(
      script,
      'import fs from "node:fs";fs.writeFileSync(' +
        JSON.stringify(marker) +
        ',"called");process.stdout.write("foreign");',
    );
    await writeFile(join(fake, 'openspec.cmd'), '@"' + process.execPath + '" "' + script + '"\r\n');
    await writeFile(
      join(fake, 'openspec'),
      '#!' +
        process.execPath +
        '\nimport fs from "node:fs";fs.writeFileSync(' +
        JSON.stringify(marker) +
        ',"called");process.stdout.write("foreign");',
      { mode: 0o755 },
    );
    const env = { ...process.env, PATH: fake + delimiter + process.env.PATH };
    const launcher = join(root, 'tools', 'openspec', 'run.mjs');
    const effective = await readFile(join(root, '.agents', 'skills', 'openspec-apply-change', 'SKILL.md'), 'utf8');
    assert.ok(effective.includes('node tools/openspec/run.mjs status --change "<name>" --json'));
    for (const cwd of [root, join(root, 'modules', 'flights')]) {
      const { stdout } = await exec(process.execPath, [launcher, 'status', '--change', 'example', '--json'], {
        cwd,
        env,
        timeout: 30000,
      });
      assert.equal(JSON.parse(stdout).changeName, 'example');
    }
    await assert.rejects(readFile(marker), { code: 'ENOENT' });
    await assert.rejects(
      exec(process.execPath, [launcher, '--no-color', 'init', '--force', '--tools', 'all'], { cwd: root, env }),
      (error) => error.code === 1 && /flags|command/.test(error.stderr),
    );
    await rm(join(root, 'tools', 'openspec', 'node_modules', '@fission-ai', 'openspec'), { recursive: true });
    await assert.rejects(
      exec(process.execPath, [launcher, '--version'], { cwd: root, env }),
      (error) => error.code === 1 && /installation|missing/.test(error.stderr),
    );
    await assert.rejects(readFile(marker), { code: 'ENOENT' });
  });
});

test('public staged init completes without an ESM entrypoint cycle', async () => {
  await cloneFixture(async (root) => {
    const launcher = join(root, 'tools', 'openspec', 'run.mjs');
    const result = await exec(process.execPath, [launcher, 'init'], { cwd: root, timeout: 30000 });
    assert.equal(result.stderr, '');
    assert.equal((await readFile(join(root, '.agents', 'skills', '.openspec-target'), 'utf8')).trim(), 'codex');
  });
});

test('actual pinned read command preserves the prepared verifier scope and full runtime footprint', async () => {
  const setup = await loadSetup();
  const { preparePinnedRuntime, workspaceDigest, assertSnapshotUnchanged } = await import('./snapshot.mjs');
  const { treeDigest } = await import('./files.mjs');
  await cloneFixture(async (root) => {
    await setup(root, ['init']);
    const prepared = await preparePinnedRuntime(repositoryRoot, root);
    const baseline = {
      workspace: await workspaceDigest(root),
      scope: prepared.scope,
      scopeDigest: await treeDigest(prepared.scope),
      runtime: true,
      runtimeFootprint: await treeDigest(join(root, 'tools', 'openspec', 'node_modules')),
    };
    const status = await runPinned(['status', '--change', 'example', '--json'], {
      repositoryRoot: root,
      environment: prepared.environment,
    });
    assert.equal(status.exitCode, 0);
    assert.equal(JSON.parse(status.stdout).changeName, 'example');
    await assertSnapshotUnchanged(root, baseline);
  });
});

test('runtime preparation accepts the actual temporary directory alias and binds its physical root', async () => {
  const { preparePinnedRuntime } = await import('./snapshot.mjs');
  await cloneFixture(
    async (root) => {
      const prepared = await preparePinnedRuntime(repositoryRoot, root);
      const marker = JSON.parse(await readFile(join(prepared.scope, 'scope.json'), 'utf8'));
      assert.equal(marker.repositoryRoot, await realpath(root));
      assert.equal(prepared.environment.TRAVEL_OPENSPEC_READ_CONFIG, prepared.scope);
    },
    { canonical: false },
  );
});

test('archived pilot can still prove the exact read command without changing source or prepared baseline', async () => {
  const { preparePinnedRuntime, workspaceDigest, assertSnapshotUnchanged } = await import('./snapshot.mjs');
  const { treeDigest } = await import('./files.mjs');
  const beforeSource = await workspaceDigest(join(repositoryRoot, 'openspec'));
  await cloneFixture(async (root) => {
    const prepared = await preparePinnedRuntime(repositoryRoot, root);
    assert.equal(prepared.statusFixture, true);
    const baseline = {
      workspace: await workspaceDigest(root),
      scope: prepared.scope,
      scopeDigest: await treeDigest(prepared.scope),
      runtime: true,
      runtimeFootprint: await treeDigest(join(root, 'tools', 'openspec', 'node_modules')),
    };
    const result = await runPinned(['status', '--change', 'flights-m3-cancellation', '--json'], {
      repositoryRoot: root,
      environment: prepared.environment,
    });
    assert.equal(result.exitCode, 0, result.stdout);
    const status = JSON.parse(result.stdout);
    assert.equal(status.changeName, 'flights-m3-cancellation');
    assert.equal(status.schemaName, 'spec-driven');
    assert.equal(status.changeRoot, join(await realpath(root), 'openspec', 'changes', 'flights-m3-cancellation'));
    await assertSnapshotUnchanged(root, baseline);
  });
  assert.equal(await workspaceDigest(join(repositoryRoot, 'openspec')), beforeSource);
});
