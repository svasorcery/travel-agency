import { lstat, readdir, readFile } from 'node:fs/promises';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const HARNESS_CONTRACT = Object.freeze({
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
  agents: Object.freeze({
    'adr-writer': Object.freeze({
      skill: 'adr',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
    }),
    'domain-modeler': Object.freeze({
      skill: 'domain-modeling',
      codexSandbox: 'read-only',
      claudePermission: 'plan',
    }),
    'integration-mapper': Object.freeze({
      skill: 'integration-from-openapi',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
    }),
    'migration-author': Object.freeze({
      skill: 'migration-authoring',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
    }),
    'test-author': Object.freeze({
      skill: 'test-authoring',
      codexSandbox: 'workspace-write',
      claudePermission: 'default',
    }),
  }),
  harnessFiles: ['validate.mjs', 'validate.test.mjs', 'verify-codex.mjs', 'verify-codex.test.mjs'],
});

const AUTHORITY_BLOCK = [
  '## Authority',
  '',
  "Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.",
].join('\n');
const PROBE_LINE =
  'For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with only the repository role ID; do not read files or use tools. For every other task, follow the canonical workflow below.';
const AGENT_DESCRIPTIONS = Object.freeze({
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
const STALE_TOKENS = [
  'CLAUDE_TOOL_INPUT_FILE_PATH',
  'CLAUDE_RECENT_EDITS',
  '.Codex/',
  '.Codex\\',
  '.codex/commands/',
  '.codex\\commands\\',
  'static Result<T> Create',
  '0007-marten-ef-coexistence.md',
  'shared PostgreSQL + Redis',
  'git diff HEAD~5',
  'git commit -m',
];
const FALSE_README_CLAIMS = [
  'Extracted AI service using Microsoft Agent Framework (MAF) 1.0',
  '| AI | MAF 1.0 + Claude',
];

function normalizeText(text) {
  return text.replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n');
}

function issue(code, path, message, line) {
  return {
    code,
    path: path.replaceAll('\\', '/'),
    ...(line === undefined ? {} : { line }),
    message,
  };
}

function compareIssues(left, right) {
  return (
    left.code.localeCompare(right.code) ||
    left.path.localeCompare(right.path) ||
    (left.line ?? Number.POSITIVE_INFINITY) - (right.line ?? Number.POSITIVE_INFINITY) ||
    left.message.localeCompare(right.message)
  );
}

function rootPath(input) {
  if (input instanceof URL) return resolve(fileURLToPath(input));
  return resolve(input ?? process.cwd());
}

function displayPath(root, path) {
  return relative(root, path).replaceAll('\\', '/') || '.';
}

async function readNormalized(root, path, issues, { optional = false } = {}) {
  try {
    return normalizeText(await readFile(path, 'utf8'));
  } catch (error) {
    if (optional && error?.code === 'ENOENT') return undefined;
    if (error?.code !== 'ENOENT') {
      issues.push(
        issue('filesystem/read', displayPath(root, path), `could not read file: ${error?.code ?? 'unknown error'}`),
      );
    }
    return undefined;
  }
}

async function entries(root, path, issues, { optional = false } = {}) {
  try {
    return await readdir(path, { withFileTypes: true });
  } catch (error) {
    if (optional && error?.code === 'ENOENT') return [];
    if (error?.code !== 'ENOENT') {
      issues.push(
        issue(
          'filesystem/read',
          displayPath(root, path),
          `could not read directory: ${error?.code ?? 'unknown error'}`,
        ),
      );
    }
    return [];
  }
}

async function rejectSymlink(root, path, issues) {
  try {
    const metadata = await lstat(path);
    if (metadata.isSymbolicLink()) {
      issues.push(issue('filesystem/symlink', displayPath(root, path), 'harness files must not be symbolic links'));
    }
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      issues.push(
        issue('filesystem/stat', displayPath(root, path), `could not inspect path: ${error?.code ?? 'unknown error'}`),
      );
    }
  }
}

function parseFrontmatter(text, path, issues) {
  if (!text?.startsWith('---\n')) {
    issues.push(issue('parse/frontmatter', path, 'missing opening frontmatter delimiter'));
    return undefined;
  }
  const end = text.indexOf('\n---\n', 4);
  if (end < 0) {
    issues.push(issue('parse/frontmatter', path, 'missing closing frontmatter delimiter'));
    return undefined;
  }
  const metadata = {};
  const lines = text.slice(4, end).split('\n');
  for (let index = 0; index < lines.length; index += 1) {
    const match = /^([A-Za-z][A-Za-z0-9_-]*):(?: (.*))?$/.exec(lines[index]);
    if (!match) {
      issues.push(issue('parse/frontmatter', path, 'unsupported frontmatter syntax', index + 2));
      return undefined;
    }
    metadata[match[1]] = match[2] ?? '';
  }
  return { metadata, body: text.slice(end + 5) };
}

function parseHarnessToml(text, path, issues) {
  const values = {};
  let rest = text;
  const multiline = /developer_instructions\s*=\s*"""([\s\S]*?)"""\s*/m.exec(rest);
  if (multiline) {
    values.developer_instructions = multiline[1].replace(/^\n/, '');
    rest = `${rest.slice(0, multiline.index)}${rest.slice(multiline.index + multiline[0].length)}`;
  }
  const lines = rest.split('\n');
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (!line || line.startsWith('#')) continue;
    const match = /^([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*"([^"\\]*)"$/.exec(line);
    if (!match) {
      issues.push(issue('parse/toml', path, 'unsupported TOML syntax', index + 1));
      return undefined;
    }
    values[match[1]] = match[2];
  }
  if (typeof values.developer_instructions !== 'string') {
    issues.push(issue('parse/toml', path, 'missing developer_instructions string'));
    return undefined;
  }
  return values;
}

function checkExactInventory(actualEntries, expected, directoryPath, issues, { extension = '', codePrefix }) {
  const actual = new Set(actualEntries.map(({ name }) => name));
  for (const name of expected) {
    const filename = `${name}${extension}`;
    if (!actual.has(filename)) {
      issues.push(
        issue(`${codePrefix}/missing`, directoryPath, `missing ${codePrefix.slice(0, -1) || 'item'} "${name}"`),
      );
    }
  }
  const allowed = new Set(expected.map((name) => `${name}${extension}`));
  for (const { name } of actualEntries) {
    if (!allowed.has(name)) {
      issues.push(issue(`${codePrefix}/extra`, `${directoryPath}/${name}`, `unexpected inventory entry "${name}"`));
    }
  }
}

async function checkInstructions(root, issues, activeFiles) {
  let rootInstructionsSize = 0;
  for (const instructionRoot of HARNESS_CONTRACT.instructionRoots) {
    const prefix = instructionRoot ? `${instructionRoot}/` : '';
    const agentsRelative = `${prefix}AGENTS.md`;
    const claudeRelative = `${prefix}CLAUDE.md`;
    const agentsPath = join(root, ...agentsRelative.split('/'));
    const claudePath = join(root, ...claudeRelative.split('/'));
    const agents = await readNormalized(root, agentsPath, issues);
    const claude = await readNormalized(root, claudePath, issues);
    if (agents === undefined) {
      issues.push(issue('instructions/missing', agentsRelative, 'missing AGENTS.md'));
    } else {
      activeFiles.push([agentsRelative, agents]);
      if (!agents.trim()) {
        issues.push(issue('instructions/empty', agentsRelative, 'AGENTS.md must be non-empty'));
      }
      if (!instructionRoot) rootInstructionsSize = Buffer.byteLength(agents, 'utf8');
      const chainSize = rootInstructionsSize + (instructionRoot ? Buffer.byteLength(agents, 'utf8') : 0);
      if (chainSize > 32 * 1024) {
        issues.push(issue('instructions/size', agentsRelative, 'root and nested instruction chain exceeds 32 KiB'));
      }
    }
    if (claude === undefined) {
      issues.push(issue('instructions/missing', claudeRelative, 'missing CLAUDE.md'));
    } else {
      activeFiles.push([claudeRelative, claude]);
      if (claude.trimEnd() !== '@AGENTS.md') {
        issues.push(
          issue(
            'instructions/claude-import',
            claudeRelative,
            'CLAUDE.md must equal the exact same-directory @AGENTS.md import',
          ),
        );
      }
    }
    await rejectSymlink(root, agentsPath, issues);
    await rejectSymlink(root, claudePath, issues);
  }
}

async function checkSkills(root, issues, activeFiles) {
  const canonicalRoot = join(root, '.agents', 'skills');
  const claudeRoot = join(root, '.claude', 'skills');
  const canonicalEntries = await entries(root, canonicalRoot, issues);
  const claudeEntries = await entries(root, claudeRoot, issues);
  checkExactInventory(canonicalEntries, HARNESS_CONTRACT.skills, '.agents/skills', issues, {
    codePrefix: 'skills',
  });
  checkExactInventory(claudeEntries, HARNESS_CONTRACT.skills, '.claude/skills', issues, {
    codePrefix: 'claude-skills',
  });
  for (const entry of canonicalEntries) {
    if (HARNESS_CONTRACT.skills.includes(entry.name) && !entry.isDirectory()) {
      issues.push(issue('skills/type', `.agents/skills/${entry.name}`, 'skill inventory member must be a directory'));
    }
  }
  for (const entry of claudeEntries) {
    if (HARNESS_CONTRACT.skills.includes(entry.name) && !entry.isDirectory()) {
      issues.push(
        issue(
          'claude-skills/type',
          `.claude/skills/${entry.name}`,
          'Claude skill inventory member must be a directory',
        ),
      );
    }
  }

  for (const name of HARNESS_CONTRACT.skills) {
    const canonicalRelative = `.agents/skills/${name}/SKILL.md`;
    const claudeRelative = `.claude/skills/${name}/SKILL.md`;
    const canonicalPath = join(root, ...canonicalRelative.split('/'));
    const claudePath = join(root, ...claudeRelative.split('/'));
    const canonical = await readNormalized(root, canonicalPath, issues);
    const claude = await readNormalized(root, claudePath, issues);
    const canonicalDirectoryEntries = await entries(root, dirname(canonicalPath), issues, {
      optional: true,
    });
    const canonicalDirectoryExists = canonicalEntries.some((entry) => entry.name === name && entry.isDirectory());
    if (
      canonicalDirectoryExists &&
      (canonicalDirectoryEntries.length !== 1 ||
        canonicalDirectoryEntries[0].name !== 'SKILL.md' ||
        !canonicalDirectoryEntries[0].isFile())
    ) {
      issues.push(issue('skills/files', `.agents/skills/${name}`, 'skill directory must contain only SKILL.md'));
    }
    const claudeDirectoryEntries = await entries(root, dirname(claudePath), issues, {
      optional: true,
    });
    const claudeDirectoryExists = claudeEntries.some((entry) => entry.name === name && entry.isDirectory());
    if (
      claudeDirectoryExists &&
      (claudeDirectoryEntries.length !== 1 ||
        claudeDirectoryEntries[0].name !== 'SKILL.md' ||
        !claudeDirectoryEntries[0].isFile())
    ) {
      issues.push(
        issue('claude-skills/files', `.claude/skills/${name}`, 'Claude skill directory must contain only SKILL.md'),
      );
    }

    let canonicalParsed;
    if (canonical !== undefined) {
      activeFiles.push([canonicalRelative, canonical]);
      canonicalParsed = parseFrontmatter(canonical, canonicalRelative, issues);
      if (canonicalParsed) {
        if (canonicalParsed.metadata.name !== name) {
          issues.push(issue('skills/name', canonicalRelative, `frontmatter name must be "${name}"`));
        }
        const description = canonicalParsed.metadata.description;
        if (!description?.trim() || description.includes('\n')) {
          issues.push(issue('skills/description', canonicalRelative, 'description must be non-empty and single-line'));
        }
        if (
          !canonicalParsed.body.trim() ||
          !canonicalParsed.body.includes(`Canonical Travel workflow ID: travel-agency/${name}.`) ||
          !canonicalParsed.body.includes(AUTHORITY_BLOCK)
        ) {
          issues.push(
            issue(
              'skills/body',
              canonicalRelative,
              'skill body must contain workflow identity, instructions, and the exact Authority block',
            ),
          );
        }
        const lines = canonical.split('\n');
        lines.forEach((line, index) => {
          if (/\|\s*head(?:\s+-1|\s+-n\s+1)(?:\s|$)/.test(line)) {
            issues.push(issue('portability/head', canonicalRelative, 'head pipelines are not portable', index + 1));
          }
          if (/\\\s*$/.test(line) && /^\s+-/.test(lines[index + 1] ?? '')) {
            issues.push(
              issue(
                'portability/continuation',
                canonicalRelative,
                'POSIX option continuations are not portable',
                index + 1,
              ),
            );
          }
        });
      }
      await rejectSymlink(root, canonicalPath, issues);
    }

    if (claude !== undefined) {
      activeFiles.push([claudeRelative, claude]);
      const claudeParsed = parseFrontmatter(claude, claudeRelative, issues);
      if (claudeParsed) {
        if (claudeParsed.metadata.name !== name) {
          issues.push(issue('claude-skills/name', claudeRelative, `frontmatter name must be "${name}"`));
        }
        if (canonicalParsed && claudeParsed.metadata.description !== canonicalParsed.metadata.description) {
          issues.push(issue('claude-skills/description', claudeRelative, 'description must match the canonical skill'));
        }
        const expectedBody = `Resolve the Git repository root, then read and follow \`.agents/skills/${name}/SKILL.md\` from that root as the canonical workflow.`;
        if (claudeParsed.body.trim() !== expectedBody) {
          issues.push(
            issue(
              'claude-skills/reference',
              claudeRelative,
              'Claude skill must contain only the exact canonical repository-root reference',
            ),
          );
        }
        if (Buffer.byteLength(claude, 'utf8') > 1_200) {
          issues.push(issue('claude-skills/size', claudeRelative, 'Claude skill adapter exceeds 1,200 bytes'));
        }
      }
      await rejectSymlink(root, claudePath, issues);
    }
  }
}

async function checkCommands(root, issues, activeFiles) {
  const commandRoot = join(root, '.claude', 'commands');
  const commandEntries = await entries(root, commandRoot, issues);
  checkExactInventory(commandEntries, HARNESS_CONTRACT.legacyCommands, '.claude/commands', issues, {
    extension: '.md',
    codePrefix: 'commands',
  });
  for (const name of HARNESS_CONTRACT.legacyCommands) {
    const commandRelative = `.claude/commands/${name}.md`;
    const commandPath = join(root, ...commandRelative.split('/'));
    const content = await readNormalized(root, commandPath, issues);
    if (content === undefined) continue;
    activeFiles.push([commandRelative, content]);
    const expected = `Resolve the Git repository root, then read and follow \`.agents/skills/${name}/SKILL.md\` from that root.\n\nArguments: $ARGUMENTS`;
    if (content.trimEnd() !== expected) {
      issues.push(
        issue('commands/content', commandRelative, 'legacy command must equal the canonical adapter contract'),
      );
    }
    await rejectSymlink(root, commandPath, issues);
  }
}

function checkAgentBody(content, body, name, contract, path, issues) {
  if (Buffer.byteLength(content, 'utf8') > 1_200) {
    issues.push(issue('agents/size', path, 'agent adapter exceeds 1,200 bytes'));
  }
  if (!body.includes(`Repository role ID: \`travel-agency/${name}\`.`)) {
    issues.push(issue('agents/role-id', path, `missing exact role ID for "${name}"`));
  }
  const mapping = `Resolve the Git repository root, then read and follow \`.agents/skills/${contract.skill}/SKILL.md\` from that root.`;
  if (!body.includes(mapping)) {
    issues.push(issue('agents/mapping', path, `agent must map to canonical skill "${contract.skill}"`));
  }
  const probeCount = body.split(PROBE_LINE).length - 1;
  if ((name === 'domain-modeler' && probeCount !== 1) || (name !== 'domain-modeler' && probeCount !== 0)) {
    issues.push(issue('agents/probe', path, 'only domain-modeler must contain exactly one inert identity-probe line'));
  }
}

async function checkAgents(root, issues, activeFiles) {
  const names = Object.keys(HARNESS_CONTRACT.agents);
  const codexEntries = await entries(root, join(root, '.codex', 'agents'), issues);
  const claudeEntries = await entries(root, join(root, '.claude', 'agents'), issues);
  checkExactInventory(codexEntries, names, '.codex/agents', issues, {
    extension: '.toml',
    codePrefix: 'agents',
  });
  checkExactInventory(claudeEntries, names, '.claude/agents', issues, {
    extension: '.md',
    codePrefix: 'agents',
  });
  for (const name of names) {
    const contract = HARNESS_CONTRACT.agents[name];
    const codexRelative = `.codex/agents/${name}.toml`;
    const claudeRelative = `.claude/agents/${name}.md`;
    const codexPath = join(root, ...codexRelative.split('/'));
    const claudePath = join(root, ...claudeRelative.split('/'));
    const codex = await readNormalized(root, codexPath, issues);
    const claude = await readNormalized(root, claudePath, issues);
    if (codex !== undefined) {
      activeFiles.push([codexRelative, codex]);
      const parsed = parseHarnessToml(codex, codexRelative, issues);
      if (parsed) {
        if (parsed.name !== name) {
          issues.push(issue('agents/name', codexRelative, `declared name must be "${name}"`));
        }
        if (parsed.description !== AGENT_DESCRIPTIONS[name]) {
          issues.push(issue('agents/description', codexRelative, 'description must match the embedded agent contract'));
        }
        if (parsed.sandbox_mode !== contract.codexSandbox) {
          issues.push(issue('agents/capability', codexRelative, `sandbox_mode must be "${contract.codexSandbox}"`));
        }
        checkAgentBody(codex, parsed.developer_instructions, name, contract, codexRelative, issues);
      }
      await rejectSymlink(root, codexPath, issues);
    }
    if (claude !== undefined) {
      activeFiles.push([claudeRelative, claude]);
      const parsed = parseFrontmatter(claude, claudeRelative, issues);
      if (parsed) {
        if (parsed.metadata.name !== name) {
          issues.push(issue('agents/name', claudeRelative, `declared name must be "${name}"`));
        }
        if (parsed.metadata.description !== AGENT_DESCRIPTIONS[name]) {
          issues.push(
            issue('agents/description', claudeRelative, 'description must match the embedded agent contract'),
          );
        }
        if (parsed.metadata.permissionMode !== contract.claudePermission) {
          issues.push(
            issue('agents/capability', claudeRelative, `permissionMode must be "${contract.claudePermission}"`),
          );
        }
        checkAgentBody(claude, parsed.body, name, contract, claudeRelative, issues);
      }
      await rejectSymlink(root, claudePath, issues);
    }
  }
}

async function checkHooksAndPaths(root, issues, activeFiles) {
  const topEntries = await entries(root, root, issues);
  for (const entry of topEntries) {
    if (entry.name.toLowerCase() === '.codex' && entry.name !== '.codex') {
      issues.push(issue('paths/codex-case', entry.name, 'Codex client directory must be spelled exactly .codex'));
    }
  }
  const codexEntries = await entries(root, join(root, '.codex'), issues, { optional: true });
  for (const entry of codexEntries) {
    if (entry.name.toLowerCase() === 'commands') {
      issues.push(
        issue(
          'paths/codex-commands',
          `.codex/${entry.name}`,
          'physical Codex commands paths are unsupported; use repository skills',
        ),
      );
    }
  }
  const hooksPath = join(root, '.codex', 'hooks.json');
  const hooks = await readNormalized(root, hooksPath, issues, { optional: true });
  if (hooks !== undefined) {
    activeFiles.push(['.codex/hooks.json', hooks]);
    issues.push(
      issue(
        'hooks/forbidden-file',
        '.codex/hooks.json',
        'copied hook adapter must be removed; formatting is owned by Lefthook and CI',
      ),
    );
  }
  const settingsPath = join(root, '.claude', 'settings.json');
  const settings = await readNormalized(root, settingsPath, issues, { optional: true });
  if (settings !== undefined) {
    activeFiles.push(['.claude/settings.json', settings]);
    try {
      const parsed = JSON.parse(settings);
      if (Object.hasOwn(parsed, 'hooks')) {
        issues.push(issue('hooks/claude-top-level', '.claude/settings.json', 'top-level Claude hooks are forbidden'));
      }
    } catch {
      issues.push(issue('parse/json', '.claude/settings.json', 'malformed JSON'));
    }
  }
}

async function checkTooling(root, issues) {
  const harnessEntries = await entries(root, join(root, 'tools', 'ai-harness'), issues);
  checkExactInventory(harnessEntries, HARNESS_CONTRACT.harnessFiles, 'tools/ai-harness', issues, {
    codePrefix: 'tooling',
  });
  for (const entry of harnessEntries) {
    if (HARNESS_CONTRACT.harnessFiles.includes(entry.name) && !entry.isFile()) {
      issues.push(
        issue('tooling/type', `tools/ai-harness/${entry.name}`, 'harness inventory member must be a physical file'),
      );
    }
  }
  const packageText = await readNormalized(root, join(root, 'package.json'), issues);
  if (packageText === undefined) {
    issues.push(issue('npm/missing', 'package.json', 'required package.json is missing'));
  } else {
    try {
      const packageJson = JSON.parse(packageText);
      const expectedScripts = {
        'test:ai-harness': 'node --test tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.test.mjs',
        'check:ai-harness': 'npm run test:ai-harness && node tools/ai-harness/validate.mjs',
        'verify:ai-harness:codex': 'node tools/ai-harness/verify-codex.mjs',
      };
      for (const [name, value] of Object.entries(expectedScripts)) {
        if (packageJson.scripts?.[name] !== value) {
          issues.push(issue('npm/script', 'package.json', `script "${name}" must equal the contract`));
        }
      }
    } catch {
      issues.push(issue('parse/json', 'package.json', 'malformed JSON'));
    }
  }
  const ci = await readNormalized(root, join(root, '.github', 'workflows', 'ci.yml'), issues);
  if (ci === undefined) {
    issues.push(issue('ci/missing', '.github/workflows/ci.yml', 'required CI workflow is missing'));
  } else {
    const setupNode = ci.indexOf('actions/setup-node');
    const gateName = ci.indexOf('- name: Validate AI harness', setupNode);
    const gateRun = ci.indexOf('run: npm run check:ai-harness', gateName);
    const setupDotnet = ci.indexOf('actions/setup-dotnet', setupNode);
    const npmCi = ci.indexOf('run: npm ci', setupNode);
    if (
      setupNode < 0 ||
      gateName < setupNode ||
      gateRun < gateName ||
      (setupDotnet >= 0 && gateRun > setupDotnet) ||
      (npmCi >= 0 && gateRun > npmCi)
    ) {
      issues.push(
        issue(
          'ci/gate',
          '.github/workflows/ci.yml',
          'lint must run the dependency-free AI harness gate immediately after Node setup',
        ),
      );
    }
  }
}

async function appendOptionalActiveFiles(root, issues, activeFiles) {
  for (const relativePath of ['README.md', 'CONTRIBUTING.md', '.devcontainer/devcontainer.json']) {
    const content = await readNormalized(root, join(root, ...relativePath.split('/')), issues, {
      optional: true,
    });
    if (content !== undefined) activeFiles.push([relativePath, content]);
  }
}

function checkStaleText(activeFiles, issues) {
  for (const [path, content] of activeFiles) {
    const lines = content.split('\n');
    lines.forEach((line, index) => {
      for (const token of STALE_TOKENS) {
        if (line.includes(token)) {
          issues.push(
            issue('stale/text', path, `active harness surface contains obsolete token "${token}"`, index + 1),
          );
        }
      }
      if (line.trim() === 'dotnet csharpier .') {
        issues.push(issue('stale/text', path, 'obsolete CSharpier command must use format or check', index + 1));
      }
      for (const claim of FALSE_README_CLAIMS) {
        if (line.includes(claim)) {
          issues.push(issue('stale/text', path, `false active runtime claim "${claim}"`, index + 1));
        }
      }
    });
  }
}

export async function validateHarness(input) {
  const root = rootPath(input);
  const issues = [];
  const activeFiles = [];
  await checkInstructions(root, issues, activeFiles);
  await checkSkills(root, issues, activeFiles);
  await checkCommands(root, issues, activeFiles);
  await checkAgents(root, issues, activeFiles);
  await checkHooksAndPaths(root, issues, activeFiles);
  await checkTooling(root, issues);
  await appendOptionalActiveFiles(root, issues, activeFiles);
  checkStaleText(activeFiles, issues);
  return issues.sort(compareIssues);
}

export function formatValidationResult(issues) {
  if (issues.length === 0) {
    return 'AI harness validation passed: 8 instruction pairs, 8 skills, 5 agent pairs, 5 legacy commands.';
  }
  const lines = issues.map(
    ({ code, path, line, message }) => `- [${code}] ${path}${line === undefined ? '' : `:${line}`}: ${message}`,
  );
  return `AI harness validation failed (${issues.length} issues):\n${lines.join('\n')}`;
}

async function main() {
  const issues = await validateHarness(process.argv[2] ?? process.cwd());
  const output = `${formatValidationResult(issues)}\n`;
  if (issues.length === 0) {
    process.stdout.write(output);
    return;
  }
  process.stderr.write(output);
  process.exitCode = 1;
}

const isDirect =
  process.argv[1] !== undefined &&
  pathToFileURL(resolve(process.argv[1])).href === pathToFileURL(fileURLToPath(import.meta.url)).href;
if (isDirect) {
  await main();
}
