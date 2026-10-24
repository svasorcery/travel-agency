import assert from 'node:assert/strict';
import { lstat, mkdir, mkdtemp, readFile, realpath, rm, symlink, writeFile } from 'node:fs/promises';
import net from 'node:net';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

const implementation = await import('./local-stack.mjs');
const id = 'a'.repeat(32);
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
