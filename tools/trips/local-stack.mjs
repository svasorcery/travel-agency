import { AsyncLocalStorage } from 'node:async_hooks';
import { spawn } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { chmod, cp, lstat, mkdir, readdir, readFile, realpath, rm, rmdir, writeFile } from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import images from './images.json' with { type: 'json' };

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const OWNER = 'travel.trips.local-stack-id';
const PROJECT = 'com.docker.compose.project';
const MANUAL_STAYS_PREVIOUS_HEAD = '4bd31d844323652038f0264191c277aaaf5b2ae3';
const MANUAL_STAYS_ROLLBACK_RECEIPT = 'manual-stays-rollback-receipt.json';
const FIXED_TIME = '2026-10-24T12:00:00Z';
const orderLinkTrip = Object.freeze({
  instant: FIXED_TIME,
  destination: 'saint-petersburg',
  origin: 'kaliningrad',
  dates: ['2027-06-13', '2027-06-14', '2027-06-15'],
});
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
  'flight-links': orderLinkTrip,
  'hotel-links': orderLinkTrip,
  'spb-option': Object.freeze({ instant: null }),
  'saved-spb-option': Object.freeze({ instant: null }),
});
const CI_PROFILES = ['moscow', 'saint-petersburg', 'flight-links'];
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
    Object.keys(state).some(
      (key) => ![...stateKeys, 'stateDir', 'e2eProfile', 'smallSubnets', 'optionCatalogue'].includes(key),
    ) ||
    stateKeys.some((key) => !(key in state))
  )
    throw new Error('Unexpected owned state fields.');
  const demo = state.mode === 'demo';
  const retained = state.mode === 'saved-spb-option';
  if (
    (retained && state.e2eProfile !== undefined && state.e2eProfile !== 'saved-spb-option') ||
    (!retained && state.e2eProfile === 'saved-spb-option')
  )
    throw new Error('Invalid saved profile identity.');
  if (
    retained
      ? state.optionCatalogue !== 'B'
      : state.e2eProfile === 'spb-option'
        ? !['A', 'B'].includes(state.optionCatalogue)
        : 'optionCatalogue' in state
  )
    throw new Error('The option catalogue must belong to the named preview profile.');
  if ('smallSubnets' in state && state.smallSubnets !== true) throw new Error('Invalid small-subnet selection.');
  if (demo && Object.hasOwn(state, 'e2eProfile')) throw new Error('Interactive demo cannot have a fixed E2E profile.');
  if (
    state.schemaVersion !== 1 ||
    state.kind !== 'travel-trips-local-stack' ||
    !/^[a-f0-9]{32}$/.test(state.id) ||
    !['demo', 'e2e', 'saved-spb-option'].includes(state.mode) ||
    typeof state.repoRoot !== 'string' ||
    !path.isAbsolute(state.repoRoot) ||
    !equalPath(state.repoRoot, repoRoot) ||
    state.projectName !== `travel-trips-${state.mode}-${state.id}` ||
    state.webPort !== (retained ? 4207 : demo ? 4205 : 4206) ||
    state.oidcPort !== (retained ? 8187 : demo ? 8185 : 8186) ||
    state.applicationAsOfUtc !== (demo || retained ? null : e2eProfile(state.e2eProfile).instant) ||
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
  if (relative.includes(path.sep) || !/^(demo|saved-spb-option|e2e-[a-f0-9]{32})$/.test(relative))
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
  if (
    path.basename(stateDir) !==
    (state.mode === 'demo' || (state.mode === 'saved-spb-option' && !state.e2eProfile) ? state.mode : `e2e-${state.id}`)
  )
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
  const hotelLinks = state.e2eProfile === 'hotel-links';
  const retained = state.mode === 'saved-spb-option';
  const option = state.e2eProfile === 'spb-option' || retained;
  const environment = hotelLinks || option ? 'Testing' : 'Development';
  return {
    ASPNETCORE_ENVIRONMENT: environment,
    DOTNET_ENVIRONMENT: environment,
    ASPNETCORE_URLS: 'http://0.0.0.0:5099',
    HealthEndpoints__InternalPort: '5098',
    ConnectionStrings__travel: `Host=postgres;Port=5432;Database=travel;Username=postgres;Password=${dbPasswordVariable}`,
    ConnectionStrings__redis: 'redis:6379',
    ConnectionStrings__nats: 'nats://nats:4222',
    Keycloak__Authority: `http://127.0.0.1:${state.oidcPort}/realms/travel`,
    Keycloak__Audience: 'travel-web',
    Trips__Enabled: 'true',
    Trips__FlightLinks__Enabled: String(hotelLinks || state.e2eProfile === 'flight-links'),
    Trips__Itinerary__Enabled: String(!hotelLinks),
    ...(option && {
      Trips__OptionPreview__Enabled: 'true',
      Trips__OptionPreview__Profile: 'spb-option',
      Trips__OptionPreview__Catalogue: state.optionCatalogue,
    }),
    ...(retained && { Trips__SavedOptions__Enabled: 'true' }),
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
    Hotels__Enabled: String(hotelLinks),
    ...(hotelLinks && {
      Trips__HotelLinks__Enabled: 'true',
      Hotels__ApiKey: 'duffel_test_fictional_local_stack',
      Hotels__Aggregation__Fictional: 'true',
      Hotels__BookingEnabled: 'false',
      Hotels__ReceiverEnabled: 'false',
      Hotels__PiiProtection__KeyRingPath: '/state/private/keys/hotels/ring',
      Hotels__PiiProtection__ActiveCertificatePath: '/state/private/keys/hotels/certificate.pfx',
      Hotels__PiiProtection__ActiveCertificatePassword: certificatePasswordVariable,
    }),
    Rail__Enabled: 'false',
    Support__Classification__Enabled: 'false',
    Logging__LogLevel__Default: 'Warning',
  };
}

function ownedMount(state, source, target) {
  return {
    type: 'bind',
    source: path.join(state.stateDir, source).replaceAll('\\', '/'),
    target,
    read_only: true,
    bind: { create_host_path: false },
  };
}

export function buildCompose(state) {
  const labels = { [OWNER]: state.id };
  const network = state.smallSubnets ? { labels, ipam: { config: [{ subnet: '0.0.0.0/24' }] } } : { labels };
  const mount = (source, target) => ownedMount(state, source, target);
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
    networks: { isolated: { internal: true, ...network }, ingress: network },
    volumes: { 'postgres-data': { labels } },
  };
}

function expectedFiles(state) {
  const files = {
    'compose.json': json(buildCompose(state)),
    Caddyfile: `{\n  admin off\n  auto_https off\n  log {\n    level ERROR\n  }\n}\nhttp://:${state.oidcPort} {\n  reverse_proxy keycloak:${state.oidcPort}\n}\nhttp://:${state.webPort} {\n  handle /api/* {\n    reverse_proxy keycloak:5099\n  }\n  handle {\n    root * /web\n    try_files {path} /index.csr.html\n    file_server\n  }\n}\n`,
    'init-db.sql': `CREATE DATABASE keycloak;\nCREATE TABLE public.trips_demo_owner (id text PRIMARY KEY);\nINSERT INTO public.trips_demo_owner (id) VALUES ('${state.id}');\n`,
  };
  if (state.mode !== 'demo')
    files['compose.e2e.json'] = json({
      services: {
        host: {
          command: ['dotnet', '/tool/Travel.Trips.LocalStack.dll', 'serve-e2e', '--state', '/state', '--host', '/host'],
        },
      },
    });
  if (state.mode === 'saved-spb-option') {
    for (const mode of savedModes(state).filter((x) => x !== 'B')) {
      files[`compose.saved-${mode}.json`] = json({ services: { host: { environment: savedSettings(mode) } } });
    }
  }
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

const optionSignal = new AsyncLocalStorage();

export async function run(
  command,
  args,
  { env = {}, secrets = [], timeout = 600_000, allowFailure = false, interruptible = false } = {},
) {
  const signal = optionSignal.getStore();
  signal?.throwIfAborted();
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
    // Finish a Docker or provisioning mutation before cleanup inspects ownership. A browser
    // verification may wait for input or a response, so terminate it and wait for its exit.
    const interrupt = () => child.kill();
    if (interruptible) signal?.addEventListener('abort', interrupt, { once: true });
    child.once('error', () => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', interrupt);
      reject(new Error(`Could not start ${path.basename(command)}; check local prerequisites.`));
    });
    child.once('close', (code, killSignal) => {
      clearTimeout(timer);
      signal?.removeEventListener('abort', interrupt);
      if (signal?.aborted) {
        reject(signal.reason);
        return;
      }
      const stdoutBytes = Buffer.concat(stdoutChunks);
      const stdout = stdoutBytes.toString('utf8');
      const stderr = Buffer.concat(stderrChunks).toString('utf8');
      if (overflow || killSignal || (code !== 0 && !allowFailure))
        reject(
          new Error(
            `${path.basename(command)} failed (${overflow ? 'output limit' : killSignal || code}). ${formatChildFailure(stdout, stderr, secrets)}`,
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
  return { endpoint, id: info.ID, architecture: info.Architecture, serverVersion: info.ServerVersion };
}

const docker = (state, args, options) => run('docker', ['--host', state.dockerEndpoint, ...args], options);
async function compose(state, args, options = {}, rollbackFile) {
  const config = await privateConfiguration(state);
  const files = ['-f', path.join(state.stateDir, 'compose.json')];
  if (state.mode !== 'demo') files.push('-f', path.join(state.stateDir, 'compose.e2e.json'));
  if (rollbackFile) files.push('-f', rollbackFile);
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
  if (['probe', 'flight-links-seed', 'flight-links-close'].includes(command))
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

// Only a new Keycloak container rebuilds its configuration and imports the realm.
// It can finish later if Host and the static UI were ready in the ordinary window.
export function startupDecision(elapsed, coldIdentity, appReadyInTime, appReady, identityReady) {
  if (elapsed >= 180_000 && !(coldIdentity && elapsed < 360_000 && appReadyInTime && appReady)) return 'expired';
  return appReady && identityReady ? 'ready' : 'wait';
}

async function waitReady(state, coldIdentity = false) {
  const start = performance.now();
  let appReadyInTime = false;
  let latest = { ready: false };
  let loopback = null;
  while (true) {
    const result = await tool(state, 'probe', [], { allowFailure: true, timeout: 20_000 });
    latest = projectProbeResult(result);
    loopback = latest.ready ? await loopbackReadiness(state) : null;
    const elapsed = performance.now() - start;
    const appReady = latest.ready && loopback.web.status >= 200 && loopback.web.status < 300;
    if (elapsed < 180_000) appReadyInTime = appReady;
    const identityReady = loopback?.discovery.status >= 200 && loopback.discovery.status < 300;
    const decision = startupDecision(elapsed, coldIdentity, appReadyInTime, appReady, identityReady);
    if (decision === 'ready') return latest;
    if (decision === 'expired') break;
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
  await mkdir(path.join(state.stateDir, 'evidence'), { recursive: true });
  await writeFile(
    path.join(state.stateDir, 'evidence/readiness.json'),
    json({
      probe: latest,
      loopback,
      coldIdentity,
      appReadyInTime,
      elapsedMs: Math.round(performance.now() - start),
      containers: safeContainerFacts(await resources(state)),
    }),
  );
  throw new Error('Owned Host/Keycloak/browser readiness did not pass within the startup budget.');
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

export function manualStaysRollbackDecision({ code, stdout }) {
  const unavailable = { outcome: 'unavailable', taggedPlans: null };
  if (typeof stdout !== 'string' || stdout.length > 128) return unavailable;
  const result = stdout.trim();
  if (code === 1 && result === '{"probeFailure":"permission-denied"}')
    return { ...unavailable, probeFailure: 'permission-denied' };
  const aggregate = /^\{"taggedPlans":(0|[1-9][0-9]*)\}$/.exec(result);
  if (code !== 0 || !aggregate) return unavailable;
  const taggedPlans = Number(aggregate[1]);
  if (!Number.isSafeInteger(taggedPlans)) return unavailable;
  return { outcome: taggedPlans === 0 ? 'allowed' : 'blocked', taggedPlans };
}

const ROLLBACK_SERVICES = ['host', 'ingress'];

async function rollbackProcesses(state, running, expectedBackends, buildDirectory) {
  const inventory = await verifyOwned(state);
  for (const service of [...CORE, 'ingress']) {
    if (inventory.filter((item) => item.type === 'container' && item.service === service).length !== 1)
      throw new Error(`Rollback rehearsal requires exactly one owned ${service} container.`);
  }
  const backends = processFacts(inventory).filter((item) => item.service !== 'host');
  if (backends.some((item) => !item.running || item.pid <= 0))
    throw new Error('Rollback rehearsal requires a running owned database and identity service.');
  if (expectedBackends && json(backends) !== json(expectedBackends))
    throw new Error('Database or identity process or storage changed during the rollback rehearsal.');
  const services = ROLLBACK_SERVICES.map((service) => inventory.find((item) => item.service === service));
  if (
    running !== undefined &&
    services.some((item) =>
      running
        ? !item.state.Running || item.state.Pid <= 0
        : item.state.Running || item.state.Pid !== 0 || item.state.Status !== 'exited',
    )
  )
    throw new Error(running ? 'Owned Host or ingress did not start.' : 'Owned Host or ingress did not exit.');
  if (running) {
    for (const [service, artifact] of [
      ['host', 'host'],
      ['host', 'tool'],
      ['ingress', 'web'],
    ]) {
      const suffix = ['TestResults', 'trips-local-stack', path.basename(state.stateDir), buildDirectory, artifact]
        .filter(Boolean)
        .join('/');
      const mount = services
        .find((item) => item.service === service)
        .mounts.find((item) => item.Destination === `/${artifact}`);
      const sourceSuffix = mount?.Source?.replaceAll('\\', '/').slice(-suffix.length - 1);
      if (mount?.Type !== 'bind' || mount.RW !== false || !sourceSuffix || !equalPath(sourceSuffix, `/${suffix}`))
        throw new Error(`Owned ${service} is serving an unexpected ${artifact} build.`);
    }
  }
  return { backends, serviceIds: services.map((item) => item.id) };
}

async function rollbackArtifacts(state) {
  const source = path.join(state.repoRoot, 'TestResults', 'trips-manual-stays-rollback');
  const marker = path.join(source, 'old-source.json');
  await noLinks(marker);
  const info = await lstat(marker);
  if (!info.isFile() || info.size <= 0 || info.size > 256)
    throw new Error('An exact prior build source receipt is required.');
  if (JSON.parse(await readFile(marker, 'utf8')).sourceHead !== MANUAL_STAYS_PREVIOUS_HEAD)
    throw new Error('Prior build source does not match the approved rollback base.');
  const directory = `manual-stays-rollback-${randomBytes(8).toString('hex')}`;
  const staged = path.join(state.stateDir, directory);
  await mkdir(staged);
  const previous = { sourceHead: MANUAL_STAYS_PREVIOUS_HEAD };
  const current = {};
  for (const [kind, from, entry] of [
    ['host', 'old-host', 'Travel.Host.dll'],
    ['tool', 'old-tool', 'Travel.Trips.LocalStack.dll'],
    ['web', 'old-web-build/browser', 'index.csr.html'],
  ]) {
    const prior = path.join(source, from);
    await noLinks(prior, true);
    if (!(await lstat(path.join(prior, entry))).isFile()) throw new Error(`Prior ${kind} build is incomplete.`);
    const destination = path.join(staged, kind);
    await cp(prior, destination, { recursive: true, force: false, errorOnExist: true });
    previous[`${kind}Sha256`] = await buildFingerprint(destination);
    current[`${kind}Sha256`] = await buildFingerprint(path.join(state.stateDir, kind));
  }
  const contents = json({
    services: {
      host: {
        volumes: [ownedMount(state, `${directory}/host`, '/host'), ownedMount(state, `${directory}/tool`, '/tool')],
      },
      ingress: { volumes: [ownedMount(state, `${directory}/web`, '/web')] },
    },
  });
  const file = path.join(staged, 'compose.json');
  await writeFile(file, contents, { flag: 'wx', mode: 0o600 });
  return { directory, file, contents, previous, current };
}

async function buildFingerprint(directory) {
  const hash = createHash('sha256');
  async function visit(relative) {
    const fullPath = path.join(directory, relative);
    for (const name of (await readdir(fullPath)).sort()) {
      const file = relative ? `${relative}/${name}` : name;
      const child = path.join(directory, file);
      const stat = await lstat(child);
      if (stat.isDirectory()) await visit(file);
      else if (stat.isFile()) {
        hash
          .update(file)
          .update('\0')
          .update(
            createHash('sha256')
              .update(await readFile(child))
              .digest(),
          );
      } else throw new Error('Rollback build contains an unsupported file type.');
    }
  }
  await visit('');
  return hash.digest('hex');
}

export async function optionBuildFingerprints(state) {
  if (
    !(state.mode === 'e2e' && state.e2eProfile === 'spb-option') &&
    !(state.mode === 'saved-spb-option' && state.e2eProfile === state.mode)
  )
    throw new Error('Expected the owned option verification profile.');
  await validateOwnedPath(state.stateDir, REPO_ROOT);
  const assemblies = {};
  for (const directory of ['host', 'tool']) {
    const names = [
      'Travel.Host',
      'Travel.Modules.Trips.Core',
      'Travel.Modules.Trips.Application',
      'Travel.Modules.Trips.Infrastructure',
      'Travel.Modules.Trips.Api',
    ];
    if (directory === 'tool') names.push('Travel.Trips.LocalStack');
    for (const name of names)
      assemblies[`${directory}/${name}.dll`] = createHash('sha256')
        .update(await readFile(path.join(state.stateDir, directory, `${name}.dll`)))
        .digest('hex');
  }
  return { assemblies, webSha256: await buildFingerprint(path.join(state.stateDir, 'web')) };
}

/**
 * @typedef {{ hostSha256: string, toolSha256: string, webSha256: string }} RollbackBuildFingerprints
 * @typedef {{ outcome: 'unavailable', taggedPlans: null, probeFailure?: 'permission-denied' } |
 *           { outcome: 'blocked', taggedPlans: number } |
 *           { outcome: 'allowed', taggedPlans: 0, previous: RollbackBuildFingerprints & { sourceHead: string },
 *             current: RollbackBuildFingerprints }} ManualStaysRollbackDecision
 */

/**
 * @template T
 * @param {Parameters<typeof verifyOwned>[0]} state
 * @param {{ unavailable?: boolean }} options
 * @param {(decision: ManualStaysRollbackDecision) => T | Promise<T>} verify
 * @returns {Promise<T>}
 */
export async function rehearseManualStaysRollback(state, { unavailable = false }, verify) {
  if (
    process.env.TRIPS_STACK_MANUAL_STAYS_ROLLBACK !== '1' ||
    state.mode !== 'e2e' ||
    state.e2eProfile !== 'hotel-links' ||
    typeof unavailable !== 'boolean' ||
    typeof verify !== 'function'
  )
    throw new Error('An explicitly enabled owned hotel-links E2E rollback rehearsal is required.');
  await validateOwnedPath(state.stateDir);
  const lock = path.join(state.stateDir, 'manual-stays-rollback.lock');
  await mkdir(lock);
  try {
    const { backends } = await rollbackProcesses(state, true);
    let restore = true;
    let switched = false;
    try {
      await compose(state, ['stop', '--timeout', '30', ...ROLLBACK_SERVICES]);
      await rollbackProcesses(state, false, backends);
      const command = unavailable ? 'manual-stays-rollback-count-unavailable' : 'manual-stays-rollback-count';
      const probe = await tool(state, command, [], { allowFailure: true, timeout: 30_000 }).catch(() => ({}));
      const decision = manualStaysRollbackDecision(probe);
      if (unavailable && decision.probeFailure !== 'permission-denied')
        throw new Error('The restricted PostgreSQL role did not prove an unavailable rollback aggregate.');
      if (decision.outcome !== 'allowed') {
        await compose(state, ['start', ...ROLLBACK_SERVICES]);
        await waitReady(state);
        await rollbackProcesses(state, true, backends);
        restore = false;
        return await verify(decision);
      }

      const { directory, file, contents, previous, current } = await rollbackArtifacts(state);
      const stopped = await rollbackProcesses(state, false, backends);
      if ((await readFile(file, 'utf8')) !== contents) throw new Error('Prior build overlay changed.');
      switched = true;
      await compose(
        state,
        ['up', '-d', '--no-deps', '--force-recreate', '--pull', 'never', ...ROLLBACK_SERVICES],
        {},
        file,
      );
      await waitReady(state);
      const prior = await rollbackProcesses(state, true, backends, directory);
      if (prior.serviceIds.some((id, index) => id === stopped.serviceIds[index]))
        throw new Error('Prior build did not replace both serving containers.');
      return await verify({ ...decision, previous, current });
    } finally {
      if (restore) {
        await rollbackProcesses(state, undefined, backends);
        if (switched) {
          await compose(state, ['stop', '--timeout', '30', ...ROLLBACK_SERVICES]);
          await rollbackProcesses(state, false, backends);
          await compose(state, ['up', '-d', '--no-deps', '--force-recreate', '--pull', 'never', ...ROLLBACK_SERVICES]);
        } else await compose(state, ['start', ...ROLLBACK_SERVICES]);
        await waitReady(state);
        await rollbackProcesses(state, true, backends);
      }
    }
  } finally {
    await rmdir(lock);
  }
}

export async function captureDatabaseSnapshot(state, tripId) {
  if (!/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(tripId))
    throw new Error('Snapshot trip ID must be a GUID.');
  await verifyOwned(state);
  return JSON.parse((await tool(state, 'snapshot', ['--trip-id', tripId])).stdout.trim());
}

export async function optionHttpObservations(state) {
  if (state.e2eProfile !== 'spb-option' || state.mode !== 'e2e') throw new Error('Expected the owned option profile.');
  const inventory = await verifyOwned(state);
  const host = inventory.find((entry) => entry.type === 'container' && entry.service === 'host');
  if (!host?.state.Running) throw new Error('Owned option Host is unavailable.');
  const logs = await docker(state, ['logs', '--tail', '2000', host.name]);
  return [...logs.stdout.matchAll(/^TRIPS_LOCAL_OPTION_HTTP=(\d+)\r?$/gm)].map((match) => Number(match[1]));
}

export async function savedOptionFixture(state) {
  if (state.mode !== 'saved-spb-option' || state.e2eProfile !== 'saved-spb-option')
    throw new Error('Disposable saved-profile fixture required.');
  const inventory = await verifyOwned(state);
  const mode = await savedEffectiveMode(state, inventory);
  const host = inventory.find((item) => item.service === 'host' && item.state.Running);
  if (!host || !mode) throw new Error('Owned saved Host is unavailable.');
  // Target the immutable full container ID from the verified inventory, including across a later recreation.
  async function command(operation, tripId) {
    if (!['saved-facts', 'saved-arm', 'saved-held', 'saved-release'].includes(operation))
      throw new Error('Invalid saved fixture command.');
    if (tripId && (operation !== 'saved-facts' || !/^[a-f0-9-]{36}$/i.test(tripId)))
      throw new Error('Invalid snapshot identity.');
    const reply = await docker(state, [
      'exec',
      host.id,
      'dotnet',
      '/tool/Travel.Trips.LocalStack.dll',
      operation,
      '--state',
      '/state',
      ...(tripId ? ['--trip-id', tripId] : []),
    ]);
    return JSON.parse(reply.stdout);
  }
  async function observations() {
    const logs = await docker(state, ['logs', '--tail', '4000', host.id]);
    return [
      ...logs.stdout.matchAll(/^TRIPS_LOCAL_SAVED_REQUEST=(preview|save|historical),(\d{3}),(\d+),(\d+),(\d+)\r?$/gm),
    ].map(([, kind, status, http, previews, packages]) => ({
      kind,
      status: Number(status),
      http: Number(http),
      previews: Number(previews),
      packages: Number(packages),
    }));
  }
  return { mode, command, observations };
}

export async function switchSavedVerification(state, target, checkRefusals = false) {
  const before = await verifyOwned(state);
  if (state.mode !== 'saved-spb-option' || state.e2eProfile !== state.mode || !savedModes(state).includes(target))
    throw new Error('Disposable saved-profile transition required.');
  const statePath = path.join(state.stateDir, 'state.json');
  const original = await savedEffectiveMode(state, before);
  if (!original || !processFacts(before).every((p) => p.running)) throw new Error('Saved Host was not active.');
  const volume = before.find((x) => x.type === 'volume').name;
  const volumeId = async () =>
    (await docker(state, ['volume', 'inspect', volume, '--format', '{{.Name}} {{.CreatedAt}}'])).stdout.trim();
  const originalVolume = await volumeId();
  await stop(state);
  const stopped = await verifyOwned(state);
  const allExited = processFacts(stopped).every((p) => !p.running && p.pid === 0);
  if (!allExited) throw new Error('Saved profile did not stop completely.');
  if (checkRefusals) {
    // Simulate an interrupted recreation only on this disposable profile. Remove by the
    // verified full ID of its exited Host, leaving the database, keys and neighbours intact.
    const host = stopped.find((item) => item.type === 'container' && item.service === 'host');
    if (host?.state.Status !== 'exited' || !/^[a-f0-9]{64}$/.test(host.id))
      throw new Error('Expected the disposable stopped Host before simulating interruption.');
    const neighbours = (inventory) =>
      safeContainerFacts(inventory)
        .filter((item) => item.containerId !== host.id)
        .sort((a, b) => a.containerId.localeCompare(b.containerId));
    const originalNeighbours = neighbours(stopped);
    await docker(state, ['container', 'rm', host.id]);
    await expectSavedRefusal(() => up(statePath), /Previous saved Host mode is unknown/);
    const interrupted = await verifyOwned(state);
    if (
      interrupted.some((item) => item.service === 'host') ||
      json(neighbours(interrupted)) !== json(originalNeighbours)
    )
      throw new Error('Plain resume changed the interrupted workspace.');

    // A separately labelled empty test volume is foreign to this Compose owner.
    // The fixture can remove only the random sentinel it just created after checking its own marker.
    const probeId = randomBytes(16).toString('hex');
    const sentinel = state.projectName + '-ownership-probe-' + probeId;
    const label = 'travel.trips.local-refusal-probe';
    await docker(state, ['volume', 'create', '--name', sentinel, '--label', label + '=' + probeId]);
    const failures = [];
    try {
      await expectSavedRefusal(() => up(statePath, false, target), /Foreign Docker resource/);
    } catch (error) {
      failures.push(error);
    }
    try {
      const owned = (
        await docker(state, ['volume', 'inspect', sentinel, '--format', `{{.Name}} {{index .Labels "${label}"}}`])
      ).stdout.trim();
      if (owned !== sentinel + ' ' + probeId) throw new Error('Refusal fixture no longer owns its test volume.');
      await docker(state, ['volume', 'rm', sentinel]);
    } catch (error) {
      failures.push(error);
    }
    if (failures.length) throw new AggregateError(failures, 'Foreign-resource refusal or sentinel cleanup failed.');
    for (const [file, refusal] of [
      ['compose.json', /configuration .* changed/],
      ['saved-storage.json', /storage and protection keys do not match/],
    ]) {
      const filePath = path.join(state.stateDir, file);
      const trusted = await readFile(filePath);
      try {
        await writeFile(filePath, Buffer.concat([trusted, Buffer.from(' ')]));
        await expectSavedRefusal(() => up(statePath, false, target), refusal);
      } finally {
        await writeFile(filePath, trusted);
      }
    }
    const listener = net.createServer();
    try {
      await new Promise((ok, no) => {
        listener.once('error', no);
        listener.listen(state.webPort, '127.0.0.1', ok);
      });
      await expectSavedRefusal(() => up(statePath, false, target), /occupied or unavailable/);
    } finally {
      await new Promise((ok) => listener.close(ok));
    }
    const refused = await verifyOwned(state);
    if (refused.some((item) => item.service === 'host') || json(neighbours(refused)) !== json(originalNeighbours))
      throw new Error('Refused explicit recovery changed the interrupted workspace.');
  }
  await up(statePath, false, target);
  if (checkRefusals) {
    // A normal stop leaves a verifiable Host. Plain resume must reuse its effective flags.
    await stop(state);
    await up(statePath);
  }
  const after = await verifyOwned(state);
  const effective = await savedEffectiveMode(state, after);
  if (effective !== target || !processFacts(after).every((p) => p.running) || (await volumeId()) !== originalVolume)
    throw new Error('Saved transition identity, volume or effective settings changed unexpectedly.');
  return {
    stackId: state.id,
    original,
    effective,
    allExited,
    refusedTampering: checkRefusals,
    refusedBusyPort: checkRefusals,
    refusedForeignOwner: checkRefusals,
    refusedUnknownMode: checkRefusals,
    refusedStorageMismatch: checkRefusals,
    resumedVerifiedMode: checkRefusals,
    originalSettings: savedSettings(original),
    effectiveSettings: savedSettings(target),
    volumePreserved: true,
    services: processFacts(after).map((p) => p.service),
  };
}

async function expectSavedRefusal(action, pattern) {
  try {
    await action();
  } catch (error) {
    if (pattern.test(error.message)) return;
    throw error;
  }
  throw new Error('Expected the saved profile to refuse the unsafe transition.');
}

export async function flightLinksFixture(state, action, tripId) {
  if (state.mode !== 'e2e' || !['flight-links', 'hotel-links'].includes(state.e2eProfile))
    throw new Error('Owned flight-links or hotel-links E2E profile required.');
  if (!['seed', 'close', 'facts'].includes(action)) throw new Error('Unknown flight-links fixture operation.');
  if (state.e2eProfile === 'hotel-links' && action === 'close') throw new Error('Flight close requires flight-links.');
  if (
    tripId !== undefined &&
    (action !== 'facts' || typeof tripId !== 'string' || !/^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(tripId))
  )
    throw new Error('Expected a trip GUID.');
  await verifyOwned(state);
  if (action !== 'facts') await waitReady(state);
  const extra = tripId !== undefined ? ['--trip-id', tripId] : [];
  return JSON.parse((await tool(state, 'flight-links-' + action, extra)).stdout.trim());
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
    ...(state.mode === 'saved-spb-option' && { savedMode: await savedEffectiveMode(state, inventory) }),
    boundary:
      state.e2eProfile === 'spb-option' || state.mode === 'saved-spb-option'
        ? 'Wall-clock Host, browser and identity; only option evidence uses the historical reference date.'
        : state.mode === 'demo'
          ? 'Wall-clock demo: immutable Moscow data can expire; saved manual plans remain available. Keys have a finite 90-day lifetime.'
          : 'Fixed application/browser time; identity validation uses wall clock. Integration proof does not prove current data or route quality.',
  };
}

async function startFresh(mode, stateDir, profile = 'moscow', smallSubnets = false, optionCatalogue) {
  await validateOwnedPath(stateDir);
  if (mode === 'demo' && path.basename(stateDir) !== 'demo')
    throw new Error('A fresh interactive demo must use the demo directory.');
  if (await exists(stateDir)) throw new Error('Fresh state directory already exists; refusing overwrite.');
  const daemon = await dockerDaemon();
  if (smallSubnets && !(Number.parseInt(daemon.serverVersion, 10) >= 29))
    throw new Error('--small-subnets requires Docker Engine 29 or newer.');
  const retained = mode === 'saved-spb-option';
  const [webPort, oidcPort] = retained ? [4207, 8187] : mode === 'demo' ? [4205, 8185] : [4206, 8186];
  await assertPortsAvailable([webPort, oidcPort]);
  const verification = path.basename(stateDir).startsWith('e2e-');
  const id = verification ? path.basename(stateDir).slice(4) : randomBytes(16).toString('hex');
  const state = {
    schemaVersion: 1,
    kind: 'travel-trips-local-stack',
    id,
    mode,
    repoRoot: REPO_ROOT,
    projectName: `travel-trips-${mode}-${id}`,
    webPort,
    oidcPort,
    applicationAsOfUtc: mode === 'e2e' ? e2eProfile(profile).instant : null,
    ...(verification ? { e2eProfile: profile } : {}),
    ...(optionCatalogue ? { optionCatalogue } : {}),
    ...(smallSubnets ? { smallSubnets: true } : {}),
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
  await waitReady(owned, mode !== 'demo');
  if (retained) {
    const inventory = await verifyOwned(owned);
    if ((await savedEffectiveMode(owned, inventory)) !== 'B')
      throw new Error('Fresh saved profile must run catalogue B.');
    await savedStorage(owned, inventory, true);
  }
  return owned;
}

function savedModes(state) {
  return state.e2eProfile === 'saved-spb-option' ? ['B', 'history', 'A', 'expired'] : ['B', 'history'];
}

function savedSettings(mode) {
  return {
    ASPNETCORE_ENVIRONMENT: 'Testing',
    DOTNET_ENVIRONMENT: 'Testing',
    Trips__Enabled: 'true',
    Trips__Itinerary__Enabled: String(mode !== 'history'),
    Trips__OptionPreview__Enabled: String(mode !== 'history'),
    Trips__OptionPreview__Profile: 'spb-option',
    Trips__OptionPreview__Catalogue: mode === 'A' ? 'A' : 'B',
    Trips__SavedOptions__Enabled: String(mode !== 'history'),
    ...(mode === 'expired' && { TRIPS_FIXTURE_EXPIRED_SOURCE: 'true' }),
  };
}

async function savedEffectiveMode(state, inventory) {
  const hosts = inventory.filter((item) => item.type === 'container' && item.service === 'host');
  if (hosts.length > 1) throw new Error('Cannot identify a single owned saved Host.');
  const [host] = hosts;
  if (!host) return null;
  // Render only these non-secret flags from Docker's actual Host configuration.
  const keys = Object.keys(savedSettings('B')).concat('TRIPS_FIXTURE_EXPIRED_SOURCE');
  const filter = keys.map((k) => `(eq (index (split . "=") 0) "${k}")`).join(' ');
  const result = await docker(state, [
    'container',
    'inspect',
    host.id,
    '--format',
    `{{range .Config.Env}}{{if or ${filter}}}{{println .}}{{end}}{{end}}`,
  ]);
  const actual = Object.fromEntries(
    result.stdout
      .trim()
      .split(/\r?\n/)
      .map((s) => s.split('=')),
  );
  for (const mode of savedModes(state)) {
    const expected = savedSettings(mode);
    if (
      Object.keys(expected).length === Object.keys(actual).length &&
      Object.entries(expected).every(([k, v]) => actual[k] === v)
    )
      return mode;
  }
  throw new Error('Saved Host effective flags differ from its validated profile.');
}

export async function stoppedSavedMode(state, inventory, requestedMode) {
  if (state.mode !== 'saved-spb-option' || (requestedMode !== undefined && !savedModes(state).includes(requestedMode)))
    throw new Error('Mode requires the matching stopped saved profile.');
  if (inventory.some((item) => item.state?.Running))
    throw new Error('Stop the owned saved stack before changing its mode.');
  const previous = await savedEffectiveMode(state, inventory);
  const target = requestedMode ?? previous;
  if (!target)
    throw new Error('Previous saved Host mode is unknown. Choose --read-saved-only or --with-saved-preview to resume.');
  return target;
}

async function savedStorage(state, inventory, record = false) {
  const volume = inventory.find((r) => r.type === 'volume');
  if (!volume) throw new Error('The retained database volume is missing.');
  const volumeIdentity = (
    await docker(state, ['volume', 'inspect', volume.name, '--format', '{{.Name}} {{.CreatedAt}}'])
  ).stdout.trim();
  const signature = json({
    stackId: state.id,
    daemon: state.dockerId,
    volumeIdentity,
    keyFingerprint: await buildFingerprint(path.join(state.stateDir, 'private', 'keys')),
  });
  const file = path.join(state.stateDir, 'saved-storage.json');
  if (record && !(await exists(file))) {
    try {
      await writeFile(file, signature, { flag: 'wx', mode: 0o600 });
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
    }
  }
  if (!(await exists(file)) || (await readFile(file, 'utf8')) !== signature)
    throw new Error('Saved storage and protection keys do not match the owned receipt.');
}

export async function up(statePath, smallSubnets = false, requestedMode) {
  if (!(await exists(statePath))) {
    if (requestedMode) throw new Error('Historical resume requires an existing stopped saved profile.');
    const mode = path.basename(path.dirname(statePath)) === 'saved-spb-option' ? 'saved-spb-option' : 'demo';
    return startFresh(
      mode,
      path.dirname(statePath),
      undefined,
      smallSubnets,
      mode === 'saved-spb-option' ? 'B' : undefined,
    );
  }
  if (smallSubnets)
    throw new Error('--small-subnets requires a fresh demo; use plain up to resume the saved topology.');
  const state = await readState(statePath);
  const inventory = await verifyOwned(state);
  const retained = state.mode === 'saved-spb-option';
  if (requestedMode && !retained) throw new Error('Mode requires the matching stopped saved profile.');
  const containers = inventory.filter((item) => item.type === 'container');
  if (containers.some((item) => item.state.Running)) {
    if (requestedMode) throw new Error('Stop the owned saved stack before changing its mode.');
    if (
      SERVICES.filter((service) => service !== 'tool').every((service) =>
        containers.some((item) => item.service === service && item.state.Running),
      )
    ) {
      if (retained) await savedEffectiveMode(state, inventory);
      return state;
    }
    throw new Error(
      'Owned stack is partially running. Stop it explicitly before resuming; mounted binaries were not changed.',
    );
  }
  if (!retained) await build(state, await dockerDaemon());
  const beforeStart = await verifyOwned(state);
  const target = retained ? await stoppedSavedMode(state, beforeStart, requestedMode) : null;
  if (retained) await savedStorage(state, beforeStart);
  const override = retained && target !== 'B' ? path.join(state.stateDir, `compose.saved-${target}.json`) : undefined;
  await compose(state, ['up', '-d'], {}, override);
  await waitReady(state);
  if (retained && (await savedEffectiveMode(state, await verifyOwned(state))) !== target)
    throw new Error('Saved Host resumed with unexpected effective settings.');
  return state;
}

export async function stop(state) {
  const before = await verifyOwned(state);
  if (state.mode === 'saved-spb-option') {
    const effective = await savedEffectiveMode(state, before);
    await savedStorage(state, before, Boolean(effective) && processFacts(before).every((p) => p.running));
  }
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
      if (
        !['browser-receipt.json', 'restart.json', 'readiness.json', MANUAL_STAYS_ROLLBACK_RECEIPT].includes(name) &&
        !/^[a-z0-9-]+\.png$/.test(name)
      )
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

export async function requireCompletedBrowserProfile(state, { manualStaysRollback = false } = {}) {
  const refuse = () => new Error('A complete profile-bound browser completion receipt is required.');
  validateStateObject(state, state.repoRoot);
  if (state.mode !== 'e2e' && !(state.mode === 'saved-spb-option' && state.e2eProfile === 'saved-spb-option'))
    throw refuse();
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
  if (profile === 'saved-spb-option') {
    if (
      receipt?.schemaVersion !== 1 ||
      receipt.status !== 'passed' ||
      receipt.stackId !== state.id ||
      receipt.profile !== profile ||
      !/^[a-f0-9]{64}$/.test(receipt.snapshotSha256) ||
      receipt.coldRead !== true ||
      receipt.protectedKeysPreserved !== true ||
      !Number.isSafeInteger(receipt.historyReads) ||
      receipt.historyReads < 1 ||
      receipt.beforeCommitGet !== 404 ||
      receipt.retryStatus !== 200 ||
      receipt.afterCommitGet !== 200 ||
      receipt.ordinaryPosts !== 0 ||
      receipt.previewPosts !== 4 ||
      receipt.savePosts !== 5 ||
      receipt.expiredPreviewStatus !== 503 ||
      receipt.netStayChecked !== true ||
      receipt.foreignBudgetChecked !== true ||
      !Array.isArray(receipt.transitions) ||
      receipt.transitions.map((x) => x.effective).join(',') !== 'history,A,history,expired' ||
      !receipt.transitions.every((x) => x.allExited === true && x.volumePreserved === true) ||
      receipt.transitions[0].refusedForeignOwner !== true ||
      receipt.transitions[0].refusedBusyPort !== true ||
      receipt.transitions[0].refusedTampering !== true ||
      receipt.transitions[0].refusedUnknownMode !== true ||
      receipt.transitions[0].refusedStorageMismatch !== true ||
      receipt.transitions[0].resumedVerifiedMode !== true ||
      receipt.afterCommit !== 'recovered-by-read' ||
      receipt.beforeCommit !== 'same-ticket-replayed' ||
      receipt.finalPlans !== 4 ||
      receipt.finalSnapshots !== 4 ||
      receipt.finalReceipts !== 4
    )
      throw refuse();
    return receipt;
  }
  if (manualStaysRollback && profile !== 'hotel-links') throw refuse();
  if (profile === 'spb-option') {
    if (
      receipt?.schemaVersion !== 1 ||
      receipt.status !== 'passed' ||
      receipt.stackId !== state.id ||
      receipt.e2eProfile !== profile ||
      receipt.catalogue !== state.optionCatalogue ||
      receipt.applicationAsOfUtc !== null ||
      receipt.referenceUtc !== '2026-10-27T12:00:00Z' ||
      receipt.previewResponses !== 5 ||
      receipt.externalRequests !== 0 ||
      receipt.writes !== 0 ||
      receipt.outboundHttpCalls !== 0 ||
      receipt.unknownTotal !== true ||
      receipt.roundTripId !== 'RT1' ||
      receipt.hotelId !== 'H1' ||
      receipt.nights !== 2 ||
      receipt.knownSubtotal !== '30000.00' ||
      receipt.arrival !== (state.optionCatalogue === 'A' ? '09:00' : '20:00') ||
      receipt.programmeMinutes?.join(',') !== (state.optionCatalogue === 'A' ? '44,38,0' : '0,38,0') ||
      receipt.shortStay !== 'net-only' ||
      receipt.missingRoom !== true ||
      receipt.unsupportedReasons?.join(',') !== 'directionUnsupported,dateRangeUnsupported,groupUnsupported' ||
      receipt.foreignBudget?.status !== 'indeterminate' ||
      receipt.foreignBudget?.currency !== 'EUR' ||
      receipt.foreignBudget?.amount !== '1.00' ||
      receipt.foreignBudget?.sourceCurrency !== 'RUB' ||
      receipt.foreignBudget?.knownSourceSubtotal !== '30000.00'
    )
      throw refuse();
    return receipt;
  }
  if (profile === 'hotel-links') {
    const hash = (value) => typeof value === 'string' && /^[0-9a-f]{64}$/.test(value);
    if (
      receipt?.schemaVersion !== 1 ||
      receipt.stackId !== state.id ||
      receipt.e2eProfile !== profile ||
      receipt.applicationAsOfUtc !== state.applicationAsOfUtc ||
      receipt.status !== 'passed' ||
      receipt.loginCount !== 4 ||
      receipt.externalRequests !== 0 ||
      receipt.attachRevision !== '1' ||
      receipt.detachRevision !== '2' ||
      receipt.restartVerified !== true ||
      !hash(receipt.planSha256) ||
      !hash(receipt.protectedPlanSha256) ||
      !hash(receipt.hotelOrderSha256)
    )
      throw refuse();
    if (manualStaysRollback) await requireManualStaysRollbackReceipt(state);
    return receipt;
  }
  if (profile === 'flight-links') {
    const hash = (s) => typeof s === 'string' && /^[0-9a-f]{64}$/.test(s);
    const counts = receipt?.flightEventCounts;
    if (
      !receipt ||
      receipt.schemaVersion !== 1 ||
      receipt.stackId !== state.id ||
      receipt.e2eProfile !== profile ||
      receipt.applicationAsOfUtc !== state.applicationAsOfUtc ||
      receipt.status !== 'passed' ||
      receipt.loginCount !== 4 ||
      receipt.externalRequests !== 0 ||
      receipt.patchCount !== 4 ||
      receipt.linksRevision !== '1' ||
      receipt.restartVerified !== true ||
      receipt.foreignStatus !== 404 ||
      receipt.foreignOrderStatus !== 404 ||
      receipt.readsAddedEvents !== false ||
      receipt.deletedPlanPresent !== false ||
      receipt.deletedCollectionPresent !== false ||
      receipt.remainingLinksRevision !== '2' ||
      !counts ||
      !Number.isSafeInteger(counts.afterSeed) ||
      counts.afterSeed < 1 ||
      counts.beforeRestart !== counts.afterSeed ||
      counts.afterRestart !== counts.afterSeed ||
      counts.afterDelete !== counts.afterSeed ||
      !Number.isSafeInteger(counts.afterClose) ||
      counts.afterClose !== counts.afterSeed + 1 ||
      counts.afterStatus !== counts.afterClose ||
      counts.afterUnlink !== counts.afterClose ||
      !hash(receipt.protectedRowSha256) ||
      !hash(receipt.protectionKeyFingerprint)
    )
      throw refuse();
    return receipt;
  }
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

async function requireManualStaysRollbackReceipt(state) {
  const refuse = () => new Error('A complete owned manual stays rollback rehearsal receipt is required.');
  const file = path.join(state.stateDir, 'evidence', MANUAL_STAYS_ROLLBACK_RECEIPT);
  let receipt;
  try {
    await noLinks(file);
    const info = await lstat(file);
    if (!info.isFile() || info.size <= 0 || info.size > 32768) throw refuse();
    receipt = JSON.parse(await readFile(file, 'utf8'));
  } catch {
    throw refuse();
  }
  if (
    receipt?.status !== 'passed' ||
    receipt.stackId !== state.id ||
    receipt.previous?.sourceHead !== MANUAL_STAYS_PREVIOUS_HEAD ||
    receipt.blockedTaggedPlans !== 1 ||
    receipt.unavailableProbe !== 'permission-denied' ||
    receipt.allowedTaggedPlans !== 0 ||
    receipt.oldWriteStatus !== 200 ||
    receipt.newWriteRejected !== true
  )
    throw refuse();
}

export async function withOptionTermination(verify, onError, cleanup, events = process) {
  const controller = new AbortController();
  const stop = () => controller.abort(new Error('Option verification interrupted.'));
  events.on('SIGINT', stop);
  events.on('SIGTERM', stop);
  try {
    try {
      await optionSignal.run(controller.signal, verify);
      controller.signal.throwIfAborted();
    } catch (error) {
      onError(error);
    } finally {
      await cleanup();
    }
    controller.signal.throwIfAborted();
  } finally {
    events.off('SIGINT', stop);
    events.off('SIGTERM', stop);
  }
}

function inspectionControl() {
  if (!process.stdin.isTTY) throw new Error('--inspect requires a local interactive terminal.');
  let stop;
  const stopped = new Promise((resolve) => {
    stop = resolve;
  });
  const terminal = createInterface({ input: process.stdin, output: process.stdout });
  terminal.on('line', stop);
  terminal.on('SIGINT', stop);
  terminal.on('close', stop);
  process.on('SIGINT', stop);
  process.on('SIGTERM', stop);
  return {
    stopped,
    close() {
      process.off('SIGINT', stop);
      process.off('SIGTERM', stop);
      terminal.close();
    },
  };
}

async function e2e(profile = 'moscow', smallSubnets = false, optionCatalogue, inspect = false) {
  const id = randomBytes(16).toString('hex');
  const stateDir = path.join(basePath(), `e2e-${id}`);
  const manualStaysRollback = profile === 'hotel-links' && process.env.TRIPS_STACK_MANUAL_STAYS_ROLLBACK === '1';
  const receipt = {
    schemaVersion: 1,
    id,
    startedAtUtc: new Date().toISOString(),
    applicationAsOfUtc: e2eProfile(profile).instant,
    e2eProfile: profile,
    ...(optionCatalogue && { optionCatalogue }),
    ...(smallSubnets ? { networkPrefixLength: 24 } : {}),
    ...(manualStaysRollback ? { manualStaysRollback: true } : {}),
    stages: {},
    source: {},
  };
  let state;
  let failure;
  let receiptPath;
  const inspection = inspect ? inspectionControl() : undefined;
  async function verify() {
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
    state = await startFresh(
      profile === 'saved-spb-option' ? profile : 'e2e',
      stateDir,
      profile,
      smallSubnets,
      profile === 'saved-spb-option' ? 'B' : optionCatalogue,
    );
    receipt.stages.start = 'passed';
    receipt.images = JSON.parse(await readFile(path.join(stateDir, 'images.json'), 'utf8'));
    const config = await privateConfiguration(state);
    await mkdir(path.join(stateDir, 'evidence'), { recursive: true });
    if (inspection) {
      process.stdout.write(
        `Open http://127.0.0.1:${state.webPort}/trips\nFictional sign-in: ${path.join(stateDir, 'private', 'credentials.json')}\nPress Enter or Ctrl+C to finish inspection and clean up this stack.\n`,
      );
      await inspection.stopped;
      receipt.stages.inspection = 'closed';
    } else {
      await run(
        process.execPath,
        [await resolveLocalCli('playwright'), 'test', '-c', 'tests/travel-e2e/playwright.trips-full-stack.config.ts'],
        {
          env: {
            TRIPS_STACK_STATE: path.join(stateDir, 'state.json'),
            TRIPS_STACK_PROFILE: profile,
            TRIPS_STACK_EVIDENCE: path.join(stateDir, 'evidence'),
            TRIPS_STACK_MANUAL_STAYS_ROLLBACK: manualStaysRollback ? '1' : '',
          },
          secrets: config.secrets,
          timeout: profile === 'saved-spb-option' ? 1_230_000 : 900_000,
          interruptible: profile === 'spb-option' || profile === 'saved-spb-option',
        },
      );
      await requireCompletedBrowserProfile(state, { manualStaysRollback });
      receipt.stages.browser = 'passed';
    }
  }
  function onError(error) {
    failure = error;
    receipt.stages.verification = 'failed';
  }
  async function cleanup() {
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
    inspection?.close();
  }
  if ((profile === 'spb-option' || profile === 'saved-spb-option') && !inspect)
    await withOptionTermination(verify, onError, cleanup);
  else {
    try {
      await verify();
    } catch (error) {
      onError(error);
    } finally {
      await cleanup();
    }
  }
  if (receiptPath) process.stdout.write(`Sanitized receipt: ${receiptPath}\n`);
  if (failure) throw failure;
  return receipt;
}

export async function runE2eProfiles(runProfile = e2e) {
  const profiles = Object.fromEntries(CI_PROFILES.map((name) => [name, { status: 'unrun' }]));
  for (const profile of CI_PROFILES) {
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
      const error = new Error('All Trips profiles and owned cleanup must pass; aggregate failed.');
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
  const subnetIndex = rest.indexOf('--small-subnets');
  const smallSubnets = subnetIndex >= 0;
  if (smallSubnets) {
    if (!['up', 'e2e', 'e2e-all'].includes(command))
      throw new Error('--small-subnets requires fresh up, e2e or e2e-all.');
    rest.splice(subnetIndex, 1);
  }
  const profileIndex = rest.indexOf('--profile');
  let profile = 'moscow';
  if (profileIndex >= 0) {
    profile = rest[profileIndex + 1];
    e2eProfile(profile);
    if (command !== 'e2e' && (profile !== 'saved-spb-option' || !['up', 'stop', 'status', 'destroy'].includes(command)))
      throw new Error('Unknown command for the selected profile.');
    rest.splice(profileIndex, 2);
  }
  const inspectIndex = rest.indexOf('--inspect');
  const inspect = inspectIndex >= 0;
  if (inspect) {
    if (command !== 'e2e' || profile !== 'spb-option') throw new Error('--inspect requires e2e --profile spb-option.');
    rest.splice(inspectIndex, 1);
  }
  let savedMode;
  for (const [flag, target] of [
    ['--read-saved-only', 'history'],
    ['--with-saved-preview', 'B'],
  ]) {
    const flagIndex = rest.indexOf(flag);
    if (flagIndex < 0) continue;
    if (command !== 'up' || savedMode) throw new Error('Choose a single saved mode for a stopped profile.');
    savedMode = target;
    rest.splice(flagIndex, 1);
  }
  let statePath = path.join(
    basePath(),
    profile === 'saved-spb-option' ? 'saved-spb-option/state.json' : 'demo/state.json',
  );
  const index = rest.indexOf('--state');
  if (index >= 0) {
    if (profileIndex >= 0) throw new Error('Use a named profile or an owned state path.');
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
      const output = path.join(directory, `${randomBytes(16).toString('hex')}-trips-aggregate.json`);
      let result;
      try {
        result = await runE2eProfiles((name) => e2e(name, smallSubnets));
      } catch (error) {
        result = error.aggregate;
        throw error;
      } finally {
        if (result) await writeFile(output, json(result), { flag: 'wx' });
      }
    } else if (profile === 'spb-option') {
      for (const version of inspect ? ['A'] : ['A', 'B']) await e2e(profile, smallSubnets, version, inspect);
    } else await e2e(profile, smallSubnets);
    return;
  }
  if (command === 'up') {
    process.stdout.write(json(await status(await up(statePath, smallSubnets, savedMode))));
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
