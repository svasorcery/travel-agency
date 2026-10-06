import { access, lstat, mkdir, mkdtemp, readdir, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { adaptSkill } from './adapter.mjs';
import { assertPhysical, normalizeText, sha256, within } from './files.mjs';
import { validateProjectConfig } from './inventory.mjs';
import { PIN, SKILLS } from './policy.mjs';
import { generateScratch, verifyInstallation } from './run.mjs';

function validateArgs(args) {
  if (!['init', 'update'].includes(args[0])) throw new Error('Unsupported setup command');
  const seen = new Set();
  for (let i = 1; i < args.length; i++) {
    const flag = args[i];
    if (seen.has(flag)) throw new Error('Duplicate setup arguments');
    seen.add(flag);
    if (flag === '--tools') {
      if (args[++i] !== 'codex') throw new Error('Only codex tools are supported');
    } else if (flag === '--profile') {
      if (args[++i] !== 'core') throw new Error('Only core profile is supported');
    } else if (!['--no-animation', '--no-copilot-cloud'].includes(flag)) throw new Error('Unsupported setup arguments');
  }
}
async function safeTarget(root, target) {
  if (!within(root, target)) throw new Error('Setup target escapes repository');
  let current = resolve(root);
  for (const part of resolve(target)
    .slice(current.length + 1)
    .split(/[\\/]/)) {
    current = join(current, part);
    try {
      const info = await lstat(current);
      if (info.isSymbolicLink()) throw new Error('Setup target cannot be a symlink');
    } catch (error) {
      if (error.code === 'ENOENT') break;
      throw error;
    }
  }
}
async function existing(path) {
  try {
    return await readFile(path, 'utf8');
  } catch (error) {
    if (error.code === 'ENOENT') return null;
    throw error;
  }
}
async function protectedHashes(root) {
  const hashes = {};
  async function walk(dir) {
    let entries;
    try {
      entries = await readdir(dir, { withFileTypes: true });
    } catch (error) {
      if (error.code === 'ENOENT') return;
      throw error;
    }
    for (const e of entries) {
      const p = join(dir, e.name);
      if (e.isSymbolicLink()) throw new Error('Protected artifact symlink is unsupported');
      if (e.isDirectory()) await walk(p);
      else if (e.isFile() && e.name.endsWith('.md')) hashes[p] = sha256(await readFile(p));
    }
  }
  await walk(join(root, 'openspec', 'changes'));
  for (const name of await readdir(join(root, '.agents', 'skills')).catch((error) =>
    error.code === 'ENOENT' ? [] : Promise.reject(error),
  ))
    if (!SKILLS.includes(name) && name !== '.openspec-target') await walk(join(root, '.agents', 'skills', name));
  for (const p of ['AGENTS.md', 'CLAUDE.md', 'modules/flights/AGENTS.md', 'modules/flights/CLAUDE.md']) {
    const text = await existing(join(root, p));
    if (text !== null) hashes[join(root, p)] = sha256(text);
  }
  return hashes;
}
async function cleanupScratch(tempBase, scratch) {
  if (!within(tempBase, scratch) || !scratch.startsWith(join(tempBase, 'travel-openspec-stage-')))
    throw new Error('Unsafe stage cleanup target');
  await rm(scratch, { recursive: true, force: true });
}
export async function setup(root, args = ['init']) {
  validateArgs(args);
  if (process.env.TRAVEL_OPENSPEC_READ_CONFIG) throw new Error('Read-only scope refuses setup mutation');
  const { physicalRoot, tool, manifest } = await verifyInstallation(root);
  if (
    Object.keys(manifest.skills ?? {})
      .sort()
      .join('|') !== SKILLS.join('|')
  )
    throw new Error('Exact six-skill manifest is required');
  const before = await protectedHashes(physicalRoot);
  const writes = [];
  const targetSkills = join(physicalRoot, '.agents', 'skills');
  for (const name of await readdir(targetSkills).catch((error) =>
    error.code === 'ENOENT' ? [] : Promise.reject(error),
  ))
    if (name.startsWith('openspec-') && !SKILLS.includes(name))
      throw new Error('Unknown OpenSpec skill inventory blocks setup');
  const marker = join(targetSkills, '.openspec-target');
  await safeTarget(physicalRoot, marker);
  const markerText = await existing(marker);
  if (markerText !== null && markerText.trim() !== 'codex') throw new Error('Existing shared skill owner differs');
  const tempBase = await realpath(tmpdir());
  const scratch = await realpath(await mkdtemp(join(tempBase, 'travel-openspec-stage-')));
  try {
    const project = join(scratch, 'project');
    await mkdir(project);
    const result = await generateScratch(physicalRoot, project);
    if (result.exitCode !== 0) throw new Error('Pinned scratch generation failed: ' + result.stderr);
    const produced = join(project, '.agents', 'skills');
    const names = (await readdir(produced)).sort();
    if (names.join('|') !== ['.openspec-target', ...SKILLS].sort().join('|'))
      throw new Error('Scratch generated unexpected skill inventory');
    if ((await readFile(join(produced, '.openspec-target'), 'utf8')).trim() !== 'codex')
      throw new Error('Scratch target marker drift');
    for (const name of SKILLS) {
      const dir = join(produced, name);
      await assertPhysical(scratch, dir);
      const children = await readdir(dir);
      if (children.join('|') !== 'SKILL.md') throw new Error('Scratch skill file inventory drift');
      const text = await readFile(await assertPhysical(scratch, join(dir, 'SKILL.md')), 'utf8');
      const record = manifest.skills[name];
      const fixture = await readFile(
        await assertPhysical(physicalRoot, join(tool, 'upstream', name, 'SKILL.md')),
        'utf8',
      );
      if (sha256(normalizeText(fixture)) !== record.upstreamHash) throw new Error('Pinned source fixture was modified');
      const effective = adaptSkill(name, text, record);
      const target = join(targetSkills, name, 'SKILL.md');
      await safeTarget(physicalRoot, target);
      const old = await existing(target);
      if (old !== null && sha256(normalizeText(old)) !== record.effectiveHash)
        throw new Error('Divergent existing generated skill requires review');
      try {
        const entries = await readdir(join(targetSkills, name), { withFileTypes: true });
        if (entries.some((e) => e.name !== 'SKILL.md' || !e.isFile() || e.isSymbolicLink()))
          throw new Error('Existing generated skill has unexpected inventory children');
      } catch (error) {
        if (error.code !== 'ENOENT') throw error;
      }
      writes.push([target, effective]);
    }
    const configPath = join(physicalRoot, 'openspec', 'config.yaml');
    await safeTarget(physicalRoot, configPath);
    const config = await existing(configPath);
    if (config === null)
      writes.push([
        configPath,
        'schema: spec-driven\ncontext: |\n  Travel Platform: follow root and module AGENTS.md.\n  One canonical change/spec corpus in OpenSpec; no duplicate feature plans.\n  Current pilot flights-m3-cancellation uses fictional data only.\n  No supplier/payment/Anthropic/paid API or local Host/DB/schema/deploy.\n  Skills do not grant execution or publication authority.\n',
      ]);
    else {
      validateProjectConfig(config);
      const { parse } = await import('./node_modules/yaml/dist/index.js');
      const decoded = parse(config);
      if (decoded?.schema !== 'spec-driven' || decoded.store)
        throw new Error('Existing project schema/store differs; review required');
    }
    const change = join(physicalRoot, 'openspec', 'changes', 'flights-m3-cancellation');
    try {
      await access(change);
      const meta = join(change, '.openspec.yaml');
      await safeTarget(physicalRoot, meta);
      const oldMeta = await existing(meta);
      if (oldMeta === null) writes.push([meta, 'schema: spec-driven\ncreated: 2026-10-04\n']);
      else {
        const { parse } = await import('./node_modules/yaml/dist/index.js');
        const decoded = parse(oldMeta);
        if (decoded?.schema !== 'spec-driven' || decoded.skip_specs === true || decoded.retire_capabilities === true)
          throw new Error('Existing change metadata schema/skip policy differs');
      }
    } catch (error) {
      if (error.code !== 'ENOENT') throw error;
    }
    writes.push([marker, 'codex\n']);
    for (const [path, text] of writes) {
      await mkdir(resolve(path, '..'), { recursive: true });
      if ((await existing(path)) !== text) await writeFile(path, text);
    }
    for (const [path, digest] of Object.entries(before))
      if (sha256(await readFile(path)) !== digest)
        throw new Error('Protected Travel/corpus artifact changed during setup');
    return { version: PIN.version, skillCount: SKILLS.length };
  } finally {
    await cleanupScratch(tempBase, scratch);
  }
}
