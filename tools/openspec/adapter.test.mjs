import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const hash = (s) => createHash('sha256').update(s).digest('hex');
const name = 'openspec-apply-change';
async function load() {
  const m = await import('./adapter.mjs').catch((e) => {
    if (e.code === 'ERR_MODULE_NOT_FOUND' && e.url === new URL('./adapter.mjs', import.meta.url).href) return {};
    throw e;
  });
  assert.equal(typeof m.adaptSkill, 'function', 'skill adapter is not implemented');
  return m;
}
async function entry() {
  const text = await readFile(new URL('./upstream/' + name + '/SKILL.md', import.meta.url), 'utf8');
  return {
    text,
    record: {
      upstreamHash: hash(text),
      commandSites: { store: 1, list: 2, new: 1, status: 3, init: 2, instructions: 2 },
      allowedTools: 'Bash(openspec:*)',
    },
  };
}
test('adapts real upstream command sites and exact tool restriction without changing trigger description', async () => {
  const { adaptSkill } = await load();
  const { text, record } = await entry();
  const effective = adaptSkill(name, text, record);
  assert.ok(effective.includes('node tools/openspec/run.mjs status --change'));
  assert.ok(effective.includes('allowed-tools: Bash(node tools/openspec/run.mjs:*)'));
  assert.ok(effective.includes('"openspec apply"'));
  assert.ok(effective.includes('cwd'));
  assert.ok(!effective.includes('Bash(node:*)'));
  assert.equal(effective, adaptSkill(name, text.replaceAll('\n', '\r\n'), record));
});
test('refuses upstream drift instead of accepting arbitrary openspec skills', async () => {
  const { adaptSkill } = await load();
  const { text, record } = await entry();
  assert.throws(() => adaptSkill(name, text + '\nopenspec delete everything\n', record), /upstream|hash|drift/i);
  assert.throws(() => adaptSkill('openspec-foreign', text, record), /name|inventory|unknown/i);
});
test('refuses a mismatched command-site count and repeated adaptation', async () => {
  const { adaptSkill } = await load();
  const { text, record } = await entry();
  assert.throws(
    () => adaptSkill(name, text, { ...record, commandSites: { ...record.commandSites, status: 99 } }),
    /site|count/i,
  );
  const effective = adaptSkill(name, text, record);
  assert.throws(() => adaptSkill(name, effective, record), /upstream|hash|adapt/i);
});
