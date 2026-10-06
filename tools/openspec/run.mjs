import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { validateCommandPrefix, validateForwardArguments, validateReadArguments } from './arguments.mjs';
import { assertPhysical, readJson, sha256, treeDigest, within } from './files.mjs';
import { PIN } from './policy.mjs';
export const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
export async function verifyInstallation(root) {
  const physicalRoot = await realpath(root);
  const tool = join(physicalRoot, 'tools', 'openspec');
  const packageDir = join(tool, 'node_modules', '@fission-ai', 'openspec');
  let installed;
  try {
    installed = await readJson(await assertPhysical(physicalRoot, join(packageDir, 'package.json')));
  } catch (error) {
    throw new Error('Pinned local installation is missing or unsafe', { cause: error });
  }
  const manifestPath = await assertPhysical(physicalRoot, join(tool, 'manifest.json'));
  const manifestText = await readFile(manifestPath, 'utf8');
  const manifest = JSON.parse(manifestText.replace(/^\uFEFF/, ''));
  if (
    manifest.schemaVersion !== 1 ||
    manifest.package?.sourceCommit !== PIN.sourceCommit ||
    manifest.profile !== 'core' ||
    manifest.delivery !== 'skills' ||
    manifest.tool !== 'codex' ||
    manifest.adapterVersion !== 1
  )
    throw new Error('Pinned manifest profile/source metadata differs');
  const declared = await readJson(await assertPhysical(physicalRoot, join(tool, 'package.json')));
  const lock = await readJson(await assertPhysical(physicalRoot, join(tool, 'package-lock.json')));
  if (
    installed.version !== PIN.version ||
    installed.name !== PIN.name ||
    declared.dependencies?.[PIN.name] !== PIN.version ||
    manifest.package?.version !== PIN.version ||
    manifest.package?.name !== PIN.name
  )
    throw new Error('Local OpenSpec version does not match the pin');
  const locked = lock.packages?.['node_modules/' + PIN.name];
  if (
    locked?.version !== PIN.version ||
    locked.integrity !== PIN.integrity ||
    manifest.package.integrity !== PIN.integrity
  )
    throw new Error('Local OpenSpec lock integrity does not match the pin');
  const digest = await treeDigest(join(tool, 'node_modules'), {
    skip: new Set(['.bin', '.package-lock.json', '.travel-read-config']),
  });
  if (digest !== manifest.runtimeDigest)
    throw new Error('Pinned runtime digest differs: local installation was modified');
  const binary = await assertPhysical(physicalRoot, join(packageDir, 'bin', 'openspec.js'));
  return { physicalRoot, tool, binary, manifest, manifestHash: sha256(manifestText) };
}
async function scopeFor(installation, args, environment) {
  const prepared = environment.TRAVEL_OPENSPEC_READ_CONFIG;
  if (prepared) {
    validateReadArguments(args);
    const expected = join(installation.tool, 'node_modules', '.travel-read-config');
    if (resolve(prepared) !== expected) throw new Error('Read-only scope must belong to this pinned installation');
    await assertPhysical(installation.physicalRoot, expected);
    const marker = await readJson(await assertPhysical(installation.physicalRoot, join(expected, 'scope.json')));
    const config = await readJson(
      await assertPhysical(installation.physicalRoot, join(expected, 'openspec', 'config.json')),
    );
    if (
      marker.kind !== 'travel-openspec-read-scope-v1' ||
      marker.repositoryRoot !== installation.physicalRoot ||
      marker.manifestHash !== installation.manifestHash ||
      Object.keys(config).sort().join('|') !== 'delivery|profile|telemetry' ||
      config.profile !== 'core' ||
      config.delivery !== 'skills' ||
      config.telemetry?.enabled !== false
    )
      throw new Error('Read-only scope binding is invalid');
    return { home: expected, data: join(expected, 'data'), cleanup: async () => {} };
  }
  const temp = await mkdtemp(join(tmpdir(), 'travel-openspec-run-'));
  await mkdir(join(temp, 'config', 'openspec'), { recursive: true });
  await writeFile(
    join(temp, 'config', 'openspec', 'config.json'),
    JSON.stringify({ profile: 'core', delivery: 'skills', telemetry: { enabled: false } }),
  );
  return {
    home: join(temp, 'config'),
    data: join(temp, 'data'),
    cleanup: async () => {
      if (!within(tmpdir(), temp) || !dirname(temp) || !temp.includes('travel-openspec-run-'))
        throw new Error('Unsafe config cleanup target');
      await rm(temp, { recursive: true, force: true });
    },
  };
}
async function executeVerified(installation, args, environment, cwd = installation.physicalRoot) {
  const scope = await scopeFor(installation, args, environment);
  const env = {
    ...environment,
    XDG_CONFIG_HOME: scope.home,
    XDG_DATA_HOME: scope.data,
    OPENSPEC_TELEMETRY: '0',
    DO_NOT_TRACK: '1',
    OPENSPEC_NO_COMPLETIONS: '1',
    OPENSPEC_NO_ANIMATION: '1',
    CI: 'true',
  };
  try {
    return await new Promise((resolveResult, reject) => {
      const child = spawn(process.execPath, [installation.binary, ...args], {
        cwd,
        env,
        shell: false,
        windowsHide: true,
        stdio: ['ignore', 'pipe', 'pipe'],
      });
      let stdout = '',
        stderr = '';
      const timeout = setTimeout(() => {
        child.kill();
        reject(new Error('Pinned CLI exceeded its 120-second execution budget'));
      }, 120000);
      child.stdout.setEncoding('utf8');
      child.stderr.setEncoding('utf8');
      const collect = (part, channel) => {
        if (Buffer.byteLength(stdout) + Buffer.byteLength(stderr) + Buffer.byteLength(part) > 8 * 1024 * 1024) {
          child.kill();
          reject(new Error('Pinned CLI output budget exceeded'));
          return;
        }
        if (channel === 'stdout') stdout += part;
        else stderr += part;
      };
      child.stdout.on('data', (part) => collect(part, 'stdout'));
      child.stderr.on('data', (part) => collect(part, 'stderr'));
      child.on('error', (error) => {
        clearTimeout(timeout);
        reject(error);
      });
      child.on('close', (code) => {
        clearTimeout(timeout);
        resolveResult({ exitCode: code ?? 1, stdout, stderr });
      });
    });
  } finally {
    await scope.cleanup();
  }
}
export async function runPinned(args, { repositoryRoot: root = repositoryRoot, environment = process.env } = {}) {
  validateForwardArguments(args, { readOnly: Boolean(environment.TRAVEL_OPENSPEC_READ_CONFIG) });
  return executeVerified(await verifyInstallation(root), args, environment);
}
export async function generateScratch(root, project) {
  if (process.env.TRAVEL_OPENSPEC_READ_CONFIG) throw new Error('Read-only scope refuses scratch mutation');
  const base = await realpath(tmpdir());
  const actual = await realpath(project);
  if (
    !within(base, actual) ||
    !dirname(actual).startsWith(join(base, 'travel-openspec-stage-')) ||
    actual !== join(dirname(actual), 'project')
  )
    throw new Error('Scratch target must be this setup-owned temporary project');
  const installation = await verifyInstallation(root);
  return executeVerified(
    installation,
    ['init', actual, '--tools', 'codex', '--profile', 'core', '--no-animation', '--no-copilot-cloud'],
    process.env,
  );
}
async function main() {
  try {
    const args = process.argv.slice(2);
    validateCommandPrefix(args);
    if (process.env.TRAVEL_OPENSPEC_READ_CONFIG) validateReadArguments(args);
    if (args[0] === 'init' || args[0] === 'update') {
      const { setup } = await import('./setup.mjs');
      await setup(repositoryRoot, args);
      return;
    }
    const result = await runPinned(args);
    process.stdout.write(result.stdout);
    process.stderr.write(result.stderr);
    process.exitCode = result.exitCode;
  } catch (error) {
    process.stderr.write('Travel OpenSpec: ' + error.message + '\n');
    process.exitCode = 1;
  }
}
// setup imports this module; a top-level await here would deadlock dynamic setup loading.
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) void main();
