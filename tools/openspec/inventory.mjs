import { lstat, readdir, readFile, realpath } from 'node:fs/promises';
import { join } from 'node:path';
import { adaptSkill, buildSkillRecord } from './adapter.mjs';
import { assertPhysical, normalizeText, sha256 } from './files.mjs';
import { ADAPTER_VERSION, PIN, SKILLS, WORKFLOWS } from './policy.mjs';
export const TOOL_FILES = Object.freeze([
  'adapter.mjs',
  'adapter.test.mjs',
  'arguments.mjs',
  'cli.integration.test.mjs',
  'files.mjs',
  'inventory.mjs',
  'manifest.json',
  'package-lock.json',
  'package.json',
  'policy.mjs',
  'run.mjs',
  'run.test.mjs',
  'setup.mjs',
  'snapshot.mjs',
  'snapshot.test.mjs',
  'upstream',
]);
function exactKeys(object, keys) {
  return (
    object &&
    typeof object === 'object' &&
    !Array.isArray(object) &&
    Object.keys(object).sort().join('|') === [...keys].sort().join('|')
  );
}
async function present(path) {
  try {
    await lstat(path);
    return true;
  } catch (e) {
    if (e.code === 'ENOENT') return false;
    throw e;
  }
}
export async function readOpenSpecInventory(input) {
  const root = await realpath(input);
  const tool = join(root, 'tools', 'openspec');
  const skillRoot = join(root, '.agents', 'skills');
  const issues = [];
  const add = (code, path, message) => issues.push({ code: 'openspec/' + code, path, message });
  const entries = await readdir(skillRoot).catch((e) => (e.code === 'ENOENT' ? [] : Promise.reject(e)));
  const configured =
    (await present(join(tool, 'manifest.json'))) ||
    (await present(join(root, 'openspec', 'config.yaml'))) ||
    entries.some((n) => n === '.openspec-target' || n.startsWith('openspec-'));
  if (!configured) return { configured: false, skills: [], issues };
  async function text(relative) {
    try {
      return normalizeText(await readFile(await assertPhysical(root, join(root, relative)), 'utf8'));
    } catch (e) {
      add(e.code === 'ENOENT' ? 'missing' : 'path', relative, 'required physical artifact unavailable or unsafe');
      return null;
    }
  }
  async function json(relative) {
    const value = await text(relative);
    if (value === null) return null;
    try {
      return JSON.parse(value);
    } catch {
      add('schema', relative, 'invalid JSON');
      return null;
    }
  }
  async function inventory(relative, expected, { optional = [], directories = [] } = {}) {
    let names;
    try {
      const path = await assertPhysical(root, join(root, relative));
      if (!(await lstat(path)).isDirectory()) {
        add('type', relative, 'inventory root must be a physical directory');
        return;
      }
      names = await readdir(path);
    } catch {
      add('missing', relative, 'inventory directory unavailable');
      return;
    }
    const accepted = new Set([...expected, ...optional]);
    const directoryNames = new Set(directories);
    for (const name of names)
      if (!accepted.has(name)) add('inventory', relative + '/' + name, 'unexpected inventory entry');
    for (const name of expected)
      if (!names.includes(name)) add('missing', relative + '/' + name, 'required inventory entry missing');
    for (const name of names.filter((n) => accepted.has(n)))
      try {
        const path = await assertPhysical(root, join(root, relative, name));
        const stat = await lstat(path);
        if (directoryNames.has(name) ? !stat.isDirectory() : !stat.isFile())
          add('type', relative + '/' + name, 'inventory member has an unexpected physical type');
      } catch {
        add('path', relative + '/' + name, 'symlink or unsafe physical path');
      }
  }
  await inventory('tools/openspec', TOOL_FILES, {
    optional: ['node_modules'],
    directories: ['upstream', 'node_modules'],
  });
  await inventory('tools/openspec/upstream', SKILLS, { directories: SKILLS });
  const manifest = await json('tools/openspec/manifest.json');
  if (
    !exactKeys(manifest, [
      'schemaVersion',
      'package',
      'profile',
      'delivery',
      'tool',
      'adapterVersion',
      'workflows',
      'runtimeDigest',
      'skills',
    ]) ||
    manifest.schemaVersion !== 1 ||
    !exactKeys(manifest.package, Object.keys(PIN)) ||
    Object.entries(PIN).some(([key, value]) => manifest.package[key] !== value) ||
    manifest.profile !== 'core' ||
    manifest.delivery !== 'skills' ||
    manifest.tool !== 'codex' ||
    manifest.adapterVersion !== ADAPTER_VERSION ||
    JSON.stringify(manifest.workflows) !== JSON.stringify(WORKFLOWS) ||
    !/^[a-f0-9]{64}$/.test(manifest.runtimeDigest) ||
    !exactKeys(manifest.skills, SKILLS)
  ) {
    add('pin', 'tools/openspec/manifest.json', 'manifest schema, version, profile or exact skill pin differs');
  } else {
    for (const name of SKILLS) {
      const record = manifest.skills[name];
      const upstreamPath = 'tools/openspec/upstream/' + name + '/SKILL.md';
      const effectivePath = '.agents/skills/' + name + '/SKILL.md';
      await inventory('tools/openspec/upstream/' + name, ['SKILL.md']);
      await inventory('.agents/skills/' + name, ['SKILL.md']);
      const upstream = await text(upstreamPath);
      const effective = await text(effectivePath);
      if (
        !exactKeys(record, [
          'upstreamHash',
          'effectiveHash',
          'commandSites',
          'allowedTools',
          'description',
          'adapterVersion',
        ]) ||
        record.adapterVersion !== ADAPTER_VERSION ||
        !/^[a-f0-9]{64}$/.test(record.upstreamHash) ||
        !/^[a-f0-9]{64}$/.test(record.effectiveHash)
      ) {
        add('schema', 'tools/openspec/manifest.json', 'invalid exact skill record');
        continue;
      }
      if (upstream !== null) {
        try {
          const source = buildSkillRecord(name, upstream);
          if (
            source.upstreamHash !== record.upstreamHash ||
            source.description !== record.description ||
            JSON.stringify(source.commandSites) !== JSON.stringify(record.commandSites)
          )
            throw new Error('source mismatch');
          const expected = adaptSkill(name, upstream, record);
          if (effective !== null && effective !== expected) throw new Error('effective mismatch');
        } catch {
          add('hash', effectivePath, 'upstream or effective provenance/content drift');
        }
      }
      if (effective !== null && sha256(effective) !== record.effectiveHash)
        add('hash', effectivePath, 'effective skill digest differs');
    }
  }
  const pkg = await json('tools/openspec/package.json');
  const lock = await json('tools/openspec/package-lock.json');
  const rootPkg = await json('package.json');
  if (pkg && (!exactKeys(pkg.dependencies, [PIN.name]) || pkg.dependencies[PIN.name] !== PIN.version))
    add('pin', 'tools/openspec/package.json', 'tool dependency must be exactly pinned');
  if (
    lock &&
    (lock.packages?.['node_modules/' + PIN.name]?.version !== PIN.version ||
      lock.packages?.['node_modules/' + PIN.name]?.integrity !== PIN.integrity)
  )
    add('pin', 'tools/openspec/package-lock.json', 'lock version/integrity differs');
  if (rootPkg?.scripts?.openspec !== 'node tools/openspec/run.mjs')
    add('pin', 'package.json', 'openspec alias must use pinned launcher');
  const marker = await text('.agents/skills/.openspec-target');
  if (marker !== null && marker !== 'codex\n')
    add('marker', '.agents/skills/.openspec-target', 'shared root must be owned by codex');
  const config = await text('openspec/config.yaml');
  if (config !== null)
    try {
      validateProjectConfig(config);
    } catch (error) {
      add('pin', 'openspec/config.yaml', error.message);
    }
  return { configured: true, skills: [...SKILLS], issues };
}

// Deliberately narrow, dependency-free YAML subset shared by setup and CI inventory.
// General OpenSpec rules/stores/flow YAML require a separately reviewed policy change.
export function validateProjectConfig(input) {
  let schemaSeen = false,
    contextSeen = false,
    inContext = false,
    contextStarted = false;
  for (const line of normalizeText(input).split('\n')) {
    if (line.includes('\t')) throw new Error('Project config does not support tabs');
    if (inContext) {
      if (!line.trim()) continue;
      if (line.startsWith('  ')) {
        if (!contextStarted && line.startsWith('   '))
          throw new Error('Literal context must begin with exactly two spaces');
        contextStarted = true;
        continue;
      }
      inContext = false;
    }
    if (!line.trim() || line.startsWith('#')) continue;
    if (/^schema: (?:spec-driven|'spec-driven'|"spec-driven")(?: +#.*)? *$/.test(line)) {
      if (schemaSeen || contextSeen) throw new Error('Project config schema must occur exactly once before context');
      schemaSeen = true;
      continue;
    }
    if (/^context: \|(?:-)?(?: +#.*)? *$/.test(line)) {
      if (!schemaSeen || contextSeen) throw new Error('Project config permits one literal context after schema');
      contextSeen = true;
      inContext = true;
      continue;
    }
    throw new Error('Project config supports only schema: spec-driven and optional literal context');
  }
  if (!schemaSeen) throw new Error('Project config requires schema: spec-driven');
}
