import { spawn } from 'node:child_process';
import { access, mkdir, readdir, readFile, rm } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { basename, delimiter, dirname, isAbsolute, join, normalize, resolve } from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath, pathToFileURL } from 'node:url';

const DOMAIN_MODELER_DESCRIPTION =
  'Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence.';
const PROBE = 'TRAVEL_AI_HARNESS_IDENTITY_PROBE';
const ROLE_ID = 'travel-agency/domain-modeler';
const EXPLORE_PROMPT =
  'Use $explore-domain to map the current Flights module from repository evidence. Read only; do not edit.';
const MIGRATION_PROMPT =
  'Use $migration-authoring to review a hypothetical required-column change. Do not edit files, generate a migration, or touch a database.';
const ROOT_SPAWN_PROMPT =
  'Spawn the project custom agent named domain-modeler exactly once. Delegate exactly TRAVEL_AI_HARNESS_IDENTITY_PROBE and do nothing else. Return only the child result.';

function normalizedPath(path) {
  return normalize(path.replaceAll('\\\\', '\\')).replaceAll('\\', '/');
}

function walk(value, visit) {
  visit(value);
  if (Array.isArray(value)) {
    for (const entry of value) walk(entry, visit);
    return;
  }
  if (value && typeof value === 'object') {
    for (const entry of Object.values(value)) walk(entry, visit);
  }
}

export function validatePromptInputEvidence(
  payload,
  { cloneRoot, skillName, expectedAgentDescription = DOMAIN_MODELER_DESCRIPTION },
) {
  const expectedPath = normalizedPath(join(cloneRoot, '.agents', 'skills', skillName, 'SKILL.md'));
  const sourcePaths = new Set();
  let markerSeen = false;
  const agentDescriptions = new Set();
  walk(payload, (value) => {
    if (typeof value === 'string') {
      const candidate = normalizedPath(value);
      if (candidate.includes('/SKILL.md') && candidate.includes(`/.agents/skills/${skillName}/`)) {
        sourcePaths.add(isAbsolute(value) ? candidate : normalizedPath(resolve(cloneRoot, value)));
      }
      return;
    }
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      if (value.name === skillName) {
        for (const key of ['sourcePath', 'path', 'source', 'file', 'filePath']) {
          if (typeof value[key] !== 'string') continue;
          const candidate = normalizedPath(value[key]);
          sourcePaths.add(isAbsolute(value[key]) ? candidate : normalizedPath(resolve(cloneRoot, value[key])));
        }
        for (const key of ['body', 'content', 'text']) {
          if (
            typeof value[key] === 'string' &&
            value[key].includes(`Canonical Travel workflow ID: travel-agency/${skillName}.`)
          ) {
            markerSeen = true;
          }
        }
      }
      if (value.name === 'domain-modeler' && typeof value.description === 'string') {
        agentDescriptions.add(value.description);
      }
    }
  });
  if (sourcePaths.size === 0) {
    throw new Error(`prompt-input did not expose a source path for ${skillName}`);
  }
  if (sourcePaths.size !== 1 || !sourcePaths.has(expectedPath)) {
    throw new Error(`prompt-input exposed a distinct skill source for ${skillName}`);
  }
  if (!markerSeen) {
    throw new Error(`prompt-input skill body lacks the canonical workflow marker for ${skillName}`);
  }
  if (agentDescriptions.size === 0) {
    throw new Error('prompt-input lacks the project domain-modeler mapping');
  }
  if (agentDescriptions.size !== 1 || !agentDescriptions.has(expectedAgentDescription)) {
    throw new Error('prompt-input contains a conflicting domain-modeler mapping');
  }
  return { skillPath: join(cloneRoot, '.agents', 'skills', skillName, 'SKILL.md') };
}

function parsePersonalAgentToml(text) {
  let remaining = text.replace(/^[ \t]*#[^\n]*(?:\n|$)/gm, '');
  remaining = remaining.replace(/^[ \t]*[A-Za-z_][A-Za-z0-9_-]*[ \t]*=[ \t]*"""[\s\S]*?"""[ \t]*(?:\n|$)/gm, '');
  const values = {};
  const lines = remaining.split(/\r?\n/);
  for (const line of lines) {
    if (!line.trim()) continue;
    const match = /^[ \t]*([A-Za-z_][A-Za-z0-9_-]*)[ \t]*=[ \t]*"([^"\\]*)"[ \t]*$/.exec(line);
    if (!match || Object.hasOwn(values, match[1])) {
      throw new Error('personal-agent inventory not provable');
    }
    values[match[1]] = match[2];
  }
  if (typeof values.name !== 'string' || values.name.length === 0) {
    throw new Error('personal-agent inventory not provable');
  }
  return values;
}

export async function inspectPersonalAgents({ codexHome, homeDirectory } = {}) {
  const activeRoot = resolve(codexHome ?? process.env.CODEX_HOME ?? join(homeDirectory ?? homedir(), '.codex'));
  const agentsRoot = join(activeRoot, 'agents');
  let entries;
  try {
    entries = await readdir(agentsRoot, { withFileTypes: true });
  } catch (error) {
    if (error?.code === 'ENOENT') return { inspected: 0 };
    throw new Error('personal-agent inventory not provable', { cause: error });
  }
  let inspected = 0;
  for (const entry of entries) {
    if (!entry.isFile() || !entry.name.toLowerCase().endsWith('.toml')) continue;
    inspected += 1;
    let parsed;
    try {
      parsed = parsePersonalAgentToml(await readFile(join(agentsRoot, entry.name), 'utf8'));
    } catch (error) {
      throw new Error('personal-agent inventory not provable', { cause: error });
    }
    if (parsed.name === 'domain-modeler') {
      throw new Error('personal-agent collision: domain-modeler');
    }
  }
  return { inspected };
}

export function parseJsonLines(text) {
  const records = [];
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  for (let index = 0; index < lines.length; index += 1) {
    if (!lines[index].trim()) continue;
    try {
      records.push(JSON.parse(lines[index]));
    } catch (error) {
      throw new Error(`malformed JSONL record ${index + 1}`, { cause: error });
    }
  }
  return records;
}

function turnFromNotification(message) {
  return message?.params?.turn ?? message?.params;
}

export function collectAppServerEvidence(messages, { rootThreadId, rootTurnId, probe = PROBE }) {
  const rootCompletions = messages.filter((message) => {
    if (message?.method !== 'turn/completed') return false;
    const turn = turnFromNotification(message);
    return turn?.id === rootTurnId && message.params?.threadId === rootThreadId;
  });
  if (rootCompletions.length !== 1) {
    throw new Error('exact root turn completion evidence is required');
  }
  const rootTurn = turnFromNotification(rootCompletions[0]);
  if (rootTurn.status !== 'completed') {
    throw new Error(`root turn status must be completed, got ${rootTurn.status ?? 'missing'}`);
  }
  const spawns = messages.filter((message) => {
    const item = message?.params?.item;
    return (
      message?.method === 'item/completed' &&
      message.params?.threadId === rootThreadId &&
      message.params?.turnId === rootTurnId &&
      item?.type === 'collabToolCall' &&
      item?.tool === 'spawn_agent'
    );
  });
  if (spawns.length !== 1) {
    throw new Error('exactly one authoritative spawn_agent collaboration item is required');
  }
  const item = spawns[0].params.item;
  if (item.status !== 'completed') {
    throw new Error('spawn_agent collaboration item must be completed');
  }
  if (item.senderThreadId !== rootThreadId) {
    throw new Error('spawn_agent sender does not match the root thread');
  }
  if (item.prompt !== probe) {
    throw new Error('spawn_agent delegated prompt does not match the identity probe');
  }
  if (typeof item.newThreadId !== 'string' || item.newThreadId.length === 0) {
    throw new Error('spawn_agent is missing a child thread ID');
  }
  return { childThreadId: item.newThreadId };
}

function itemText(item) {
  if (typeof item?.text === 'string') return item.text;
  if (typeof item?.content === 'string') return item.content;
  if (Array.isArray(item?.content)) {
    return item.content.map((part) => (typeof part === 'string' ? part : (part?.text ?? ''))).join('');
  }
  return '';
}

export function assertChildEvidence(response, { childThreadId, roleId = ROLE_ID }) {
  const thread = response?.thread ?? response;
  if (thread?.id !== childThreadId || !Array.isArray(thread?.turns)) {
    throw new Error('child thread evidence is unavailable');
  }
  const items = thread.turns.flatMap((turn) => turn?.items ?? []);
  const toolTypes = new Set([
    'commandExecution',
    'fileChange',
    'mcpToolCall',
    'collabToolCall',
    'webSearch',
    'imageGeneration',
  ]);
  if (items.some((item) => toolTypes.has(item?.type))) {
    throw new Error('child tool use is forbidden for the identity probe');
  }
  const identityMessages = items.filter((item) => item?.type === 'agentMessage' && itemText(item) === roleId);
  if (identityMessages.length !== 1) {
    throw new Error('child agentMessage must contain the exact repository role ID');
  }
}

export function assertListedChild(response, { rootThreadId, childThreadId }) {
  const threads = response?.data ?? response?.threads ?? [];
  const matches = threads.filter(
    (thread) =>
      thread?.id === childThreadId &&
      thread?.parentThreadId === rootThreadId &&
      typeof thread?.source === 'string' &&
      thread.source.startsWith('subAgent'),
  );
  if (matches.length !== 1) {
    throw new Error('thread/list did not return the exact linked child');
  }
}

export function validateCleanupTarget(target, base = tmpdir()) {
  if (!target) throw new Error('cleanup target is empty');
  const resolvedBase = resolve(base);
  const resolvedTarget = resolve(target);
  const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
  if (dirname(resolvedTarget) !== resolvedBase || !guid.test(basename(resolvedTarget))) {
    throw new Error('cleanup target must be a GUID directory directly under the temp base');
  }
  return resolvedTarget;
}

function generateGuid() {
  const hex = () => Math.floor(Math.random() * 16).toString(16);
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (token) => {
    const value = Number.parseInt(hex(), 16);
    return token === 'x' ? value.toString(16) : ((value & 3) | 8).toString(16);
  });
}

async function findExecutable(name, environment = process.env) {
  const pathValue = environment.PATH ?? environment.Path ?? '';
  const extensions = process.platform === 'win32' ? ['.cmd', '.exe', '.bat', ''] : [''];
  for (const directory of pathValue.split(delimiter).filter(Boolean)) {
    for (const extension of extensions) {
      const candidate = join(directory, `${name}${extension}`);
      try {
        await access(candidate);
        return candidate;
      } catch {
        // Continue the bounded PATH search.
      }
    }
  }
  throw new Error(`required CLI is unavailable: ${name}`);
}

function runProcessDefault(file, args, { cwd, input, timeoutMs = 120_000 } = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(file, args, { cwd, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => {
      child.kill();
      rejectPromise(new Error(`process timeout: ${basename(file)}`));
    }, timeoutMs);
    child.stdout.setEncoding('utf8').on('data', (chunk) => {
      stdout += chunk;
    });
    child.stderr.setEncoding('utf8').on('data', (chunk) => {
      stderr += chunk;
    });
    child.on('error', (error) => {
      clearTimeout(timer);
      rejectPromise(error);
    });
    child.on('close', (code) => {
      clearTimeout(timer);
      if (code !== 0) {
        rejectPromise(new Error(`${basename(file)} failed with exit ${code}: ${stderr.trim()}`));
        return;
      }
      resolvePromise({ stdout, stderr });
    });
    if (input !== undefined) child.stdin.write(input);
    child.stdin.end();
  });
}

function createAppServerDefault(file, args, { cwd }) {
  const child = spawn(file, args, { cwd, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  const messages = [];
  const pending = new Map();
  const waiters = new Set();
  let nextId = 1;
  let stderr = '';
  child.stderr.setEncoding('utf8').on('data', (chunk) => {
    stderr += chunk;
  });
  const lines = createInterface({ input: child.stdout, crlfDelay: Number.POSITIVE_INFINITY });
  lines.on('line', (line) => {
    let message;
    try {
      message = JSON.parse(line);
    } catch (error) {
      for (const { reject } of pending.values())
        reject(new Error('App Server emitted malformed JSON', { cause: error }));
      pending.clear();
      return;
    }
    if (message.id !== undefined && pending.has(message.id)) {
      const operation = pending.get(message.id);
      pending.delete(message.id);
      if (message.error) operation.reject(new Error(`App Server error: ${JSON.stringify(message.error)}`));
      else operation.resolve(message.result);
      return;
    }
    messages.push(message);
    for (const waiter of waiters) waiter(message);
  });
  child.on('error', (error) => {
    for (const { reject } of pending.values()) reject(error);
    pending.clear();
  });
  function send(payload) {
    child.stdin.write(`${JSON.stringify(payload)}\n`);
  }
  function request(method, params, timeoutMs = 60_000) {
    const id = nextId;
    nextId += 1;
    return new Promise((resolvePromise, rejectPromise) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        rejectPromise(new Error(`App Server request timeout: ${method}`));
      }, timeoutMs);
      pending.set(id, {
        resolve(value) {
          clearTimeout(timer);
          resolvePromise(value);
        },
        reject(error) {
          clearTimeout(timer);
          rejectPromise(error);
        },
      });
      send({ jsonrpc: '2.0', id, method, params });
    });
  }
  function notify(method, params) {
    send({ jsonrpc: '2.0', method, params });
  }
  function waitFor(predicate, timeoutMs = 120_000) {
    const existing = messages.find(predicate);
    if (existing) return Promise.resolve(existing);
    return new Promise((resolvePromise, rejectPromise) => {
      const timer = setTimeout(() => {
        waiters.delete(onMessage);
        rejectPromise(new Error('App Server notification timeout'));
      }, timeoutMs);
      function onMessage(message) {
        if (!predicate(message)) return;
        clearTimeout(timer);
        waiters.delete(onMessage);
        resolvePromise(message);
      }
      waiters.add(onMessage);
    });
  }
  async function stop() {
    lines.close();
    child.stdin.end();
    if (!child.killed) child.kill();
  }
  return { messages, notify, request, stderr: () => stderr, stop, waitFor };
}

function finalExecOutput(records) {
  const candidates = [];
  walk(records, (value) => {
    if (
      value &&
      typeof value === 'object' &&
      !Array.isArray(value) &&
      (value.type === 'agent_message' || value.type === 'agentMessage')
    ) {
      const text = itemText(value);
      if (text) candidates.push(text);
    }
  });
  if (candidates.length === 0) throw new Error('Codex exec did not return a final agent response');
  return candidates.at(-1);
}

async function assertCloneClean(git, root, runProcess) {
  const { stdout } = await runProcess(git, ['-C', root, 'status', '--porcelain=v1'], {
    cwd: root,
  });
  if (stdout.trim()) throw new Error('Codex verification produced unexpected writes in the clone');
}

export async function runCodexVerifier(repository = process.cwd(), dependencies = {}) {
  const runProcess = dependencies.runProcess ?? runProcessDefault;
  const createAppServer = dependencies.createAppServer ?? createAppServerDefault;
  const inspectAgents = dependencies.inspectPersonalAgents ?? inspectPersonalAgents;
  const remove = dependencies.remove ?? rm;
  const tempBase = resolve(dependencies.tempBase ?? tmpdir());
  const sourceRoot = resolve(repository);
  const git = await (dependencies.findExecutable ?? findExecutable)('git');
  const codex = await (dependencies.findExecutable ?? findExecutable)('codex');
  const { stdout: version } = await runProcess(codex, ['--version'], { cwd: sourceRoot });
  process.stdout.write(`Codex: ${version.trim()}\n`);

  let cloneRoot;
  let appServer;
  let rootThreadId;
  let primaryError;
  const cleanupErrors = [];
  try {
    for (let attempt = 0; attempt < 4; attempt += 1) {
      const candidate = validateCleanupTarget(join(tempBase, generateGuid()), tempBase);
      try {
        await mkdir(candidate);
        await rm(candidate, { recursive: true });
        cloneRoot = candidate;
        break;
      } catch (error) {
        if (attempt === 3) throw error;
      }
    }
    if (!cloneRoot) throw new Error('could not reserve a verifier GUID directory');
    await runProcess(git, ['clone', '--no-hardlinks', sourceRoot, cloneRoot], {
      cwd: sourceRoot,
      timeoutMs: 120_000,
    });
    await runProcess(process.execPath, [join(cloneRoot, 'tools', 'ai-harness', 'validate.mjs'), cloneRoot], {
      cwd: cloneRoot,
    });

    const promptRuns = [
      ['explore-domain', join(cloneRoot, 'modules', 'flights'), EXPLORE_PROMPT],
      ['migration-authoring', cloneRoot, MIGRATION_PROMPT],
    ];
    for (const [skillName, cwd, prompt] of promptRuns) {
      const { stdout } = await runProcess(codex, ['debug', 'prompt-input', '--cwd', cwd, '--json', prompt], { cwd });
      let promptEvidence;
      try {
        promptEvidence = JSON.parse(stdout);
      } catch (error) {
        throw new Error('Codex prompt-input schema is not valid JSON', { cause: error });
      }
      validatePromptInputEvidence(promptEvidence, {
        cloneRoot,
        skillName,
        expectedAgentDescription: DOMAIN_MODELER_DESCRIPTION,
      });
    }

    await inspectAgents();

    for (const [skillName, cwd, prompt] of promptRuns) {
      const { stdout } = await runProcess(
        codex,
        ['exec', '--ephemeral', '--ignore-user-config', '--sandbox', 'read-only', '--json', prompt],
        { cwd, timeoutMs: 180_000 },
      );
      const output = finalExecOutput(parseJsonLines(stdout));
      if (
        skillName === 'migration-authoring' &&
        !output.includes('docs/adr/0007-storage-strategy-marten-ef-coexistence.md')
      ) {
        throw new Error('migration-authoring smoke did not cite the required storage ADR');
      }
      process.stdout.write(`\n${skillName} response:\n${output}\n`);
    }

    appServer = createAppServer(codex, ['app-server'], { cwd: cloneRoot });
    await appServer.request('initialize', {
      clientInfo: { name: 'travel-ai-harness-verifier', version: '1' },
      capabilities: { experimentalApi: true },
    });
    appServer.notify('initialized', {});
    const startedThread = await appServer.request('thread/start', {
      cwd: cloneRoot,
      approvalPolicy: 'never',
      sandbox: 'readOnly',
    });
    const rootThread = startedThread?.thread ?? startedThread;
    rootThreadId = rootThread?.id;
    if (typeof rootThreadId !== 'string' || rootThreadId.length === 0) {
      throw new Error('App Server thread/start did not return a root thread ID');
    }
    const instructionSources = rootThread?.instructionSources ?? [];
    const expectedInstructions = normalizedPath(join(cloneRoot, 'AGENTS.md'));
    if (
      !instructionSources.some((source) => {
        const value = typeof source === 'string' ? source : (source?.path ?? source?.source);
        return typeof value === 'string' && normalizedPath(value) === expectedInstructions;
      })
    ) {
      throw new Error('root thread did not report the clone-root AGENTS.md instruction source');
    }
    if (ROOT_SPAWN_PROMPT.includes(ROLE_ID)) {
      throw new Error('identity probe prompt must not contain the expected role ID');
    }
    const startedTurn = await appServer.request('turn/start', {
      threadId: rootThreadId,
      input: [{ type: 'text', text: ROOT_SPAWN_PROMPT }],
    });
    const rootTurnId = (startedTurn?.turn ?? startedTurn)?.id;
    if (typeof rootTurnId !== 'string' || rootTurnId.length === 0) {
      throw new Error('App Server turn/start did not return the root turn ID');
    }
    await appServer.waitFor((message) => {
      if (message?.method !== 'turn/completed') return false;
      const turn = turnFromNotification(message);
      return turn?.id === rootTurnId && message.params?.threadId === rootThreadId;
    });
    const { childThreadId } = collectAppServerEvidence(appServer.messages, {
      rootThreadId,
      rootTurnId,
      probe: PROBE,
    });
    const child = await appServer.request('thread/read', {
      threadId: childThreadId,
      includeTurns: true,
    });
    assertChildEvidence(child, { childThreadId, roleId: ROLE_ID });
    const listed = await appServer.request('thread/list', {
      parentThreadId: rootThreadId,
      sourceKinds: ['subAgent', 'subAgentReview', 'subAgentCompact', 'subAgentOther'],
    });
    assertListedChild(listed, { rootThreadId, childThreadId });
    await assertCloneClean(git, cloneRoot, runProcess);
    process.stdout.write(
      'Verified tracked skill provenance and a real project-agent child spawn. The current public protocol does not return the selected custom-agent TOML source path directly.\n',
    );
  } catch (error) {
    primaryError = error;
  } finally {
    if (appServer && rootThreadId) {
      try {
        await appServer.request('thread/delete', { threadId: rootThreadId });
      } catch (error) {
        cleanupErrors.push(new Error('failed to delete the verifier-owned root thread', { cause: error }));
      }
    }
    if (appServer) {
      try {
        await appServer.stop();
      } catch (error) {
        cleanupErrors.push(new Error('failed to stop the verifier-owned App Server', { cause: error }));
      }
    }
    if (cloneRoot) {
      try {
        await remove(validateCleanupTarget(cloneRoot, tempBase), { recursive: true, force: true });
      } catch (error) {
        cleanupErrors.push(new Error('failed to remove the verifier-owned clone', { cause: error }));
      }
    }
  }
  if (cleanupErrors.length > 0) {
    throw new AggregateError(
      primaryError ? [primaryError, ...cleanupErrors] : cleanupErrors,
      'Codex verifier cleanup failed',
    );
  }
  if (primaryError) throw primaryError;
}

async function main() {
  try {
    await runCodexVerifier(process.argv[2] ?? process.cwd());
  } catch (error) {
    process.stderr.write(`AI harness Codex verification failed: ${error.message}\n`);
    process.exitCode = 1;
  }
}

const isDirect =
  process.argv[1] !== undefined &&
  pathToFileURL(resolve(process.argv[1])).href === pathToFileURL(fileURLToPath(import.meta.url)).href;
if (isDirect) {
  await main();
}
