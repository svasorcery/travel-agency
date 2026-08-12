import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, symlink, writeFile } from 'node:fs/promises';
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
  readCompletedChild,
  runCodexVerifier,
  runProcess,
  stopOwnedProcess,
  validateCleanupTarget,
  validateExecRun,
  validateInitializeResult,
  validateSkillsListEvidence,
} from './verify-codex.mjs';

const probe = 'TRAVEL_AI_HARNESS_IDENTITY_PROBE';
const roleId = 'travel-agency/domain-modeler';
const expectedSpawnPrompt =
  'Call spawn_agent exactly once with agent_type `domain-modeler`, task_name `harness_identity`, fork_turns `none`, and message exactly `TRAVEL_AI_HARNESS_IDENTITY_PROBE`. Do not wait for the child or call any other tool. After spawn_agent returns successfully, end this turn immediately with a short acknowledgement.';
const skillDescriptions = {
  'explore-domain':
    'Produce a read-only, evidence-backed current-state map of a Travel domain module. Use when asked what a module contains, implements, tests, or still lacks.',
  'migration-authoring':
    'Design, generate, and review safe EF Core source migrations for approved model changes. Use for schema changes, migration safety, generated SQL review, rollback, and deployment notes; never apply a database implicitly.',
};

function execJsonl(text = 'substantive smoke output') {
  return [
    { type: 'thread.started', thread_id: 'exec-thread-1' },
    { type: 'turn.started' },
    { type: 'item.completed', item: { id: 'exec-message-1', type: 'agent_message', text } },
    {
      type: 'turn.completed',
      usage: {
        input_tokens: 1,
        cached_input_tokens: 0,
        cache_write_input_tokens: 0,
        output_tokens: 1,
        reasoning_output_tokens: 0,
      },
    },
  ]
    .map((record) => JSON.stringify(record))
    .join('\n')
    .concat('\n');
}

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
  const callId = 'call-spawn-1';
  return [
    {
      method: 'rawResponseItem/completed',
      params: {
        threadId: 'root-1',
        turnId: 'turn-root',
        item: {
          type: 'function_call',
          name: 'spawn_agent',
          call_id: callId,
          arguments: JSON.stringify({
            message: probe,
            task_name: 'harness_identity',
            agent_type: 'domain-modeler',
            fork_turns: 'none',
          }),
        },
      },
    },
    {
      method: 'item/completed',
      params: {
        threadId: 'root-1',
        turnId: 'turn-root',
        item: {
          id: callId,
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
      params: { threadId: 'root-1', turn: { id: 'turn-root', status, error: null } },
    },
  ];
}

function v2AppMessages({ status = 'completed', childId = 'child-1', agentPath = '/root/harness_identity' } = {}) {
  const callId = 'call-spawn-1';
  return [
    {
      method: 'rawResponseItem/completed',
      params: {
        threadId: 'root-1',
        turnId: 'turn-root',
        item: {
          type: 'function_call',
          name: 'spawn_agent',
          call_id: callId,
          arguments: JSON.stringify({
            message: probe,
            task_name: 'harness_identity',
            agent_type: 'domain-modeler',
            fork_turns: 'none',
          }),
        },
      },
    },
    {
      method: 'item/completed',
      params: {
        threadId: 'root-1',
        turnId: 'turn-root',
        item: {
          id: callId,
          type: 'subAgentActivity',
          kind: 'started',
          agentThreadId: childId,
          agentPath,
        },
      },
    },
    {
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-root', status, error: null } },
    },
  ];
}

function childRead({ text = 'bounded probe complete', extraItem } = {}) {
  const items = [{ type: 'agentMessage', text }];
  if (extraItem) items.push(extraItem);
  return {
    thread: {
      id: 'child-1',
      turns: [{ id: 'turn-child', status: 'completed', itemsView: 'full', items, error: null }],
    },
  };
}

function v2ChildRead({
  ownStatus = 'completed',
  ownItems = [{ type: 'agentMessage', text: 'bounded probe complete' }],
} = {}) {
  return {
    thread: {
      id: 'child-1',
      turns: [{ id: 'turn-child', status: ownStatus, itemsView: 'full', items: ownItems, error: null }],
    },
  };
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

  assert.deepEqual(
    await validateSkillsListEvidence(skillsListResponse(root), {
      targets: skillTargets(root),
      repositoryRoot: root,
    }),
    [
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
    ],
  );
});

test('skills/list fails closed for cwd, error, state, metadata, duplicate, and path drift', async () => {
  const root = join(tmpdir(), 'catalog-root');
  const targets = skillTargets(root);
  const mutations = [
    (value) => value.data.push({ cwd: join(root, 'extra'), skills: [], errors: [] }),
    (value) =>
      value.data[0].errors.push({
        path: join(root, '.agents', 'skills', 'broken', 'SKILL.md'),
        message: 'broken repo skill',
      }),
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
    await assert.rejects(
      validateSkillsListEvidence(response, { targets, repositoryRoot: root }, pathInspector),
      /skills\/list/,
    );
  }
});

test('skills/list ignores unrelated external discovery errors while retaining exact repo evidence', async () => {
  const root = join(tmpdir(), 'catalog-root');
  const response = skillsListResponse(root);
  response.data[1].errors.push({
    path: join(tmpdir(), 'personal-skills', 'broken', 'SKILL.md'),
    message: 'missing field description',
  });
  const selected = await validateSkillsListEvidence(
    response,
    { targets: skillTargets(root), repositoryRoot: root },
    {
      inspectPath: async (path) => ({ physicalPath: path, isFile: true, isSymbolicLink: false }),
    },
  );
  assert.deepEqual(
    selected.map(({ name }) => name),
    ['migration-authoring', 'explore-domain'],
  );
});

test('skills/list rejects a symlink or non-file even when catalog metadata matches', async () => {
  const root = join(tmpdir(), 'catalog-root');
  await assert.rejects(
    validateSkillsListEvidence(
      skillsListResponse(root),
      { targets: skillTargets(root), repositoryRoot: root },
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
    { targets, repositoryRoot: root },
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
    params: { threadId: 'other-thread', turn: { id: 'other-turn', status: 'completed', error: null } },
  };
  const completed = {
    method: 'turn/completed',
    params: { threadId: 'root-1', turn: { id: 'turn-structured', status: 'completed', error: null } },
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

test('structured discovery probe does not repeat the expensive migration review', async () => {
  const { runStructuredSkillProbe } = await import('./verify-codex.mjs');
  assert.equal(typeof runStructuredSkillProbe, 'function');
  const returnedPath = join(tmpdir(), 'repo', '.agents', 'skills', 'migration-authoring', 'SKILL.md');
  const calls = [];
  const completed = {
    method: 'turn/completed',
    params: { threadId: 'root-1', turn: { id: 'turn-probe', status: 'completed', error: null } },
  };
  const appServer = {
    messages: [completed],
    async request(method, params) {
      calls.push({ method, params });
      return { turn: { id: 'turn-probe' } };
    },
    async waitFor(predicate) {
      assert.equal(predicate(completed), true);
      return completed;
    },
  };

  assert.equal(
    await runStructuredSkillProbe(appServer, {
      threadId: 'root-1',
      skill: { name: 'migration-authoring', path: returnedPath },
    }),
    'turn-probe',
  );
  assert.deepEqual(calls, [
    {
      method: 'turn/start',
      params: {
        threadId: 'root-1',
        input: [
          {
            type: 'text',
            text: 'Use $migration-authoring for this protocol probe. Do not inspect files or use tools; reply briefly that no migration work was requested.',
          },
          { type: 'skill', name: 'migration-authoring', path: returnedPath },
        ],
      },
    },
  ]);
});

test('turn, thread-read, thread-list, and completion evidence reject undocumented bare wrappers', async () => {
  const { runStructuredSkillTurn, waitForCompletedTurn } = await import('./verify-codex.mjs');
  await assert.rejects(
    runStructuredSkillTurn(
      {
        messages: [],
        request: async () => ({ id: 'turn-bare' }),
      },
      {
        threadId: 'root-1',
        prompt: 'probe',
        skill: { name: 'migration-authoring', path: join(tmpdir(), 'SKILL.md') },
      },
    ),
    /turn ID/,
  );
  await assert.rejects(
    waitForCompletedTurn(
      {
        waitFor: async (predicate) => {
          const bare = { method: 'turn/completed', params: { id: 'turn-1', status: 'completed' } };
          if (!predicate(bare)) throw new Error('bare notification rejected');
          return bare;
        },
      },
      { threadId: 'root-1', turnId: 'turn-1', stage: 'wrapper probe' },
    ),
    /bare notification rejected/,
  );
  await assert.rejects(
    readCompletedChild(
      { request: async () => ({ id: 'child-1', turns: [] }) },
      { childThreadId: 'child-1', maxAttempts: 1 },
    ),
    /unavailable/,
  );
  assert.throws(() => assertChildEvidence({ id: 'child-1', turns: [] }, { childThreadId: 'child-1' }), /unavailable/);
  assert.throws(
    () =>
      assertListedChild(
        { threads: [{ id: 'child-1', parentThreadId: 'root-1', source: 'subAgent' }] },
        { rootThreadId: 'root-1', childThreadId: 'child-1' },
      ),
    /linked child/,
  );
});

test('read-only thread startup scopes trust and MultiAgentV2 to the exact clone without mutating user config', async () => {
  const { startReadOnlyThread } = await import('./verify-codex.mjs');
  assert.equal(typeof startReadOnlyThread, 'function');
  const calls = [];
  const response = { thread: { id: 'root-1' } };
  const result = await startReadOnlyThread(
    {
      request: async (method, params) => {
        calls.push({ method, params });
        return response;
      },
    },
    'C:\\repo',
  );

  assert.equal(result, response);
  assert.deepEqual(calls, [
    {
      method: 'thread/start',
      params: {
        cwd: 'C:\\repo',
        approvalPolicy: 'never',
        sandbox: 'read-only',
        experimentalRawEvents: true,
        config: {
          projects: { 'C:\\repo': { trust_level: 'trusted' } },
          features: { multi_agent_v2: { enabled: true, wait_agent_enabled: false } },
          agents: { enabled: true },
          model_reasoning_effort: 'low',
        },
      },
    },
  ]);
});

test('thread startup evidence attests the effective clone, safety policy, effort, and instruction sources', async () => {
  const { validateThreadStartEvidence } = await import('./verify-codex.mjs');
  assert.equal(typeof validateThreadStartEvidence, 'function');
  const expectedInstructions = 'C:\\repo\\AGENTS.md';
  const response = {
    thread: { id: 'root-1', instructionSources: [expectedInstructions] },
    cwd: 'C:\\repo',
    approvalPolicy: 'never',
    sandbox: { type: 'readOnly', networkAccess: false },
    reasoningEffort: 'low',
    instructionSources: ['C:\\Users\\tester\\.codex\\AGENTS.md', expectedInstructions],
  };
  const thread = validateThreadStartEvidence(response, expectedInstructions);
  assert.deepEqual(thread, { id: 'root-1', instructionSources: [expectedInstructions] });

  for (const mutate of [
    (candidate) => {
      candidate.cwd = 'C:\\other';
    },
    (candidate) => {
      candidate.approvalPolicy = 'on-request';
    },
    (candidate) => {
      candidate.sandbox = { type: 'workspaceWrite', networkAccess: false };
    },
    (candidate) => {
      candidate.sandbox.networkAccess = true;
    },
    (candidate) => {
      candidate.reasoningEffort = 'medium';
    },
    (candidate) => {
      candidate.instructionSources = [];
    },
  ]) {
    const candidate = structuredClone(response);
    mutate(candidate);
    assert.throws(() => validateThreadStartEvidence(candidate, expectedInstructions));
  }
});

test('turn completion timeout identifies the exact live-gate stage', async () => {
  const { waitForCompletedTurn } = await import('./verify-codex.mjs');
  assert.equal(typeof waitForCompletedTurn, 'function');
  await assert.rejects(
    waitForCompletedTurn(
      {
        messages: [],
        waitFor: async () => {
          throw new Error('App Server notification timeout');
        },
      },
      { threadId: 'root-1', turnId: 'turn-1', stage: 'structured skill turn' },
    ),
    /structured skill turn.*notification timeout/,
  );
});

test('literal migration smoke accepts a completed substantive response without exact-path wording', async () => {
  const { validateLiteralSkillResponse } = await import('./verify-codex.mjs');
  assert.equal(typeof validateLiteralSkillResponse, 'function');
  assert.equal(
    validateLiteralSkillResponse(
      'migration-authoring',
      'Use expand, backfill, verify, and contract phases. Do not apply the database migration implicitly.',
    ),
    'Use expand, backfill, verify, and contract phases. Do not apply the database migration implicitly.',
  );
  assert.throws(() => validateLiteralSkillResponse('migration-authoring', '  '), /substantive final response/);
});

test('literal skill smokes run from clone root so read-only exploration can inspect sibling tests', async () => {
  const { buildLiteralSkillRuns } = await import('./verify-codex.mjs');
  assert.equal(typeof buildLiteralSkillRuns, 'function');
  const cloneRoot = join(tmpdir(), 'clean-clone');
  assert.deepEqual(
    buildLiteralSkillRuns(cloneRoot).map(({ skillName, cwd }) => ({ skillName, cwd })),
    [
      { skillName: 'explore-domain', cwd: cloneRoot },
      { skillName: 'migration-authoring', cwd: cloneRoot },
    ],
  );
});

test('typed skill discovery retains exact root and nested cwd targets independently of smoke cwd', async () => {
  const { buildSkillDiscoveryTargets } = await import('./verify-codex.mjs');
  assert.equal(typeof buildSkillDiscoveryTargets, 'function');
  const cloneRoot = join(tmpdir(), 'clean-clone');
  assert.deepEqual(
    buildSkillDiscoveryTargets(cloneRoot).map(({ skillName, cwd }) => ({ skillName, cwd })),
    [
      { skillName: 'migration-authoring', cwd: cloneRoot },
      { skillName: 'explore-domain', cwd: join(cloneRoot, 'modules', 'flights') },
    ],
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

test('personal agent collision detection matches Codex name trimming', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  await mkdir(join(codexHome, 'agents'));
  await writeFile(
    join(codexHome, 'agents', 'padded-name.toml'),
    'name = "  domain-modeler  "\ndescription = "personal"\n',
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

test('personal agent inventory recursively detects a nested custom-agent collision', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  const nestedAgents = join(codexHome, 'agents', 'team', 'architecture');
  await mkdir(nestedAgents, { recursive: true });
  await writeFile(
    join(nestedAgents, 'role.toml'),
    'name = "domain-modeler"\ndescription = "collision"\ndeveloper_instructions = """\nnone\n"""\n',
    'utf8',
  );
  await assert.rejects(inspectPersonalAgents({ codexHome }), /personal-agent collision/);
});

test('personal agent inventory parses Windows CRLF multiline manifests', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  await mkdir(join(codexHome, 'agents'));
  await writeFile(
    join(codexHome, 'agents', 'windows.toml'),
    'name = "writer"\r\ndescription = "personal"\r\ndeveloper_instructions = """\r\nRead only.\r\n"""\r\n',
    'utf8',
  );
  assert.deepEqual(await inspectPersonalAgents({ codexHome }), { inspected: 1 });
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

test('personal agent inventory rejects a symlinked agents root', async (t) => {
  const codexHome = await mkdtemp(join(tmpdir(), 'travel-codex-home-'));
  const externalAgents = await mkdtemp(join(tmpdir(), 'travel-external-agents-'));
  t.after(() => rm(codexHome, { recursive: true, force: true }));
  t.after(() => rm(externalAgents, { recursive: true, force: true }));
  await symlink(externalAgents, join(codexHome, 'agents'), process.platform === 'win32' ? 'junction' : 'dir');
  await assert.rejects(inspectPersonalAgents({ codexHome }), /personal-agent inventory not provable/);
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

test('malformed thread startup evidence still deletes the verifier-owned root thread', async (t) => {
  const tempBase = await mkdtemp(join(tmpdir(), 'travel-verifier-start-cleanup-'));
  t.after(() => rm(tempBase, { recursive: true, force: true }));
  const events = [];
  let cloneRoot;
  const runProcess = async (command, args) => {
    if (args[0] === '--version') return { stdout: 'codex-test\n', stderr: '' };
    if (args[0] === 'clone') {
      cloneRoot = args.at(-1);
      await mkdir(join(cloneRoot, 'modules', 'flights'), { recursive: true });
      await writeFile(join(cloneRoot, 'AGENTS.md'), '# test instructions\n');
      for (const skillName of ['explore-domain', 'migration-authoring']) {
        const skillDirectory = join(cloneRoot, '.agents', 'skills', skillName);
        await mkdir(skillDirectory, { recursive: true });
        await writeFile(join(skillDirectory, 'SKILL.md'), canonicalSkillBody(skillName));
      }
      return { stdout: '', stderr: '' };
    }
    if (command === process.execPath) return { stdout: '', stderr: '' };
    throw new Error(`unexpected process call: ${command} ${args.join(' ')}`);
  };
  const appServer = {
    messages: [],
    notify() {},
    async request(method, params) {
      if (method === 'initialize') {
        return {
          userAgent: 'codex-test',
          codexHome: join(tempBase, 'codex-home'),
          platformFamily: 'windows',
          platformOs: 'windows',
        };
      }
      if (method === 'skills/list') return skillsListResponse(cloneRoot);
      if (method === 'thread/start') {
        return {
          thread: { id: 'root-created' },
          cwd: params.cwd,
          approvalPolicy: 'never',
          sandbox: { type: 'readOnly', networkAccess: false },
          reasoningEffort: 'low',
          instructionSources: [],
        };
      }
      if (method === 'thread/delete') {
        assert.deepEqual(params, { threadId: 'root-created' });
        events.push('thread-delete');
        return {};
      }
      throw new Error(`unexpected App Server request: ${method}`);
    },
    async stop() {
      events.push('app-server-stop');
    },
  };

  await assert.rejects(
    runCodexVerifier(process.cwd(), {
      tempBase,
      findExecutable: async (name) => name,
      runProcess,
      createAppServer: () => appServer,
      inspectPersonalAgents: async () => ({ inspected: 0 }),
    }),
    /instruction source/,
  );
  assert.deepEqual(events, ['thread-delete', 'app-server-stop']);
});

test('App Server acceptance closes before long literal behavior smokes begin', async (t) => {
  const tempBase = await mkdtemp(join(tmpdir(), 'travel-verifier-sequence-'));
  t.after(() => rm(tempBase, { recursive: true, force: true }));
  const events = [];
  let cloneRoot;
  const runProcess = async (command, args) => {
    if (args[0] === '--version') return { stdout: 'codex-test\n', stderr: '' };
    if (args[0] === 'clone') {
      cloneRoot = args.at(-1);
      await mkdir(join(cloneRoot, 'modules', 'flights'), { recursive: true });
      await writeFile(join(cloneRoot, 'AGENTS.md'), '# test instructions\n');
      for (const skillName of ['explore-domain', 'migration-authoring']) {
        const skillDirectory = join(cloneRoot, '.agents', 'skills', skillName);
        await mkdir(skillDirectory, { recursive: true });
        await writeFile(join(skillDirectory, 'SKILL.md'), canonicalSkillBody(skillName));
      }
      return { stdout: '', stderr: '' };
    }
    if (command === process.execPath) return { stdout: '', stderr: '' };
    if (args[0] === 'exec') {
      events.push('literal-exec');
      return { stdout: execJsonl(), stderr: '' };
    }
    if (args[0] === '-C' && args[2] === 'status') return { stdout: '', stderr: '' };
    throw new Error(`unexpected process call: ${command} ${args.join(' ')}`);
  };
  const messages = [
    {
      method: 'turn/completed',
      params: { threadId: 'root-1', turn: { id: 'turn-structured', status: 'completed', error: null } },
    },
    ...v2AppMessages(),
  ];
  let turnStarts = 0;
  const appServer = {
    messages,
    notify() {},
    async request(method, params) {
      if (method === 'initialize') {
        return {
          userAgent: 'codex-test',
          codexHome: join(tempBase, 'codex-home'),
          platformFamily: 'windows',
          platformOs: 'windows',
        };
      }
      if (method === 'skills/list') return skillsListResponse(cloneRoot);
      if (method === 'thread/start') {
        return {
          thread: { id: 'root-1' },
          cwd: params.cwd,
          approvalPolicy: 'never',
          sandbox: { type: 'readOnly', networkAccess: false },
          reasoningEffort: 'low',
          instructionSources: [join(params.cwd, 'AGENTS.md')],
        };
      }
      if (method === 'turn/start') {
        turnStarts += 1;
        if (turnStarts === 2) {
          assert.deepEqual(params.input, [{ type: 'text', text: expectedSpawnPrompt }]);
        }
        return { turn: { id: turnStarts === 1 ? 'turn-structured' : 'turn-root' } };
      }
      if (method === 'thread/read') return v2ChildRead();
      if (method === 'thread/list') {
        return {
          data: [
            {
              id: 'child-1',
              parentThreadId: 'root-1',
              source: {
                subAgent: {
                  thread_spawn: {
                    parent_thread_id: 'root-1',
                    depth: 1,
                    agent_path: '/root/harness_identity',
                    agent_role: 'domain-modeler',
                  },
                },
              },
            },
          ],
        };
      }
      if (method === 'thread/delete') {
        events.push('thread-delete');
        return {};
      }
      throw new Error(`unexpected App Server request: ${method} ${JSON.stringify(params)}`);
    },
    async waitFor(predicate) {
      const message = messages.find(predicate);
      if (!message) throw new Error('missing fixture notification');
      return message;
    },
    async stop() {
      events.push('app-server-stop');
    },
  };

  await runCodexVerifier(process.cwd(), {
    tempBase,
    findExecutable: async (name) => name,
    runProcess,
    createAppServer: () => appServer,
    inspectPersonalAgents: async () => ({ inspected: 0 }),
  });
  assert.deepEqual(events, ['thread-delete', 'app-server-stop', 'literal-exec', 'literal-exec']);
});

test('JSONL is parsed structurally and rejects malformed records', () => {
  assert.deepEqual(parseJsonLines('{"type":"message","text":"ok"}\n\n{"type":"done"}\n'), [
    { type: 'message', text: 'ok' },
    { type: 'done' },
  ]);
  assert.throws(() => parseJsonLines('{"ok":true}\nnot-json\n'), /JSONL record 2/);
});

test('Codex exec JSONL requires one successful root turn and its final completed agent message', () => {
  const usage = {
    input_tokens: 10,
    cached_input_tokens: 2,
    cache_write_input_tokens: 0,
    output_tokens: 4,
    reasoning_output_tokens: 1,
  };
  const records = [
    { type: 'thread.started', thread_id: 'thread-1' },
    { type: 'turn.started' },
    {
      type: 'item.started',
      item: {
        id: 'command-1',
        type: 'command_execution',
        command: 'rg --files',
        aggregated_output: '',
        exit_code: null,
        status: 'in_progress',
      },
    },
    {
      type: 'item.completed',
      item: {
        id: 'command-1',
        type: 'command_execution',
        command: 'rg --files',
        aggregated_output: 'AGENTS.md\n',
        exit_code: 0,
        status: 'completed',
      },
    },
    { type: 'item.completed', item: { id: 'message-1', type: 'agent_message', text: 'substantive result' } },
    { type: 'turn.completed', usage },
  ];
  assert.deepEqual(validateExecRun(records), { output: 'substantive result', warnings: [] });
  const withWarning = records.toSpliced(4, 0, {
    type: 'item.completed',
    item: { id: 'warning-1', type: 'error', message: 'non-fatal warning' },
  });
  assert.deepEqual(validateExecRun(withWarning), {
    output: 'substantive result',
    warnings: ['non-fatal warning'],
  });

  const invalid = [
    records.slice(1),
    [records[0], records[0], ...records.slice(1)],
    [records[0], ...records.slice(2)],
    [...records.slice(0, -1), { type: 'turn.failed', error: { message: 'failed' } }],
    [...records, { type: 'item.completed', item: { id: 'late', type: 'agent_message', text: 'late' } }],
    [...records.slice(0, -1), { type: 'future.event' }, records.at(-1)],
    [
      records[0],
      records[1],
      { type: 'item.completed', item: { id: '', type: 'agent_message', text: 'bad' } },
      records.at(-1),
    ],
    [
      records[0],
      records[1],
      { type: 'item.completed', item: { id: 'message-1', type: 'agent_message', text: '   ' } },
      records.at(-1),
    ],
    [...records.slice(0, -1), { type: 'turn.completed', usage: { ...usage, output_tokens: -1 } }],
    [...records.slice(0, -1), { type: 'turn.completed', usage: { ...usage, output_tokens: 1.5 } }],
    records.toSpliced(2, 1, {
      type: 'item.started',
      item: { id: 'command-1', type: 'command_execution', command: 'rg --files' },
    }),
    records.toSpliced(3, 1, {
      type: 'item.completed',
      item: {
        id: 'command-1',
        type: 'command_execution',
        command: 'rg --files',
        aggregated_output: 'AGENTS.md\n',
        exit_code: 0,
        status: 'future_status',
      },
    }),
  ];
  for (const candidate of invalid) assert.throws(() => validateExecRun(candidate));
});

test('Codex exec validates every supported 0.147 item payload union', () => {
  const usage = {
    input_tokens: 1,
    cached_input_tokens: 0,
    cache_write_input_tokens: 0,
    output_tokens: 1,
    reasoning_output_tokens: 0,
  };
  const items = [
    { id: 'agent', type: 'agent_message', text: 'intermediate' },
    { id: 'reasoning', type: 'reasoning', text: 'summary' },
    {
      id: 'command',
      type: 'command_execution',
      command: 'rg --files',
      aggregated_output: 'AGENTS.md\n',
      exit_code: 0,
      status: 'completed',
    },
    { id: 'file', type: 'file_change', changes: [{ path: 'README.md', kind: 'update' }], status: 'completed' },
    {
      id: 'mcp',
      type: 'mcp_tool_call',
      server: 'example',
      tool: 'read',
      arguments: {},
      result: { content: [], structured_content: null },
      error: null,
      status: 'completed',
    },
    {
      id: 'collab',
      type: 'collab_tool_call',
      tool: 'wait',
      sender_thread_id: 'root',
      receiver_thread_ids: ['child'],
      prompt: null,
      agents_states: { child: { status: 'completed', message: null } },
      status: 'completed',
    },
    { id: 'web', type: 'web_search', query: 'Codex docs', action: { type: 'other' } },
    { id: 'todo', type: 'todo_list', items: [{ text: 'verify', completed: true }] },
    { id: 'warning', type: 'error', message: 'non-fatal warning' },
  ];

  for (const item of items) {
    const records = [
      { type: 'thread.started', thread_id: 'thread-1' },
      { type: 'turn.started' },
      { type: 'item.completed', item },
      { type: 'item.completed', item: { id: 'final', type: 'agent_message', text: 'done' } },
      { type: 'turn.completed', usage },
    ];
    assert.doesNotThrow(() => validateExecRun(records), item.type);
  }

  for (const invalidItem of [
    { id: 'command', type: 'command_execution', command: 'rg', exit_code: 0, status: 'completed' },
    { id: 'file', type: 'file_change', changes: [{ path: 'README.md', kind: 'move' }], status: 'completed' },
    {
      id: 'mcp',
      type: 'mcp_tool_call',
      server: 'example',
      tool: 'read',
      arguments: {},
      result: { content: [] },
      error: null,
      status: 'completed',
    },
    {
      id: 'collab',
      type: 'collab_tool_call',
      tool: 'wait',
      sender_thread_id: 'root',
      receiver_thread_ids: ['child'],
      agents_states: {},
      status: 'completed',
    },
    { id: 'web', type: 'web_search', query: 'Codex', action: { type: 'search', queries: [1] } },
    { id: 'todo', type: 'todo_list', items: [{ text: 'verify', completed: 'yes' }] },
  ]) {
    assert.throws(() =>
      validateExecRun([
        { type: 'thread.started', thread_id: 'thread-1' },
        { type: 'turn.started' },
        { type: 'item.completed', item: invalidItem },
        { type: 'item.completed', item: { id: 'final', type: 'agent_message', text: 'done' } },
        { type: 'turn.completed', usage },
      ]),
    );
  }
});

test('App Server evidence rejects a legacy spawn downgrade when MultiAgentV2 is required', () => {
  assert.throws(
    () =>
      collectAppServerEvidence(appMessages(), {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /MultiAgentV2/,
  );
});

test('App Server evidence accepts one exact MultiAgentV2 started activity with a canonical child path', () => {
  assert.deepEqual(
    collectAppServerEvidence(v2AppMessages(), {
      rootThreadId: 'root-1',
      rootTurnId: 'turn-root',
      probe,
    }),
    { childThreadId: 'child-1' },
  );

  for (const messages of [
    v2AppMessages({ childId: '' }),
    v2AppMessages({ childId: 'root-1' }),
    v2AppMessages({ agentPath: '' }),
    v2AppMessages({ agentPath: '/other/domain_modeler' }),
    v2AppMessages({ agentPath: '/root/../domain_modeler' }),
    v2AppMessages({ agentPath: '/root/domain_modeler' }),
    [...v2AppMessages(), structuredClone(v2AppMessages()[1])],
  ]) {
    assert.throws(() =>
      collectAppServerEvidence(messages, {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    );
  }

  for (const mutate of [
    (item) => {
      item.name = 'other_tool';
    },
    (item) => {
      item.call_id = 'other-call';
    },
    (item) => {
      item.arguments = '{not-json';
    },
    (item) => {
      item.arguments = JSON.stringify({
        message: probe,
        task_name: 'harness_identity',
        agent_type: 'domain-modeler',
      });
    },
    (item) => {
      item.arguments = JSON.stringify({
        message: probe,
        task_name: 'harness_identity',
        agent_type: 'domain-modeler',
        fork_turns: 'all',
      });
    },
  ]) {
    const messages = v2AppMessages();
    mutate(messages[0].params.item);
    assert.throws(() =>
      collectAppServerEvidence(messages, {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    );
  }
});

test('child completion before root does not terminate or satisfy root evidence', () => {
  const messages = [
    {
      method: 'turn/completed',
      params: { threadId: 'child-1', turn: { id: 'turn-child', status: 'completed', error: null } },
    },
    ...v2AppMessages(),
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
      collectAppServerEvidence(v2AppMessages({ childId: '' }), {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /child thread ID/,
  );
  for (const status of ['failed', 'inProgress']) {
    assert.throws(
      () =>
        collectAppServerEvidence(v2AppMessages({ status }), {
          rootThreadId: 'root-1',
          rootTurnId: 'turn-root',
          probe,
        }),
      /root turn status/,
    );
  }
  const errored = v2AppMessages();
  errored.at(-1).params.turn.error = { message: 'persisted root error' };
  assert.throws(
    () =>
      collectAppServerEvidence(errored, {
        rootThreadId: 'root-1',
        rootTurnId: 'turn-root',
        probe,
      }),
    /root turn status/,
  );
});

test('App Server evidence requires root-thread correlation and exact spawn fields', () => {
  const missingThread = v2AppMessages();
  delete missingThread[2].params.threadId;
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
      messages[1].params.threadId = 'other-root';
    },
    (messages) => {
      messages[0].params.threadId = 'other-root';
    },
    (messages) => {
      messages.splice(2, 0, structuredClone(messages[1]));
    },
  ]) {
    const messages = v2AppMessages();
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

test('root self-report cannot substitute for a non-empty child response', () => {
  const canary = childRead({ text: '' });
  canary.rootResponse = roleId;
  assert.throws(() => assertChildEvidence(canary, { childThreadId: 'child-1' }), /non-empty agentMessage/);
});

test('child history read waits for the exact turn to become completed', async () => {
  const { readCompletedChild } = await import('./verify-codex.mjs');
  assert.equal(typeof readCompletedChild, 'function');
  const responses = [
    { thread: { id: 'child-1', turns: [] } },
    {
      thread: {
        id: 'child-1',
        turns: [{ id: 'turn-child', status: 'inProgress', itemsView: 'full', items: [], error: null }],
      },
    },
    childRead(),
  ];
  let requests = 0;
  let delays = 0;
  const result = await readCompletedChild(
    {
      request: async (method, params) => {
        assert.equal(method, 'thread/read');
        assert.deepEqual(params, { threadId: 'child-1', includeTurns: true });
        const response = responses[requests];
        requests += 1;
        return response;
      },
    },
    {
      childThreadId: 'child-1',
      maxAttempts: 3,
      delay: async () => {
        delays += 1;
      },
    },
  );
  assert.equal(result, responses[2]);
  assert.equal(requests, 3);
  assert.equal(delays, 2);

  await assert.rejects(
    readCompletedChild(
      {
        request: async () => ({
          thread: {
            id: 'child-1',
            turns: [
              { id: 'turn-a', status: 'completed', itemsView: 'full', items: [], error: null },
              { id: 'turn-b', status: 'completed', itemsView: 'full', items: [], error: null },
            ],
          },
        }),
      },
      { childThreadId: 'child-1', maxAttempts: 1 },
    ),
    /child history has 2 turns.*completed:none.*completed:none/,
  );
});

test('child history default polling window accommodates an authenticated model turn longer than ten seconds', async () => {
  let requests = 0;
  const inProgress = {
    thread: {
      id: 'child-1',
      turns: [{ id: 'turn-child', status: 'inProgress', itemsView: 'full', items: [], error: null }],
    },
  };
  const completed = childRead();
  const result = await readCompletedChild(
    { request: async () => (++requests <= 40 ? inProgress : completed) },
    { childThreadId: 'child-1', delay: async () => {} },
  );

  assert.equal(requests, 41);
  assert.equal(result, completed);
});

test('fork-free MultiAgentV2 child evidence accepts exactly one child-owned turn', async () => {
  const inProgress = v2ChildRead({ ownStatus: 'inProgress', ownItems: [] });
  const completed = v2ChildRead();
  const responses = [{ thread: { id: 'child-1', turns: [] } }, inProgress, completed];
  let requests = 0;
  const result = await readCompletedChild(
    { request: async () => responses[requests++] },
    {
      childThreadId: 'child-1',
      maxAttempts: 3,
      delay: async () => {},
    },
  );

  assert.equal(result, completed);
  assert.doesNotThrow(() => assertChildEvidence(completed, { childThreadId: 'child-1' }));
});

test('child evidence accepts one non-empty response and rejects every child tool-use item', () => {
  assert.doesNotThrow(() => assertChildEvidence(childRead(), { childThreadId: 'child-1' }));
  for (const type of ['commandExecution', 'fileChange', 'mcpToolCall', 'collabToolCall']) {
    assert.throws(
      () =>
        assertChildEvidence(childRead({ extraItem: { type } }), {
          childThreadId: 'child-1',
        }),
      /child tool use/,
    );
  }
});

test('child evidence requires one completed turn and rejects unknown item types', () => {
  for (const status of ['failed', 'inProgress']) {
    const response = childRead();
    response.thread.turns[0].status = status;
    assert.throws(() => assertChildEvidence(response, { childThreadId: 'child-1' }), /completed child turn/);
  }
  assert.throws(
    () =>
      assertChildEvidence(childRead({ extraItem: { type: 'futureToolThing' } }), {
        childThreadId: 'child-1',
      }),
    /unsupported child item/,
  );
  const missingItems = childRead();
  delete missingItems.thread.turns[0].items;
  assert.throws(() => assertChildEvidence(missingItems, { childThreadId: 'child-1' }), /items are unavailable/);
  const multiple = childRead();
  multiple.thread.turns.push({ id: 'turn-extra', status: 'completed', itemsView: 'full', items: [], error: null });
  assert.throws(() => assertChildEvidence(multiple, { childThreadId: 'child-1' }), /final child evidence has 2 turns/);
});

test('child polling and evidence require a full persisted item projection', async () => {
  const summaryOnly = childRead();
  summaryOnly.thread.turns[0].itemsView = 'summary';
  await assert.rejects(
    readCompletedChild({ request: async () => summaryOnly }, { childThreadId: 'child-1', maxAttempts: 1 }),
    /itemsView.*full/,
  );
  assert.throws(() => assertChildEvidence(summaryOnly, { childThreadId: 'child-1' }), /itemsView.*full/);
});

test('child polling and evidence require persisted turn IDs and null errors', async () => {
  const missingId = childRead();
  delete missingId.thread.turns[0].id;
  await assert.rejects(
    readCompletedChild({ request: async () => missingId }, { childThreadId: 'child-1', maxAttempts: 1 }),
    /turn ID/,
  );

  const errored = childRead();
  errored.thread.turns[0].id = 'turn-child';
  errored.thread.turns[0].error = { message: 'unexpected persisted error' };
  assert.throws(() => assertChildEvidence(errored, { childThreadId: 'child-1' }), /turn error must be null/);
});

test('thread listing rejects a legacy source without structured MultiAgentV2 role linkage', () => {
  assert.throws(
    () =>
      assertListedChild(
        { data: [{ id: 'child-1', parentThreadId: 'root-1', source: 'subAgent' }] },
        { rootThreadId: 'root-1', childThreadId: 'child-1' },
      ),
    /linked child/,
  );
});

test('thread listing accepts the exact MultiAgentV2 role-linked child source', () => {
  assert.doesNotThrow(() =>
    assertListedChild(
      {
        data: [
          {
            id: 'child-1',
            parentThreadId: 'root-1',
            source: {
              subAgent: {
                thread_spawn: {
                  parent_thread_id: 'root-1',
                  depth: 1,
                  agent_path: '/root/harness_identity',
                  agent_role: 'domain-modeler',
                },
              },
            },
          },
        ],
      },
      { rootThreadId: 'root-1', childThreadId: 'child-1', roleId },
    ),
  );
  for (const mutation of [
    (source) => {
      source.parent_thread_id = 'other-root';
    },
    (source) => {
      source.agent_role = 'default';
    },
    (source) => {
      source.depth = 2;
    },
    (source) => {
      source.agent_path = '/root/domain_modeler';
    },
  ]) {
    const source = {
      parent_thread_id: 'root-1',
      depth: 1,
      agent_path: '/root/harness_identity',
      agent_role: 'domain-modeler',
    };
    mutation(source);
    assert.throws(() =>
      assertListedChild(
        {
          data: [
            {
              id: 'child-1',
              parentThreadId: 'root-1',
              source: { subAgent: { thread_spawn: source } },
            },
          ],
        },
        { rootThreadId: 'root-1', childThreadId: 'child-1', roleId },
      ),
    );
  }

  assert.throws(
    () =>
      assertListedChild(
        { data: [{ id: 'child-1', parentThreadId: 'root-1', source: 'subAgent' }] },
        { rootThreadId: 'root-1', childThreadId: 'child-1', roleId },
      ),
    /linked child/,
  );
});

test('subagent listing includes the MultiAgentV2 thread-spawn source kind', async () => {
  const { listSubagentThreads } = await import('./verify-codex.mjs');
  assert.equal(typeof listSubagentThreads, 'function');
  const calls = [];
  const response = { data: [] };
  assert.equal(
    await listSubagentThreads(
      {
        request: async (method, params) => {
          calls.push({ method, params });
          return response;
        },
      },
      'root-1',
    ),
    response,
  );
  assert.deepEqual(calls, [
    {
      method: 'thread/list',
      params: {
        parentThreadId: 'root-1',
        sourceKinds: ['subAgent', 'subAgentReview', 'subAgentCompact', 'subAgentThreadSpawn', 'subAgentOther'],
      },
    },
  ]);
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

test('clone target reservation keeps ownership before a failed removal so finally cleans the exact directory', async (t) => {
  const tempBase = await mkdtemp(join(tmpdir(), 'travel-verifier-reservation-'));
  t.after(() => rm(tempBase, { recursive: true, force: true }));
  const removeCalls = [];
  let processCalls = 0;

  await assert.rejects(
    runCodexVerifier(process.cwd(), {
      tempBase,
      findExecutable: async (name) => name,
      runProcess: async (_command, args) => {
        processCalls += 1;
        if (args[0] === '--version') return { stdout: 'codex-test\n', stderr: '' };
        throw new Error('clone must not start after reservation removal fails');
      },
      makeDirectory: mkdir,
      remove: async (target, options) => {
        removeCalls.push({ target, options });
        if (removeCalls.length === 1) throw new Error('reservation blocked');
        await rm(target, options);
      },
    }),
    /reservation blocked/,
  );

  assert.equal(processCalls, 1);
  assert.equal(removeCalls.length, 2);
  assert.equal(removeCalls[0].target, removeCalls[1].target);
  assert.deepEqual(removeCalls[0].options, { recursive: true });
  assert.deepEqual(removeCalls[1].options, { recursive: true, force: true });
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

test('App Server stop accepts authoritative wrapper closure without force termination', async () => {
  const child = new FakeChild(2468);
  const lines = new FakeLines();
  let forceCalls = 0;
  child.stdin.end = () => {
    child.exitCode = 0;
    child.emit('close', 0, null);
  };
  const client = createAppServerClient(child, {
    lineReaderFactory: () => lines,
    stopProcess: async () => {
      forceCalls += 1;
    },
    gracefulStopMs: 100,
  });

  await client.stop();

  assert.equal(forceCalls, 0);
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

test('Windows cleanup accepts a taskkill not-found race after authoritative owned-process close', async () => {
  const child = new FakeChild(2112);
  const taskkill = new FakeChild(3112);
  const stopped = stopOwnedProcess(child, {
    platform: 'win32',
    systemRoot: 'C:\\Windows',
    spawnProcess: () => taskkill,
    timeoutMs: 100,
  });

  child.exitCode = 0;
  child.emit('close', 0, null);
  taskkill.exitCode = 128;
  taskkill.emit('close', 128, null);

  await stopped;
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

test('POSIX owned process cleanup escalates a non-cooperative process group from SIGTERM to SIGKILL', async () => {
  const child = new FakeChild(7777);
  const signals = [];
  await stopOwnedProcess(child, {
    platform: 'linux',
    timeoutMs: 5,
    killProcess: (pid, signal) => {
      signals.push({ pid, signal });
      if (signal === 'SIGKILL') {
        child.signalCode = signal;
        queueMicrotask(() => child.emit('close', null, signal));
      }
    },
  });
  assert.deepEqual(signals, [
    { pid: -7777, signal: 'SIGTERM' },
    { pid: -7777, signal: 'SIGKILL' },
  ]);
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
