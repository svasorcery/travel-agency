import { spawn } from 'node:child_process';
import { access, mkdir, readdir, readFile, rm } from 'node:fs/promises';
import { homedir, tmpdir } from 'node:os';
import { basename, delimiter, dirname, isAbsolute, join, normalize, resolve, win32 } from 'node:path';
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

export async function inspectPersonalAgents({
  codexHome,
  homeDirectory,
  readDirectory = readdir,
  readText = readFile,
} = {}) {
  const activeRoot = resolve(codexHome ?? process.env.CODEX_HOME ?? join(homeDirectory ?? homedir(), '.codex'));
  const agentsRoot = join(activeRoot, 'agents');
  let entries;
  try {
    entries = await readDirectory(agentsRoot, { withFileTypes: true });
  } catch (error) {
    if (error?.code === 'ENOENT') return { inspected: 0 };
    throw new Error('personal-agent inventory not provable', { cause: error });
  }
  let inspected = 0;
  for (const entry of entries) {
    if (!entry.name.toLowerCase().endsWith('.toml')) continue;
    if (!entry.isFile() || entry.isSymbolicLink?.()) {
      throw new Error('personal-agent inventory not provable');
    }
    inspected += 1;
    let parsed;
    try {
      parsed = parsePersonalAgentToml(await readText(join(agentsRoot, entry.name), 'utf8'));
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

export function parseJsonRpcResponse(message, expectedId) {
  if (!message || typeof message !== 'object' || Array.isArray(message)) {
    throw new Error('JSON-RPC response schema is invalid');
  }
  const hasResult = Object.hasOwn(message, 'result');
  const hasError = Object.hasOwn(message, 'error');
  if (message.jsonrpc !== '2.0' || message.id !== expectedId || hasResult === hasError) {
    throw new Error('JSON-RPC response schema is invalid');
  }
  if (hasError) {
    if (
      !message.error ||
      typeof message.error !== 'object' ||
      Array.isArray(message.error) ||
      !Number.isInteger(message.error.code) ||
      typeof message.error.message !== 'string'
    ) {
      throw new Error('JSON-RPC response schema is invalid');
    }
    throw new Error(`App Server error ${message.error.code}: ${message.error.message}`);
  }
  return message.result;
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
  if (thread.turns.length !== 1 || thread.turns[0]?.status !== 'completed') {
    throw new Error('exactly one authoritative completed child turn is required');
  }
  const items = thread.turns[0].items ?? [];
  const allowedItemTypes = new Set(['agentMessage', 'reasoning']);
  const unsupported = items.find((item) => !allowedItemTypes.has(item?.type));
  if (unsupported) {
    const knownToolTypes = new Set([
      'commandExecution',
      'fileChange',
      'mcpToolCall',
      'collabToolCall',
      'webSearch',
      'imageGeneration',
    ]);
    if (knownToolTypes.has(unsupported.type)) {
      throw new Error('child tool use is forbidden for the identity probe');
    }
    throw new Error(`unsupported child item type: ${unsupported.type ?? 'missing'}`);
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

function errorDetail(error) {
  const maxDepth = 32;
  const maxLength = 4_096;
  const truncatedMarker = '[truncated]';
  const contentLimit = maxLength - truncatedMarker.length;
  const parts = [];
  const seen = new Set();
  let length = 0;
  let outputTruncated = false;

  function truncateOutput() {
    if (outputTruncated) return;
    parts.push(truncatedMarker);
    length += truncatedMarker.length;
    outputTruncated = true;
  }

  function append(value) {
    if (outputTruncated) return false;
    const text = String(value);
    const available = contentLimit - length;
    if (text.length <= available) {
      parts.push(text);
      length += text.length;
      return true;
    }
    if (available > 0) {
      parts.push(text.slice(0, available));
      length += available;
    }
    truncateOutput();
    return false;
  }

  function visit(value, depth) {
    if (outputTruncated) return;
    if (depth > maxDepth) {
      append(truncatedMarker);
      return;
    }
    if ((typeof value === 'object' && value !== null) || typeof value === 'function') {
      if (seen.has(value)) {
        append('[circular]');
        return;
      }
      seen.add(value);
    }
    try {
      const message = value?.message;
      append(message ?? String(value));
      if (value instanceof AggregateError) {
        const errors = value.errors;
        if (Array.isArray(errors)) {
          append(': [');
          for (const [index, entry] of errors.entries()) {
            if (index > 0) append('; ');
            visit(entry, depth + 1);
          }
          append(']');
        }
      }
      const cause = value?.cause;
      if (cause) {
        append(': ');
        visit(cause, depth + 1);
      }
    } catch {
      append('[unprintable error]');
    }
  }

  visit(error, 0);
  return parts.join('');
}

export function formatVerifierFailure(error) {
  const prefix = 'AI harness Codex verification failed:';
  try {
    if (!(error instanceof AggregateError) || !Array.isArray(error.cleanupErrors)) {
      return `${prefix} ${errorDetail(error)}`;
    }
    const lines = [prefix];
    if (error.primaryError) lines.push(`- primary: ${errorDetail(error.primaryError)}`);
    for (const cleanupError of error.cleanupErrors) {
      lines.push(`- cleanup: ${errorDetail(cleanupError)}`);
    }
    return lines.join('\n');
  } catch {
    return `${prefix} [unprintable error]`;
  }
}

function generateGuid() {
  const hex = () => Math.floor(Math.random() * 16).toString(16);
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (token) => {
    const value = Number.parseInt(hex(), 16);
    return token === 'x' ? value.toString(16) : ((value & 3) | 8).toString(16);
  });
}

export function executableExtensions(platform = process.platform) {
  return platform === 'win32' ? ['.exe', '.cmd', '.bat', ''] : [''];
}

export function normalizeProcessLaunch(
  file,
  args,
  { platform = process.platform, comspec = process.env.ComSpec ?? process.env.COMSPEC } = {},
) {
  const extension = file.slice(file.lastIndexOf('.')).toLowerCase();
  if (platform !== 'win32' || (extension !== '.cmd' && extension !== '.bat')) {
    return { file, args, windowsVerbatimArguments: false };
  }
  if (!comspec) throw new Error('Windows command processor is unavailable for batch CLI fallback');
  const tokens = [file, ...args];
  for (const token of tokens) {
    if (typeof token !== 'string' || /[&|<>^%!"\r\n]/.test(token)) {
      throw new Error('unsafe Windows command token for batch CLI fallback');
    }
  }
  const command = `"${tokens.map((token) => `"${token}"`).join(' ')}"`;
  return {
    file: comspec,
    args: ['/d', '/s', '/v:off', '/c', command],
    windowsVerbatimArguments: true,
  };
}

async function findExecutable(name, environment = process.env) {
  const pathValue = environment.PATH ?? environment.Path ?? '';
  const extensions = executableExtensions();
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

const childLifecycles = new WeakMap();

function childLifecycle(child) {
  const existing = childLifecycles.get(child);
  if (existing) return existing;
  const state = { closed: false, result: undefined, stopPromise: undefined };
  state.closePromise = new Promise((resolvePromise, rejectPromise) => {
    child.once('close', (code, signal) => {
      state.closed = true;
      state.result = { code, signal };
      resolvePromise(state.result);
    });
    child.once('error', rejectPromise);
  });
  childLifecycles.set(child, state);
  return state;
}

function observeChildClose(child) {
  return childLifecycle(child).closePromise;
}

function bounded(promise, timeoutMs, label) {
  return new Promise((resolvePromise, rejectPromise) => {
    const timer = setTimeout(() => rejectPromise(new Error(`${label} timeout`)), timeoutMs);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolvePromise(value);
      },
      (error) => {
        clearTimeout(timer);
        rejectPromise(error);
      },
    );
  });
}

export function stopOwnedProcess(
  child,
  {
    platform = process.platform,
    spawnProcess = spawn,
    killProcess = process.kill,
    closePromise,
    systemRoot = process.env.SystemRoot ?? process.env.SYSTEMROOT,
    timeoutMs = 5_000,
  } = {},
) {
  const lifecycle = childLifecycle(child);
  if (lifecycle.stopPromise) return lifecycle.stopPromise;
  lifecycle.stopPromise = (async () => {
    if (!Number.isInteger(child?.pid) || child.pid <= 0) {
      throw new Error('owned process PID is unavailable');
    }
    if (lifecycle.closed) return;
    const exited = child.exitCode !== null || child.signalCode !== null;
    if (!exited && platform === 'win32') {
      if (typeof systemRoot !== 'string' || !win32.isAbsolute(systemRoot) || systemRoot.startsWith('\\\\')) {
        throw new Error('trusted Windows system root is unavailable');
      }
      const normalizedSystemRoot = win32.normalize(systemRoot);
      const parsedSystemRoot = win32.parse(normalizedSystemRoot);
      if (
        win32.basename(normalizedSystemRoot).toLowerCase() !== 'windows' ||
        win32.dirname(normalizedSystemRoot).toLowerCase() !== parsedSystemRoot.root.toLowerCase()
      ) {
        throw new Error('trusted Windows system root must be the drive-root Windows directory');
      }
      const taskkillPath = win32.join(normalizedSystemRoot, 'System32', 'taskkill.exe');
      if (win32.dirname(taskkillPath) !== win32.join(normalizedSystemRoot, 'System32')) {
        throw new Error('trusted Windows taskkill path escaped System32');
      }
      const taskkill = spawnProcess(taskkillPath, ['/pid', String(child.pid), '/t', '/f'], {
        stdio: 'ignore',
        windowsHide: true,
      });
      const taskkillResult = await bounded(observeChildClose(taskkill), timeoutMs, 'process-tree termination');
      if (taskkillResult.code !== 0) {
        throw new Error(`process-tree termination failed with exit ${taskkillResult.code ?? 'missing'}`);
      }
    } else if (!exited) {
      try {
        killProcess(-child.pid, 'SIGTERM');
      } catch (error) {
        if (error?.code !== 'ESRCH') throw error;
      }
    }
    await bounded(closePromise ?? lifecycle.closePromise, timeoutMs, 'owned process close');
  })();
  return lifecycle.stopPromise;
}

export function runProcess(file, args, { cwd, input, timeoutMs = 120_000 } = {}, dependencies = {}) {
  return new Promise((resolvePromise, rejectPromise) => {
    const platform = dependencies.platform ?? process.platform;
    const spawnProcess = dependencies.spawnProcess ?? spawn;
    const terminateProcess = dependencies.terminateProcess ?? stopOwnedProcess;
    const launch = normalizeProcessLaunch(file, args, {
      platform,
      comspec: dependencies.comspec,
    });
    const child = spawnProcess(launch.file, launch.args, {
      cwd,
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
      windowsVerbatimArguments: launch.windowsVerbatimArguments,
      detached: platform !== 'win32',
    });
    let stdout = '';
    let stderr = '';
    let timedOut = false;
    const closePromise = observeChildClose(child);
    const timer = setTimeout(async () => {
      timedOut = true;
      try {
        await terminateProcess(child, {
          platform,
          spawnProcess,
          closePromise,
          systemRoot: dependencies.systemRoot,
          timeoutMs: dependencies.stopTimeoutMs ?? 5_000,
        });
        rejectPromise(new Error(`process timeout: ${basename(file)}`));
      } catch (error) {
        rejectPromise(new AggregateError([error], `process timeout cleanup failed: ${basename(file)}`));
      }
    }, timeoutMs);
    child.stdout.setEncoding('utf8').on('data', (chunk) => {
      stdout += chunk;
    });
    child.stderr.setEncoding('utf8').on('data', (chunk) => {
      stderr += chunk;
    });
    closePromise.then(
      ({ code }) => {
        clearTimeout(timer);
        if (timedOut) return;
        if (code !== 0) {
          rejectPromise(new Error(`${basename(file)} failed with exit ${code}: ${stderr.trim()}`));
          return;
        }
        resolvePromise({ stdout, stderr });
      },
      (error) => {
        clearTimeout(timer);
        if (!timedOut) rejectPromise(error);
      },
    );
    if (input !== undefined) child.stdin.write(input);
    child.stdin.end();
  });
}

function runProcessDefault(file, args, options) {
  return runProcess(file, args, options);
}

function createAppServerDefault(file, args, { cwd }) {
  const launch = normalizeProcessLaunch(file, args);
  const child = spawn(launch.file, launch.args, {
    cwd,
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,
    windowsVerbatimArguments: launch.windowsVerbatimArguments,
    detached: process.platform !== 'win32',
  });
  return createAppServerClient(child);
}

export function createAppServerClient(
  child,
  { lineReaderFactory = createInterface, stopProcess = stopOwnedProcess } = {},
) {
  const closePromise = observeChildClose(child);
  const messages = [];
  const pending = new Map();
  const waiters = new Set();
  let nextId = 1;
  let stderr = '';
  let fatalError;
  let stopPromise;
  child.stderr.setEncoding('utf8').on('data', (chunk) => {
    stderr += chunk;
  });
  const lines = lineReaderFactory({ input: child.stdout, crlfDelay: Number.POSITIVE_INFINITY });

  function failFatal(error) {
    if (fatalError) return fatalError;
    fatalError = error;
    for (const operation of pending.values()) operation.reject(fatalError);
    pending.clear();
    for (const waiter of waiters) waiter.reject(fatalError);
    waiters.clear();
    return fatalError;
  }

  closePromise.then(
    ({ code, signal }) => {
      failFatal(new Error(`App Server closed: exit ${code ?? 'null'}, signal ${signal ?? 'none'}`));
    },
    (error) => {
      failFatal(new Error('App Server fatal: child process error', { cause: error }));
    },
  );

  lines.on('line', (line) => {
    if (fatalError) return;
    let message;
    try {
      message = JSON.parse(line);
    } catch (error) {
      failFatal(new Error('App Server fatal: malformed JSON stream', { cause: error }));
      return;
    }
    if (!message || typeof message !== 'object' || Array.isArray(message)) {
      failFatal(new Error('App Server fatal: invalid notification'));
      return;
    }
    if (Object.hasOwn(message, 'id') && !pending.has(message.id)) {
      failFatal(new Error(`App Server fatal: unknown response ID ${String(message.id)}`));
      return;
    }
    if (Object.hasOwn(message, 'id')) {
      const operation = pending.get(message.id);
      try {
        const result = parseJsonRpcResponse(message, message.id);
        pending.delete(message.id);
        operation.resolve(result);
      } catch (error) {
        const validError =
          message.jsonrpc === '2.0' &&
          Object.hasOwn(message, 'error') &&
          !Object.hasOwn(message, 'result') &&
          message.error &&
          typeof message.error === 'object' &&
          !Array.isArray(message.error) &&
          Number.isInteger(message.error.code) &&
          typeof message.error.message === 'string';
        if (validError) {
          pending.delete(message.id);
          operation.reject(error);
        } else {
          failFatal(new Error('App Server fatal: invalid JSON-RPC response', { cause: error }));
        }
      }
      return;
    }
    const validParams =
      !Object.hasOwn(message, 'params') || (message.params !== null && typeof message.params === 'object');
    if (
      message.jsonrpc !== '2.0' ||
      typeof message.method !== 'string' ||
      message.method.length === 0 ||
      Object.hasOwn(message, 'result') ||
      Object.hasOwn(message, 'error') ||
      !validParams
    ) {
      failFatal(new Error('App Server fatal: invalid notification'));
      return;
    }
    messages.push(message);
    for (const waiter of waiters) waiter.onMessage(message);
  });
  function send(payload) {
    if (fatalError) throw fatalError;
    child.stdin.write(`${JSON.stringify(payload)}\n`);
  }
  function request(method, params, timeoutMs = 60_000) {
    if (fatalError) return Promise.reject(fatalError);
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
      try {
        send({ jsonrpc: '2.0', id, method, params });
      } catch (error) {
        pending.get(id)?.reject(error);
        pending.delete(id);
      }
    });
  }
  function notify(method, params) {
    send({ jsonrpc: '2.0', method, params });
  }
  function waitFor(predicate, timeoutMs = 120_000) {
    if (fatalError) return Promise.reject(fatalError);
    const existing = messages.find(predicate);
    if (existing) return Promise.resolve(existing);
    return new Promise((resolvePromise, rejectPromise) => {
      const timer = setTimeout(() => {
        waiters.delete(waiter);
        rejectPromise(new Error('App Server notification timeout'));
      }, timeoutMs);
      function onMessage(message) {
        if (!predicate(message)) return;
        clearTimeout(timer);
        waiters.delete(waiter);
        resolvePromise(message);
      }
      const waiter = {
        onMessage,
        reject(error) {
          clearTimeout(timer);
          rejectPromise(error);
        },
      };
      waiters.add(waiter);
    });
  }
  function stop() {
    if (stopPromise) return stopPromise;
    stopPromise = (async () => {
      lines.close();
      child.stdin.end();
      await stopProcess(child, { closePromise });
    })();
    return stopPromise;
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
    const aggregate = new AggregateError(
      primaryError ? [primaryError, ...cleanupErrors] : cleanupErrors,
      'Codex verifier cleanup failed',
    );
    aggregate.primaryError = primaryError;
    aggregate.cleanupErrors = cleanupErrors;
    throw aggregate;
  }
  if (primaryError) throw primaryError;
}

async function main() {
  try {
    await runCodexVerifier(process.argv[2] ?? process.cwd());
  } catch (error) {
    process.stderr.write(`${formatVerifierFailure(error)}\n`);
    process.exitCode = 1;
  }
}

const isDirect =
  process.argv[1] !== undefined &&
  pathToFileURL(resolve(process.argv[1])).href === pathToFileURL(fileURLToPath(import.meta.url)).href;
if (isDirect) {
  await main();
}
