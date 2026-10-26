import assert from 'node:assert/strict';
import { lstat, mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const implementation = await import('./local-stack.mjs');
const id = 'a'.repeat(32);

async function completionWorkspace(t, profile = 'moscow') {
  const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'trips-completion-')));
  const owned = {
    ...state(root, 'e2e'),
    e2eProfile: profile,
    applicationAsOfUtc: implementation.e2eProfile(profile).instant,
    stateDir: path.join(root, 'TestResults', 'trips-local-stack', `e2e-${id}`),
  };
  await mkdir(path.join(owned.stateDir, 'evidence'), { recursive: true });
  t.after(() => rm(root, { recursive: true, force: true }));
  return owned;
}

test('actual Playwright skipped profile exits zero but cannot satisfy completion gate', async (t) => {
  const owned = await completionWorkspace(t);
  const testDir = path.join(owned.stateDir, 'skip-proof');
  await mkdir(testDir);
  const api = createRequire(import.meta.url).resolve('@playwright/test');
  await writeFile(
    path.join(testDir, 'skipped.spec.cjs'),
    `const {test}=require(${JSON.stringify(api)});test.skip('profile must really execute',async()=>{});`,
  );
  const config = path.join(testDir, 'playwright.config.cjs');
  await writeFile(
    config,
    "module.exports={testDir:__dirname,testMatch:'skipped.spec.cjs',workers:1,retries:0,reporter:'line',outputDir:__dirname+'/output'};",
  );
  const result = await implementation.run(
    process.execPath,
    [await implementation.resolveLocalCli('playwright'), 'test', '--config', config],
    { timeout: 30000 },
  );
  assert.equal(result.code, 0);
  assert.match(result.stdout, /1 skipped/);
  await assert.rejects(() => implementation.requireCompletedBrowserProfile(owned), /completion receipt/);
});

test('completion gate requires the full receipt for its exact city and stack', async (t) => {
  for (const profile of ['moscow', 'saint-petersburg']) {
    const owned = await completionWorkspace(t, profile);
    const file = path.join(owned.stateDir, 'evidence', 'browser-receipt.json');
    const name = profile === 'moscow' ? 'moscow-browser.json' : 'petersburg-browser.json';
    const original = JSON.parse(
      await readFile(path.join('openspec/changes/trips-m22-saint-petersburg/receipts', name), 'utf8'),
    );
    original.stackId = id;
    await writeFile(file, JSON.stringify(original));
    await implementation.requireCompletedBrowserProfile(owned);
    const mutations = [
      (value) => {
        value.status = 'skipped';
      },
      (value) => {
        value.stackId = 'b'.repeat(32);
      },
      (value) => {
        value.e2eProfile = 'foreign';
      },
      (value) => {
        value.applicationAsOfUtc = '2026-01-01T00:00:00Z';
      },
      (value) => {
        value.stages.pop();
      },
      (value) => {
        value.stages[1] = value.stages[0];
      },
      (value) => {
        value.stages[0].outcome = 'skipped';
      },
      (value) => {
        value.candidateRequests = 0;
      },
      (value) => {
        value.candidateRequests = 2;
      },
      (value) => {
        value.loginCount = 2;
      },
      (value) => {
        value.externalRequests = 1;
      },
      (value) => {
        value.restart.allExited = false;
      },
      (value) => {
        value.restart.storagePreserved = false;
      },
      (value) => {
        value.protectedRowSha256 = null;
      },
      (value) => {
        value.errorClass = 'Error';
      },
    ];
    if (profile === 'saint-petersburg')
      mutations.push((value) => {
        value.historicalMoscowPlanSha256 = null;
      });
    for (const mutate of mutations) {
      const value = structuredClone(original);
      mutate(value);
      await writeFile(file, JSON.stringify(value));
      await assert.rejects(() => implementation.requireCompletedBrowserProfile(owned), /completion receipt/);
    }
  }
});

test('actual child preserves split UTF8 in both streams and exact stdout bytes', async () => {
  const script =
    'process.stdout.write(Buffer.from([0xe2,0x82]));process.stderr.write(Buffer.from([0xf0,0x9f]));setTimeout(()=>{process.stdout.write(Buffer.from([0xac]));process.stderr.write(Buffer.from([0xa7,0xad]));},200);';
  const result = await implementation.run(process.execPath, ['-e', script], { timeout: 5000 });
  assert.equal(result.stdout, '€');
  assert.equal(result.stderr, '🧭');
  assert.deepEqual(result.stdoutBytes, Buffer.from('€'));
});

test('actual child keeps nonzero/allowFailure semantics and redacts a split Unicode secret', async () => {
  const secret = 'секрет🧭';
  const script =
    "const bytes=Buffer.from('секрет🧭');process.stdout.write('public diagnostic');process.stderr.write(bytes.subarray(0,bytes.length-2));setTimeout(()=>{process.stderr.write(bytes.subarray(bytes.length-2));process.exit(7);},200);";
  await assert.rejects(
    () => implementation.run(process.execPath, ['-e', script], { secrets: [secret], timeout: 5000 }),
    (error) => {
      assert.match(error.message, /failed \(7\)/);
      assert.match(error.message, /public diagnostic/);
      assert.match(error.message, /\[REDACTED\]/);
      assert(!error.message.includes('секрет'));
      return true;
    },
  );
  const allowed = await implementation.run(process.execPath, ['-e', script], { allowFailure: true, timeout: 5000 });
  assert.equal(allowed.code, 7);
  assert.equal(allowed.stderr, secret);
});

test('actual child output cap counts UTF8 bytes across streams', async () => {
  const script = "process.stdout.write('界'.repeat(1536*1024));process.stderr.write('界'.repeat(1536*1024));";
  await assert.rejects(() => implementation.run(process.execPath, ['-e', script], { timeout: 5000 }), /output limit/);
});

test('actual child keeps arbitrary source bytes without a UTF8 roundtrip', async () => {
  const result = await implementation.run(process.execPath, ['-e', 'process.stdout.write(Buffer.from([255,0,128]));'], {
    timeout: 5000,
  });
  assert.deepEqual(result.stdoutBytes, Buffer.from([255, 0, 128]));
});

test('loopback readiness retains bounded independent statuses, releases bodies and never exports errors', async () => {
  let canceled = 0;
  const calls = [];
  const result = await implementation.loopbackReadiness(state(path.resolve('fixture'), 'e2e'), async (url, options) => {
    calls.push({ url, redirect: options.redirect, credentials: options.credentials });
    return {
      status: calls.length === 1 ? 503 : 200,
      body: {
        cancel: async () => {
          canceled++;
        },
      },
    };
  });
  assert.deepEqual(result, { discovery: { status: 503, failure: null }, web: { status: 200, failure: null } });
  assert.equal(canceled, 2);
  assert(
    calls.every((c) => c.url.startsWith('http://127.0.0.1:') && c.redirect === 'error' && c.credentials === 'omit'),
  );
  const failure = await implementation.loopbackReadiness(state(path.resolve('fixture'), 'e2e'), async () => {
    throw new Error('PRIVATE diagnostic');
  });
  assert.deepEqual(failure, {
    discovery: { status: null, failure: 'request-failed' },
    web: { status: null, failure: 'request-failed' },
  });
  assert(!JSON.stringify(failure).includes('PRIVATE'));
});

test('named E2E city clocks are bound and interactive/legacy states remain unchanged', () => {
  const root = path.resolve('fixture'),
    legacy = state(root, 'e2e');
  assert.doesNotThrow(() => implementation.validateStateObject(legacy, root));
  assert.doesNotThrow(() => implementation.validateStateObject({ ...legacy, e2eProfile: 'moscow' }, root));
  assert.doesNotThrow(() =>
    implementation.validateStateObject(
      { ...legacy, e2eProfile: 'saint-petersburg', applicationAsOfUtc: '2026-10-27T12:00:00Z' },
      root,
    ),
  );
  for (const patch of [
    { e2eProfile: 'saint-petersburg' },
    { e2eProfile: 'unknown' },
    { applicationAsOfUtc: '2026-10-27T12:00:00Z' },
  ])
    assert.throws(() => implementation.validateStateObject({ ...legacy, ...patch }, root));
  assert.throws(() => implementation.validateStateObject({ ...state(root), e2eProfile: 'moscow' }, root));
});

test('both city profiles and their cleanup must pass the ordinary aggregate', async () => {
  const seen = [];
  const good = await implementation.runE2eProfiles(async (profile) => {
    seen.push(profile);
    return { stages: { browser: 'passed', cleanup: 'passed' } };
  });
  assert.equal(good.status, 'passed');
  assert.deepEqual(seen, ['moscow', 'saint-petersburg']);
  for (const result of [
    { stages: { browser: 'skipped', cleanup: 'passed' } },
    { stages: { browser: 'passed', cleanup: 'failed' } },
    { stages: { cleanup: 'passed' } },
  ])
    await assert.rejects(() => implementation.runE2eProfiles(async () => result));
  await assert.rejects(() =>
    implementation.runE2eProfiles(async () => {
      throw Error('failed');
    }),
  );
});
function state(repoRoot, mode = 'demo') {
  return {
    schemaVersion: 1,
    kind: 'travel-trips-local-stack',
    id,
    mode,
    repoRoot,
    projectName: `travel-trips-${mode}-${id}`,
    webPort: mode === 'demo' ? 4205 : 4206,
    oidcPort: mode === 'demo' ? 8185 : 8186,
    applicationAsOfUtc: mode === 'demo' ? null : '2026-10-24T12:00:00Z',
    createdAtUtc: '2026-10-24T12:00:00Z',
    dockerEndpoint: 'unix:///var/run/docker.sock',
    dockerId: 'local-daemon',
  };
}

test('rejects tampered project, ports, clock and copied checkout state', () => {
  assert.equal(typeof implementation.validateStateObject, 'function');
  const root = path.resolve('fixture');
  assert.doesNotThrow(() => implementation.validateStateObject(state(root), root));
  for (const patch of [
    { projectName: 'foreign' },
    { webPort: 4200 },
    { oidcPort: 8180 },
    { applicationAsOfUtc: '2026-01-01T00:00:00Z' },
    { repoRoot: path.resolve('other') },
    { dockerEndpoint: 'tcp://127.0.0.1:2375' },
    { id: '../escape' },
  ]) {
    assert.throws(() => implementation.validateStateObject({ ...state(root), ...patch }, root));
  }
});

test('refuses remote endpoints, including a local-looking SSH host', () => {
  assert.equal(typeof implementation.validateDockerEndpoint, 'function');
  for (const endpoint of [
    'tcp://127.0.0.1:2375',
    'ssh://localhost',
    'unix://remote/socket',
    'npipe:////server/pipe/docker_engine',
  ]) {
    assert.throws(() => implementation.validateDockerEndpoint(endpoint));
  }
  assert.equal(
    implementation.validateDockerEndpoint('npipe:////./pipe/dockerDesktopLinuxEngine'),
    'npipe:////./pipe/dockerDesktopLinuxEngine',
  );
});

test('rejects foreign paths and junction traversal before touching their contents', async () => {
  assert.equal(typeof implementation.validateOwnedPath, 'function');
  const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'trips-controller-')));
  try {
    const base = path.join(root, 'TestResults', 'trips-local-stack');
    const outside = path.join(root, 'foreign');
    await mkdir(base, { recursive: true });
    await mkdir(outside);
    await writeFile(path.join(outside, 'retain.txt'), 'owned by someone else');
    await assert.rejects(implementation.validateOwnedPath(outside, root));
    await symlink(outside, path.join(base, 'demo'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(implementation.validateOwnedPath(path.join(base, 'demo'), root), /link|reparse/i);
    assert.equal(await readFile(path.join(outside, 'retain.txt'), 'utf8'), 'owned by someone else');
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('owner labels and daemon identity must all agree before resource mutation', () => {
  assert.equal(typeof implementation.validateResources, 'function');
  const current = state(path.resolve('fixture'));
  const resource = {
    type: 'container',
    name: `${current.projectName}-host-1`,
    service: 'host',
    labels: { 'com.docker.compose.project': current.projectName, 'travel.trips.local-stack-id': id },
  };
  assert.doesNotThrow(() =>
    implementation.validateResources(current, { id: 'local-daemon', endpoint: current.dockerEndpoint }, [resource]),
  );
  assert.throws(() =>
    implementation.validateResources(current, { id: 'other-daemon', endpoint: current.dockerEndpoint }, [resource]),
  );
  assert.throws(() =>
    implementation.validateResources(current, { id: 'local-daemon', endpoint: current.dockerEndpoint }, [
      { ...resource, labels: {} },
    ]),
  );
  assert.throws(() =>
    implementation.validateResources(current, { id: 'local-daemon', endpoint: current.dockerEndpoint }, [
      { ...resource, name: 'foreign-container' },
    ]),
  );
});

test('occupied port rejects a fresh start before invoking build or creating state', async () => {
  assert.equal(typeof implementation.assertPortsAvailable, 'function');
  const server = net.createServer();
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  try {
    await assert.rejects(implementation.assertPortsAvailable([server.address().port]), /occupied|unavailable/i);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
});

test('redacts credentials, bearer tokens, cookies and OIDC query material', () => {
  assert.equal(typeof implementation.redactOutput, 'function');
  const text =
    'password=private-secret Authorization: Bearer abc.def.ghi\nCookie: session=bad\nSet-Cookie: x=bad\nhttp://127.0.0.1/callback?code=secret-code&state=secret-state&session_state=secret-session\n{"access_token":"token123","password":"other-secret"}';
  const output = implementation.redactOutput(text, ['private-secret']);
  for (const secret of [
    'private-secret',
    'abc.def.ghi',
    'session=bad',
    'x=bad',
    'secret-code',
    'secret-state',
    'secret-session',
    'token123',
    'other-secret',
  ])
    assert.ok(!output.includes(secret), secret);
});

test('compose isolates backends, publishes loopback only and keeps helper clock out of normal Host', () => {
  assert.equal(typeof implementation.buildCompose, 'function');
  const root = path.resolve('fixture');
  const compose = implementation.buildCompose({
    ...state(root),
    stateDir: path.join(root, 'TestResults/trips-local-stack/demo'),
  });
  assert.equal(compose.networks.isolated.internal, true);
  assert.deepEqual(compose.services.postgres.healthcheck.test, [
    'CMD-SHELL',
    'pg_isready -h 127.0.0.1 -U postgres -d travel',
  ]);
  assert.deepEqual(compose.services.ingress.ports, ['127.0.0.1:4205:4205', '127.0.0.1:8185:8185']);
  for (const [name, service] of Object.entries(compose.services)) {
    assert.equal(service.labels['travel.trips.local-stack-id'], id);
    if (name !== 'ingress') assert.equal(service.ports, undefined);
  }
  assert.equal(compose.services.host.network_mode, 'service:keycloak');
  assert.deepEqual(compose.services.host.command, ['dotnet', '/host/Travel.Host.dll']);
  assert.equal(compose.services.host.environment.Flights__Smtp__Port, '0');
  assert.equal(compose.services.host.environment.Flights__Smtp__Host, '');
  assert.equal(compose.services.host.environment.Flights__Smtp__FromAddress, '');
  assert.deepEqual(compose.services.ingress.tmpfs, ['/data', '/config']);
  assert.deepEqual(compose.services.redis.tmpfs, ['/data']);
});

test('refuses a junction hidden below an apparently owned directory before recursive cleanup', async () => {
  const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'trips-controller-')));
  try {
    const owned = path.join(root, 'TestResults/trips-local-stack/demo');
    const foreign = path.join(root, 'foreign');
    await mkdir(path.join(owned, 'private'), { recursive: true });
    await mkdir(foreign);
    await symlink(foreign, path.join(owned, 'private/keys'), process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(implementation.validateOwnedPath(owned, root, true), /link|reparse/i);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('releases only its own reservations when a later port is occupied', async () => {
  const occupied = net.createServer();
  await new Promise((resolve) => occupied.listen(0, '127.0.0.1', resolve));
  const temporary = net.createServer();
  await new Promise((resolve) => temporary.listen(0, '127.0.0.1', resolve));
  const freePort = temporary.address().port;
  await new Promise((resolve) => temporary.close(resolve));
  try {
    await assert.rejects(
      implementation.assertPortsAvailable([freePort, occupied.address().port]),
      /occupied|unavailable/i,
    );
    await implementation.assertPortsAvailable([freePort]);
    assert.equal(occupied.listening, true);
  } finally {
    await new Promise((resolve) => occupied.close(resolve));
  }
});

test('requires both owner and compose labels on every discovered resource', () => {
  const current = state(path.resolve('fixture'));
  const daemon = { id: current.dockerId, endpoint: current.dockerEndpoint };
  for (const labels of [
    { 'com.docker.compose.project': current.projectName },
    { 'travel.trips.local-stack-id': current.id },
    { 'com.docker.compose.project': 'foreign', 'travel.trips.local-stack-id': current.id },
  ]) {
    assert.throws(() =>
      implementation.validateResources(current, daemon, [
        { type: 'volume', name: `${current.projectName}_postgres-data`, labels },
      ]),
    );
  }
});

test('generated SQL is readable by PostgreSQL UID while configuration stays private on POSIX', async () => {
  assert.equal(typeof implementation.writeStackConfiguration, 'function');
  const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'trips-controller-')));
  const stateDir = path.join(root, 'TestResults/trips-local-stack/demo');
  try {
    await mkdir(stateDir, { recursive: true });
    await implementation.writeStackConfiguration({ ...state(root), stateDir }, root);
    assert.match(await readFile(path.join(stateDir, 'init-db.sql'), 'utf8'), /CREATE DATABASE keycloak;/);
    assert.equal(
      JSON.parse(await readFile(path.join(stateDir, 'compose.json'), 'utf8')).name,
      `travel-trips-demo-${id}`,
    );
    if (process.platform !== 'win32') {
      assert.equal((await lstat(path.join(stateDir, 'init-db.sql'))).mode & 0o777, 0o644);
      assert.equal((await lstat(path.join(stateDir, 'compose.json'))).mode & 0o777, 0o600);
      assert.equal((await lstat(path.join(stateDir, 'Caddyfile'))).mode & 0o777, 0o600);
    }
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('resolves installed Nx and Playwright executables from their package bin metadata', async () => {
  assert.equal(typeof implementation.resolveLocalCli, 'function');
  const root = await realpath(await mkdtemp(path.join(os.tmpdir(), 'trips-controller-')));
  try {
    await mkdir(path.join(root, 'node_modules/nx/dist/bin'), { recursive: true });
    await writeFile(
      path.join(root, 'node_modules/nx/package.json'),
      JSON.stringify({ bin: { nx: './dist/bin/nx.js' } }),
    );
    await writeFile(path.join(root, 'node_modules/nx/dist/bin/nx.js'), '// Local executable');
    await mkdir(path.join(root, 'node_modules/@playwright/test'), { recursive: true });
    await writeFile(
      path.join(root, 'node_modules/@playwright/test/package.json'),
      JSON.stringify({ bin: { playwright: 'cli.js' } }),
    );
    await writeFile(path.join(root, 'node_modules/@playwright/test/cli.js'), '// Local executable');
    assert.equal(await implementation.resolveLocalCli('nx', root), path.join(root, 'node_modules/nx/dist/bin/nx.js'));
    assert.equal(
      await implementation.resolveLocalCli('playwright', root),
      path.join(root, 'node_modules/@playwright/test/cli.js'),
    );
    await writeFile(
      path.join(root, 'node_modules/nx/package.json'),
      JSON.stringify({ bin: { nx: '../../foreign.js' } }),
    );
    await assert.rejects(implementation.resolveLocalCli('nx', root), /local package|outside/i);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('failed readiness preserves only bounded health facts and never promotes exit one to ready', () => {
  assert.equal(typeof implementation.projectProbeResult, 'function');
  const result = implementation.projectProbeResult({
    code: 1,
    stdout: JSON.stringify({
      ready: true,
      wallUtc: '2026-10-24T12:00:00Z',
      applicationAsOfUtc: '2026-10-24T12:00:00Z',
      checks: { ready: 503, dependencies: 200, private: 'secret' },
      dependencies: {
        trips: 'Unhealthy',
        postgres: 'Healthy',
        redis: 'Healthy',
        nats: 'Healthy',
        duffel: 'Degraded',
        private: 'secret',
      },
      password: 'never-include',
      details: { connectionString: 'never-include' },
    }),
  });
  assert.deepEqual(result, {
    ready: false,
    exitCode: 1,
    wallUtc: '2026-10-24T12:00:00Z',
    applicationAsOfUtc: '2026-10-24T12:00:00Z',
    checks: { ready: 503, dependencies: 200 },
    dependencies: { trips: 'Unhealthy', postgres: 'Healthy', redis: 'Healthy', nats: 'Healthy', duffel: 'Degraded' },
  });
  assert.deepEqual(implementation.projectProbeResult({ code: 1, stdout: 'not JSON private-secret' }), {
    ready: false,
    exitCode: 1,
    failureClass: 'InvalidProbeJson',
  });
  assert.equal(implementation.projectProbeResult({ code: 0, stdout: '{"ready":true}' }).ready, true);
});

test('child failure retains bounded stdout and stderr while redacting secrets in both', () => {
  assert.equal(typeof implementation.formatChildFailure, 'function');
  const diagnostic = implementation.formatChildFailure(
    '{"errorClass":"LocalStackRefusal","detail":"known-private-value"}',
    'Cannot load native library; Authorization: Bearer secret-token',
    ['known-private-value'],
  );
  assert.match(diagnostic, /LocalStackRefusal/);
  assert.match(diagnostic, /Cannot load native library/);
  assert.ok(!diagnostic.includes('known-private-value'));
  assert.ok(!diagnostic.includes('secret-token'));
  const bounded = implementation.formatChildFailure(
    `${'a'.repeat(12000)}stdout-tail`,
    `${'b'.repeat(12000)}stderr-tail`,
  );
  assert.ok(bounded.length <= 8000);
  assert.match(bounded, /stdout-tail/);
  assert.match(bounded, /stderr-tail/);
});
