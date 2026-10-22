import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, relative, resolve } from 'node:path';
import { sourceManifest } from './source-manifest.mjs';

const m2 = process.argv.includes('--m2');
const profile = resolve(m2 ? 'tools/rail/measured-m2-profile.json' : 'tools/rail/measured-profile.json');
const bytes = readFileSync(profile);
if (process.argv.length === 2 || process.argv.includes('--describe')) {
  console.log(
    JSON.stringify(
      { profile: JSON.parse(bytes), sha256: createHash('sha256').update(bytes).digest('hex'), executed: false },
      null,
      2,
    ),
  );
  process.exit(0);
}
const args = process.argv.slice(2).filter((x) => x !== '--m2');
if (
  args.length !== 4 ||
  args[0] !== '--run-authorized' ||
  args[1] !== '--actual-host-db-authorized' ||
  args[2] !== '--receipt-dir'
)
  throw new Error(
    'Separate measured and actual Host/DB authorization plus a new receipt directory are required. No resources started.',
  );
const receipt = resolve(args[3]);
const location = relative(resolve('TestResults'), receipt);
if (!location || location.startsWith('..') || isAbsolute(location) || existsSync(receipt))
  throw new Error('Choose a new attempt directory beneath repository TestResults; source-root outputs are prohibited.');
mkdirSync(dirname(receipt), { recursive: true });
const log = openSync(`${receipt}.process.log`, 'wx');
const before = sourceManifest(m2 ? 'a8721effd9056459d217966083816455fbea599d' : undefined);
const project = 'tools/rail/Travel.Rail.Fixture/Travel.Rail.Fixture.csproj';
const binaries = () =>
  [
    'Travel.Host',
    'Travel.Modules.Rail.Core',
    'Travel.Modules.Rail.Application',
    'Travel.Modules.Rail.Infrastructure',
    'Travel.Modules.Rail.Api',
    'Travel.ServiceDefaults',
    'Travel.Shared.Infrastructure',
    'Travel.Shared.Web',
    'Travel.Host.Tests.Integration',
    'Travel.Rail.Fixture',
  ].map((name) => {
    const path = `tools/rail/Travel.Rail.Fixture/bin/Release/net10.0/${name}.dll`;
    return { path, sha256: createHash('sha256').update(readFileSync(path)).digest('hex') };
  });
try {
  const build = spawnSync('dotnet', ['build', project, '--configuration', 'Release', '--no-restore'], {
    stdio: ['ignore', log, log],
    windowsHide: true,
  });
  if (build.error || build.status !== 0)
    throw new Error('Fresh guarded Release build failed; no measured run started.');
  const built = sourceManifest(m2 ? 'a8721effd9056459d217966083816455fbea599d' : undefined);
  if (before.sourceSha256 !== built.sourceSha256 || before.head !== built.head)
    throw new Error('Source changed during fresh build; no measured run started.');
  const executedBinaries = binaries();
  writeFileSync(
    `${receipt}.source.json`,
    JSON.stringify(
      { ...built, executedBinaries, buildExitCode: 0, profileSha256: createHash('sha256').update(bytes).digest('hex') },
      null,
      2,
    ),
    { flag: 'wx' },
  );
  const result = spawnSync(
    'dotnet',
    [
      'run',
      '--project',
      project,
      '--configuration',
      'Release',
      '--no-build',
      '--',
      '--run-authorized',
      '--actual-host-db-authorized',
      profile,
      receipt,
    ],
    { stdio: ['ignore', log, log], windowsHide: true },
  );
  const after = sourceManifest(m2 ? 'a8721effd9056459d217966083816455fbea599d' : undefined);
  const sourceUnchanged = built.sourceSha256 === after.sourceSha256 && built.head === after.head;
  const binariesUnchanged = JSON.stringify(executedBinaries) === JSON.stringify(binaries());
  const passed = !result.error && result.status === 0 && sourceUnchanged && binariesUnchanged;
  writeFileSync(
    `${receipt}.launcher.json`,
    JSON.stringify(
      {
        passed,
        buildExitCode: 0,
        runExitCode: result.status,
        sourceUnchanged,
        binariesUnchanged,
        sourceIdentity: built.sourceSha256,
        automaticRetry: false,
      },
      null,
      2,
    ),
    { flag: 'wx' },
  );
  process.exitCode = passed ? 0 : 1;
} catch (error) {
  writeFileSync(
    `${receipt}.launcher-error.json`,
    JSON.stringify(
      { passed: false, error: String(error), sourceIdentity: before.sourceSha256, automaticRetry: false },
      null,
      2,
    ),
    { flag: 'wx' },
  );
  process.exitCode = 1;
} finally {
  closeSync(log);
}
