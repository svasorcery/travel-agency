import { lstat, readdir, readFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve, win32 } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const INVENTORY_PATH = 'tools/ci/dotnet-inventory.json';
const SOLUTION_PATH = 'Travel.slnx';
const CI_PATH = '.github/workflows/ci.yml';
const SKIPPED_DIRECTORIES = new Set(['.git', 'node_modules', 'bin', 'obj']);
const CLASSIFICATIONS = new Set(['application', 'library', 'test', 'tool', 'excluded-legacy']);
const E2E_REQUIRED_NEEDS = [
  'lint',
  'build-dotnet',
  'frontend-affected',
  'test-flights-unit',
  'test-ai',
  'test-host-http',
  'test-architecture',
  'test-contract',
  'test-flights-integration',
  'test-host-integration',
  'test-aspire-smoke',
];
const ASPIRE_IMAGES = [
  'pgvector/pgvector:pg17',
  'redis:8.6',
  'nats:2.12',
  'quay.io/keycloak/keycloak:26.6',
  'dpage/pgadmin4:9.15.0',
  'axllent/mailpit:v1.20',
];
const NORMAL_REQUIRED_JOBS = E2E_REQUIRED_NEEDS;

function githubExpression(body) {
  return ['$', `{{ ${body} }}`].join('');
}

function normalizeText(text) {
  return text.replace(/^\uFEFF/, '').replace(/\r\n?/g, '\n');
}

function rootPath(input) {
  if (input instanceof URL) return resolve(fileURLToPath(input));
  return resolve(input ?? process.cwd());
}

function displayPath(root, path) {
  return relative(root, path).replaceAll('\\', '/') || '.';
}

function issue(code, path, message) {
  return { code, path: path.replaceAll('\\', '/'), message };
}

function compareIssues(left, right) {
  return (
    left.code.localeCompare(right.code) ||
    left.path.localeCompare(right.path) ||
    left.message.localeCompare(right.message)
  );
}

function exactKeys(value, allowed, path, issues) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    issues.push(issue('inventory/schema', path, 'expected an object'));
    return false;
  }
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      issues.push(issue('inventory/schema', path, `unknown key "${key}"`));
    }
  }
  return true;
}

function isCanonicalRelativePath(value) {
  if (typeof value !== 'string' || value.length === 0 || value.includes('\\')) return false;
  if (isAbsolute(value) || win32.isAbsolute(value)) return false;
  const segments = value.split('/');
  return segments.every((segment) => segment.length > 0 && segment !== '.' && segment !== '..');
}

async function readPhysicalText(root, relativePath, issues) {
  const absolutePath = join(root, ...relativePath.split('/'));
  try {
    const metadata = await lstat(absolutePath);
    if (metadata.isSymbolicLink()) {
      issues.push(issue('filesystem/symlink', relativePath, 'inventory-owned files must not be symbolic links'));
      return undefined;
    }
    if (!metadata.isFile()) {
      issues.push(issue('filesystem/read', relativePath, 'expected a regular file'));
      return undefined;
    }
    return normalizeText(await readFile(absolutePath, 'utf8'));
  } catch (error) {
    issues.push(issue('filesystem/read', relativePath, `could not read file: ${error?.code ?? 'unknown error'}`));
    return undefined;
  }
}

async function enumerateFiles(root, issues, predicate, start = root) {
  const found = [];
  async function walk(directory) {
    let entries;
    try {
      entries = await readdir(directory, { withFileTypes: true });
    } catch (error) {
      issues.push(
        issue(
          'filesystem/read',
          displayPath(root, directory),
          `could not read directory: ${error?.code ?? 'unknown error'}`,
        ),
      );
      return;
    }
    entries.sort((left, right) => left.name.localeCompare(right.name));
    for (const entry of entries) {
      if (entry.isDirectory() && SKIPPED_DIRECTORIES.has(entry.name)) continue;
      const absolutePath = join(directory, entry.name);
      const relativePath = displayPath(root, absolutePath);
      if (entry.isSymbolicLink()) {
        if (predicate(relativePath) || relativePath.toLowerCase().endsWith('.csproj')) {
          issues.push(issue('filesystem/symlink', relativePath, 'inventory-owned paths must not be symbolic links'));
        }
        continue;
      }
      if (entry.isDirectory()) {
        await walk(absolutePath);
      } else if (entry.isFile() && predicate(relativePath)) {
        found.push(relativePath);
      }
    }
  }
  await walk(start);
  return found.sort((left, right) => left.localeCompare(right));
}

function activeXml(text, path, issues, code) {
  const stripped = (text ?? '').replace(/<!--[\s\S]*?-->/g, '');
  if (stripped.includes('<!--') || stripped.includes('-->')) {
    issues.push(issue(code, path, 'XML comments must be balanced'));
    return '';
  }
  return stripped;
}

function parseSolution(text, issues) {
  const projects = [];
  if (text === undefined) return projects;
  const xml = activeXml(text, SOLUTION_PATH, issues, 'solution/schema');
  for (const element of xml.matchAll(/<Project\b[^>]*>/g)) {
    const match = /^<Project\s+Path="([^"]+\.csproj)"\s*\/>$/.exec(element[0]);
    if (!match) {
      issues.push(issue('solution/schema', SOLUTION_PATH, `unrecognized Project element "${element[0]}"`));
      continue;
    }
    const path = match[1];
    if (!isCanonicalRelativePath(path)) {
      issues.push(issue('solution/path', SOLUTION_PATH, `project path must be canonical: "${path}"`));
      continue;
    }
    projects.push(path);
  }
  const seen = new Set();
  for (const path of projects) {
    if (seen.has(path)) issues.push(issue('solution/duplicate', SOLUTION_PATH, `duplicate project "${path}"`));
    seen.add(path);
  }
  return projects;
}

function parseManifest(text, issues) {
  if (text === undefined) return undefined;
  let manifest;
  try {
    manifest = JSON.parse(text);
  } catch {
    issues.push(issue('inventory/json', INVENTORY_PATH, 'inventory must be valid JSON'));
    return undefined;
  }
  if (!exactKeys(manifest, new Set(['schemaVersion', 'projects', 'lanes']), INVENTORY_PATH, issues)) return undefined;
  if (manifest.schemaVersion !== 1) {
    issues.push(issue('inventory/schema', INVENTORY_PATH, 'schemaVersion must equal 1'));
  }
  if (!Array.isArray(manifest.projects)) {
    issues.push(issue('inventory/schema', INVENTORY_PATH, 'projects must be an array'));
    manifest.projects = [];
  }
  if (!Array.isArray(manifest.lanes)) {
    issues.push(issue('inventory/schema', INVENTORY_PATH, 'lanes must be an array'));
    manifest.lanes = [];
  }
  return manifest;
}

function validateManifestShape(manifest, issues) {
  let structurallyUsable = true;
  const projectPaths = new Set();
  for (let index = 0; index < manifest.projects.length; index += 1) {
    const project = manifest.projects[index];
    const projectLocation = `${INVENTORY_PATH}#projects[${index}]`;
    if (!exactKeys(project, new Set(['path', 'classification', 'coverage', 'reason']), projectLocation, issues)) {
      structurallyUsable = false;
      continue;
    }
    if (!isCanonicalRelativePath(project.path) || !project.path.endsWith('.csproj')) {
      issues.push(
        issue('inventory/path', projectLocation, 'project path must be a canonical slash-normalized .csproj path'),
      );
    }
    if (projectPaths.has(project.path)) {
      issues.push(issue('inventory/duplicate', projectLocation, `duplicate project "${project.path}"`));
    }
    projectPaths.add(project.path);
    if (!CLASSIFICATIONS.has(project.classification)) {
      issues.push(
        issue('inventory/classification', projectLocation, `unknown classification "${project.classification}"`),
      );
    }
    if (project.classification === 'excluded-legacy') {
      if (typeof project.reason !== 'string' || project.reason.trim().length === 0) {
        issues.push(
          issue(
            'legacy/reason',
            project.path ?? projectLocation,
            'excluded legacy projects require a non-empty reason',
          ),
        );
      }
      if (Object.hasOwn(project, 'coverage')) {
        issues.push(issue('inventory/schema', projectLocation, 'excluded legacy projects cannot declare coverage'));
      }
    } else {
      if (Object.hasOwn(project, 'reason')) {
        issues.push(issue('inventory/schema', projectLocation, 'reason is allowed only for excluded-legacy projects'));
      }
      if (project.classification !== 'test' && Object.hasOwn(project, 'coverage')) {
        issues.push(issue('inventory/schema', projectLocation, 'coverage is allowed only for test projects'));
      }
    }
  }

  const laneIds = new Set();
  for (let index = 0; index < manifest.lanes.length; index += 1) {
    const lane = manifest.lanes[index];
    const laneLocation = `${INVENTORY_PATH}#lanes[${index}]`;
    if (!exactKeys(lane, new Set(['id', 'job', 'project', 'filter']), laneLocation, issues)) {
      structurallyUsable = false;
      continue;
    }
    for (const key of ['id', 'job', 'project']) {
      if (typeof lane[key] !== 'string' || lane[key].trim().length === 0) {
        issues.push(issue('inventory/schema', laneLocation, `${key} must be a non-empty string`));
      }
    }
    if (laneIds.has(lane.id)) issues.push(issue('lane/duplicate', laneLocation, `duplicate lane id "${lane.id}"`));
    laneIds.add(lane.id);
    if (!isCanonicalRelativePath(lane.project) || !lane.project.endsWith('.csproj')) {
      issues.push(
        issue('inventory/path', laneLocation, 'lane project must be a canonical slash-normalized .csproj path'),
      );
    }
    if (Object.hasOwn(lane, 'filter') && (typeof lane.filter !== 'string' || lane.filter.trim().length === 0)) {
      issues.push(issue('inventory/schema', laneLocation, 'filter must be a non-empty string when present'));
    }
  }
  return structurallyUsable;
}

function projectSignals(projectText, projectPath, issues) {
  const text = activeXml(projectText, projectPath, issues, 'project/schema');
  const test =
    /<IsTestProject>\s*true\s*<\/IsTestProject>/i.test(text) ||
    /<PackageReference\s+Include="(?:Microsoft\.NET\.Test\.Sdk|xunit(?:\.v3)?)"/i.test(text);
  const executable =
    /<Project\s+Sdk="(?:Microsoft\.NET\.Sdk\.(?:Web|Worker)|Aspire\.AppHost\.Sdk)(?:\/[^" ]+)?"/i.test(text) ||
    /<OutputType>\s*(?:Exe|WinExe)\s*<\/OutputType>/i.test(text);
  return { test, executable };
}

function stripCsharpComments(text) {
  let output = '';
  let state = 'code';
  let verbatim = false;
  for (let index = 0; index < text.length; index += 1) {
    const current = text[index];
    const next = text[index + 1];
    if (state === 'line-comment') {
      if (current === '\n') {
        output += current;
        state = 'code';
      } else output += ' ';
      continue;
    }
    if (state === 'block-comment') {
      if (current === '*' && next === '/') {
        output += '  ';
        index += 1;
        state = 'code';
      } else output += current === '\n' ? '\n' : ' ';
      continue;
    }
    if (state === 'string') {
      output += current;
      if (verbatim && current === '"' && next === '"') {
        output += next;
        index += 1;
      } else if (!verbatim && current === '\\' && next !== undefined) {
        output += next;
        index += 1;
      } else if (current === '"') state = 'code';
      continue;
    }
    if (state === 'character') {
      output += current;
      if (current === '\\' && next !== undefined) {
        output += next;
        index += 1;
      } else if (current === "'") state = 'code';
      continue;
    }
    if (current === '/' && next === '/') {
      output += '  ';
      index += 1;
      state = 'line-comment';
    } else if (current === '/' && next === '*') {
      output += '  ';
      index += 1;
      state = 'block-comment';
    } else {
      output += current;
      if (current === '"') {
        state = 'string';
        verbatim = text[index - 1] === '@';
      } else if (current === "'") state = 'character';
    }
  }
  return output;
}

function maskCsharpStrings(text) {
  let output = '';
  let state = 'code';
  let verbatim = false;
  for (let index = 0; index < text.length; index += 1) {
    const current = text[index];
    const next = text[index + 1];
    if (state === 'string') {
      output += current === '\n' ? '\n' : ' ';
      if (verbatim && current === '"' && next === '"') {
        output += ' ';
        index += 1;
      } else if (!verbatim && current === '\\' && next !== undefined) {
        output += next === '\n' ? '\n' : ' ';
        index += 1;
      } else if (current === '"') state = 'code';
      continue;
    }
    if (state === 'character') {
      output += current === '\n' ? '\n' : ' ';
      if (current === '\\' && next !== undefined) {
        output += next === '\n' ? '\n' : ' ';
        index += 1;
      } else if (current === "'") state = 'code';
      continue;
    }
    if (current === '"') {
      output += ' ';
      state = 'string';
      verbatim = text[index - 1] === '@';
    } else if (current === "'") {
      output += ' ';
      state = 'character';
    } else output += current;
  }
  return output;
}

function leadingAttributes(line) {
  const attributes = [];
  let rest = line;
  while (true) {
    const match = /^\s*\[([^\]]*)\]/.exec(rest);
    if (!match) break;
    attributes.push(match[1]);
    rest = rest.slice(match[0].length);
  }
  return { attributes, rest };
}

function attributeSignals(attributes) {
  const categories = [];
  let testCount = 0;
  for (const attribute of attributes) {
    for (const match of attribute.matchAll(
      /(?:^|,)\s*(?:(?:global::)?[A-Za-z_][\w.]*\.)?Trait(?:Attribute)?\s*\(\s*"Category"\s*,\s*"([^"]+)"\s*\)/g,
    )) {
      categories.push(match[1]);
    }
    testCount += [
      ...attribute.matchAll(/(?:^|,)\s*(?:(?:global::)?[A-Za-z_][\w.]*\.)?(?:Fact|Theory)(?:Attribute)?\b/g),
    ].length;
  }
  return { categories, testCount };
}

function collapseCsharpBracketLines(text) {
  let output = '';
  let state = 'code';
  let verbatim = false;
  let bracketDepth = 0;
  let balanced = true;
  for (let index = 0; index < text.length; index += 1) {
    const current = text[index];
    const next = text[index + 1];
    if (state === 'string') {
      output += current;
      if (verbatim && current === '"' && next === '"') {
        output += next;
        index += 1;
      } else if (!verbatim && current === '\\' && next !== undefined) {
        output += next;
        index += 1;
      } else if (current === '"') state = 'code';
      continue;
    }
    if (state === 'character') {
      output += current;
      if (current === '\\' && next !== undefined) {
        output += next;
        index += 1;
      } else if (current === "'") state = 'code';
      continue;
    }
    if (current === '"') {
      state = 'string';
      verbatim = text[index - 1] === '@';
      output += current;
    } else if (current === "'") {
      state = 'character';
      output += current;
    } else if (current === '[') {
      bracketDepth += 1;
      output += current;
    } else if (current === ']') {
      bracketDepth -= 1;
      if (bracketDepth < 0) {
        balanced = false;
        bracketDepth = 0;
      }
      output += current;
    } else if (current === '\n' && bracketDepth > 0) output += ' ';
    else output += current;
  }
  return { text: output, balanced: balanced && bracketDepth === 0 };
}

function csharpTestCases(text, path, issues) {
  const collapsed = collapseCsharpBracketLines(stripCsharpComments(text));
  if (!collapsed.balanced && /\b(?:Fact|Theory|Trait)\b/.test(text)) {
    issues.push(issue('coverage/parser', path, 'xUnit-like attribute brackets must be balanced'));
  }
  const lines = collapsed.text.split('\n');
  const testCases = [];
  const classStack = [];
  let awaitingClass;
  let braceDepth = 0;
  let pendingAttributes = [];
  for (const line of lines) {
    while (classStack.length > 0 && braceDepth < classStack.at(-1).bodyDepth) classStack.pop();
    const parsed = leadingAttributes(line);
    pendingAttributes.push(...parsed.attributes);
    const rest = parsed.rest.trim();
    const structure = maskCsharpStrings(rest);
    const opens = [...structure.matchAll(/\{/g)].length;
    const closes = [...structure.matchAll(/\}/g)].length;

    if (awaitingClass && opens > 0) {
      classStack.push({ bodyDepth: braceDepth + 1, categories: awaitingClass.categories });
      awaitingClass = undefined;
    }

    if (rest.length > 0) {
      const signals = attributeSignals(pendingAttributes);
      if (/\b(?:class|struct|record(?:\s+(?:class|struct))?)\s+[A-Za-z_]\w*/.test(structure)) {
        const classInfo = { categories: signals.categories };
        if (opens > 0) classStack.push({ bodyDepth: braceDepth + 1, categories: classInfo.categories });
        else awaitingClass = classInfo;
        const classBody = rest.slice(rest.indexOf('{') + 1);
        const inlineAttributes = [...classBody.matchAll(/\[([^\]]*)\]/g)].map((match) => match[1]);
        const inlineSignals = attributeSignals(inlineAttributes);
        for (let index = 0; index < inlineSignals.testCount; index += 1) {
          testCases.push({ path, categories: [...classInfo.categories, ...inlineSignals.categories] });
        }
      } else if (signals.testCount > 0) {
        const categories = [...(classStack.at(-1)?.categories ?? []), ...signals.categories];
        for (let index = 0; index < signals.testCount; index += 1) testCases.push({ path, categories });
      }
      pendingAttributes = [];
    }
    braceDepth += opens - closes;
  }
  return testCases;
}

async function collectTestCases(root, projectPath, issues) {
  const projectDirectory = join(root, dirname(projectPath));
  const files = await enumerateFiles(root, issues, (path) => path.endsWith('.cs'), projectDirectory);
  const testCases = [];
  for (const file of files) {
    const text = await readPhysicalText(root, file, issues);
    if (text === undefined) continue;
    testCases.push(...csharpTestCases(text, file, issues));
  }
  return testCases;
}

function filterMatches(filter, categories) {
  const clauses = filter.split('&').map((clause) => clause.trim());
  if (clauses.length === 0 || clauses.some((clause) => clause.length === 0)) return undefined;
  for (const clause of clauses) {
    const match = /^Category(!?=)([^&|()]+)$/.exec(clause);
    if (!match) return undefined;
    const present = categories.includes(match[2]);
    if (match[1] === '=' && !present) return false;
    if (match[1] === '!=' && present) return false;
  }
  return true;
}

function yamlJobBlock(ci, job) {
  const lines = ci.split('\n');
  const start = lines.indexOf(`  ${job}:`);
  if (start < 0) return undefined;
  let end = lines.length;
  for (let index = start + 1; index < lines.length; index += 1) {
    if (/^ {2}[A-Za-z0-9_-]+:\s*$/.test(lines[index])) {
      end = index;
      break;
    }
  }
  return lines.slice(start, end).join('\n');
}

function jobRunCommands(block) {
  return jobSteps(block)
    .map(({ run }) => run)
    .filter((command) => command !== undefined);
}

function jobSteps(block) {
  const steps = [];
  let current;
  let inEnvironment = false;
  let inRunBlock = false;
  for (const line of (block ?? '').split('\n')) {
    const start = /^ {6}- (name|run|uses|if|continue-on-error|timeout-minutes):\s*(.+?)\s*$/.exec(line);
    if (start) {
      current = { env: {}, [start[1]]: start[2] };
      steps.push(current);
      inEnvironment = false;
      inRunBlock = start[1] === 'run' && /^[|>][-+0-9]*$/.test(start[2]);
      continue;
    }
    if (!current) continue;
    if (inRunBlock) {
      const content = /^ {10}(.*)$/.exec(line);
      if (content) {
        current.run += `\n${content[1]}`;
        continue;
      }
      inRunBlock = false;
    }
    const field = /^ {8}(name|run|uses|if|continue-on-error|timeout-minutes):\s*(.+?)\s*$/.exec(line);
    if (field) {
      current[field[1]] = field[2];
      inEnvironment = false;
      inRunBlock = field[1] === 'run' && /^[|>][-+0-9]*$/.test(field[2]);
      continue;
    }
    if (/^ {8}env:\s*$/.test(line)) {
      inEnvironment = true;
      continue;
    }
    if (inEnvironment) {
      const variable = /^ {10}([A-Za-z_][A-Za-z0-9_]*):\s*(.+?)\s*$/.exec(line);
      if (variable) {
        current.env[variable[1]] = variable[2];
        continue;
      }
    }
    if (/^ {8}\S/.test(line)) inEnvironment = false;
  }
  return steps;
}

function topLevelEnvScalar(ci, key) {
  const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  for (const line of ci.split('\n')) {
    const match = new RegExp(`^ {2}${escapedKey}:\\s+(.+?)\\s*$`).exec(line);
    if (match) return match[1];
  }
  return undefined;
}

function jobScalar(block, key) {
  const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  for (const line of (block ?? '').split('\n')) {
    const match = new RegExp(`^ {4}${escapedKey}:\\s+(.+?)\\s*$`).exec(line);
    if (match) return match[1];
  }
  return undefined;
}

function jobNeeds(block) {
  const lines = (block ?? '').split('\n');
  const start = lines.indexOf('    needs:');
  if (start < 0) return [];
  const needs = [];
  for (let index = start + 1; index < lines.length; index += 1) {
    const match = /^ {6}- ([A-Za-z0-9_-]+)\s*$/.exec(lines[index]);
    if (match) {
      needs.push(match[1]);
      continue;
    }
    if (/^ {4}\S/.test(lines[index])) break;
  }
  return needs;
}

function dotnetProjectCommand(command, verb, project) {
  const escapedProject = project.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return new RegExp(`^dotnet\\s+${verb}\\s+${escapedProject}(?:\\s|$)`).test(command);
}

function commandCanGate(command) {
  if (/^[|>][-+0-9]*(?:\n|$)/.test(command)) return true;
  return (
    typeof command === 'string' &&
    !/(?:^|\s)(?:--list-tests|--help|-h)(?=\s|$)/.test(command) &&
    !hasUnquotedShellControl(command)
  );
}

function hasUnquotedShellControl(command) {
  let quote;
  for (let index = 0; index < command.length; index += 1) {
    const current = command[index];
    const next = command[index + 1];
    if (quote) {
      if (current === '\\' && quote === '"' && next !== undefined) index += 1;
      else if (current === quote) quote = undefined;
      continue;
    }
    if (current === '"' || current === "'") quote = current;
    else if (current === ';' || current === '|' || current === '&') return true;
  }
  return false;
}

function nonGatingJobReason(block, allowedJobCondition, allowStepCondition = () => false) {
  if (block === undefined) return undefined;
  if (jobScalar(block, 'continue-on-error') !== undefined) return 'required job cannot declare continue-on-error';
  const condition = jobScalar(block, 'if');
  if (condition !== allowedJobCondition) return 'required job has an unexpected condition';
  for (const step of jobSteps(block)) {
    if (step.if !== undefined && !allowStepCondition(step)) return 'required step cannot declare a condition';
    if (step['continue-on-error'] !== undefined) return 'required step cannot declare continue-on-error';
    if (step.run !== undefined && !commandCanGate(step.run)) return 'required step uses a non-gating shell command';
  }
  return undefined;
}

function eventBranches(ci, eventName) {
  const lines = ci.split('\n');
  const start = lines.indexOf(`  ${eventName}:`);
  if (start < 0) return [];
  for (let index = start + 1; index < lines.length; index += 1) {
    if (/^ {2}[A-Za-z0-9_-]+:\s*$/.test(lines[index])) break;
    const match = /^ {4}branches:\s*\[([^\]]+)\]\s*$/.exec(lines[index]);
    if (match) return match[1].split(',').map((branch) => branch.trim());
  }
  return [];
}

export function validateDeliveryWorkflow(input) {
  const ci = normalizeText(input ?? '');
  const issues = [];
  for (const line of ci.split('\n')) {
    const match = /^\s+(?:-\s+)?uses:\s+([^\s#]+)(?:\s+#.*)?$/.exec(line);
    if (!match) continue;
    const reference = match[1];
    if (reference.startsWith('./') || reference.startsWith('docker://')) continue;
    if (!/^[^/@]+\/[^@]+@[0-9a-f]{40}$/.test(reference)) {
      issues.push(issue('ci/action-pins', CI_PATH, `external action must use a full commit SHA: "${reference}"`));
    }
  }
  for (const eventName of ['push', 'pull_request']) {
    const branches = eventBranches(ci, eventName);
    if (branches.length !== 2 || !branches.includes('dev') || !branches.includes('master')) {
      issues.push(issue('ci/triggers', CI_PATH, `${eventName} must target exactly dev and master`));
    }
  }
  for (const job of NORMAL_REQUIRED_JOBS) {
    const reason = nonGatingJobReason(yamlJobBlock(ci, job), undefined);
    if (reason) issues.push(issue('ci/non-gating', CI_PATH, `${job}: ${reason}`));
  }

  const lint = yamlJobBlock(ci, 'lint') ?? '';
  const lintCommands = jobRunCommands(lint);
  for (const command of [
    'npm run check:ai-harness',
    'npm run check:dotnet-inventory',
    'npx biome ci .',
    'dotnet csharpier check .',
  ]) {
    if (!lintCommands.includes(command)) issues.push(issue('ci/lint', CI_PATH, `lint job must run "${command}"`));
  }

  const dotnetBuild = yamlJobBlock(ci, 'build-dotnet') ?? '';
  const dotnetBuildCommands = jobRunCommands(dotnetBuild);
  const solutionBuildLine =
    dotnetBuildCommands.find((command) => dotnetProjectCommand(command, 'build', 'Travel.slnx')) ?? '';
  if (
    !dotnetBuildCommands.some((command) => dotnetProjectCommand(command, 'restore', 'Travel.slnx')) ||
    !solutionBuildLine.split(/\s+/).includes('--no-restore')
  ) {
    issues.push(issue('ci/build', CI_PATH, 'build-dotnet must restore and explicitly build Travel.slnx'));
  }

  const frontend = yamlJobBlock(ci, 'frontend-affected') ?? '';
  const affectedLine = jobRunCommands(frontend).find((command) => /^npx\s+nx\s+affected(?:\s|$)/.test(command)) ?? '';
  const nxBase = topLevelEnvScalar(ci, 'NX_BASE');
  if (!['build', 'test', 'lint'].every((target) => affectedLine.split(/\s+/).includes(target))) {
    issues.push(issue('ci/frontend-targets', CI_PATH, 'frontend affected job must run build, test, and lint targets'));
  }
  if (!affectedLine.split(/\s+/).includes('--exclude=travel-agency')) {
    issues.push(
      issue(
        'ci/frontend-scope',
        CI_PATH,
        'frontend affected job must exclude the non-frontend travel-agency workspace-root project',
      ),
    );
  }
  if (
    !new Set([
      githubExpression('github.event.pull_request.base.sha || github.event.before'),
      githubExpression(
        "github.event.pull_request.base.sha || github.event.before || format('origin/{0}', github.event.repository.default_branch)",
      ),
    ]).has(nxBase) ||
    !affectedLine.includes('--base=$NX_BASE') ||
    !affectedLine.includes('--head=$NX_HEAD')
  ) {
    issues.push(issue('ci/frontend-base', CI_PATH, 'frontend affected job must use the actual pull-request base SHA'));
  }

  const paidEvals = yamlJobBlock(ci, 'test-ai-evals') ?? '';
  const paidEvalsIf = jobScalar(paidEvals, 'if');
  const paidEvalsCommands = jobRunCommands(paidEvals);
  const paidEvalsSteps = jobSteps(paidEvals);
  const credentialValue = githubExpression('secrets.ANTHROPIC_API_KEY');
  const credentialPreflight = paidEvalsSteps.find(({ run }) => run === 'test -n "$ANTHROPIC_API_KEY"');
  const paidTest = paidEvalsSteps.find(({ run }) =>
    run?.startsWith('dotnet test tests/Travel.Tests.AiEvals/Travel.Tests.AiEvals.csproj'),
  );
  const paidNonGatingReason = nonGatingJobReason(
    paidEvals,
    "github.event_name == 'workflow_dispatch' && inputs.run_paid_ai_evals == true",
  );
  if (paidNonGatingReason) issues.push(issue('ci/non-gating', CI_PATH, `test-ai-evals: ${paidNonGatingReason}`));
  if (
    !/^ {2}workflow_dispatch:\s*$/m.test(ci) ||
    !/^ {6}run_paid_ai_evals:\s*$/m.test(ci) ||
    !/^ {8}type: boolean\s*$/m.test(ci) ||
    paidEvalsIf !== "github.event_name == 'workflow_dispatch' && inputs.run_paid_ai_evals == true" ||
    jobScalar(paidEvals, 'environment') !== 'paid-ai-evals' ||
    !paidEvalsCommands.includes('test -n "$ANTHROPIC_API_KEY"') ||
    credentialPreflight?.env.ANTHROPIC_API_KEY !== credentialValue ||
    paidTest?.env.ANTHROPIC_API_KEY !== credentialValue
  ) {
    issues.push(
      issue(
        'ci/paid-evals',
        CI_PATH,
        'paid AI evals require a boolean workflow_dispatch input, paid-ai-evals environment, and step-scoped credential preflight',
      ),
    );
  }

  const e2e = yamlJobBlock(ci, 'test-e2e') ?? '';
  const e2eCondition =
    "github.event_name == 'pull_request' && (github.base_ref == 'dev' || github.base_ref == 'master')";
  const e2eCommandStep = jobSteps(e2e).find(({ run }) => run === 'npx nx e2e travel-e2e');
  const e2eNonGatingReason = nonGatingJobReason(
    e2e,
    e2eCondition,
    (step) => step.name === 'Cleanup' && step.if === 'always()',
  );
  if (e2eNonGatingReason) issues.push(issue('ci/non-gating', CI_PATH, `test-e2e: ${e2eNonGatingReason}`));
  if (
    jobScalar(e2e, 'if') !== e2eCondition ||
    !e2eCommandStep ||
    e2eCommandStep.if !== undefined ||
    e2eCommandStep['continue-on-error'] !== undefined
  ) {
    issues.push(
      issue('ci/e2e', CI_PATH, 'E2E must actively run its Nx target for pull requests targeting dev or master'),
    );
  }
  const e2eNeeds = jobNeeds(e2e);
  if (
    e2eNeeds.length !== E2E_REQUIRED_NEEDS.length ||
    E2E_REQUIRED_NEEDS.some((required) => !e2eNeeds.includes(required))
  ) {
    issues.push(issue('ci/e2e-needs', CI_PATH, 'E2E must depend on every normal required build and test lane'));
  }
  for (const jobName of ['test-aspire-smoke', 'test-e2e']) {
    const imageStep = jobSteps(yamlJobBlock(ci, jobName)).find((step) => step.name === 'Pre-pull Aspire images');
    const commands = imageStep?.run
      ?.split('\n')
      .slice(1)
      .filter((line) => line.length > 0);
    const expectedCommands = ASPIRE_IMAGES.map((image) => `docker pull ${image}`);
    if (
      imageStep?.['timeout-minutes'] !== '10' ||
      imageStep.if !== undefined ||
      imageStep['continue-on-error'] !== undefined ||
      commands?.length !== expectedCommands.length ||
      expectedCommands.some((command, index) => commands[index] !== command)
    ) {
      issues.push(
        issue(
          'ci/aspire-images',
          CI_PATH,
          `${jobName} must pre-pull the exact pinned Aspire image set in an unconditional 10-minute step`,
        ),
      );
    }
  }
  return issues.sort(compareIssues);
}

function validateCiLanes(manifest, ci, issues) {
  if (ci === undefined) return;
  for (const lane of manifest.lanes) {
    if (typeof lane?.job !== 'string' || typeof lane?.project !== 'string') continue;
    const block = yamlJobBlock(ci, lane.job);
    if (block === undefined) {
      issues.push(issue('lane/job', CI_PATH, `lane "${lane.id}" references missing job "${lane.job}"`));
      continue;
    }
    const steps = jobSteps(block);
    const commands = steps.map(({ run }) => run).filter((command) => command !== undefined);
    const testStep = steps.find(({ run }) => run && dotnetProjectCommand(run, 'test', lane.project));
    const commandLine = testStep?.run;
    if (!commands.some((command) => dotnetProjectCommand(command, 'restore', lane.project))) {
      issues.push(issue('lane/restore', CI_PATH, `job "${lane.job}" must restore its exact project`));
    }
    if (!commands.some((command) => dotnetProjectCommand(command, 'build', lane.project))) {
      issues.push(issue('lane/build', CI_PATH, `job "${lane.job}" must build its exact project`));
    }
    if (!commandLine) {
      issues.push(issue('lane/project', CI_PATH, `job "${lane.job}" does not run project "${lane.project}"`));
      continue;
    }
    if (!commandCanGate(commandLine) || testStep.if !== undefined || testStep['continue-on-error'] !== undefined) {
      issues.push(issue('lane/execution', CI_PATH, `job "${lane.job}" must execute its tests as a gating step`));
    }
    if (typeof lane.filter === 'string') {
      if (!commandLine.includes(lane.filter)) {
        issues.push(issue('lane/filter', CI_PATH, `job "${lane.job}" does not use exact filter "${lane.filter}"`));
      }
    } else if (commandLine.includes('--filter')) {
      issues.push(issue('lane/filter', CI_PATH, `full-project lane "${lane.id}" must not use --filter`));
    }
  }
}

async function validateProjects(root, manifest, repositoryProjects, solutionProjects, issues) {
  const repositorySet = new Set(repositoryProjects);
  const solutionSet = new Set(solutionProjects);
  const inventorySet = new Set(manifest.projects.map(({ path }) => path));
  const laneById = new Map(manifest.lanes.map((lane) => [lane.id, lane]));
  const referencedLaneIds = new Set();

  for (const path of repositoryProjects) {
    if (!inventorySet.has(path))
      issues.push(issue('inventory/missing', path, 'repository project is absent from inventory'));
  }
  for (const project of manifest.projects) {
    if (!repositorySet.has(project.path)) {
      issues.push(
        issue('inventory/extra', project.path ?? INVENTORY_PATH, 'inventory project does not exist in repository'),
      );
    }
  }
  for (const path of solutionProjects) {
    const entry = manifest.projects.find((project) => project.path === path);
    if (!repositorySet.has(path) || !entry || entry.classification === 'excluded-legacy') {
      issues.push(
        issue('solution/mismatch', path, 'solution project must exist and be a non-excluded inventory member'),
      );
    }
  }

  for (const project of manifest.projects) {
    if (typeof project?.path !== 'string' || !repositorySet.has(project.path)) continue;
    const inSolution = solutionSet.has(project.path);
    if (project.classification === 'excluded-legacy') {
      if (inSolution)
        issues.push(issue('legacy/exclusion', project.path, 'excluded legacy project must stay outside Travel.slnx'));
      continue;
    }
    if (!inSolution) {
      issues.push(
        issue('solution/mismatch', project.path, 'non-excluded inventory project is absent from Travel.slnx'),
      );
      issues.push(issue('legacy/exclusion', project.path, 'off-solution project must be classified excluded-legacy'));
    }

    const projectText = await readPhysicalText(root, project.path, issues);
    const signals = projectSignals(projectText, project.path, issues);
    if ((project.classification === 'test') !== signals.test) {
      issues.push(
        issue(
          'classification/mismatch',
          project.path,
          signals.test ? 'test project must be classified as test' : 'non-test project cannot be classified as test',
        ),
      );
    }
    if (project.classification === 'library' && signals.executable) {
      issues.push(issue('classification/mismatch', project.path, 'executable project cannot be classified as library'));
    }
    if ((project.classification === 'application' || project.classification === 'tool') && !signals.executable) {
      issues.push(
        issue('classification/mismatch', project.path, `${project.classification} project must be executable`),
      );
    }
    if (project.classification !== 'test') continue;

    const testCases = await collectTestCases(root, project.path, issues);
    const coverage = project.coverage;
    if (!coverage || typeof coverage !== 'object' || Array.isArray(coverage)) {
      issues.push(issue('coverage/missing', project.path, 'test project requires an explicit coverage declaration'));
      continue;
    }
    if (coverage.mode === 'full') {
      exactKeys(coverage, new Set(['mode', 'lane']), `${project.path}#coverage`, issues);
      if (typeof coverage.lane !== 'string' || coverage.lane.length === 0) {
        issues.push(issue('coverage/missing', project.path, 'full coverage requires one lane id'));
        continue;
      }
      referencedLaneIds.add(coverage.lane);
      const lane = laneById.get(coverage.lane);
      if (!lane || lane.project !== project.path || Object.hasOwn(lane, 'filter')) {
        issues.push(
          issue(
            'coverage/missing',
            project.path,
            'full coverage lane must exist, target this project, and be unfiltered',
          ),
        );
      }
    } else if (coverage.mode === 'filtered') {
      exactKeys(coverage, new Set(['mode', 'lanes']), `${project.path}#coverage`, issues);
      if (
        !Array.isArray(coverage.lanes) ||
        coverage.lanes.length < 2 ||
        new Set(coverage.lanes).size !== coverage.lanes.length
      ) {
        issues.push(issue('coverage/missing', project.path, 'filtered coverage requires at least two unique lane ids'));
        continue;
      }
      const lanes = coverage.lanes.map((id) => laneById.get(id));
      for (const id of coverage.lanes) referencedLaneIds.add(id);
      if (lanes.some((lane) => !lane || lane.project !== project.path || typeof lane.filter !== 'string')) {
        issues.push(
          issue(
            'coverage/missing',
            project.path,
            'every filtered lane must exist, target this project, and declare a filter',
          ),
        );
        continue;
      }
      for (const lane of lanes) {
        if (filterMatches(lane.filter, []) === undefined) {
          issues.push(issue('lane/filter', project.path, `unsupported test filter "${lane.filter}"`));
        }
      }
      for (const testCase of testCases) {
        const matches = lanes.filter((lane) => filterMatches(lane.filter, testCase.categories) === true);
        if (matches.length !== 1) {
          issues.push(
            issue(
              matches.length > 1 ? 'coverage/overlap' : 'coverage/gap',
              testCase.path,
              `test matches ${matches.length} filtered lanes for "${project.path}"`,
            ),
          );
        }
      }
    } else if (coverage.mode === 'empty-scaffold') {
      exactKeys(coverage, new Set(['mode', 'reason']), `${project.path}#coverage`, issues);
      if (typeof coverage.reason !== 'string' || coverage.reason.trim().length === 0) {
        issues.push(issue('coverage/missing', project.path, 'empty scaffold requires a non-empty reason'));
      }
      if (testCases.length > 0) {
        issues.push(issue('coverage/empty', project.path, 'empty scaffold contains Fact or Theory tests'));
      }
    } else {
      issues.push(issue('coverage/missing', project.path, `unknown coverage mode "${coverage.mode}"`));
    }
  }

  const unfilteredByProject = new Map();
  for (const lane of manifest.lanes) {
    if (typeof lane?.project !== 'string') continue;
    if (!Object.hasOwn(lane, 'filter')) {
      unfilteredByProject.set(lane.project, (unfilteredByProject.get(lane.project) ?? 0) + 1);
    }
    if (!referencedLaneIds.has(lane.id)) {
      issues.push(issue('lane/unreferenced', lane.id ?? INVENTORY_PATH, 'lane is not referenced by its test project'));
    }
  }
  for (const [project, count] of unfilteredByProject) {
    if (count > 1)
      issues.push(issue('coverage/overlap', project, `${count} unfiltered lanes execute the full project`));
  }
}

export async function validateDotnetInventory(input) {
  const root = rootPath(input);
  const issues = [];
  const [inventoryText, solutionText, ciText] = await Promise.all([
    readPhysicalText(root, INVENTORY_PATH, issues),
    readPhysicalText(root, SOLUTION_PATH, issues),
    readPhysicalText(root, CI_PATH, issues),
  ]);
  const manifest = parseManifest(inventoryText, issues);
  const solutionProjects = parseSolution(solutionText, issues);
  const repositoryProjects = await enumerateFiles(root, issues, (path) => path.toLowerCase().endsWith('.csproj'));
  if (manifest) {
    if (validateManifestShape(manifest, issues)) {
      await validateProjects(root, manifest, repositoryProjects, solutionProjects, issues);
      validateCiLanes(manifest, ciText, issues);
    }
  }
  if (ciText !== undefined) issues.push(...validateDeliveryWorkflow(ciText));
  return issues.sort(compareIssues);
}

export function formatInventoryIssues(issues) {
  return issues.map(({ code, path, message }) => `[${code}] ${path}: ${message}`).join('\n');
}

async function main() {
  const issues = await validateDotnetInventory(new URL('../../', import.meta.url));
  if (issues.length > 0) {
    console.error(formatInventoryIssues(issues));
    process.exitCode = 1;
    return;
  }
  console.log('dotnet inventory validation passed');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await main();
}
