import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdir, mkdtemp, readdir, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { formatValidationResult, HARNESS_CONTRACT, validateHarness } from './validate.mjs';

function execFileAsync(file, args) {
  return new Promise((resolvePromise, rejectPromise) => {
    execFile(file, args, (error, stdout, stderr) => {
      if (error) {
        Object.assign(error, { stdout, stderr });
        rejectPromise(error);
        return;
      }
      resolvePromise({ stdout, stderr });
    });
  });
}
const repositoryRoot = fileURLToPath(new URL('../../', import.meta.url));
const EXPECTED_HARNESS_CONTRACT = {
  instructionRoots: [
    '',
    'apps/Travel.AI',
    'modules/flights',
    'modules/hotels',
    'modules/identity',
    'modules/rail',
    'modules/trips',
    'shared',
  ],
  skills: [
    'adr',
    'domain-modeling',
    'explore-domain',
    'integration-from-openapi',
    'migration-authoring',
    'spec',
    'test-authoring',
    'test-this',
  ],
  legacyCommands: ['adr', 'explore-domain', 'integration-from-openapi', 'spec', 'test-this'],
  agents: {
    'adr-writer': {
      skill: 'adr',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
      claudeTools: 'Read, Grep, Glob, Write, Edit',
    },
    'domain-modeler': {
      skill: 'domain-modeling',
      codexSandbox: 'read-only',
      claudePermission: 'plan',
      claudeTools: 'Read, Grep, Glob',
    },
    'integration-mapper': {
      skill: 'integration-from-openapi',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
      claudeTools: 'Read, Grep, Glob, WebFetch, WebSearch, Write, Edit',
    },
    'migration-author': {
      skill: 'migration-authoring',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
      claudeTools: 'Read, Grep, Glob, Write, Edit, Bash',
    },
    'test-author': {
      skill: 'test-authoring',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
      claudeTools: 'Read, Grep, Glob, Write, Edit, Bash',
    },
  },
  harnessFiles: ['validate.mjs', 'validate.test.mjs', 'verify-codex.mjs', 'verify-codex.test.mjs'],
};
const authority = [
  '## Authority',
  '',
  "Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.",
].join('\n');

const skillDescriptions = Object.freeze({
  adr: 'Write or revise a Travel Architecture Decision Record for a confirmed architectural decision.',
  'domain-modeling': 'Model Travel domain concepts using aggregates and invariants.',
  'explore-domain': 'Produce a read-only current-state map of a Travel domain module.',
  'integration-from-openapi': 'Design or implement a provider anti-corruption layer from OpenAPI.',
  'migration-authoring': 'Design and review safe EF Core source migrations.',
  spec: 'Design and write a Travel feature specification.',
  'test-authoring': 'Write focused tests using the repository testing strategy.',
  'test-this': 'Select the right tests for an explicit changed file.',
});

const agentDescriptions = Object.freeze({
  'adr-writer':
    'Writes or revises one requested Travel Architecture Decision Record from a confirmed decision and current repository evidence.',
  'domain-modeler':
    'Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence.',
  'integration-mapper':
    'Designs or implements a provider anti-corruption layer while keeping external DTOs behind the Infrastructure boundary.',
  'migration-author':
    'Designs, generates, and reviews safe EF Core source migrations without implicitly applying a database change.',
  'test-author':
    "Writes focused .NET or TypeScript tests using the repository's seven-layer strategy and nearest existing patterns.",
});

async function put(root, relativePath, content) {
  const path = join(root, ...relativePath.split('/'));
  await mkdir(resolve(path, '..'), { recursive: true });
  await writeFile(path, content, 'utf8');
}

function skillText(name) {
  return `---\nname: ${name}\ndescription: ${skillDescriptions[name]}\n---\n\nCanonical Travel workflow ID: travel-agency/${name}.\n\n# ${name}\n\nFollow repository evidence.\n\n${authority}\n`;
}

function claudeSkillText(name) {
  return `---\nname: ${name}\ndescription: ${skillDescriptions[name]}\n---\n\nResolve the Git repository root, then read and follow \`.agents/skills/${name}/SKILL.md\` from that root as the canonical workflow.\n`;
}

function agentBody(name) {
  const skill = EXPECTED_HARNESS_CONTRACT.agents[name].skill;
  const probe =
    name === 'domain-modeler'
      ? '\nFor the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with exactly `travel-agency/domain-modeler` and nothing else; do not shorten it, read files, or use tools. For every other task, follow the canonical workflow below.'
      : '';
  return `Repository role ID: \`travel-agency/${name}\`.${probe}\nResolve the Git repository root, then read and follow \`.agents/skills/${skill}/SKILL.md\` from that root.\n`;
}

function codexAgentText(name) {
  const contract = EXPECTED_HARNESS_CONTRACT.agents[name];
  return `name = "${name}"\ndescription = "${agentDescriptions[name]}"\nsandbox_mode = "${contract.codexSandbox}"\ndeveloper_instructions = """\n${agentBody(name)}"""\n`;
}

function claudeAgentText(name) {
  const contract = EXPECTED_HARNESS_CONTRACT.agents[name];
  return `---\nname: ${name}\ndescription: ${agentDescriptions[name]}\ntools: ${contract.claudeTools}\npermissionMode: ${contract.claudePermission}\n---\n\n${agentBody(name)}`;
}

async function createValidFixture(t, { reverse = false } = {}) {
  const root = await mkdtemp(join(tmpdir(), 'travel-ai-harness-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const writes = [];
  for (const instructionRoot of EXPECTED_HARNESS_CONTRACT.instructionRoots) {
    const prefix = instructionRoot ? `${instructionRoot}/` : '';
    writes.push([`${prefix}AGENTS.md`, '# Instructions\n']);
    writes.push([`${prefix}CLAUDE.md`, '@AGENTS.md\n']);
  }
  for (const name of EXPECTED_HARNESS_CONTRACT.skills) {
    writes.push([`.agents/skills/${name}/SKILL.md`, skillText(name)]);
    writes.push([`.claude/skills/${name}/SKILL.md`, claudeSkillText(name)]);
  }
  for (const name of EXPECTED_HARNESS_CONTRACT.legacyCommands) {
    writes.push([
      `.claude/commands/${name}.md`,
      `Resolve the Git repository root, then read and follow \`.agents/skills/${name}/SKILL.md\` from that root.\n\nArguments: $ARGUMENTS\n`,
    ]);
  }
  for (const name of Object.keys(EXPECTED_HARNESS_CONTRACT.agents)) {
    writes.push([`.codex/agents/${name}.toml`, codexAgentText(name)]);
    writes.push([`.claude/agents/${name}.md`, claudeAgentText(name)]);
  }
  for (const name of EXPECTED_HARNESS_CONTRACT.harnessFiles) {
    writes.push([`tools/ai-harness/${name}`, '// fixture inventory member\n']);
  }
  writes.push([
    'package.json',
    `${JSON.stringify(
      {
        scripts: {
          'test:ai-harness': 'node --test tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.test.mjs',
          'check:ai-harness': 'npm run test:ai-harness && node tools/ai-harness/validate.mjs',
          'verify:ai-harness:codex': 'node tools/ai-harness/verify-codex.mjs',
        },
      },
      null,
      2,
    )}\n`,
  ]);
  writes.push([
    '.github/workflows/ci.yml',
    'jobs:\n  lint:\n    steps:\n      - uses: actions/setup-node@v4\n      - name: Validate AI harness\n        run: npm run check:ai-harness\n      - uses: actions/setup-dotnet@v4\n      - run: npm ci\n',
  ]);
  for (const [path, content] of reverse ? writes.reverse() : writes) {
    await put(root, path, content);
  }
  return root;
}

function codes(issues) {
  return new Set(issues.map(({ code }) => code));
}

test('the production harness contract matches the independently declared acceptance inventory', () => {
  assert.deepEqual(HARNESS_CONTRACT, EXPECTED_HARNESS_CONTRACT);
});

test('the current repository tree satisfies the complete harness contract', async () => {
  const issues = await validateHarness(new URL('../../', import.meta.url));
  assert.deepEqual(issues, []);
});

test('valid UTF-8 BOM and CRLF instruction imports are normalized', async (t) => {
  const fixture = await createValidFixture(t);
  await put(fixture, 'modules/flights/CLAUDE.md', '\uFEFF@AGENTS.md\r\n');
  assert.deepEqual(await validateHarness(fixture), []);
});

test('issues are deterministic and use slash-normalized paths', async (t) => {
  const fixture = await createValidFixture(t);
  await rm(join(fixture, 'modules', 'flights', 'AGENTS.md'));
  await put(fixture, '.agents/skills/extra/SKILL.md', skillText('adr'));
  const first = await validateHarness(fixture);
  const second = await validateHarness(fixture);
  assert.deepEqual(first, second);
  assert.ok(first.every((entry) => !entry.path.includes('\\')));
});

test('instruction pairs reject missing canonical files and duplicated adapters', async (t) => {
  const fixture = await createValidFixture(t);
  await rm(join(fixture, 'shared', 'AGENTS.md'));
  await put(fixture, 'modules/rail/CLAUDE.md', '@AGENTS.md\n# duplicated content\n');
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('instructions/missing'));
  assert.ok(found.has('instructions/claude-import'));
});

test('canonical skill inventory and frontmatter/body contracts are enforced', async (t) => {
  const fixture = await createValidFixture(t);
  await rm(join(fixture, '.agents', 'skills', 'adr'), { recursive: true });
  await put(fixture, '.agents/skills/extra/SKILL.md', skillText('adr'));
  await put(fixture, '.agents/skills/spec/SKILL.md', '---\nname: wrong\ndescription:\n---\n');
  await put(
    fixture,
    '.agents/skills/test-this/SKILL.md',
    skillText('test-this').replace('description:', 'allowed-tools: Bash\ndescription:'),
  );
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('skills/missing'));
  assert.ok(found.has('skills/extra'));
  assert.ok(found.has('skills/name'));
  assert.ok(found.has('skills/description'));
  assert.ok(found.has('skills/body'));
  assert.ok(found.has('skills/schema'));
});

test('skill directories require exactly one physical SKILL.md file', async (t) => {
  const fixture = await createValidFixture(t);
  await rm(join(fixture, '.agents', 'skills', 'adr', 'SKILL.md'));
  await rm(join(fixture, '.claude', 'skills', 'spec', 'SKILL.md'));
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('skills/files'));
  assert.ok(found.has('claude-skills/files'));
});

test('Claude skills reject metadata, canonical-reference, and duplicated-body drift', async (t) => {
  const fixture = await createValidFixture(t);
  await put(
    fixture,
    '.claude/skills/adr/SKILL.md',
    claudeSkillText('adr').replace(skillDescriptions.adr, 'Drifted description.'),
  );
  await put(
    fixture,
    '.claude/skills/spec/SKILL.md',
    `${claudeSkillText('spec')
      .replace('description:', 'allowed-tools: Bash\ndescription:')
      .replace('.agents/skills/spec', '.agents/skills/adr')}x${'x'.repeat(1_300)}`,
  );
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('claude-skills/description'));
  assert.ok(found.has('claude-skills/reference'));
  assert.ok(found.has('claude-skills/size'));
  assert.ok(found.has('claude-skills/schema'));
});

test('agent inventory, mapping, role identity, probe, and capabilities are enforced', async (t) => {
  const fixture = await createValidFixture(t);
  await rm(join(fixture, '.codex', 'agents', 'test-author.toml'));
  await put(fixture, '.codex/agents/extra.toml', codexAgentText('adr-writer'));
  await put(
    fixture,
    '.codex/agents/adr-writer.toml',
    codexAgentText('adr-writer')
      .replace('name = "adr-writer"', 'name = "wrong"')
      .replace('travel-agency/adr-writer', 'travel-agency/wrong')
      .replace('.agents/skills/adr', '.agents/skills/spec')
      .replace('sandbox_mode = "workspace-write"', 'sandbox_mode = "read-only"'),
  );
  await put(
    fixture,
    '.claude/agents/domain-modeler.md',
    claudeAgentText('domain-modeler')
      .replace('TRAVEL_AI_HARNESS_IDENTITY_PROBE', 'WRONG_PROBE')
      .replace('permissionMode: plan', 'permissionMode: default'),
  );
  await put(
    fixture,
    '.claude/agents/test-author.md',
    claudeAgentText('test-author').replace(
      'Resolve the Git repository root',
      'For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with exactly `travel-agency/domain-modeler` and nothing else; do not shorten it, read files, or use tools. For every other task, follow the canonical workflow below.\nResolve the Git repository root',
    ),
  );
  const found = codes(await validateHarness(fixture));
  for (const expected of [
    'agents/missing',
    'agents/extra',
    'agents/name',
    'agents/role-id',
    'agents/mapping',
    'agents/probe',
    'agents/capability',
  ]) {
    assert.ok(found.has(expected), expected);
  }
});

test('domain-modeler probe requires the literal non-inferable repository role ID', async (t) => {
  const fixture = await createValidFixture(t);
  const oldProbe =
    'For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with only the repository role ID; do not read files or use tools. For every other task, follow the canonical workflow below.';
  const deterministicProbe =
    'For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with exactly `travel-agency/domain-modeler` and nothing else; do not shorten it, read files, or use tools. For every other task, follow the canonical workflow below.';
  for (const path of ['.codex/agents/domain-modeler.toml', '.claude/agents/domain-modeler.md']) {
    const content = await readFile(join(fixture, path), 'utf8');
    await put(fixture, path, content.replace(deterministicProbe, oldProbe));
  }

  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('agents/probe'));
});

test('agent manifests reject extra keys, model pins, tool drift, and additional body instructions', async (t) => {
  const fixture = await createValidFixture(t);
  await put(
    fixture,
    '.codex/agents/adr-writer.toml',
    codexAgentText('adr-writer').replace(
      'sandbox_mode = "workspace-write"',
      'model = "gpt-5"\nsandbox_mode = "workspace-write"',
    ),
  );
  await put(
    fixture,
    '.claude/agents/domain-modeler.md',
    claudeAgentText('domain-modeler')
      .replace('tools: Read, Grep, Glob', 'tools: Read, Write')
      .replace('permissionMode: plan', 'model: claude-opus\npermissionMode: plan')
      .replace(/\n$/, '\nDo an additional unapproved step.\n'),
  );

  const found = codes(await validateHarness(fixture));
  for (const expected of ['agents/schema', 'agents/tools', 'agents/body']) {
    assert.ok(found.has(expected), expected);
  }
});

test('hooks and obsolete Claude environment tokens are rejected', async (t) => {
  const fixture = await createValidFixture(t);
  await put(fixture, '.codex/hooks.json', '{}\n');
  await put(fixture, '.claude/settings.json', '{"hooks": {}}\n');
  await put(fixture, 'README.md', 'CLAUDE_TOOL_INPUT_FILE_PATH\nCLAUDE_RECENT_EDITS\n');
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('hooks/forbidden-file'));
  assert.ok(found.has('hooks/claude-top-level'));
  assert.ok(found.has('stale/text'));
});

test('unsupported Codex paths and non-portable canonical skill commands are rejected', async (t) => {
  const fixture = await createValidFixture(t);
  await put(fixture, '.codex/commands/example.md', 'unsupported\n');
  await put(
    fixture,
    '.agents/skills/adr/SKILL.md',
    `${skillText('adr')}git log | head -1\ngit log | head -n 1\ndotnet test \\\n  --filter Unit\n`,
  );
  const found = codes(await validateHarness(fixture));
  assert.ok(found.has('paths/codex-commands'));
  assert.ok(found.has('portability/head'));
  assert.ok(found.has('portability/continuation'));
});

test('a case-only Codex directory drift is detected through directory entries', async (t) => {
  const fixture = await createValidFixture(t);
  const exact = join(fixture, '.codex');
  const probe = join(fixture, '__codex_case_probe__');
  const drifted = join(fixture, '.Codex');
  await rename(exact, probe);
  await rename(probe, drifted);
  assert.ok((await readdir(fixture)).includes('.Codex'));
  assert.ok(codes(await validateHarness(fixture)).has('paths/codex-case'));
});

test('malformed JSON, TOML, and frontmatter become structured issues', async (t) => {
  const fixture = await createValidFixture(t);
  await put(fixture, '.claude/settings.json', '{');
  await put(fixture, '.codex/agents/adr-writer.toml', 'name = [unsupported]\n');
  await put(fixture, '.agents/skills/adr/SKILL.md', '---\nname: adr\n');
  const issues = await validateHarness(fixture);
  assert.ok(codes(issues).has('parse/json'));
  assert.ok(codes(issues).has('parse/toml'));
  assert.ok(codes(issues).has('parse/frontmatter'));
  assert.ok(issues.every((entry) => typeof entry.message === 'string'));
});

test('harness tooling inventory members must be physical files', async (t) => {
  const fixture = await createValidFixture(t);
  const member = join(fixture, 'tools', 'ai-harness', 'verify-codex.mjs');
  await rm(member);
  await mkdir(member);
  assert.ok(codes(await validateHarness(fixture)).has('tooling/type'));
});

test('missing package and CI contracts produce deterministic dedicated issues', async (t) => {
  const packageFixture = await createValidFixture(t);
  await rm(join(packageFixture, 'package.json'));
  assert.deepEqual(
    (await validateHarness(packageFixture)).filter((entry) => entry.path === 'package.json'),
    [{ code: 'npm/missing', path: 'package.json', message: 'required package.json is missing' }],
  );

  const ciFixture = await createValidFixture(t);
  await rm(join(ciFixture, '.github', 'workflows', 'ci.yml'));
  assert.deepEqual(
    (await validateHarness(ciFixture)).filter((entry) => entry.path === '.github/workflows/ci.yml'),
    [
      {
        code: 'ci/missing',
        path: '.github/workflows/ci.yml',
        message: 'required CI workflow is missing',
      },
    ],
  );
});

test('CLI writes success to stdout and failures to stderr with stable exit codes', async (t) => {
  const fixture = await createValidFixture(t);
  const cli = join(repositoryRoot, 'tools', 'ai-harness', 'validate.mjs');
  const success = await execFileAsync(process.execPath, [cli, fixture]);
  assert.equal(success.stderr, '');
  assert.equal(
    success.stdout.trim(),
    'AI harness validation passed: 8 instruction pairs, 8 skills, 5 agent pairs, 5 legacy commands.',
  );
  await rm(join(fixture, '.codex', 'agents', 'test-author.toml'));
  await assert.rejects(execFileAsync(process.execPath, [cli, fixture]), (error) => {
    assert.equal(error.code, 1);
    assert.equal(error.stdout, '');
    assert.match(error.stderr, /AI harness validation failed/);
    assert.match(error.stderr, /\[agents\/missing\]/);
    return true;
  });
});

test('issue ordering is independent of fixture creation order', async (t) => {
  const first = await createValidFixture(t);
  const second = await createValidFixture(t, { reverse: true });
  for (const fixture of [first, second]) {
    await rm(join(fixture, '.codex', 'agents', 'test-author.toml'));
    await put(fixture, '.codex/hooks.json', '{}\n');
  }
  assert.deepEqual(await validateHarness(first), await validateHarness(second));
});

test('the formatter emits the exact stable failure shape', () => {
  assert.equal(
    formatValidationResult([
      {
        code: 'agents/missing',
        path: '.codex/agents',
        message: 'missing agent "test-author"',
      },
      {
        code: 'hooks/forbidden-file',
        path: '.codex/hooks.json',
        message: 'copied hook adapter must be removed; formatting is owned by Lefthook and CI',
      },
    ]),
    'AI harness validation failed (2 issues):\n- [agents/missing] .codex/agents: missing agent "test-author"\n- [hooks/forbidden-file] .codex/hooks.json: copied hook adapter must be removed; formatting is owned by Lefthook and CI',
  );
});
