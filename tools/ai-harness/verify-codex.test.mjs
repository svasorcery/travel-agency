import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  assertChildEvidence,
  assertListedChild,
  collectAppServerEvidence,
  createAppServerClient,
  executableExtensions,
  formatVerifierFailure,
  initializeAppServer,
  inspectPersonalAgents,
  normalizeProcessLaunch,
  parseJsonLines,
  parseJsonRpcResponse,
  runCodexVerifier,
  runProcess,
  stopOwnedProcess,
  validateCleanupTarget,
  validateInitializeResult,
  validateSkillsListEvidence,
} from './verify-codex.mjs';

const probe = 'TRAVEL_AI_HARNESS_IDENTITY_PROBE';
const roleId = 'travel-agency/domain-modeler';
const skillDescriptions = {
  'explore-domain':
    'Produce a read-only, evidence-backed current-state map of a Travel domain module. Use when asked what a module contains, implements, tests, or still lacks.',
  'migration-authoring':
    'Design, generate, and review safe EF Core source migrations for approved model changes. Use for schema changes, migration safety, generated SQL review, rollback, and deployment notes; never apply a database implicitly.',
};

function canonicalSkillBody(skill = 'explore-domain') {
  return `---\nname: ${skill}\ndescription: ${skillDescriptions[skill]}\n---\n\nCanonical Travel workflow ID: travel-agency/${skill}.\n\nFollow repository evidence.\n`;
}

function skillsListResponse(root) {
  const nested = join(root, 'modules', 'flights');
  return {
    data: [
      {
        cwd: root,
        skills: [
          {
            name: 'migration-authoring',
            description: skillDescriptions['migration-authoring'],
            path: join(root, '.agents', 'skills', 'migration-authoring', 'SKILL.md'),
            scope: 'repo',
            enabled: true,
          },
        ],
        errors: [],
      },
      {
        cwd: nested,
        skills: [
          {
            name: 'explore-domain',
            description: skillDescriptions['explore-domain'],
            path: join(root, '.agents', 'skills', 'explore-domain', 'SKILL.md'),
            scope: 'repo',
            enabled: true,
          },
        ],
        errors: [],
      },
    ],
  };
}

function skillTargets(root) {
  return [
    {
      cwd: root,
      skillName: 'migration-authoring',
      description: skillDescriptions['migration-authoring'],
      expectedPath: join(root, '.agents', 'skills', 'migration-authoring', 'SKILL.md'),
    },
    {
      cwd: join(root, 'modules', 'flights'),
      skillName: 'explore-domain',
      description: skillDescriptions['explore-domain'],
      expectedPath: join(root, '.agents', 'skills', 'explore-domain', 'SKILL.md'),
    },
  ];
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
    this.writes = [];
    this.stdin = {
      write: (chunk) => this.writes.push(chunk),
      end() {},
    };
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

class FakeLines extends FakeChild {
  close() {}
}

test('initialize result supplies the exact Codex home used for collision inventory', () => {
  const codexHome = join(tmpdir(), 'codex-home');
  assert.deepEqual(
    validateInitializeResult({
      userAgent: 'codex-app-server',
      codexHome,
      platformFamily: 'windows',
      platformOs: 'windows',
    }),
    { codexHome },
  );
  for (const malformed of [null, {}, { codexHome }, { userAgent: 'codex', codexHome, platformFamily: 'windows' }]) {
    assert.throws(() => validateInitializeResult(malformed), /initialize result schema/);
  }
});

test('App Server handshake emits initialized without params and returns validated Codex home', async () => {
  const codexHome = join(tmpdir(), 'codex-home');
  const calls = [];
  const result = await initializeAppServer({
    request: async (method, params) => {
      calls.push({ type: 'request', method, params });
      return {
        userAgent: 'codex-app-server',
        codexHome,
        platformFamily: 'windows',
        platformOs: 'windows',
      };
    },
    notify: (method, params) => calls.push({ type: 'notify', method, params }),
  });
  assert.deepEqual(result, { codexHome });
  assert.deepEqual(calls, [
    {
      type: 'request',
      method: 'initialize',
      params: {
        clientInfo: { name: 'travel-ai-harness-verifier', title: 'Travel AI Harness Verifier', version: '1' },
        capabilities: { experimentalApi: true },
      },
    },
    { type: 'notify', method: 'initialized', params: undefined },
  ]);
});

test('skills/list binds exact cwd, repo metadata, and physical canonical paths', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'travel-skill-catalog-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const target of skillTargets(root)) {
    await mkdir(target.cwd, { recursive: true });
    await mkdir(join(target.expectedPath, '..'), { recursive: true });
    await writeFile(target.expectedPath, canonicalSkillBody(target.skillName), 'utf8');
  }

  assert.deepEqual(await validateSkillsListEvidence(skillsListResponse(root), { targets: skillTargets(root) }), [
    {
      cwd: root,
      name: 'migration-authoring',
      path: join(root, '.agents', 'skills', 'migration-authoring', 'SKILL.md'),
    },
    {
      cwd: join(root, 'modules', 'flights'),
      name: 'explore-domain',
      path: join(root, '.agents', 'skills', 'explore-domain', 'SKILL.md'),
    },
  ]);
});

test('skills/list fails closed for cwd, error, state, metadata, duplicate, and path drift', async () => {
  const root = join(tmpdir(), 'catalog-root');
  const targets = skillTargets(root);
  const mutations = [
    (value) => value.data.push({ cwd: join(root, 'extra'), skills: [], errors: [] }),
    (value) => value.data[0].errors.push({ path: 'bad', message: 'broken skill' }),
    (value) => (value.data[0].skills[0].enabled = false),
    (value) => (value.data[0].skills[0].scope = 'user'),
    (value) => (value.data[0].skills[0].description = 'wrong'),
    (value) => value.data[0].skills.push({ ...value.data[0].skills[0], path: join(root, 'other', 'SKILL.md') }),
    (value) => value.data[0].skills.push({ ...value.data[0].skills[0], name: 'conflicting-name' }),
    (value) => value.data[0].skills.push({ ...value.data[1].skills[0], name: 'cross-cwd-conflict' }),
    (value) => value.data[0].skills.push({ ...value.data[1].skills[0], path: join(root, 'alternate', 'SKILL.md') }),
  ];
  const pathInspector = {
    inspectPath: async (path) => ({ physicalPath: path, isFile: true, isSymbolicLink: false }),
  };
  for (const mutate of mutations) {
    const response = structuredClone(skillsListResponse(root));
    mutate(response);
    await assert.rejects(validateSkillsListEvidence(response, { targets }, pathInspector), /skills\/list/);
  }
});

test('skills/list rejects a symlink or non-file even when catalog metadata matches', async () => {
  const root = join(tmpdir(), 'catalog-root');
  await assert.rejects(
    validateSkillsListEvidence(
      skillsListResponse(root),
      { targets: skillTargets(root) },
      {
        inspectPath: async (path) => ({ physicalPath: path, isFile: true, isSymbolicLink: true }),
      },
    ),
    /physical (?:non-symlink )?file/,
  );
});

test('skills/list compares physical paths after resolving both lexical aliases', async () => {
  const root = join(tmpdir(), 'LEXICAL-ALIAS', 'catalog-root');
  const targets = skillTargets(root);
  const response = skillsListResponse(root);
  for (const entry of response.data) {
    entry.cwd = entry.cwd.replace('LEXICAL-ALIAS', 'canonical-directory');
    for (const skill of entry.skills) {
      skill.path = skill.path.replace('LEXICAL-ALIAS', 'canonical-directory');
    }
  }
  const selected = await validateSkillsListEvidence(
    response,
    { targets },
    {
      inspectPath: async (path) => ({
        physicalPath: path.replace('LEXICAL-ALIAS', 'canonical-directory'),
        isFile: true,
        isSymbolicLink: false,
      }),
    },
  );
  assert.equal(selected.length, 2);
});

test('structured skill turn uses the server-returned path verbatim and exact completion identity', async () => {
  const { runStructuredSkillTurn } = await import('./verify-codex.mjs');
  assert.equal(typeof runStructuredSkillTurn, 'function');
  const returnedPath = join(tmpdir(), 'LEXICAL-ALIAS', 'migration-authoring', 'SKILL.md');
  const calls = [];
  const unrelated = {
    method: 'turn/completed',
    params: { threadId: 'other-thread', turn: { id: 'other-turn', status: 'completed' } },
  };
  const completed = {
    method: 'turn/completed',
    params: { threadId: 'root-1', turn: { id: 'turn-structured', status: 'completed' } },
  };
  const appServer = {
    messages: [unrelated, completed],
    async request(method, params) {
      calls.push({ method, params });
      return { turn: { id: 'turn-structured', status: 'inProgress', items: [], error: null } };
    },
    async waitFor(predicate) {
      assert.equal(predicate(unrelated), false);
      assert.equal(predicate(completed), true);
      return completed;
    },
  };

  assert.equal(
    await runStructuredSkillTurn(appServer, {
      threadId: 'root-1',
      prompt: 'Use $migration-authoring read only.',
      skill: { name: 'migration-authoring', path: returnedPath },
    }),
    'turn-structured',
  );
  assert.deepEqual(calls, [
    {
      method: 'turn/start',
      params: {
        threadId: 'root-1',
        input: [
          { type: 'text', text: 'Use $migration-authoring read only.' },
          { type: 'skill', name: 'migration-authoring', path: returnedPath },
        ],
      },
    },
  ]);
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

test('personal-agent collision aborts before any live behavior smoke', async (t) => {
  const tempBase = await mkdtemp(join(tmpdir(), 'travel-verifier-order-'));
  t.after(() => rm(tempBase, { recursive: true, force: true }));
  let cloneRoot;
  let execCalls = 0;
  let debugCalls = 0;
  const skillBodies = {
    'explore-domain': '---\nname: explore-domain\ndescription: Explore.\n---\n\nExplore body.\n',
    'migration-authoring': '---\nname: migration-authoring\ndescription: Migrate.\n---\n\nMigration body.\n',
  };
  const runProcess = async (command, args, _options) => {
    if (args[0] === '--version') return { stdout: 'codex-test\n', stderr: '' };
    if (args[0] === 'clone') {
      cloneRoot = args.at(-1);
      await mkdir(join(cloneRoot, 'modules', 'flights'), { recursive: true });
      for (const [skillName, body] of Object.entries(skillBodies)) {
        const skillDirectory = join(cloneRoot, '.agents', 'skills', skillName);
        await mkdir(skillDirectory, { recursive: true });
        await writeFile(join(skillDirectory, 'SKILL.md'), body);
      }
      return { stdout: '', stderr: '' };
    }
    if (command === process.execPath) return { stdout: '', stderr: '' };
    if (args[0] === 'debug' && args[1] === 'prompt-input') {
      debugCalls += 1;
      throw new Error('debug prompt-input cannot supply typed skill selection');
    }
    if (args[0] === 'exec') {
      execCalls += 1;
      throw new Error('behavior smoke ran before personal-agent collision check');
    }
    throw new Error(`unexpected process call: ${command} ${args.join(' ')}`);
  };
  const appServer = {
    messages: [],
    notify() {},
    async request(method) {
      if (method !== 'initialize') throw new Error(`unexpected App Server request: ${method}`);
      return {
        userAgent: 'codex-test',
        codexHome: join(tempBase, 'codex-home'),
        platformFamily: 'windows',
        platformOs: 'windows',
      };
    },
    async stop() {},
  };

  await assert.rejects(
    runCodexVerifier(process.cwd(), {
      tempBase,
      findExecutable: async (name) => name,
      runProcess,
      createAppServer: () => appServer,
      inspectPersonalAgents: async () => {
        throw new Error('personal-agent collision: domain-modeler');
      },
    }),
    /personal-agent collision: domain-modeler/,
  );
  assert.equal(debugCalls, 0);
  assert.equal(execCalls, 0);
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

test('process launch failures identify the requested executable and probe stage', async () => {
  await assert.rejects(
    runProcess(
      'C:\\Program Files\\WindowsApps\\OpenAI.Codex_test\\codex.exe',
      ['--version'],
      {},
      {
        platform: 'win32',
        spawnProcess: () => {
          throw new Error('spawn EPERM');
        },
      },
    ),
    /could not launch .*codex\.exe.*--version.*spawn EPERM/,
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
    systemRoot: 'C:\\Windows',
    spawnProcess: (file, args) => {
      taskkillLaunch = { file, args };
      return taskkill;
    },
    timeoutMs: 100,
  });
  assert.deepEqual(taskkillLaunch, {
    file: 'C:\\Windows\\System32\\taskkill.exe',
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

test('process exit suppresses PID termination but still requires bounded authoritative close', async () => {
  const exitedWithoutClose = new FakeChild(1111);
  exitedWithoutClose.exitCode = 0;
  let launchCount = 0;
  const timeoutOptions = {
    platform: 'win32',
    systemRoot: 'C:\\Windows',
    spawnProcess: () => {
      launchCount += 1;
      throw new Error('must not launch');
    },
    timeoutMs: 10,
  };
  const first = stopOwnedProcess(exitedWithoutClose, timeoutOptions);
  const second = stopOwnedProcess(exitedWithoutClose, timeoutOptions);
  assert.equal(first, second);
  await assert.rejects(first, /owned process close timeout/);
  assert.equal(stopOwnedProcess(exitedWithoutClose, timeoutOptions), first);

  const exitedThenClosed = new FakeChild(1112);
  exitedThenClosed.exitCode = 0;
  const stopped = stopOwnedProcess(exitedThenClosed, timeoutOptions);
  let settled = false;
  stopped.then(() => {
    settled = true;
  });
  await Promise.resolve();
  assert.equal(settled, false);
  exitedThenClosed.emit('close', 0, null);
  await stopped;
  assert.equal(launchCount, 0);
});

test('owned process stop is idempotent and never terminates an already closed or reused PID', async () => {
  const child = new FakeChild(2222);
  const taskkill = new FakeChild(3333);
  let launchCount = 0;
  const options = {
    platform: 'win32',
    systemRoot: 'C:\\Windows',
    spawnProcess: () => {
      launchCount += 1;
      return taskkill;
    },
    timeoutMs: 100,
  };
  const first = stopOwnedProcess(child, options);
  const second = stopOwnedProcess(child, options);
  assert.equal(first, second);
  assert.equal(launchCount, 1);
  taskkill.exitCode = 0;
  taskkill.emit('close', 0);
  child.signalCode = 'SIGTERM';
  child.emit('close', null, 'SIGTERM');
  await Promise.all([first, second]);
  await stopOwnedProcess(child, options);
  assert.equal(launchCount, 1);
});

test('Windows taskkill resolution rejects an absolute path outside the system Windows directory', async () => {
  const child = new FakeChild(6666);
  await assert.rejects(
    stopOwnedProcess(child, {
      platform: 'win32',
      systemRoot: 'C:\\Temp',
      spawnProcess: () => {
        throw new Error('untrusted executable launched');
      },
    }),
    /trusted Windows system root/,
  );
});

test('App Server fatal close, child error, and malformed stream reject pending work immediately', async () => {
  for (const fatal of ['close', 'error', 'malformed']) {
    const child = new FakeChild(4444);
    const lines = new FakeLines();
    const client = createAppServerClient(child, {
      lineReaderFactory: () => lines,
      stopProcess: async () => {},
    });
    const request = client.request('thread/read', { threadId: 'child' }, 10_000);
    const wait = client.waitFor(() => true, 10_000);
    if (fatal === 'close') {
      child.exitCode = 7;
      child.emit('close', 7, null);
    } else if (fatal === 'error') {
      child.emit('error', new Error('child failed'));
    } else {
      lines.emit('line', 'not-json');
    }
    await assert.rejects(request, /App Server (closed|fatal)/);
    await assert.rejects(wait, /App Server (closed|fatal)/);
    await assert.rejects(client.request('thread/list', {}, 10_000), /App Server (closed|fatal)/);
    await assert.rejects(
      client.waitFor(() => true, 10_000),
      /App Server (closed|fatal)/,
    );
  }
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

test('verifier failure formatting recursively renders standard AggregateError details', () => {
  const nested = new AggregateError([new Error('owned close timed out')], 'nested cleanup');
  const aggregate = new AggregateError([new Error('taskkill denied'), nested], 'process timeout cleanup failed');
  assert.equal(
    formatVerifierFailure(aggregate),
    'AI harness Codex verification failed: process timeout cleanup failed: [taskkill denied; nested cleanup: [owned close timed out]]',
  );
});

test('verifier failure formatting marks self, cause, and repeated-object cycles deterministically', () => {
  const selfCycle = new AggregateError([], 'self cycle');
  selfCycle.errors.push(selfCycle);
  assert.equal(formatVerifierFailure(selfCycle), 'AI harness Codex verification failed: self cycle: [[circular]]');

  const causeCycle = new Error('outer cause');
  causeCycle.cause = new Error('inner cause', { cause: causeCycle });
  assert.equal(
    formatVerifierFailure(causeCycle),
    'AI harness Codex verification failed: outer cause: inner cause: [circular]',
  );

  const repeated = new Error('shared failure');
  assert.equal(
    formatVerifierFailure(new AggregateError([repeated, repeated], 'repeated failure')),
    'AI harness Codex verification failed: repeated failure: [shared failure; [circular]]',
  );
});

test('verifier failure formatting bounds recursive depth and output length', () => {
  const deep = new Error('depth 0');
  let cursor = deep;
  for (let depth = 1; depth <= 100; depth += 1) {
    cursor.cause = new Error(`depth ${depth}`);
    cursor = cursor.cause;
  }
  const deepOutput = formatVerifierFailure(deep);
  assert.match(deepOutput, /\[truncated\]/);
  assert.ok(deepOutput.length < 1_000);

  const wide = new AggregateError(
    Array.from({ length: 100 }, (_, index) => new Error(`failure ${index} ${'x'.repeat(100)}`)),
    'wide failure',
  );
  const wideOutput = formatVerifierFailure(wide);
  assert.match(wideOutput, /\[truncated\]/);
  assert.ok(wideOutput.length <= 4_200);
});

test('verifier failure formatting handles null-prototype values without throwing', () => {
  assert.equal(formatVerifierFailure(Object.create(null)), 'AI harness Codex verification failed: [unprintable error]');
});

test('verifier failure formatting handles a throwing message getter without throwing', () => {
  const throwingMessage = {};
  Object.defineProperty(throwingMessage, 'message', {
    get() {
      throw new Error('message getter escaped');
    },
  });
  assert.equal(formatVerifierFailure(throwingMessage), 'AI harness Codex verification failed: [unprintable error]');
});

test('verifier failure formatting contains hostile proxy access and retains useful detail', () => {
  const hostile = new Proxy(Object.create(null), {
    get() {
      throw new Error('property access escaped');
    },
    getPrototypeOf() {
      throw new Error('prototype access escaped');
    },
  });
  assert.equal(formatVerifierFailure(hostile), 'AI harness Codex verification failed: [unprintable error]');
  assert.equal(
    formatVerifierFailure(new AggregateError([new Error('useful detail'), hostile], 'outer failure')),
    'AI harness Codex verification failed: outer failure: [useful detail; [unprintable error]]',
  );
});

test('pending App Server responses require the documented headerless result-or-error shape', () => {
  assert.deepEqual(parseJsonRpcResponse({ id: 7, result: { ok: true } }, 7), { ok: true });
  assert.throws(
    () => parseJsonRpcResponse({ id: 7, error: { code: -32_000, message: 'failed' } }, 7),
    /App Server error -32000: failed/,
  );
  for (const malformed of [
    { jsonrpc: '2.0', id: 7, result: null },
    { id: 8, result: null },
    { id: 7 },
    { id: 7, result: null, method: 'turn/completed' },
    { id: 7, result: null, error: { code: -1, message: 'both' } },
    { id: 7, error: { code: 'bad', message: 'failed' } },
  ]) {
    assert.throws(() => parseJsonRpcResponse(malformed, 7), /JSON-RPC response schema/);
  }
});

test('App Server client writes headerless requests and notifications', async () => {
  const child = new FakeChild(5554);
  const lines = new FakeLines();
  const client = createAppServerClient(child, {
    lineReaderFactory: () => lines,
    stopProcess: async () => {},
  });
  const response = client.request('skills/list', { cwds: ['C:/repo'], forceReload: true }, 10_000);
  assert.deepEqual(JSON.parse(child.writes[0]), {
    id: 1,
    method: 'skills/list',
    params: { cwds: ['C:/repo'], forceReload: true },
  });
  lines.emit('line', JSON.stringify({ id: 1, result: { data: [] } }));
  assert.deepEqual(await response, { data: [] });
  client.notify('initialized');
  assert.deepEqual(JSON.parse(child.writes[1]), { method: 'initialized' });
});

test('App Server accepts the documented optional notification timestamp only', async () => {
  const child = new FakeChild(5553);
  const lines = new FakeLines();
  const client = createAppServerClient(child, {
    lineReaderFactory: () => lines,
    stopProcess: async () => {},
  });
  const notification = client.waitFor((message) => message.method === 'turn/completed', 10_000);
  lines.emit(
    'line',
    JSON.stringify({
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status: 'completed' } },
      emittedAtMs: 1_786_000_000_000,
    }),
  );
  assert.equal((await notification).emittedAtMs, 1_786_000_000_000);
});

test('unknown response IDs and invalid notifications poison App Server protocol state', async () => {
  for (const canary of [
    {
      id: 999,
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status: 'completed' } },
    },
    {
      jsonrpc: '2.0',
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status: 'completed' } },
    },
    {
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status: 'completed' } },
      unexpected: true,
    },
    {
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status: 'completed' } },
      emittedAtMs: 'not-a-timestamp',
    },
  ]) {
    const child = new FakeChild(5555);
    const lines = new FakeLines();
    const client = createAppServerClient(child, {
      lineReaderFactory: () => lines,
      stopProcess: async () => {},
    });
    const wait = client.waitFor((message) => message?.method === 'turn/completed', 10_000);
    lines.emit('line', JSON.stringify(canary));
    await assert.rejects(wait, /App Server fatal: (unknown response ID|invalid notification)/);
    assert.deepEqual(client.messages, []);
    await assert.rejects(client.request('thread/read', {}, 10_000), /App Server fatal/);
    await assert.rejects(
      client.waitFor(() => true, 10_000),
      /App Server fatal/,
    );
  }
});
