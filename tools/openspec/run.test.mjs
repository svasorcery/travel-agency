import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';

const integrity = 'sha512-V+zitRK918I6B3EIcYL3gVYoPoPgzknSxE1zL0zUQdbd4NsO5kbrxU5jVceuNNEznD94pbRPCvpCDIa6BeEaPA==';
const sha = (value) => createHash('sha256').update(value).digest('hex');
async function load() {
  const mod = await import('./run.mjs').catch((error) => {
    if (error.code === 'ERR_MODULE_NOT_FOUND' && error.url === new URL('./run.mjs', import.meta.url).href) return {};
    throw error;
  });
  assert.equal(typeof mod.runPinned, 'function', 'pinned launcher is not implemented');
  return mod;
}
async function fixture(fn) {
  const root = await realpath(await mkdtemp(join(tmpdir(), 'travel-openspec test ')));
  try {
    const tool = join(root, 'tools', 'openspec');
    const pkg = join(tool, 'node_modules', '@fission-ai', 'openspec');
    await mkdir(join(pkg, 'bin'), { recursive: true });
    const binary = `import fs from 'node:fs';const home=process.env.XDG_CONFIG_HOME;const cfg=JSON.parse(fs.readFileSync(home+'/openspec/config.json','utf8'));process.stdout.write(JSON.stringify({args:process.argv.slice(2),cwd:process.cwd(),home,data:process.env.XDG_DATA_HOME,profile:cfg.profile,delivery:cfg.delivery,telemetry:process.env.OPENSPEC_TELEMETRY,dnt:process.env.DO_NOT_TRACK,completion:process.env.OPENSPEC_NO_COMPLETIONS,version:'1.14.0'}));if(process.argv.includes('missing'))process.exitCode=9;`;
    const packageText = JSON.stringify({ name: '@fission-ai/openspec', version: '1.14.0', type: 'module' });
    await writeFile(join(pkg, 'bin', 'openspec.js'), binary);
    await writeFile(join(pkg, 'package.json'), packageText);
    await writeFile(join(tool, 'package.json'), JSON.stringify({ dependencies: { '@fission-ai/openspec': '1.14.0' } }));
    await writeFile(
      join(tool, 'package-lock.json'),
      JSON.stringify({
        lockfileVersion: 3,
        packages: {
          '': { dependencies: { '@fission-ai/openspec': '1.14.0' } },
          'node_modules/@fission-ai/openspec': { version: '1.14.0', integrity },
        },
      }),
    );
    const lines = [
      ['@fission-ai/openspec/bin/openspec.js', binary],
      ['@fission-ai/openspec/package.json', packageText],
    ]
      .map(([p, text]) => p + '\0' + sha(text) + '\n')
      .join('');
    const manifest = {
      schemaVersion: 1,
      package: {
        name: '@fission-ai/openspec',
        version: '1.14.0',
        integrity,
        sourceCommit: '94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5',
      },
      profile: 'core',
      delivery: 'skills',
      tool: 'codex',
      adapterVersion: 1,
      runtimeDigest: sha(lines),
      skills: {},
    };
    await writeFile(join(tool, 'manifest.json'), JSON.stringify(manifest));
    await fn({ root, tool, pkg, binary, manifest });
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}
test('runs the pinned Node entrypoint from physical root with isolated config and exact arguments', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root }) => {
    const result = await runPinned(['status', '--change', 'flights-m3-cancellation', '--json'], {
      repositoryRoot: root,
      cwd: join(root, 'modules', 'flights'),
      environment: { ...process.env, XDG_CONFIG_HOME: 'foreign', XDG_DATA_HOME: 'foreign' },
    });
    const value = JSON.parse(result.stdout);
    assert.equal(result.exitCode, 0);
    assert.equal(value.cwd, root);
    assert.deepEqual(value.args, ['status', '--change', 'flights-m3-cancellation', '--json']);
    assert.equal(value.profile, 'core');
    assert.equal(value.delivery, 'skills');
    assert.equal(value.telemetry, '0');
    assert.equal(value.dnt, '1');
    assert.equal(value.completion, '1');
    assert.notEqual(value.home, 'foreign');
    assert.notEqual(value.data, 'foreign');
    await assert.rejects(readFile(join(value.home, 'openspec', 'config.json')), { code: 'ENOENT' });
  });
});
test('CLI failure preserves exit/output and still cleans only its temporary config', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root }) => {
    const result = await runPinned(['status', '--change', 'missing', '--json'], { repositoryRoot: root });
    const value = JSON.parse(result.stdout);
    assert.equal(result.exitCode, 9);
    await assert.rejects(readFile(join(value.home, 'openspec', 'config.json')), { code: 'ENOENT' });
    assert.equal(value.version, '1.14.0');
  });
});
test('rejects missing or wrong local installation instead of falling back to PATH', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root, pkg }) => {
    await writeFile(join(pkg, 'package.json'), JSON.stringify({ version: '9.0.0' }));
    await assert.rejects(runPinned(['--version'], { repositoryRoot: root }), /version|pin/i);
    await rm(pkg, { recursive: true });
    await assert.rejects(runPinned(['--version'], { repositoryRoot: root }), /installation|missing|ENOENT/i);
  });
});
test('detects a tampered binary with unchanged version and lock integrity', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root, pkg }) => {
    await writeFile(join(pkg, 'bin', 'openspec.js'), 'process.stdout.write("untrusted")');
    await assert.rejects(runPinned(['--version'], { repositoryRoot: root }), /digest|integrity|modified/i);
  });
});
test('requires local pin integrity and rejects foreign store selection', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root, tool }) => {
    await assert.rejects(runPinned(['status', '--store', 'foreign'], { repositoryRoot: root }), /store|local/i);
    const lock = JSON.parse(await readFile(join(tool, 'package-lock.json'), 'utf8'));
    lock.packages['node_modules/@fission-ai/openspec'].integrity = 'sha512-foreign';
    await writeFile(join(tool, 'package-lock.json'), JSON.stringify(lock));
    await assert.rejects(runPinned(['--version'], { repositoryRoot: root }), /integrity|pin/i);
  });
});
test('read-only prepared scope works without temp writes and cannot run mutating commands', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root, tool }) => {
    const scope = join(tool, 'node_modules', '.travel-read-config');
    await mkdir(join(scope, 'openspec'), { recursive: true });
    await writeFile(
      join(scope, 'openspec', 'config.json'),
      JSON.stringify({ profile: 'core', delivery: 'skills', telemetry: { enabled: false } }),
    );
    const manifestText = await readFile(join(tool, 'manifest.json'), 'utf8');
    await writeFile(
      join(scope, 'scope.json'),
      JSON.stringify({ kind: 'travel-openspec-read-scope-v1', repositoryRoot: root, manifestHash: sha(manifestText) }),
    );
    const environment = { ...process.env, TRAVEL_OPENSPEC_READ_CONFIG: scope };
    const result = await runPinned(['status', '--json'], { repositoryRoot: root, environment });
    assert.equal(JSON.parse(result.stdout).home, scope);
    assert.ok(await readFile(join(scope, 'scope.json'), 'utf8'));
    await assert.rejects(runPinned(['archive', 'example'], { repositoryRoot: root, environment }), /read.only|mutat/i);
  });
});

test('leading global flags cannot reach an upstream init/update bypass', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root }) => {
    await assert.rejects(
      runPinned(['--no-color', 'init', '--force', '--tools', 'all'], { repositoryRoot: root }),
      /command|flag|unsupported/i,
    );
    await assert.rejects(runPinned(['--version', 'init'], { repositoryRoot: root }), /arguments|isolated|flag/i);
  });
});
test('read-only command grammar rejects both split and equals write flags', async () => {
  const { runPinned } = await load();
  await fixture(async ({ root }) => {
    for (const args of [
      ['context', '--code-workspace', 'AGENTS.md', '--force'],
      ['context', '--code-workspace=AGENTS.md', '--force'],
      ['status', '--output=AGENTS.md'],
    ])
      await assert.rejects(runPinned(args, { repositoryRoot: root }), /arguments|flag|read.only|unsupported/i);
  });
});
