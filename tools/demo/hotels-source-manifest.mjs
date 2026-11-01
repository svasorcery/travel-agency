import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, realpathSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = realpathSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..'));
const m2Base = 'TestResults/hotels-m2-memory-load-acceptance';
const m2Archive = 'openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance';
const m2Change = 'openspec/changes/hotels-m2-memory-load-acceptance';

function within(parent, child) {
  const relative = path.relative(parent, child);
  return relative !== '' && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}

function resolveOwned(relative, allowedRoot, mayNotExist = false) {
  if (path.isAbsolute(relative)) throw new Error('Use a repository-relative path.');
  const target = path.resolve(root, relative);
  const boundary = path.resolve(root, allowedRoot);
  if (!within(boundary, target)) throw new Error('Path is outside the selected evidence/profile tree.');
  let existing = target;
  while (!existsSync(existing) && mayNotExist) {
    const parent = path.dirname(existing);
    if (parent === existing) throw new Error('No existing parent.');
    existing = parent;
  }
  const physical = realpathSync(existing);
  // Check both the project and, when it exists, the selected subtree. Junctions must not
  // silently redirect an apparently owned output to M1 or another directory.
  if (physical !== root && !within(root, physical)) throw new Error('Physical path leaves the repository.');
  if (existsSync(boundary)) {
    const physicalBoundary = realpathSync(boundary);
    if (physicalBoundary !== boundary) throw new Error('The evidence/profile root is redirected.');
    if (physical !== boundary && !within(boundary, physical))
      throw new Error('Physical path leaves the selected tree.');
  }
  return target;
}

function options(args) {
  if (args.length === 0) {
    return {
      profiles: ['TestResults/hotels-m1/l1-profile.md', 'TestResults/hotels-m1/l1-memory-diagnostic-profile.md'],
      output: path.join(root, 'TestResults/hotels-m1/source-manifest.json'),
      m2: false,
    };
  }
  if (args[0] !== '--m2') throw new Error('Expected --m2 --profile <path> --output <path>.');
  const profiles = [];
  let output;
  for (let i = 1; i < args.length; i += 2) {
    const value = args[i + 1];
    if (!value || value.startsWith('--')) throw new Error('Missing argument value.');
    if (args[i] === '--profile') {
      const normalized = path.relative(root, path.resolve(root, value)).split(path.sep).join('/');
      const tree = normalized.startsWith(`${m2Change}/`)
        ? m2Change
        : normalized.startsWith(m2Archive + '/')
          ? m2Archive
          : m2Base;
      resolveOwned(value, tree);
      if (!/\.(json|md)$/i.test(value)) throw new Error('A profile must be JSON or Markdown.');
      profiles.push(normalized);
    } else if (args[i] === '--output' && output === undefined) {
      output = resolveOwned(value, m2Base, true);
    } else throw new Error('Unknown or duplicate argument.');
  }
  if (!output || profiles.length === 0) throw new Error('M2 requires output and at least one profile.');
  if (new Set(profiles).size !== profiles.length) throw new Error('Duplicate profile.');
  if (profiles.some((profile) => path.resolve(root, profile) === output)) throw new Error('Output is also an input.');
  return { profiles, output, m2: true };
}

try {
  const selected = options(process.argv.slice(2));
  if (existsSync(selected.output)) throw new Error('Existing manifest must be preserved; choose a new run path.');
  const paths = execFileSync('git', ['ls-files', '-z', '-c', '-o', '--exclude-standard'], {
    cwd: root,
    encoding: 'utf8',
    windowsHide: true,
  })
    .split('\0')
    .filter((p) => p && !/^(TestResults|docs|openspec|\.agents|\.codex)\//.test(p));
  paths.push(...selected.profiles);
  const manifest = Object.fromEntries(
    [...new Set(paths)].sort().map((p) => {
      const physical = realpathSync(path.join(root, p));
      if (!within(root, physical)) throw new Error('Manifest source leaves the repository.');
      return [p, createHash('sha256').update(readFileSync(physical)).digest('hex').toUpperCase()];
    }),
  );
  mkdirSync(path.dirname(selected.output), { recursive: true });
  if (selected.m2) resolveOwned(path.relative(root, selected.output), m2Base, true);
  const content = `${JSON.stringify(manifest, null, 2)}\n`;
  writeFileSync(selected.output, content, { flag: 'wx' });
  console.log(
    JSON.stringify({
      output: selected.output,
      files: Object.keys(manifest).length,
      sha256: createHash('sha256').update(content).digest('hex').toUpperCase(),
    }),
  );
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
