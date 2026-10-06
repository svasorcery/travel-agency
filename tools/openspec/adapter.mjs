import { normalizeText, sha256 } from './files.mjs';
import { ADAPTER_VERSION, CLI_COMMANDS, LAUNCHER, PIN, SKILLS } from './policy.mjs';

const pattern = () =>
  new RegExp('(?<![\\w/$.-])openspec(?=[ \\t]+(' + CLI_COMMANDS.join('|') + ')(?=[ \\t`"\\n]|$))', 'g');
const preamble = [
  '## Travel pinned launcher',
  '',
  'Resolve the physical Git repository root before any CLI command and set its command cwd to that root, including when this skill was invoked in modules/flights. Use the command working-directory option; do not depend on the current shell directory.',
  'Every CLI invocation below uses node tools/openspec/run.mjs. Never call a bare/global openspec or npx. This launcher binds the local package, version, root and isolated configuration. Its generated skills do not grant permission to implement, stage, commit, publish, migrate or deploy; follow AGENTS.md and the current user-authorized scope.',
  '',
].join('\n');
function split(text) {
  const normalized = normalizeText(text);
  const end = normalized.indexOf('\n---\n', 4);
  if (!normalized.startsWith('---\n') || end < 0) throw new Error('Upstream skill frontmatter is invalid');
  return { normalized, header: normalized.slice(0, end), body: normalized.slice(end + 5) };
}
function sites(body) {
  const counts = {};
  for (const match of body.matchAll(pattern())) counts[match[1]] = (counts[match[1]] ?? 0) + 1;
  return counts;
}
export function buildSkillRecord(name, text) {
  if (!SKILLS.includes(name)) throw new Error('Unknown OpenSpec skill inventory name');
  const { normalized, header, body } = split(text);
  const description = /^description: (.+)$/m.exec(header)?.[1];
  if (
    !header.includes('name: ' + name + '\n') ||
    !description ||
    !header.includes('generatedBy: "' + PIN.version + '"')
  )
    throw new Error('Pinned upstream metadata drift');
  if (!header.includes('allowed-tools: Bash(openspec:*)')) throw new Error('Upstream invocation restriction drift');
  return {
    upstreamHash: sha256(normalized),
    commandSites: sites(body),
    allowedTools: 'Bash(openspec:*)',
    description,
    adapterVersion: ADAPTER_VERSION,
  };
}
export function adaptSkill(name, text, record) {
  if (!SKILLS.includes(name)) throw new Error('Unknown OpenSpec skill inventory name');
  const { normalized, header, body } = split(text);
  if (sha256(normalized) !== record.upstreamHash) throw new Error('Upstream skill hash drift');
  const observed = sites(body);
  const expected = record.commandSites;
  if (
    Object.keys(observed).sort().join('|') !== Object.keys(expected).sort().join('|') ||
    Object.entries(observed).some(([key, count]) => expected[key] !== count)
  )
    throw new Error('CLI command-site count drift');
  if (!header.includes('allowed-tools: ' + record.allowedTools) || record.allowedTools !== 'Bash(openspec:*)')
    throw new Error('Upstream allowed-tools drift');
  const adaptedBody = body.replace(pattern(), LAUNCHER);
  if (
    /(?<![\w/$.-])openspec[ \t]+[a-z][a-z0-9-]*/.test(adaptedBody) ||
    /\bnpx\b.*@fission-ai\/openspec/.test(adaptedBody)
  )
    throw new Error('Unconverted upstream CLI command site');
  const adaptedHeader = header
    .replace('allowed-tools: Bash(openspec:*)', 'allowed-tools: Bash(node tools/openspec/run.mjs:*)')
    .replace('compatibility: Requires openspec CLI.', 'compatibility: Requires the pinned Travel OpenSpec launcher.');
  const effective = adaptedHeader + '\n---\n\n' + preamble + adaptedBody.trimStart();
  if (record.effectiveHash && sha256(effective) !== record.effectiveHash) throw new Error('Effective skill hash drift');
  return effective;
}
