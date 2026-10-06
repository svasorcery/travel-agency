import { cp, lstat, mkdir, readFile, realpath, writeFile } from 'node:fs/promises';
import { isAbsolute, join, resolve } from 'node:path';
import { assertPhysical, sha256, treeDigest, within } from './files.mjs';
import { TOOL_FILES } from './inventory.mjs';
import { SKILLS } from './policy.mjs';
import { verifyInstallation } from './run.mjs';

const rootFiles = new Set([
  'AGENTS.md',
  'CLAUDE.md',
  'modules/flights/AGENTS.md',
  'modules/flights/CLAUDE.md',
  'package.json',
  '.gitignore',
  '.agents/skills/spec/SKILL.md',
  'openspec/config.yaml',
]);
const changeFiles = new Set([
  'proposal.md',
  'baseline.md',
  'research.md',
  'design.md',
  'harness-integration.md',
  'tasks.md',
  'review.md',
  'process-log.md',
  '.openspec.yaml',
  'specs/flights-whole-order-cancellation/spec.md',
]);
const harnessFiles = new Set(['validate.mjs', 'validate.test.mjs', 'verify-codex.mjs', 'verify-codex.test.mjs']);
export function validateSnapshotPaths(paths) {
  if (!Array.isArray(paths)) throw new Error('Snapshot path list must be an array');
  const result = [];
  for (const path of paths) {
    if (
      typeof path !== 'string' ||
      !path ||
      isAbsolute(path) ||
      path.includes('\\') ||
      path.includes(':') ||
      path.split('/').some((p) => p === '..' || p === '.' || !p)
    )
      throw new Error('Unsafe working snapshot path');
    let accepted = rootFiles.has(path);
    if (path.startsWith('tools/ai-harness/')) accepted = harnessFiles.has(path.slice(17));
    if (path.startsWith('tools/openspec/')) {
      const rest = path.slice(15);
      accepted = TOOL_FILES.includes(rest) && rest !== 'upstream';
      if (rest.startsWith('upstream/')) accepted = SKILLS.some((n) => rest === 'upstream/' + n + '/SKILL.md');
    }
    if (path.startsWith('.agents/skills/'))
      accepted =
        path === '.agents/skills/spec/SKILL.md' ||
        path === '.agents/skills/.openspec-target' ||
        SKILLS.some((n) => path === '.agents/skills/' + n + '/SKILL.md');
    const change = 'openspec/changes/flights-m3-cancellation/';
    if (path.startsWith(change)) accepted = changeFiles.has(path.slice(change.length));
    if (!accepted) throw new Error('Unapproved T1/T2 working snapshot path: ' + path);
    result.push(path);
  }
  return [...new Set(result)].sort();
}
export async function collectWorkingPaths(source, git, runProcess) {
  const changed = await runProcess(git, ['-C', source, 'diff', '--name-only', '-z', 'HEAD'], { cwd: source });
  const untracked = await runProcess(git, ['-C', source, 'ls-files', '--others', '--exclude-standard', '-z'], {
    cwd: source,
  });
  return validateSnapshotPaths((changed.stdout + '\0' + untracked.stdout).split('\0').filter(Boolean));
}
async function safeDestination(root, path) {
  if (!within(root, path)) throw new Error('Snapshot destination escapes owned clone');
  const relative = path.slice(root.length + 1).split(/[\\/]/);
  let cursor = root;
  for (const part of relative) {
    cursor = join(cursor, part);
    try {
      const stat = await lstat(cursor);
      if (stat.isSymbolicLink()) throw new Error('Snapshot destination symlink');
    } catch (e) {
      if (e.code === 'ENOENT') break;
      throw e;
    }
  }
}
export async function overlayWorkingSnapshot(sourceInput, targetInput, paths) {
  const source = await realpath(sourceInput),
    target = await realpath(targetInput);
  if (source === target) throw new Error('Snapshot target must be a separate owned clone');
  const rows = [];
  for (const path of validateSnapshotPaths(paths)) {
    const from = await assertPhysical(source, join(source, path));
    const stat = await lstat(from);
    if (!stat.isFile()) throw new Error('Snapshot source must be a physical file');
    const data = await readFile(from);
    const to = join(target, path);
    await safeDestination(target, to);
    await mkdir(resolve(to, '..'), { recursive: true });
    await writeFile(to, data);
    rows.push([path, sha256(data)]);
  }
  for (const [path, hash] of rows)
    if (sha256(await readFile(await assertPhysical(source, join(source, path)))) !== hash)
      throw new Error('Source changed while snapshot was being copied');
  return { paths: rows.map(([p]) => p), digest: sha256(rows.map(([p, h]) => p + '\0' + h + '\n').join('')) };
}
async function separateRuntimeRoots(sourceInput, targetInput) {
  const source = await realpath(sourceInput),
    target = await realpath(targetInput);
  if (within(source, target) || within(target, source))
    throw new Error('Pinned runtime requires separate, non-overlapping source and verifier trees');
  return { source, target };
}
export async function preparePinnedStatusFixture(sourceInput, targetInput) {
  const { target } = await separateRuntimeRoots(sourceInput, targetInput);
  const change = join(target, 'openspec', 'changes', 'flights-m3-cancellation');
  await safeDestination(target, change);
  try {
    if (!(await lstat(change)).isDirectory()) throw new Error('Existing status change must be a physical directory');
    return false;
  } catch (error) {
    if (error.code !== 'ENOENT') throw error;
  }
  await mkdir(change, { recursive: true });
  await writeFile(join(change, '.openspec.yaml'), 'schema: spec-driven\n');
  await writeFile(
    join(change, 'proposal.md'),
    '# Isolated verifier fixture\n\nThis fictional scaffold verifies pinned CLI execution, not product-change status. It exists only in the owned temporary verifier clone.\n',
  );
  return true;
}
export async function preparePinnedRuntime(source, target) {
  await separateRuntimeRoots(source, target);
  const installation = await verifyInstallation(source);
  const physicalTarget = await realpath(target);
  const from = join(installation.tool, 'node_modules'),
    to = join(physicalTarget, 'tools', 'openspec', 'node_modules');
  await safeDestination(physicalTarget, to);
  await cp(from, to, {
    recursive: true,
    filter: (path) => !path.includes('.travel-read-config') && !path.split(/[\\/]/).includes('.bin'),
  });
  const cloned = await verifyInstallation(physicalTarget);
  const statusFixture = await preparePinnedStatusFixture(installation.physicalRoot, physicalTarget);
  const scope = join(cloned.tool, 'node_modules', '.travel-read-config');
  await mkdir(join(scope, 'openspec'), { recursive: true });
  await mkdir(join(scope, 'data'), { recursive: true });
  await writeFile(
    join(scope, 'openspec', 'config.json'),
    JSON.stringify({ profile: 'core', delivery: 'skills', telemetry: { enabled: false } }),
  );
  await writeFile(
    join(scope, 'scope.json'),
    JSON.stringify({
      kind: 'travel-openspec-read-scope-v1',
      repositoryRoot: cloned.physicalRoot,
      manifestHash: cloned.manifestHash,
    }),
  );
  return { environment: { ...process.env, TRAVEL_OPENSPEC_READ_CONFIG: scope }, scope, statusFixture };
}
export async function workspaceDigest(root) {
  return treeDigest(root, { skipPaths: new Set(['.git', 'tools/openspec/node_modules']) });
}
export async function assertSnapshotUnchanged(root, baseline) {
  if ((await workspaceDigest(root)) !== baseline.workspace)
    throw new Error('Codex verification produced unexpected writes in the working snapshot');
  if (baseline.scope && (await treeDigest(baseline.scope)) !== baseline.scopeDigest)
    throw new Error('Codex verification modified its read-only CLI scope');
  if (baseline.runtime) {
    await verifyInstallation(root);
    if (
      baseline.runtimeFootprint &&
      (await treeDigest(join(root, 'tools', 'openspec', 'node_modules'))) !== baseline.runtimeFootprint
    )
      throw new Error('Codex verification modified the runtime footprint');
  }
}
function tokens(text) {
  const result = [];
  let remaining = text.trim();
  while (remaining) {
    const match = /^(?:"([^"]*)"|'([^']*)'|([^\s"']+))(?:\s+|$)/.exec(remaining);
    if (!match) return null;
    result.push(match[1] ?? match[2] ?? match[3]);
    remaining = remaining.slice(match[0].length);
  }
  return result;
}
function pinnedInvocation(raw, root) {
  let command = String(raw).replaceAll('\\', '/').trim();
  if (/[;|\n\r]|&&|\$\(|`/.test(command)) return false;
  const wrapper =
    /^(?:"([^"]+)"|'([^']+)'|(\S+))\s+(?:(?:-NoProfile|-NoLogo|-NonInteractive)\s+)*(?:-Command|-c|-lc)\s+([\s\S]+)$/i.exec(
      command,
    );
  // Wrapper basename is not executable provenance; reject until independently attested.
  if (wrapper) return false;
  if (command.startsWith('& ')) command = command.slice(2).trim();
  const parts = tokens(command);
  if (parts?.length !== 6) return false;
  const normalize = (p) => (process.platform === 'win32' ? p.toLowerCase() : p);
  if (!['node', 'node.exe', normalize(process.execPath.replaceAll('\\', '/'))].includes(normalize(parts[0])))
    return false;
  if (normalize(resolve(root, parts[1])) !== normalize(join(root, 'tools', 'openspec', 'run.mjs'))) return false;
  return parts.slice(2).join('|') === 'status|--change|flights-m3-cancellation|--json';
}
export function validatePinnedCommandProof(records, root) {
  const matches = [];
  const normalizedRoot = resolve(root);
  for (const record of records) {
    const item = record?.item;
    if (
      record.type !== 'item.completed' ||
      item?.type !== 'command_execution' ||
      item.exit_code !== 0 ||
      item.status !== 'completed' ||
      !pinnedInvocation(item.command, normalizedRoot)
    )
      continue;
    try {
      const output = JSON.parse(item.aggregated_output.trim());
      if (
        output.changeName !== 'flights-m3-cancellation' ||
        output.schemaName !== 'spec-driven' ||
        typeof output.changeRoot !== 'string' ||
        resolve(output.changeRoot) !== join(normalizedRoot, 'openspec', 'changes', 'flights-m3-cancellation')
      )
        continue;
      matches.push(output);
    } catch {
      continue;
    }
  }
  if (matches.length !== 1) {
    const executions = records.filter(
      (record) => record.type === 'item.completed' && record.item?.type === 'command_execution',
    );
    const facts = executions.slice(0, 4).map(({ item }) => {
      let output;
      try {
        output = JSON.parse(item.aggregated_output.trim());
      } catch {
        output = undefined;
      }
      const raw = typeof item.command === 'string' ? item.command : '';
      const status = ['completed', 'failed', 'declined', 'in_progress', 'inProgress'].includes(item.status)
        ? item.status
        : 'unknown';
      const rootExact =
        typeof output?.changeRoot === 'string' &&
        resolve(output.changeRoot) === join(normalizedRoot, 'openspec', 'changes', 'flights-m3-cancellation');
      return `status=${status},zeroExit=${item.exit_code === 0},invocation=${pinnedInvocation(item.command, normalizedRoot)},compound=${/[;|\n\r]|&&|\$\(|`/.test(raw)},powershell=${/powershell|pwsh/i.test(raw)},json=${output !== undefined},identity=${output?.changeName === 'flights-m3-cancellation' && output?.schemaName === 'spec-driven'},root=${rootExact}`;
    });
    throw new Error(
      `Actual pinned launcher command execution proof is missing or ambiguous: executions=${executions.length}; ${facts.join('; ')}`,
    );
  }
  return matches[0];
}

export async function preparePinnedStatusPlan(rootInput, shellInput) {
  const root = await realpath(rootInput),
    node = await realpath(process.execPath),
    shell = await realpath(shellInput);
  const launcher = await assertPhysical(root, join(root, 'tools', 'openspec', 'run.mjs'));
  for (const path of [root, node, shell, launcher])
    if (/["'`$;|\r\n]/.test(path)) throw new Error('Unsupported path in exact status plan');
  const hashes = {};
  for (const path of [node, shell]) {
    const stat = await lstat(path);
    if (!stat.isFile() || stat.isSymbolicLink()) throw new Error('Status executable must be a physical file');
    hashes[path] = sha256(await readFile(path));
  }
  await verifyInstallation(root);
  return {
    root,
    node,
    shell,
    command: '& "' + node + '" "' + launcher + '" status --change flights-m3-cancellation --json',
    hashes,
  };
}
export async function assertPinnedStatusPlanUnchanged(plan) {
  for (const [path, hash] of Object.entries(plan.hashes))
    if (sha256(await readFile(path)) !== hash) throw new Error('Status executable changed');
  await verifyInstallation(plan.root);
}
export function matchesPinnedStatusPlan(raw, plan, { requireWrapper = true } = {}) {
  if (typeof raw !== 'string') return false;
  const norm = (text) =>
    process.platform === 'win32' ? text.replaceAll('\\', '/').replace(/\/+/g, '/').toLowerCase() : text;
  let command = raw.trim();
  const wrapper =
    /^(?:"([^"]+)"|'([^']+)'|(\S+))\s+((?:-(?:NoProfile|NoLogo|NonInteractive)\s+)*)-Command\s+([\s\S]+)$/i.exec(
      command,
    );
  if (wrapper) {
    if (norm(wrapper[1] ?? wrapper[2] ?? wrapper[3]) !== norm(plan.shell)) return false;
    const flags = wrapper[4].trim().toLowerCase().split(/\s+/);
    if (!flags.includes('-noprofile') || new Set(flags).size !== flags.length) return false;
    command = wrapper[5].trim();
    let decoded;
    if (command[0] === '"') {
      try {
        decoded = JSON.parse(command);
      } catch {
        // Older CLI displays used plain shell quoting rather than JSON strings.
      }
    }
    if (decoded !== undefined) {
      if (typeof decoded !== 'string' || JSON.stringify(decoded) !== command) return false;
      command = decoded;
    } else if ((command[0] === '"' || command[0] === "'") && command.at(-1) === command[0]) {
      command = command.slice(1, -1);
    }
  }
  if (requireWrapper && !wrapper) return false;
  return norm(command) === norm(plan.command);
}
export function validatePlannedStatusProof(messages, { plan, threadId, turnId, approvedItemId }) {
  const commands = messages.filter(
    (m) =>
      m.method === 'item/completed' &&
      m.params?.threadId === threadId &&
      m.params?.turnId === turnId &&
      m.params?.item?.type === 'commandExecution',
  );
  if (commands.length !== 1) {
    const types = {};
    for (const m of messages.filter((x) => x.method === 'item/completed' && x.params?.threadId === threadId)) {
      const candidate = m.params?.item?.type;
      const type = ['agentMessage', 'commandExecution', 'reasoning', 'dynamicToolCall', 'mcpToolCall'].includes(
        candidate,
      )
        ? candidate
        : 'other';
      types[type] = (types[type] ?? 0) + 1;
    }
    throw new Error(
      `Exact status command completion count differs: count=${commands.length}, grant=${approvedItemId === undefined ? 0 : 1}, types=${JSON.stringify(types)}`,
    );
  }
  const item = commands[0].params.item;
  const norm = (p) => (process.platform === 'win32' ? resolve(p).toLowerCase() : resolve(p));
  if (
    item.status !== 'completed' ||
    item.exitCode !== 0 ||
    typeof item.cwd !== 'string' ||
    norm(item.cwd) !== norm(plan.root) ||
    !matchesPinnedStatusPlan(item.command, plan) ||
    (approvedItemId !== undefined && item.id !== approvedItemId)
  )
    throw new Error(
      'Exact status execution/provenance binding differs: ' +
        JSON.stringify({
          status: ['completed', 'declined', 'failed'].includes(item.status) ? item.status : 'other',
          zeroExit: item.exitCode === 0,
          cwd: typeof item.cwd === 'string' && norm(item.cwd) === norm(plan.root),
          match: matchesPinnedStatusPlan(item.command, plan),
          node:
            typeof item.command === 'string' &&
            item.command.replaceAll('\\', '/').toLowerCase().includes(plan.node.replaceAll('\\', '/').toLowerCase()),
          shell:
            typeof item.command === 'string' &&
            item.command.replaceAll('\\', '/').toLowerCase().includes(plan.shell.replaceAll('\\', '/').toLowerCase()),
          flags:
            typeof item.command === 'string'
              ? [...item.command.matchAll(/-(NoProfile|NoLogo|NonInteractive|ExecutionPolicy|Command|File)\b/gi)].map(
                  (m) => m[1].toLowerCase(),
                )
              : [],
          approved: approvedItemId !== undefined,
        }),
    );
  const output = JSON.parse(item.aggregatedOutput.trim());
  if (
    output.changeName !== 'flights-m3-cancellation' ||
    output.schemaName !== 'spec-driven' ||
    typeof output.changeRoot !== 'string' ||
    norm(output.changeRoot) !== norm(join(plan.root, 'openspec', 'changes', 'flights-m3-cancellation'))
  )
    throw new Error('Status JSON binding differs');
  return output;
}
