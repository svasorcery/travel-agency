import { spawn } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { chmod, cp, lstat, mkdir, readdir, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import images from './images.json' with { type: 'json' };

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const OWNER = 'travel.trips.local-stack-id';
const PROJECT = 'com.docker.compose.project';
const FIXED_TIME = '2026-10-24T12:00:00Z';
export const E2E_PROFILES = Object.freeze({
  moscow: Object.freeze({
    instant: FIXED_TIME,
    destination: 'moscow',
    origin: 'saint-petersburg',
    dates: ['2026-10-25', '2026-10-26', '2026-10-27'],
  }),
  'saint-petersburg': Object.freeze({
    instant: '2026-10-27T12:00:00Z',
    destination: 'saint-petersburg',
    origin: 'moscow',
    dates: ['2026-10-28', '2026-10-29', '2026-10-30'],
  }),
});
export function e2eProfile(name = 'moscow') {
  if (!Object.hasOwn(E2E_PROFILES, name)) throw new Error('Unknown E2E city profile.');
  return E2E_PROFILES[name];
}
const SERVICES = ['postgres', 'redis', 'nats', 'keycloak', 'host', 'ingress', 'tool'];
const CORE = ['host', 'keycloak', 'postgres'];
const stateKeys = [
  'schemaVersion',
  'kind',
  'id',
  'mode',
  'repoRoot',
  'projectName',
  'webPort',
  'oidcPort',
  'applicationAsOfUtc',
  'createdAtUtc',
  'dockerEndpoint',
  'dockerId',
];
const equalPath = (a, b) => (process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b);
const basePath = (root = REPO_ROOT) => path.join(root, 'TestResults', 'trips-local-stack');
const json = (value) => `${JSON.stringify(value, null, 2)}\n`;
// Compose, rather than JavaScript, expands these private environment values.
const dbPasswordVariable = `\${TRIPS_DATABASE_PASSWORD}`;
const certificatePasswordVariable = `\${TRIPS_CERTIFICATE_PASSWORD}`;

export function validateDockerEndpoint(endpoint) {
  if (
    typeof endpoint !== 'string' ||
    !(/^(unix:\/\/\/[^\0\r\n]+)$/.test(endpoint) || /^npipe:\/\/\/\/\.\/pipe\/[a-zA-Z0-9_.-]+$/.test(endpoint))
  ) {
    throw new Error('Only a local Unix socket or Windows named pipe Docker endpoint is supported.');
  }
  return endpoint;
}

export function validateStateObject(state, repoRoot = REPO_ROOT) {
  if (!state || typeof state !== 'object' || Array.isArray(state)) throw new Error('Invalid owned state.');
  if (
    Object.keys(state).some((key) => ![...stateKeys, 'stateDir', 'e2eProfile'].includes(key)) ||
    stateKeys.some((key) => !(key in state))
  )
    throw new Error('Unexpected owned state fields.');
  const demo = state.mode === 'demo';
  if (demo && Object.hasOwn(state, 'e2eProfile')) throw new Error('Interactive demo cannot have a fixed E2E profile.');
  if (
    state.schemaVersion !== 1 ||
    state.kind !== 'travel-trips-local-stack' ||
    !/^[a-f0-9]{32}$/.test(state.id) ||
    !['demo', 'e2e'].includes(state.mode) ||
    typeof state.repoRoot !== 'string' ||
    !path.isAbsolute(state.repoRoot) ||
    !equalPath(state.repoRoot, repoRoot) ||
    state.projectName !== `travel-trips-${state.mode}-${state.id}` ||
    state.webPort !== (demo ? 4205 : 4206) ||
    state.oidcPort !== (demo ? 8185 : 8186) ||
    state.applicationAsOfUtc !== (demo ? null : e2eProfile(state.e2eProfile).instant) ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/.test(state.createdAtUtc) ||
    !Number.isFinite(Date.parse(state.createdAtUtc)) ||
    typeof state.dockerId !== 'string' ||
    !/^[A-Za-z0-9:_-]{3,160}$/.test(state.dockerId)
  )
    throw new Error('Owned state identity, checkout, derived profile or clock does not match.');
  validateDockerEndpoint(state.dockerEndpoint);
  return state;
}

async function exists(target) {
  try {
    await lstat(target);
    return true;
  } catch (error) {
    if (error.code === 'ENOENT') return false;
    throw error;
  }
}

async function noLinks(target, recursive = false) {
  const absolute = path.resolve(target);
  const parsed = path.parse(absolute);
  let cursor = parsed.root;
  for (const part of absolute.slice(parsed.root.length).split(path.sep).filter(Boolean)) {
    cursor = path.join(cursor, part);
    let stat;
    try {
      stat = await lstat(cursor);
    } catch (error) {
      if (error.code === 'ENOENT') return;
      throw error;
    }
    if (stat.isSymbolicLink() || !equalPath(await realpath(cursor), cursor))
      throw new Error('Symlink/reparse traversal is forbidden for owned stack files.');
  }
  if (recursive) await noLinkedChildren(absolute);
}

async function noLinkedChildren(directory) {
  if (!(await lstat(directory)).isDirectory()) return;
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const child = path.join(directory, entry.name);
    if (entry.isSymbolicLink() || !equalPath(await realpath(child), child))
      throw new Error('Symlink/reparse traversal is forbidden for owned stack files.');
    if (entry.isDirectory()) await noLinkedChildren(child);
  }
}

export async function validateOwnedPath(stateDir, repoRoot = REPO_ROOT, recursive = false) {
  if (typeof stateDir !== 'string' || !path.isAbsolute(stateDir)) throw new Error('State directory must be absolute.');
  const normalized = path.resolve(stateDir);
  const relative = path.relative(basePath(repoRoot), normalized);
  if (relative.includes(path.sep) || !/^(demo|e2e-[a-f0-9]{32})$/.test(relative))
    throw new Error('State must be directly inside this checkout TestResults/trips-local-stack.');
  await noLinks(normalized, recursive);
  return normalized;
}

export async function readState(statePath) {
  if (!path.isAbsolute(statePath) || path.basename(statePath) !== 'state.json')
    throw new Error('Use the absolute owned state.json path.');
  const stateDir = await validateOwnedPath(path.dirname(statePath));
  await noLinks(statePath);
  const state = validateStateObject(JSON.parse(await readFile(statePath, 'utf8')));
  if (path.basename(stateDir) !== (state.mode === 'demo' ? 'demo' : `e2e-${state.id}`))
    throw new Error('State directory and identity do not match.');
  return { ...state, stateDir };
}

export function redactOutput(value, secrets = []) {
  let result = String(value);
  for (const secret of secrets
    .filter((item) => typeof item === 'string' && item.length > 0)
    .sort((a, b) => b.length - a.length))
    result = result.split(secret).join('[REDACTED]');
  return result
    .replace(/\bBearer\s+[^\s"'<>]+/gi, 'Bearer [REDACTED]')
    .replace(/\b(?:Set-Cookie|Cookie|Authorization)\s*:\s*[^\r\n]*/gi, '[REDACTED HEADER]')
    .replace(
      /([?&](?:code|state|session_state|id_token|access_token|refresh_token|code_verifier|code_challenge)=)[^&#\s"']*/gi,
      '$1[REDACTED]',
    )
    .replace(
      /((?:"|')?(?:password|databasePassword|certificatePassword|access_token|refresh_token|id_token|client_secret)(?:"|')?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;}]+)/gi,
      '$1"[REDACTED]"',
    );
}

export async function assertPortsAvailable(ports) {
  const held = [];
  try {
    for (const port of ports) {
      const server = net.createServer();
      await new Promise((resolve, reject) => {
        server.once('error', () =>
          reject(new Error(`Loopback port ${port} is occupied or unavailable; no other service was stopped.`)),
        );
        server.listen({ port, host: '127.0.0.1', exclusive: true }, resolve);
      });
      held.push(server);
    }
  } finally {
    await Promise.all(held.map((server) => new Promise((resolve) => server.close(resolve))));
  }
}

export function validateResources(state, daemon, resources) {
  validateStateObject(state, state.repoRoot);
  if (daemon.id !== state.dockerId || daemon.endpoint !== state.dockerEndpoint)
    throw new Error('Docker daemon identity changed; no resources were modified.');
  for (const resource of resources) {
    if (
      resource.labels?.[PROJECT] !== state.projectName ||
      resource.labels?.[OWNER] !== state.id ||
      (!resource.name.startsWith(`${state.projectName}-`) && !resource.name.startsWith(`${state.projectName}_`))
    )
      throw new Error('Foreign Docker resource or ownership labels do not match.');
    if (resource.type === 'container' && !SERVICES.includes(resource.service))
      throw new Error('Unexpected service in the owned project.');
    if (
      resource.type === 'network' &&
      !['isolated', 'ingress'].some((name) => resource.name === `${state.projectName}_${name}`)
    )
      throw new Error('Unexpected network in the owned project.');
    if (resource.type === 'volume' && resource.name !== `${state.projectName}_postgres-data`)
      throw new Error('Unexpected volume in the owned project.');
  }
}

function hostEnvironment(state) {
  return {
    ASPNETCORE_ENVIRONMENT: 'Development',
    DOTNET_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: 'http://0.0.0.0:5099',
    HealthEndpoints__InternalPort: '5098',
    ConnectionStrings__travel: `Host=postgres;Port=5432;Database=travel;Username=postgres;Password=${dbPasswordVariable}`,
    ConnectionStrings__redis: 'redis:6379',
    ConnectionStrings__nats: 'nats://nats:4222',
    Keycloak__Authority: `http://127.0.0.1:${state.oidcPort}/realms/travel`,
    Keycloak__Audience: 'travel-web',
    Trips__Enabled: 'true',
    Trips__Itinerary__Enabled: 'true',
    Trips__Protection__KeyRingPath: '/state/private/keys/trips/ring',
    Trips__Protection__ActiveCertificatePath: '/state/private/keys/trips/certificate.pfx',
    Trips__Protection__ActiveCertificatePassword: certificatePasswordVariable,
    Flights__PiiProtection__KeyRingPath: '/state/private/keys/flights/ring',
    Flights__PiiProtection__ActiveCertificatePath: '/state/private/keys/flights/certificate.pfx',
    Flights__PiiProtection__ActiveCertificatePassword: certificatePasswordVariable,
    Support__TextProtection__KeyRingPath: '/state/private/keys/support/ring',
    Support__TextProtection__ActiveCertificatePath: '/state/private/keys/support/certificate.pfx',
    Support__TextProtection__ActiveCertificatePassword: certificatePasswordVariable,
    Flights__FeatureFlags__NlSearch__Enabled: 'false',
    Flights__FeatureFlags__Travelpayouts__Enabled: 'false',
    Flights__Duffel__BaseUrl: 'http://127.0.0.1:1',
    Flights__Providers__Frankfurter__BaseAddress: 'http://127.0.0.1:1',
    Flights__Smtp__Host: '',
    Flights__Smtp__Port: '0',
    Flights__Smtp__FromAddress: '',
    Hotels__Enabled: 'false',
    Rail__Enabled: 'false',
    Support__Classification__Enabled: 'false',
    Logging__LogLevel__Default: 'Warning',
  };
}

export function buildCompose(state) {
  const labels = { [OWNER]: state.id };
  const mount = (source, target, readOnly = true) => ({
    type: 'bind',
    source: path.join(state.stateDir, source).replaceAll('\\', '/'),
    target,
    read_only: readOnly,
    bind: { create_host_path: false },
  });
  const toolMounts = [mount('.', '/state'), mount('host', '/host'), mount('tool', '/tool')];
  return {
    name: state.projectName,
    services: {
      postgres: {
        image: images.postgres,
        labels,
        environment: {
          POSTGRES_USER: 'postgres',
          POSTGRES_PASSWORD: dbPasswordVariable,
          POSTGRES_DB: 'travel',
        },
        volumes: [
          'postgres-data:/var/lib/postgresql/data',
          mount('init-db.sql', '/docker-entrypoint-initdb.d/10-trips.sql'),
        ],
        networks: ['isolated'],
        healthcheck: {
          // The initialization server accepts only Unix sockets; schema needs the final TCP server.
          test: ['CMD-SHELL', 'pg_isready -h 127.0.0.1 -U postgres -d travel'],
          interval: '2s',
          timeout: '2s',
          retries: 60,
        },
      },
      redis: { image: images.redis, labels, tmpfs: ['/data'], networks: ['isolated'] },
      nats: { image: images.nats, labels, command: ['-js'], networks: ['isolated'] },
      keycloak: {
        image: images.keycloak,
        labels,
        // Realm is mode 0600 in an owned bind mount; no private file is made world-readable.
        user: '0:0',
        command: ['start-dev', `--http-port=${state.oidcPort}`, '--import-realm', '--log-level=warn'],
        environment: {
          KC_DB: 'postgres',
          KC_DB_URL: 'jdbc:postgresql://postgres:5432/keycloak',
          KC_DB_USERNAME: 'postgres',
          KC_DB_PASSWORD: dbPasswordVariable,
          KC_HOSTNAME: `http://127.0.0.1:${state.oidcPort}`,
        },
        volumes: [mount('private/realm', '/opt/keycloak/data/import')],
        depends_on: { postgres: { condition: 'service_healthy' } },
        networks: ['isolated'],
      },
      host: {
        image: images.aspnet,
        labels,
        working_dir: '/host',
        command: ['dotnet', '/host/Travel.Host.dll'],
        network_mode: 'service:keycloak',
        depends_on: ['keycloak', 'redis', 'nats'],
        environment: hostEnvironment(state),
        volumes: toolMounts,
      },
      tool: {
        image: images.aspnet,
        labels,
        profiles: ['tools'],
        working_dir: '/tool',
        entrypoint: ['dotnet', '/tool/Travel.Trips.LocalStack.dll'],
        environment: hostEnvironment(state),
        volumes: toolMounts,
        networks: ['isolated'],
      },
      ingress: {
        image: images.caddy,
        labels,
        tmpfs: ['/data', '/config'],
        ports: [`127.0.0.1:${state.webPort}:${state.webPort}`, `127.0.0.1:${state.oidcPort}:${state.oidcPort}`],
        volumes: [mount('Caddyfile', '/etc/caddy/Caddyfile'), mount('web', '/web')],
        networks: ['ingress', 'isolated'],
        depends_on: ['host', 'keycloak'],
      },
    },
    networks: { isolated: { internal: true, labels }, ingress: { labels } },
    volumes: { 'postgres-data': { labels } },
  };
}

function expectedFiles(state) {
  const files = {
    'compose.json': json(buildCompose(state)),
    Caddyfile: `{\n  admin off\n  auto_https off\n  log {\n    level ERROR\n  }\n}\nhttp://:${state.oidcPort} {\n  reverse_proxy keycloak:${state.oidcPort}\n}\nhttp://:${state.webPort} {\n  handle /api/* {\n    reverse_proxy keycloak:5099\n  }\n  handle {\n    root * /web\n    try_files {path} /index.csr.html\n    file_server\n  }\n}\n`,
    'init-db.sql': `CREATE DATABASE keycloak;\nCREATE TABLE public.trips_demo_owner (id text PRIMARY KEY);\nINSERT INTO public.trips_demo_owner (id) VALUES ('${state.id}');\n`,
  };
  if (state.mode === 'e2e')
    files['compose.e2e.json'] = json({
      services: {
        host: {
          command: ['dotnet', '/tool/Travel.Trips.LocalStack.dll', 'serve-e2e', '--state', '/state', '--host', '/host'],
        },
      },
    });
  return files;
}

export async function writeStackConfiguration(state, repoRoot = REPO_ROOT) {
  validateStateObject(state, repoRoot);
  await validateOwnedPath(state.stateDir, repoRoot);
  for (const [file, content] of Object.entries(expectedFiles(state))) {
    const target = path.join(state.stateDir, file);
    // PostgreSQL drops to its own UID before reading this nonsecret bind-mounted SQL.
    const mode = file === 'init-db.sql' ? 0o644 : 0o600;
    await writeFile(target, content, { flag: 'wx', mode });
    if (file === 'init-db.sql' && process.platform !== 'win32') await chmod(target, mode);
  }
}

async function validateFiles(state) {
  await validateOwnedPath(state.stateDir, REPO_ROOT, true);
  for (const [file, content] of Object.entries(expectedFiles(state))) {
    if ((await readFile(path.join(state.stateDir, file), 'utf8')) !== content)
      throw new Error(`Owned configuration ${file} changed; refusing resource mutation.`);
  }
}

async function privateConfiguration(state) {
  const provision = JSON.parse(await readFile(path.join(state.stateDir, 'private/provision.json'), 'utf8'));
  if (
    !/^[a-fA-F0-9]{32,128}$/.test(provision.databasePassword) ||
    !/^[a-fA-F0-9]{32,128}$/.test(provision.certificatePassword)
  )
    throw new Error('Owned private configuration is invalid.');
  const credentials = JSON.parse(await readFile(path.join(state.stateDir, 'private/credentials.json'), 'utf8'));
  if (!Array.isArray(credentials) || credentials.length !== 2)
    throw new Error('Owned fictional credentials are invalid.');
  return {
    env: {
      TRIPS_DATABASE_PASSWORD: provision.databasePassword,
      TRIPS_CERTIFICATE_PASSWORD: provision.certificatePassword,
    },
    secrets: [
      provision.databasePassword,
      provision.certificatePassword,
      ...credentials.flatMap((item) => [item.username, item.password]),
    ],
  };
}

export function formatChildFailure(stdout, stderr, secrets = []) {
  // Redact before truncation so a split credential cannot escape filtering.
  const out = redactOutput(stdout, secrets);
  const error = redactOutput(stderr, secrets);
  const labels = ['stdout:\n', '\nstderr:\n'];
  const eachLimit = Math.floor((8000 - labels.join('').length) / 2);
  return `${labels[0]}${out.slice(-eachLimit)}${labels[1]}${error.slice(-eachLimit)}`;
}

export async function run(command, args, { env = {}, secrets = [], timeout = 600_000, allowFailure = false } = {}) {
  return await new Promise((resolve, reject) => {
    const environment = { ...process.env, ...env };
    for (const key of ['DOCKER_HOST', 'DOCKER_CONTEXT', 'COMPOSE_FILE', 'COMPOSE_PROJECT_NAME', 'COMPOSE_PROFILES'])
      delete environment[key];
    const child = spawn(command, args, {
      cwd: REPO_ROOT,
      env: environment,
      shell: false,
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const stdoutChunks = [];
    const stderrChunks = [];
    let capturedBytes = 0;
    let overflow = false;
    const capture = (chunk, error) => {
      if (overflow) return;
      capturedBytes += chunk.length;
      if (capturedBytes > 8 * 1024 * 1024) {
        overflow = true;
        child.kill();
        return;
      }
      (error ? stderrChunks : stdoutChunks).push(chunk);
    };
    child.stdout.on('data', (chunk) => capture(chunk, false));
    child.stderr.on('data', (chunk) => capture(chunk, true));
    const timer = setTimeout(() => child.kill(), timeout);
    child.once('error', () => {
      clearTimeout(timer);
      reject(new Error(`Could not start ${path.basename(command)}; check local prerequisites.`));
    });
    child.once('close', (code, signal) => {
      clearTimeout(timer);
      const stdoutBytes = Buffer.concat(stdoutChunks);
      const stdout = stdoutBytes.toString('utf8');
      const stderr = Buffer.concat(stderrChunks).toString('utf8');
      if (overflow || signal || (code !== 0 && !allowFailure))
        reject(
          new Error(
            `${path.basename(command)} failed (${overflow ? 'output limit' : signal || code}). ${formatChildFailure(stdout, stderr, secrets)}`,
          ),
        );
      else resolve({ code, stdout, stderr, stdoutBytes });
    });
  });
}

async function dockerDaemon() {
  let endpoint = process.env.DOCKER_HOST;
  if (endpoint) validateDockerEndpoint(endpoint);
  if (!endpoint || process.env.DOCKER_CONTEXT) {
    const context = process.env.DOCKER_CONTEXT || (await run('docker', ['context', 'show'])).stdout.trim();
    const result = await run('docker', ['context', 'inspect', context]);
    endpoint = JSON.parse(result.stdout)[0]?.Endpoints?.docker?.Host;
  }
  validateDockerEndpoint(endpoint);
  const info = JSON.parse((await run('docker', ['--host', endpoint, 'info', '--format', '{{json .}}'])).stdout);
  if (info.OSType !== 'linux' || typeof info.ID !== 'string' || !info.ID)
    throw new Error('A local Linux Docker daemon is required.');
  return { endpoint, id: info.ID, architecture: info.Architecture };
}

const docker = (state, args, options) => run('docker', ['--host', state.dockerEndpoint, ...args], options);
async function compose(state, args, options = {}) {
  const config = await privateConfiguration(state);
  const files = ['-f', path.join(state.stateDir, 'compose.json')];
  if (state.mode === 'e2e') files.push('-f', path.join(state.stateDir, 'compose.e2e.json'));
  return docker(
    state,
    ['compose', '--project-name', state.projectName, '--project-directory', state.stateDir, ...files, ...args],
    { ...options, env: { ...config.env, ...options.env }, secrets: config.secrets },
  );
}

async function resources(state) {
  const found = [];
  for (const type of ['container', 'network', 'volume']) {
    const base = type === 'container' ? ['ps', '-a'] : [type, 'ls'];
    const lines = (await docker(state, [...base, '--format', '{{json .}}'])).stdout
      .trim()
      .split(/\r?\n/)
      .filter(Boolean)
      .map((line) => JSON.parse(line));
    const selected = lines.filter((item) => {
      const name = type === 'container' ? item.Names : item.Name;
      return (
        name?.startsWith(`${state.projectName}-`) ||
        name?.startsWith(`${state.projectName}_`) ||
        String(item.Labels).includes(`${OWNER}=${state.id}`) ||
        String(item.Labels).includes(`${PROJECT}=${state.projectName}`)
      );
    });
    for (const item of selected) {
      const identifier = type === 'container' ? item.ID : item.Name;
      const format =
        type === 'container'
          ? '{"name":{{json .Name}},"id":{{json .Id}},"labels":{{json .Config.Labels}},"state":{{json .State}},"mounts":{{json .Mounts}},"ports":{{json .NetworkSettings.Ports}}}'
          : '{"name":{{json .Name}},"labels":{{json .Labels}}}';
      const detail = JSON.parse((await docker(state, [type, 'inspect', identifier, '--format', format])).stdout);
      found.push({
        ...detail,
        name: detail.name.replace(/^\//, ''),
        type,
        service: detail.labels?.['com.docker.compose.service'],
      });
    }
  }
  return found;
}

async function verifyOwned(state, { ports = true } = {}) {
  const loaded = await readState(path.join(state.stateDir, 'state.json'));
  if (json(loaded) !== json(state)) throw new Error('State changed after it was loaded.');
  await validateFiles(state);
  const daemon = await dockerDaemon();
  if (daemon.id !== state.dockerId || daemon.endpoint !== state.dockerEndpoint)
    throw new Error('Docker daemon changed; refusing resource mutation.');
  const inventory = await resources(state);
  validateResources(state, daemon, inventory);
  if (ports) {
    const ingress = inventory.find((resource) => resource.service === 'ingress' && resource.state?.Running);
    const unownedPorts = [state.webPort, state.oidcPort].filter(
      (port) =>
        !ingress?.ports?.[`${port}/tcp`]?.some(
          (binding) => binding.HostIp === '127.0.0.1' && binding.HostPort === String(port),
        ),
    );
    await assertPortsAvailable(unownedPorts);
  }
  return inventory;
}

async function build(state, daemon) {
  if (!['x86_64', 'amd64', 'aarch64', 'arm64'].includes(daemon.architecture))
    throw new Error('Unsupported local Docker architecture.');
  const rid = ['aarch64', 'arm64'].includes(daemon.architecture) ? 'linux-arm64' : 'linux-x64';
  for (const [project, output] of [
    ['tools/trips/Travel.Trips.LocalStack/Travel.Trips.LocalStack.csproj', 'tool'],
    ['apps/Travel.Host/Travel.Host.csproj', 'host'],
  ]) {
    process.stdout.write(`Building Trips local ${output} binaries.\n`);
    await run(
      'dotnet',
      [
        'publish',
        project,
        '-c',
        'Release',
        '-r',
        rid,
        '--self-contained',
        'false',
        '--maxcpucount:1',
        '--disable-build-servers',
        '--artifacts-path',
        path.join(state.stateDir, 'build'),
        '-o',
        path.join(state.stateDir, output),
      ],
      { timeout: 1_200_000 },
    );
  }
  process.stdout.write('Building ordinary Angular client assets.\n');
  await run(
    process.execPath,
    [
      await resolveLocalCli('nx'),
      'build',
      'web',
      '--configuration=production',
      `--outputPath=${path.join(state.stateDir, 'web-build')}`,
      '--skip-nx-cache',
    ],
    { env: { NX_DAEMON: 'false' }, timeout: 1_200_000 },
  );
  const browser = path.join(state.stateDir, 'web-build/browser');
  if (!(await exists(path.join(browser, 'index.csr.html'))))
    throw new Error('Ordinary Angular CSR index.csr.html is missing.');
  await cp(browser, path.join(state.stateDir, 'web'), { recursive: true });
}

export async function resolveLocalCli(command, repoRoot = REPO_ROOT) {
  const packageName = { nx: 'nx', playwright: '@playwright/test' }[command];
  if (!packageName) throw new Error('Unsupported local package executable.');
  const packageDir = path.join(repoRoot, 'node_modules', packageName);
  const metadata = JSON.parse(await readFile(path.join(packageDir, 'package.json'), 'utf8'));
  const bin = typeof metadata.bin === 'string' ? metadata.bin : metadata.bin?.[command];
  if (typeof bin !== 'string' || !bin || path.isAbsolute(bin))
    throw new Error('Installed local package does not declare the required executable.');
  const executable = path.resolve(packageDir, bin);
  const relative = path.relative(packageDir, executable);
  if (relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative))
    throw new Error('Executable points outside the installed local package.');
  if (!(await lstat(executable)).isFile()) throw new Error('Installed local package executable is missing.');
  return executable;
}

async function ensureImages(state) {
  const ids = {};
  for (const [name, reference] of Object.entries(images)) {
    if (!/^[a-z0-9./-]+@sha256:[a-f0-9]{64}$/.test(reference)) throw new Error('Images must be pinned by digest.');
    let inspection = await docker(state, ['image', 'inspect', reference, '--format', '{{.Id}}'], {
      allowFailure: true,
    });
    if (inspection.code !== 0) {
      process.stdout.write(`Pulling pinned ${name} image.\n`);
      await docker(state, ['pull', reference]);
      inspection = await docker(state, ['image', 'inspect', reference, '--format', '{{.Id}}']);
    }
    if (!/^sha256:[a-f0-9]{64}$/.test(inspection.stdout.trim())) throw new Error('Image identity is invalid.');
    ids[name] = { reference, id: inspection.stdout.trim() };
  }
  await writeFile(path.join(state.stateDir, 'images.json'), json(ids));
  return ids;
}

async function tool(state, command, additional = [], options) {
  if (command === 'probe')
    return compose(
      state,
      ['exec', '-T', 'host', 'dotnet', '/tool/Travel.Trips.LocalStack.dll', command, '--state', '/state'],
      options,
    );
  return compose(
    state,
    ['run', '--rm', '--no-deps', '-T', 'tool', command, '--state', '/state', ...additional],
    options,
  );
}

export async function loopbackReadiness(state, fetchImpl = fetch) {
  async function status(url) {
    let response;
    try {
      response = await fetchImpl(url, { signal: AbortSignal.timeout(5000), redirect: 'error', credentials: 'omit' });
      return Number.isInteger(response.status) && response.status >= 100 && response.status <= 599
        ? { status: response.status, failure: null }
        : { status: null, failure: 'invalid-status' };
    } catch (error) {
      return {
        status: null,
        failure: error?.name === 'TimeoutError' || error?.name === 'AbortError' ? 'timeout' : 'request-failed',
      };
    } finally {
      await response?.body?.cancel().catch(() => {});
    }
  }
  return {
    discovery: await status(`http://127.0.0.1:${state.oidcPort}/realms/travel/.well-known/openid-configuration`),
    web: await status(`http://127.0.0.1:${state.webPort}/trips`),
  };
}

async function waitReady(state) {
  const deadline = Date.now() + 180_000;
  let latest = { ready: false };
  let loopback = null;
  while (Date.now() < deadline) {
    const result = await tool(state, 'probe', [], { allowFailure: true, timeout: 20_000 });
    latest = projectProbeResult(result);
    if (result.code === 0 && latest.ready) {
      loopback = await loopbackReadiness(state);
      if (Object.values(loopback).every((result) => result.status >= 200 && result.status < 300)) return latest;
    }
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  await mkdir(path.join(state.stateDir, 'evidence'), { recursive: true });
  await writeFile(
    path.join(state.stateDir, 'evidence/readiness.json'),
    json({ probe: latest, loopback, containers: safeContainerFacts(await resources(state)) }),
  );
  throw new Error('Owned Host/Keycloak/browser readiness did not pass within 180 seconds.');
}

export function projectProbeResult({ code, stdout }) {
  const result = { ready: false, exitCode: Number.isInteger(code) && code >= 0 && code <= 255 ? code : null };
  if (code !== 0 && code !== 1) return { ...result, failureClass: 'UnexpectedProbeExit' };
  let probe;
  try {
    if (typeof stdout !== 'string' || stdout.length > 65_536) throw new Error('Bounded probe JSON required.');
    probe = JSON.parse(stdout);
  } catch {
    return { ...result, failureClass: 'InvalidProbeJson' };
  }
  if (!probe || typeof probe !== 'object' || Array.isArray(probe))
    return { ...result, failureClass: 'InvalidProbeShape' };
  result.ready = code === 0 && probe.ready === true;
  const instant = (value) =>
    typeof value === 'string' &&
    value.length <= 40 &&
    /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|\+00:00)$/.test(value) &&
    Number.isFinite(Date.parse(value));
  if (instant(probe.wallUtc)) result.wallUtc = probe.wallUtc;
  if (probe.applicationAsOfUtc === null || instant(probe.applicationAsOfUtc))
    result.applicationAsOfUtc = probe.applicationAsOfUtc;
  result.checks = {};
  for (const name of ['ready', 'dependencies']) {
    const status = probe.checks?.[name];
    if (Number.isInteger(status) && status >= 100 && status <= 599) result.checks[name] = status;
  }
  result.dependencies = {};
  for (const name of ['trips', 'postgres', 'redis', 'nats', 'duffel']) {
    const status = probe.dependencies?.[name];
    if (['Healthy', 'Degraded', 'Unhealthy'].includes(status)) result.dependencies[name] = status;
  }
  if (typeof probe.ready !== 'boolean') result.failureClass = 'ProbeReportedFailure';
  return result;
}

function safeContainerFacts(inventory) {
  return inventory
    .filter((resource) => resource.type === 'container')
    .map((resource) => ({
      service: resource.service,
      containerId: resource.id,
      running: resource.state.Running,
      pid: resource.state.Pid,
      exitCode: resource.state.ExitCode,
      oomKilled: resource.state.OOMKilled,
      startedAt: resource.state.StartedAt,
      finishedAt: resource.state.FinishedAt,
    }));
}

function processFacts(inventory) {
  return CORE.map((service) => {
    const container = inventory.find((resource) => resource.type === 'container' && resource.service === service);
    if (!container) throw new Error(`Owned ${service} container is missing.`);
    return {
      service,
      containerId: container.id,
      pid: container.state.Pid,
      startedAt: container.state.StartedAt,
      running: container.state.Running,
      mounts: container.mounts
        .map((mount) => ({
          type: mount.Type,
          name: mount.Name ?? null,
          source: mount.Source,
          destination: mount.Destination,
          rw: mount.RW,
        }))
        .sort((a, b) => a.destination.localeCompare(b.destination)),
    };
  });
}

export async function restartStack(state) {
  const before = processFacts(await verifyOwned(state));
  if (before.some((process) => !process.running || process.pid <= 0))
    throw new Error('Complete restart proof requires all three services to be running first.');
  await compose(state, ['stop', '--timeout', '30', 'host', 'keycloak', 'postgres']);
  const stopped = processFacts(await verifyOwned(state));
  if (stopped.some((process) => process.running || process.pid !== 0))
    throw new Error('A required process did not exit; restart refused.');
  await compose(state, ['start', 'postgres', 'keycloak', 'host']);
  await waitReady(state);
  const after = processFacts(await verifyOwned(state));
  for (let i = 0; i < before.length; i++) {
    if (
      !after[i].running ||
      after[i].pid <= 0 ||
      after[i].pid === before[i].pid ||
      after[i].startedAt === before[i].startedAt ||
      before[i].containerId !== after[i].containerId ||
      json(before[i].mounts) !== json(after[i].mounts)
    )
      throw new Error('Restart did not preserve owned storage and replace every process identity.');
  }
  const receipt = { before, stopped, after, storagePreserved: true };
  await mkdir(path.join(state.stateDir, 'evidence'), { recursive: true });
  await writeFile(path.join(state.stateDir, 'evidence/restart.json'), json(receipt));
  return receipt;
}

export async function captureDatabaseSnapshot(state, tripId) {
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(tripId))
    throw new Error('Snapshot trip ID must be a GUID.');
  await verifyOwned(state);
  return JSON.parse((await tool(state, 'snapshot', ['--trip-id', tripId])).stdout.trim());
}

async function status(state) {
  const inventory = await verifyOwned(state);
  const running = inventory
    .filter((item) => item.type === 'container' && item.service !== 'tool')
    .map((item) => ({ service: item.service, running: item.state.Running }));
  return {
    id: state.id,
    mode: state.mode,
    statePath: path.join(state.stateDir, 'state.json'),
    webUrl: `http://127.0.0.1:${state.webPort}/trips`,
    oidcUrl: `http://127.0.0.1:${state.oidcPort}`,
    credentialsPath: path.join(state.stateDir, 'private/credentials.json'),
    wallUtc: new Date().toISOString(),
    applicationAsOfUtc: state.applicationAsOfUtc,
    services: running,
    boundary:
      state.mode === 'demo'
        ? 'Wall-clock demo: immutable Moscow data can expire; saved manual plans remain available. Keys have a finite 90-day lifetime.'
        : 'Fixed application/browser time; identity validation uses wall clock. Integration proof does not prove current data or route quality.',
  };
}

async function startFresh(mode, stateDir, profile = 'moscow') {
  await validateOwnedPath(stateDir);
  if (mode === 'demo' && path.basename(stateDir) !== 'demo')
    throw new Error('A fresh interactive demo must use the demo directory.');
  if (await exists(stateDir)) throw new Error('Fresh state directory already exists; refusing overwrite.');
  const daemon = await dockerDaemon();
  await assertPortsAvailable(mode === 'demo' ? [4205, 8185] : [4206, 8186]);
  const id = mode === 'demo' ? randomBytes(16).toString('hex') : path.basename(stateDir).slice(4);
  const state = {
    schemaVersion: 1,
    kind: 'travel-trips-local-stack',
    id,
    mode,
    repoRoot: REPO_ROOT,
    projectName: `travel-trips-${mode}-${id}`,
    webPort: mode === 'demo' ? 4205 : 4206,
    oidcPort: mode === 'demo' ? 8185 : 8186,
    applicationAsOfUtc: mode === 'demo' ? null : e2eProfile(profile).instant,
    ...(mode === 'e2e' ? { e2eProfile: profile } : {}),
    createdAtUtc: new Date().toISOString(),
    dockerEndpoint: daemon.endpoint,
    dockerId: daemon.id,
  };
  validateStateObject(state);
  const owned = { ...state, stateDir };
  // Check even improbable pre-existing resource name/label collisions before writing state.
  const collision = await resources(owned);
  if (collision.length) throw new Error('Fresh Docker resource identity already exists; refusing adoption.');
  await mkdir(stateDir, { recursive: true, mode: 0o700 });
  await writeFile(path.join(stateDir, 'state.json'), json(state), { flag: 'wx', mode: 0o600 });
  await writeStackConfiguration(owned);
  await build(owned, daemon);
  await run('dotnet', [
    path.join(stateDir, 'tool/Travel.Trips.LocalStack.dll'),
    'provision',
    '--state',
    stateDir,
    '--repo',
    REPO_ROOT,
  ]);
  await ensureImages(owned);
  await verifyOwned(owned);
  await compose(owned, ['up', '-d', '--wait', '--wait-timeout', '120', 'postgres']);
  await tool(owned, 'schema');
  await compose(owned, ['up', '-d']);
  await waitReady(owned);
  return owned;
}

async function up(statePath) {
  if (!(await exists(statePath))) return startFresh('demo', path.dirname(statePath));
  const state = await readState(statePath);
  const inventory = await verifyOwned(state);
  const containers = inventory.filter((item) => item.type === 'container');
  if (containers.some((item) => item.state.Running)) {
    if (
      SERVICES.filter((service) => service !== 'tool').every((service) =>
        containers.some((item) => item.service === service && item.state.Running),
      )
    )
      return state;
    throw new Error(
      'Owned stack is partially running. Stop it explicitly before resuming; mounted binaries were not changed.',
    );
  }
  await build(state, await dockerDaemon());
  await verifyOwned(state);
  await compose(state, ['up', '-d']);
  await waitReady(state);
  return state;
}

async function stop(state) {
  await verifyOwned(state);
  await compose(state, ['stop', '--timeout', '30']);
  const inventory = await verifyOwned(state);
  if (inventory.some((resource) => resource.state?.Running)) throw new Error('Owned services did not all stop.');
}

async function destroy(state) {
  const inventory = await verifyOwned(state);
  // Provision/build can fail before private files exist and before any Docker resource exists.
  if (inventory.length) {
    await compose(state, ['down', '--volumes', '--timeout', '30']);
    if ((await resources(state)).length) throw new Error('Owned cleanup left resources; private state was retained.');
  }
  await validateOwnedPath(state.stateDir, REPO_ROOT, true);
  await rm(state.stateDir, { recursive: true, force: false });
}

async function exportEvidence(state, receipt) {
  const destination = path.join(basePath(), 'evidence');
  await noLinks(destination);
  await mkdir(destination, { recursive: true, mode: 0o700 });
  const config = await privateConfiguration(state).catch(() => ({ secrets: [] }));
  const source = path.join(state.stateDir, 'evidence');
  if (await exists(source)) {
    await noLinks(source, true);
    for (const name of await readdir(source)) {
      if (!['browser-receipt.json', 'restart.json', 'readiness.json'].includes(name) && !/^[a-z0-9-]+\.png$/.test(name))
        continue;
      const file = path.join(source, name);
      const size = (await lstat(file)).size;
      if (size > 5 * 1024 * 1024) throw new Error('Evidence file exceeded bounded size.');
      if (name.endsWith('.json')) {
        const safe = redactOutput(await readFile(file, 'utf8'), config.secrets);
        JSON.parse(safe);
        await writeFile(path.join(destination, `${state.id}-${name}`), safe);
      } else await cp(file, path.join(destination, `${state.id}-${name}`));
    }
  }
  const output = path.join(destination, `${state.id}-receipt.json`);
  await writeFile(output, redactOutput(json(receipt), config.secrets));
  return output;
}

export async function requireCompletedBrowserProfile(state) {
  const refuse = () => new Error('A complete profile-bound browser completion receipt is required.');
  validateStateObject(state, state.repoRoot);
  if (state.mode !== 'e2e') throw refuse();
  await validateOwnedPath(state.stateDir, state.repoRoot);
  const file = path.join(state.stateDir, 'evidence', 'browser-receipt.json');
  let receipt;
  try {
    await noLinks(file);
    const info = await lstat(file);
    if (!info.isFile() || info.size <= 0 || info.size > 32768) throw refuse();
    receipt = JSON.parse(await readFile(file, 'utf8'));
  } catch {
    throw refuse();
  }
  const profile = state.e2eProfile ?? 'moscow';
  const stages = [
    'validate-owned-state',
    'anonymous-refusal',
    'owner-login',
    'create-foundation',
    'generate-without-write',
    'adopt-without-write',
    'edit-and-save',
    'list-and-reopen',
    ...(profile === 'saint-petersburg' ? ['historical-moscow-save'] : []),
    'complete-service-restart',
    'fresh-login-and-compare',
    'foreign-owner-refusals',
    'stale-revision-refusal',
    'private-evidence',
  ];
  const hash = (value) => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
  if (
    !receipt ||
    receipt.schemaVersion !== 1 ||
    receipt.stackId !== state.id ||
    receipt.status !== 'passed' ||
    receipt.e2eProfile !== profile ||
    receipt.applicationAsOfUtc !== state.applicationAsOfUtc ||
    receipt.fixedBrowserDateOnly !== true ||
    receipt.realIdentityClock !== true ||
    !Array.isArray(receipt.stages) ||
    receipt.stages.length !== stages.length ||
    !receipt.stages.every((stage, index) => stage?.name === stages[index] && stage.outcome === 'passed') ||
    receipt.candidateRequests !== 1 ||
    receipt.loginCount !== 3 ||
    receipt.externalRequests !== 0 ||
    receipt.restart?.serviceCount !== 3 ||
    receipt.restart.allExited !== true ||
    receipt.restart.storagePreserved !== true ||
    !hash(receipt.acceptedPlanSha256) ||
    !hash(receipt.protectedRowSha256) ||
    !hash(receipt.protectionKeyFingerprint) ||
    (profile === 'saint-petersburg'
      ? !hash(receipt.historicalMoscowPlanSha256) || !hash(receipt.historicalMoscowRowSha256)
      : receipt.historicalMoscowPlanSha256 !== null || receipt.historicalMoscowRowSha256 !== null) ||
    ['failureLine', 'errorClass', 'failureCategory', 'responseFailure', 'applicationFailure'].some(
      (key) => receipt[key] !== null,
    ) ||
    !Number.isFinite(Date.parse(receipt.startedAtUtc)) ||
    !Number.isFinite(Date.parse(receipt.completedAtUtc)) ||
    Date.parse(receipt.startedAtUtc) < Date.parse(state.createdAtUtc) ||
    Date.parse(receipt.completedAtUtc) < Date.parse(receipt.startedAtUtc)
  )
    throw refuse();
  return receipt;
}

async function e2e(profile = 'moscow') {
  const id = randomBytes(16).toString('hex');
  const stateDir = path.join(basePath(), `e2e-${id}`);
  const receipt = {
    schemaVersion: 1,
    id,
    startedAtUtc: new Date().toISOString(),
    applicationAsOfUtc: e2eProfile(profile).instant,
    e2eProfile: profile,
    stages: {},
    source: {},
  };
  let state;
  let failure;
  let receiptPath;
  try {
    // Import/discovery is resource-free. Catch decoder/module-loading regressions
    // before publishing binaries or provisioning fictional credentials and services.
    await run(
      process.execPath,
      [
        await resolveLocalCli('playwright'),
        'test',
        '-c',
        'tests/travel-e2e/playwright.trips-full-stack.config.ts',
        '--list',
      ],
      { timeout: 90_000 },
    );
    receipt.stages.discovery = 'passed';
    receipt.source.head = (await run('git', ['rev-parse', 'HEAD'])).stdout.trim();
    receipt.source.dirty = Boolean((await run('git', ['status', '--porcelain'])).stdout.trim());
    const sourceHash = createHash('sha256');
    sourceHash.update((await run('git', ['diff', '--binary', 'HEAD'])).stdoutBytes);
    const added = (await run('git', ['ls-files', '--others', '--exclude-standard', '-z'])).stdout
      .split('\0')
      .filter(Boolean)
      .sort();
    for (const file of added) {
      sourceHash.update(file);
      sourceHash.update(await readFile(path.join(REPO_ROOT, file)));
    }
    receipt.source.worktreeDeltaSha256 = sourceHash.digest('hex');
    receipt.source.fingerprintMethod = 'raw-git-diff-and-sorted-untracked-bytes-v2';
    state = await startFresh('e2e', stateDir, profile);
    receipt.stages.start = 'passed';
    receipt.images = JSON.parse(await readFile(path.join(stateDir, 'images.json'), 'utf8'));
    const config = await privateConfiguration(state);
    await mkdir(path.join(stateDir, 'evidence'), { recursive: true });
    await run(
      process.execPath,
      [await resolveLocalCli('playwright'), 'test', '-c', 'tests/travel-e2e/playwright.trips-full-stack.config.ts'],
      {
        env: {
          TRIPS_STACK_STATE: path.join(stateDir, 'state.json'),
          TRIPS_STACK_EVIDENCE: path.join(stateDir, 'evidence'),
        },
        secrets: config.secrets,
        timeout: 900_000,
      },
    );
    await requireCompletedBrowserProfile(state);
    receipt.stages.browser = 'passed';
  } catch (error) {
    failure = error;
    receipt.stages.verification = 'failed';
  } finally {
    if (!state && (await exists(path.join(stateDir, 'state.json'))))
      state = await readState(path.join(stateDir, 'state.json'));
    if (state) {
      receipt.finishedAtUtc = new Date().toISOString();
      try {
        receipt.containersBeforeCleanup = safeContainerFacts(await verifyOwned(state));
      } catch {
        receipt.stages.ownershipInspection = 'failed';
        failure ??= new Error('Owned resource inspection failed.');
      }
      try {
        receiptPath = await exportEvidence(state, receipt);
      } catch (error) {
        failure ??= error;
        receipt.stages.export = 'failed';
      }
      try {
        await destroy(state);
        receipt.stages.cleanup = 'passed';
      } catch (error) {
        failure ??= error;
        receipt.stages.cleanup = 'failed';
      }
      if (receiptPath) await writeFile(receiptPath, json(receipt));
    }
  }
  if (receiptPath) process.stdout.write(`Sanitized receipt: ${receiptPath}\n`);
  if (failure) throw failure;
  return receipt;
}

export async function runE2eProfiles(runProfile = e2e) {
  const profiles = { moscow: { status: 'unrun' }, 'saint-petersburg': { status: 'unrun' } };
  for (const profile of ['moscow', 'saint-petersburg']) {
    try {
      profiles[profile] = await runProfile(profile);
      const stages = profiles[profile]?.stages;
      if (
        stages?.browser !== 'passed' ||
        stages?.cleanup !== 'passed' ||
        Object.values(stages).some((value) => value !== 'passed')
      )
        throw new Error('Incomplete profile.');
    } catch {
      if (profiles[profile].status === 'unrun') profiles[profile] = { status: 'failed' };
      const error = new Error('Both city profiles and owned cleanup must pass; aggregate failed.');
      error.aggregate = { schemaVersion: 1, status: 'failed', profiles };
      throw error;
    }
  }
  return { schemaVersion: 1, status: 'passed', profiles };
}

async function main(args) {
  const command = args[0];
  if (!['up', 'status', 'restart', 'stop', 'destroy', 'e2e', 'e2e-all'].includes(command))
    throw new Error(
      'Usage: node tools/trips/local-stack.mjs up|status|restart|stop|destroy --yes|e2e [--state ABSOLUTE_STATE_JSON]',
    );
  const rest = [...args.slice(1)];
  const profileIndex = rest.indexOf('--profile');
  let profile = 'moscow';
  if (profileIndex >= 0) {
    if (command !== 'e2e' || typeof rest[profileIndex + 1] !== 'string')
      throw new Error('Profile is only allowed for e2e.');
    profile = rest[profileIndex + 1];
    e2eProfile(profile);
    rest.splice(profileIndex, 2);
  }
  let statePath = path.join(basePath(), 'demo/state.json');
  const index = rest.indexOf('--state');
  if (index >= 0) {
    statePath = rest[index + 1];
    rest.splice(index, 2);
  }
  const yes = rest.includes('--yes');
  if (
    rest.some((item) => item !== '--yes') ||
    (yes && command !== 'destroy') ||
    (command === 'destroy' && !yes) ||
    (['e2e', 'e2e-all'].includes(command) && index >= 0)
  )
    throw new Error('Invalid arguments; destroy requires --yes and e2e always creates its own fresh state.');
  if (typeof statePath !== 'string' || path.basename(statePath) !== 'state.json')
    throw new Error('Expected an absolute state.json path.');
  await validateOwnedPath(path.dirname(statePath));
  if (command === 'e2e' || command === 'e2e-all') {
    if (command === 'e2e-all') {
      const directory = path.join(basePath(), 'evidence');
      await noLinks(directory);
      await mkdir(directory, { recursive: true });
      const output = path.join(directory, `${randomBytes(16).toString('hex')}-both-city-aggregate.json`);
      let result;
      try {
        result = await runE2eProfiles();
      } catch (error) {
        result = error.aggregate;
        throw error;
      } finally {
        if (result) await writeFile(output, json(result), { flag: 'wx' });
      }
    } else await e2e(profile);
    return;
  }
  if (command === 'up') {
    process.stdout.write(json(await status(await up(statePath))));
    return;
  }
  const state = await readState(statePath);
  if (command === 'status') process.stdout.write(json(await status(state)));
  else if (command === 'restart') {
    await restartStack(state);
    process.stdout.write(json(await status(state)));
  } else if (command === 'stop') {
    await stop(state);
    process.stdout.write('Owned stack stopped. Database, users and keys preserved.\n');
  } else {
    await destroy(state);
    process.stdout.write('Owned stack resources and private state removed.\n');
  }
}

if (process.argv[1] && equalPath(path.resolve(process.argv[1]), fileURLToPath(import.meta.url))) {
  main(process.argv.slice(2)).catch((error) => {
    process.stderr.write(`${redactOutput(error.message)}\n`);
    process.exitCode = 1;
  });
}
