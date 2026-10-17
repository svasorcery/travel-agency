import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const paths = execFileSync('git', ['ls-files', '-c', '-o', '--exclude-standard'], { cwd: root, encoding: 'utf8' })
  .split(/\r?\n/)
  .filter((p) => p && !/^(TestResults|docs|openspec|\.agents|\.codex)\//.test(p));
paths.push('TestResults/hotels-m1/l1-profile.md');
paths.push('TestResults/hotels-m1/l1-memory-diagnostic-profile.md');
const manifest = Object.fromEntries(
  [...new Set(paths)].sort().map((p) => [
    p,
    createHash('sha256')
      .update(readFileSync(path.join(root, p)))
      .digest('hex')
      .toUpperCase(),
  ]),
);
const output = path.join(root, 'TestResults/hotels-m1/source-manifest.json');
mkdirSync(path.dirname(output), { recursive: true });
writeFileSync(output, `${JSON.stringify(manifest, null, 2)}\n`);
console.log(
  JSON.stringify({
    output,
    files: Object.keys(manifest).length,
    sha256: createHash('sha256').update(readFileSync(output)).digest('hex').toUpperCase(),
  }),
);
