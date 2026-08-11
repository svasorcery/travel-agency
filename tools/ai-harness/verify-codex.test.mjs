import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  assertChildEvidence,
  assertListedChild,
  collectAppServerEvidence,
  executableExtensions,
  formatVerifierFailure,
  inspectPersonalAgents,
  normalizeProcessLaunch,
  parseJsonLines,
  parseJsonRpcResponse,
  runProcess,
  stopOwnedProcess,
  validateCleanupTarget,
  validatePromptInputEvidence,
} from './verify-codex.mjs';

const description =
  'Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence.';
const probe = 'TRAVEL_AI_HARNESS_IDENTITY_PROBE';
const roleId = 'travel-agency/domain-modeler';

function promptInput(root, skill = 'explore-domain') {
  const sourcePath = join(root, '.agents', 'skills', skill, 'SKILL.md');
  return {
    skills: [
      {
        name: skill,
        sourcePath,
        body: `Canonical Travel workflow ID: travel-agency/${skill}.`,
      },
      {
        name: skill,
        sourcePath,
        body: `Canonical Travel workflow ID: travel-agency/${skill}.`,
      },
    ],
    agents: [
      { name: 'domain-modeler', description },
      { name: 'domain-modeler', description },
    ],
  };
}

function appMessages({ status = 'completed', childId = 'child-1' } = {}) {
  return [
    {
      method: 'item/completed',
      params: {
        threadId: 'root-1',
        turnId: 'turn-root',
        item: {
          type: 'collabToolCall',
          status: 'completed',
          tool: 'spawn_agent',
          senderThreadId: 'root-1',
          newThreadId: childId,
          prompt: probe,
        },
      },
    },
    {
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status } },
    },
  ];
}

function childRead({ text = roleId, extraItem } = {}) {
  const items = [{ type: 'agentMessage', text }];
  if (extraItem) items.push(extraItem);
  return { thread: { id: 'child-1', turns: [{ status: 'completed', items }] } };
}

class FakeChild {
  constructor(pid = 1234) {
    this.pid = pid;
    this.exitCode = null;
    this.signalCode = null;
    this.listeners = new Map();
    const readable = {
      setEncoding: () => readable,
      on: () => readable,
    };
    this.stdout = readable;
    this.stderr = readable;
    this.stdin = { write() {}, end() {} };
  }

  on(name, listener) {
    const listeners = this.listeners.get(name) ?? [];
    listeners.push({ listener, once: false });
    this.listeners.set(name, listeners);
    return this;
  }

  once(name, listener) {
    const listeners = this.listeners.get(name) ?? [];
    listeners.push({ listener, once: true });
    this.listeners.set(name, listeners);
    return this;
  }

  emit(name, ...args) {
    const listeners = this.listeners.get(name) ?? [];
    this.listeners.set(
      name,
      listeners.filter(({ once }) => !once),
    );
    for (const { listener } of listeners) listener(...args);
  }
}

test('prompt-input provenance accepts repeated expected references and exact workflow marker', () => {
  const root = join(tmpdir(), 'clone-root');
  assert.deepEqual(
    validatePromptInputEvidence(promptInput(root), {
      cloneRoot: root,
      skillName: 'explore-domain',
      expectedAgentDescription: description,
    }),
    { skillPath: join(root, '.agents', 'skills', 'explore-domain', 'SKILL.md') },
  );
});

test('prompt-input provenance rejects marker-only and distinct duplicate skill sources', () => {
  const root = join(tmpdir(), 'clone-root');
  assert.throws(
    () =>
      validatePromptInputEvidence(
        { response: 'Canonical Travel workflow ID: travel-agency/explore-domain.' },
        { cloneRoot: root, skillName: 'explore-domain', expectedAgentDescription: description },
      ),
    /source path/,
  );
  const evidence = promptInput(root);
  evidence.skills.push({
    name: 'explore-domain',
    sourcePath: join(root, 'other', 'SKILL.md'),
    body: 'Canonical Travel workflow ID: travel-agency/explore-domain.',
  });
  assert.throws(
    () =>
      validatePromptInputEvidence(evidence, {
        cloneRoot: root,
        skillName: 'explore-domain',
        expectedAgentDescription: description,
      }),
    /distinct skill source/,
  );
});

test('prompt-input provenance requires the marker in the loaded skill body, not a response', () => {
  const root = join(tmpdir(), 'clone-root');
  const evidence = promptInput(root);
  for (const skill of evidence.skills) skill.body = 'body without marker';
  evidence.response = 'Canonical Travel workflow ID: travel-agency/explore-domain.';
  assert.throws(
    () =>
      validatePromptInputEvidence(evidence, {
        cloneRoot: root,
        skillName: 'explore-domain',
        expectedAgentDescription: description,
      }),
    /skill body/,
  );
});

test('project agent evidence permits identical repetition but rejects conflicting mappings', () => {
  const root = join(tmpdir(), 'clone-root');
  const evidence = promptInput(root);
  evidence.agents.push({ name: 'domain-modeler', description: 'conflict' });
  assert.throws(
    () =>
      validatePromptInputEvidence(evidence, {
        cloneRoot: root,
        skillName: 'explore-domain',
        expectedAgentDescription: description,
      }),
    /conflicting domain-modeler/,
  );
});

test('personal agent collision detection parses names independently of basename', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  await mkdir(join(codexHome, 'agents'));
  await writeFile(join(codexHome, 'surprising-name.toml'), '', 'utf8').catch(() => {});
  await writeFile(
    join(codexHome, 'agents', 'not-domain-modeler.toml'),
    'name = "domain-modeler"\ndescription = "personal"\n',
    'utf8',
  );
  await assert.rejects(inspectPersonalAgents({ codexHome }), /personal-agent collision/);
});

test('personal agent inventory honors a CODEX_HOME-style override and accepts no collision', async (t) => {
  const override = await mkdtemp(join(tmpdir(), 'travel-codex-override-'));
  t.after(() => rm(override, { recursive: true, force: true }));
  await mkdir(join(override, 'agents'));
  await writeFile(join(override, 'agents', 'writer.toml'), 'name = "writer"\n', 'utf8');
  const previous = process.env.CODEX_HOME;
  process.env.CODEX_HOME = override;
  try {
    assert.deepEqual(await inspectPersonalAgents(), { inspected: 1 });
  } finally {
    if (previous === undefined) delete process.env.CODEX_HOME;
    else process.env.CODEX_HOME = previous;
  }
});

test('personal agent inventory fails closed for malformed and unsupported valid TOML name forms', async (t) => {
  for (const source of ['name = [broken\n', "name = '''domain-modeler'''\n"]) {
    const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
    t.after(() => rm(codexHome, { recursive: true, force: true }));
    await mkdir(join(codexHome, 'agents'));
    await writeFile(join(codexHome, 'agents', 'agent.toml'), source, 'utf8');
    await assert.rejects(inspectPersonalAgents({ codexHome }), /personal-agent inventory not provable/);
  }
});

test('personal agent inventory rejects non-file and symlink TOML entries', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  await mkdir(join(codexHome, 'agents', 'directory.toml'), { recursive: true });
  await assert.rejects(inspectPersonalAgents({ codexHome }), /personal-agent inventory not provable/);

  const linkedHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(linkedHome, { recursive: true, force: true }));
  await mkdir(join(linkedHome, 'agents'));
  await assert.rejects(
    inspectPersonalAgents({
      codexHome: linkedHome,
      readDirectory: async () => [
        {
          name: 'linked.toml',
          isFile: () => false,
          isSymbolicLink: () => true,
        },
      ],
    }),
    /personal-agent inventory not provable/,
  );
});

test('JSONL is parsed structurally and rejects malformed records', () => {
  assert.deepEqual(parseJsonLines('{"type":"message","text":"ok"}\n\n{"type":"done"}\n'), [
    { type: 'message', text: 'ok' },
    { type: 'done' },
  ]);
  assert.throws(() => parseJsonLines('{"ok":true}\nnot-json\n'), /JSONL record 2/);
});

test('App Server evidence accepts one exact completed root spawn', () => {
  assert.deepEqual(
    collectAppServerEvidence(appMessages(), {
      rootThreadId: 'root-1',
      rootTurnId: 'turn-root',
      probe,
    }),
    { childThreadId: 'child-1' },
  );
});

test('child completion before root does not terminate or satisfy root evidence', () => {
  const messages = [
    {
      method: 'turn/completed',
      params: { threadId: 'child-1', turn: { id: 'turn-child', status: 'completed' } },
    },
    ...appMessages(),
  ];
  assert.deepEqual(
    collectAppServerEvidence(messages, {
      rootThreadId: 'root-1',
      rootTurnId: 'turn-root',
      probe,
    }),
    { childThreadId: 'child-1' },
  );
  assert.throws(
    () =>
      collectAppServerEvidence(messages.slice(0, 1), {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /root turn completion/,
  );
});

test('App Server evidence fails closed for missing child IDs and failed or incomplete root turns', () => {
  assert.throws(
    () =>
      collectAppServerEvidence(appMessages({ childId: '' }), {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /child thread ID/,
  );
  for (const status of ['failed', 'inProgress']) {
    assert.throws(
      () =>
        collectAppServerEvidence(appMessages({ status }), {
          rootThreadId: 'root-1',
          rootTurnId: 'turn-root',
          probe,
        }),
      /root turn status/,
    );
  }
});

test('App Server evidence requires root-thread correlation and exact spawn fields', () => {
  const missingThread = appMessages();
  delete missingThread[1].params.threadId;
  assert.throws(
    () =>
      collectAppServerEvidence(missingThread, {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /root turn completion/,
  );
  for (const mutation of [
    (messages) => {
      messages[0].params.item.senderThreadId = 'other-root';
    },
    (messages) => {
      messages[0].params.item.prompt = 'generic task';
    },
    (messages) => {
      messages.splice(1, 0, structuredClone(messages[0]));
    },
  ]) {
    const messages = appMessages();
    mutation(messages);
    assert.throws(() =>
      collectAppServerEvidence(messages, {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    );
  }
});

test('root self-report and generic child output cannot substitute for child identity evidence', () => {
  const canary = childRead({ text: 'generic agent output' });
  canary.rootResponse = roleId;
  assert.throws(() => assertChildEvidence(canary, { childThreadId: 'child-1', roleId }), /exact repository role ID/);
});

test('child evidence accepts exact identity and rejects every child tool-use item', () => {
  assert.doesNotThrow(() => assertChildEvidence(childRead(), { childThreadId: 'child-1', roleId }));
  for (const type of ['commandExecution', 'fileChange', 'mcpToolCall', 'collabToolCall']) {
    assert.throws(
      () =>
        assertChildEvidence(childRead({ extraItem: { type } }), {
          childThreadId: 'child-1',
          roleId,
        }),
      /child tool use/,
    );
  }
});

test('child evidence requires one completed turn and rejects unknown item types', () => {
  for (const status of ['failed', 'inProgress']) {
    const response = childRead();
    response.thread.turns[0].status = status;
    assert.throws(() => assertChildEvidence(response, { childThreadId: 'child-1', roleId }), /completed child turn/);
  }
  assert.throws(
    () =>
      assertChildEvidence(childRead({ extraItem: { type: 'futureToolThing' } }), {
        childThreadId: 'child-1',
        roleId,
      }),
    /unsupported child item/,
  );
});

test('thread listing must link the exact child to the exact root', () => {
  assert.doesNotThrow(() =>
    assertListedChild(
      { data: [{ id: 'child-1', parentThreadId: 'root-1', source: 'subAgent' }] },
      { rootThreadId: 'root-1', childThreadId: 'child-1' },
    ),
  );
  assert.throws(
    () =>
      assertListedChild(
        { data: [{ id: 'other', parentThreadId: 'root-1', source: 'subAgent' }] },
        { rootThreadId: 'root-1', childThreadId: 'child-1' },
      ),
    /linked child/,
  );
});

test('cleanup guard permits only a GUID directory directly under the temp base', () => {
  const base = join(tmpdir(), 'travel-cleanup-base');
  const valid = join(base, '123e4567-e89b-42d3-a456-426614174000');
  assert.equal(validateCleanupTarget(valid, base), valid);
  for (const invalid of [
    '',
    base,
    join(base, 'not-a-guid'),
    join(base, '123e4567-e89b-42d3-a456-426614174000', 'nested'),
    join(base, '..', 'sibling', '123e4567-e89b-42d3-a456-426614174000'),
  ]) {
    assert.throws(() => validateCleanupTarget(invalid, base), /cleanup target/);
  }
});

test('Windows launch normalization prefers executables and safely wraps cmd fallbacks', () => {
  assert.deepEqual(executableExtensions('win32'), ['.exe', '.cmd', '.bat', '']);
  assert.deepEqual(
    normalizeProcessLaunch('C:\\Tools\\codex.exe', ['--version'], {
      platform: 'win32',
      comspec: 'C:\\Windows\\System32\\cmd.exe',
    }),
    { file: 'C:\\Tools\\codex.exe', args: ['--version'], windowsVerbatimArguments: false },
  );
  assert.deepEqual(
    normalizeProcessLaunch('C:\\Tools\\codex.cmd', ['exec', '--json', 'safe prompt'], {
      platform: 'win32',
      comspec: 'C:\\Windows\\System32\\cmd.exe',
    }),
    {
      file: 'C:\\Windows\\System32\\cmd.exe',
      args: ['/d', '/s', '/v:off', '/c', '""C:\\Tools\\codex.cmd" "exec" "--json" "safe prompt""'],
      windowsVerbatimArguments: true,
    },
  );
  assert.throws(
    () =>
      normalizeProcessLaunch('C:\\Tools\\codex.cmd', ['unsafe & injected'], {
        platform: 'win32',
        comspec: 'C:\\Windows\\System32\\cmd.exe',
      }),
    /unsafe Windows command token/,
  );
});

test('process timeout and App Server stop await bounded process-tree closure', async () => {
  const timedChild = new FakeChild();
  let releaseTermination;
  let terminationStarted = false;
  const run = runProcess(
    'tool.exe',
    [],
    { timeoutMs: 1 },
    {
      spawnProcess: () => timedChild,
      terminateProcess: async () => {
        terminationStarted = true;
        await new Promise((resolvePromise) => {
          releaseTermination = resolvePromise;
        });
      },
    },
  );
  await new Promise((resolvePromise) => setTimeout(resolvePromise, 10));
  assert.equal(terminationStarted, true);
  let timeoutSettled = false;
  run.catch(() => {
    timeoutSettled = true;
  });
  await Promise.resolve();
  assert.equal(timeoutSettled, false);
  releaseTermination();
  await assert.rejects(run, /process timeout/);

  const appChild = new FakeChild(4321);
  const taskkill = new FakeChild(9876);
  let taskkillLaunch;
  const stopped = stopOwnedProcess(appChild, {
    platform: 'win32',
    spawnProcess: (file, args) => {
      taskkillLaunch = { file, args };
      return taskkill;
    },
    timeoutMs: 100,
  });
  assert.deepEqual(taskkillLaunch, {
    file: 'taskkill.exe',
    args: ['/pid', '4321', '/t', '/f'],
  });
  let stopSettled = false;
  stopped.then(() => {
    stopSettled = true;
  });
  taskkill.exitCode = 0;
  taskkill.emit('close', 0);
  await Promise.resolve();
  assert.equal(stopSettled, false);
  appChild.signalCode = 'SIGTERM';
  appChild.emit('close', null, 'SIGTERM');
  await stopped;
  assert.equal(stopSettled, true);
});

test('verifier failure formatting reports primary and every cleanup failure separately', () => {
  assert.equal(
    formatVerifierFailure(new Error('primary failure')),
    'AI harness Codex verification failed: primary failure',
  );
  const cleanupOne = new Error('failed to delete root thread', { cause: new Error('permission denied') });
  const cleanupTwo = new Error('failed to remove clone', { cause: new Error('directory locked') });
  const aggregate = new AggregateError([new Error('primary failure'), cleanupOne, cleanupTwo], 'cleanup failed');
  aggregate.primaryError = aggregate.errors[0];
  aggregate.cleanupErrors = [cleanupOne, cleanupTwo];
  assert.equal(
    formatVerifierFailure(aggregate),
    [
      'AI harness Codex verification failed:',
      '- primary: primary failure',
      '- cleanup: failed to delete root thread: permission denied',
      '- cleanup: failed to remove clone: directory locked',
    ].join('\n'),
  );
});

test('pending App Server responses require an exact JSON-RPC 2.0 result-or-error shape', () => {
  assert.deepEqual(parseJsonRpcResponse({ jsonrpc: '2.0', id: 7, result: { ok: true } }, 7), { ok: true });
  assert.throws(
    () => parseJsonRpcResponse({ jsonrpc: '2.0', id: 7, error: { code: -32_000, message: 'failed' } }, 7),
    /App Server error -32000: failed/,
  );
  for (const malformed of [
    { id: 7, result: null },
    { jsonrpc: '1.0', id: 7, result: null },
    { jsonrpc: '2.0', id: 8, result: null },
    { jsonrpc: '2.0', id: 7 },
    { jsonrpc: '2.0', id: 7, result: null, error: { code: -1, message: 'both' } },
    { jsonrpc: '2.0', id: 7, error: { code: 'bad', message: 'failed' } },
  ]) {
    assert.throws(() => parseJsonRpcResponse(malformed, 7), /JSON-RPC response schema/);
  }
});
