import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const git = (...args) => {
  const result = spawnSync('git', args, { encoding: 'utf8', windowsHide: true });
  if (result.status !== 0) throw new Error(`Read-only git identity failed: ${args.join(' ')}`);
  return result.stdout.trim();
};
const base = '799e31a6b31ff256ba017861cd911e547e4e9896';
const roots = [
  'modules/rail/',
  'tests/rail/',
  'tools/rail/',
  'apps/web/src/app/rail/',
  'tests/Travel.Host.Tests.Integration/Rail/',
  'tests/Travel.Tests.Architecture/',
  'shared/ts/api-client/src/rail',
  'openspec/changes/rail-m1-yandex-search/specs/',
];
const exact = [
  'AGENTS.md',
  'README.md',
  'package.json',
  'Travel.slnx',
  '.github/workflows/ci.yml',
  'tools/ci/dotnet-inventory.json',
  'apps/Travel.Host/Program.cs',
  'apps/Travel.Host/Travel.Host.csproj',
  'shared/ts/api-client/src/index.ts',
  'apps/web/src/main.ts',
  'apps/web/src/app/app.routes.ts',
  'apps/web/src/app/app.routes.server.ts',
  'apps/web/src/app/app.html',
  'apps/web/src/app/app.scss',
  'apps/web/test-fixtures/rail-fixture.ts',
  'apps/web/src/app/auth/travel-frame-policy.ts',
  'apps/web/src/app/auth/travel-frame-policy.spec.ts',
  'apps/web/public/rail-fictional-banner.html',
  'tests/fixtures/rail-schedule.ts',
  'shared/ts/api-client/tests/rail-fixture.ts',
  'tests/travel-e2e/demo/rail-search.spec.ts',
  'tools/demo/rail-search.mjs',
  'tools/demo/rail-search.d.mts',
  'tools/demo/flights-search-api.mjs',
  'docs/superpowers/specs/2026-05-03-travel-platform-concept.md',
  'docs/architecture/current-state.md',
  'docs/examples/rail-requests.json',
  'tests/Travel.Host.Tests.Integration/Web/Verified/HostWebContractTests.OpenApi.verified.txt',
  'openspec/changes/rail-m1-yandex-search/proposal.md',
  'openspec/changes/rail-m1-yandex-search/design.md',
];
export function sourceManifest() {
  const paths = git('ls-files', '--cached', '--others', '--exclude-standard')
    .split(/\r?\n/)
    .filter(
      (p) =>
        (exact.includes(p) || roots.some((root) => p.startsWith(root))) &&
        !p.split('/').some((part) => ['bin', 'obj', 'TestResults'].includes(part)) &&
        !/\.(?:dll|pdb|exe|cache|tgz)$/.test(p),
    );
  const files = [...new Set(paths)]
    .sort()
    .map((path) => ({ path, sha256: createHash('sha256').update(readFileSync(path)).digest('hex') }));
  git('merge-base', '--is-ancestor', base, 'HEAD');
  return {
    requestedBase: base,
    head: git('rev-parse', 'HEAD'),
    branch: git('branch', '--show-current'),
    files,
    sourceSha256: createHash('sha256').update(JSON.stringify(files)).digest('hex'),
    uncommitted: git('status', '--porcelain').length > 0,
  };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const value = JSON.stringify(sourceManifest(), null, 2);
  if (process.argv[2]) writeFileSync(process.argv[2], value, { flag: 'wx' });
  else console.log(value);
}
