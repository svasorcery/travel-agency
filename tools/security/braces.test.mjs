import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { basename, dirname, join } from 'node:path';
import { after, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { within } from '../openspec/files.mjs';
import { readPackageArchive, verifyBracesArtifact, verifyInstalledBraces } from './braces-artifact.mjs';

const require = createRequire(import.meta.url);
const repository = fileURLToPath(new URL('../..', import.meta.url));
const proof = await verifyBracesArtifact(repository);
// Installed APIs are loaded only after their actual complete bytes are verified.
for (const [scope, path] of [
  ['root', 'package-lock.json'],
  ['openspec', 'tools/openspec/package-lock.json'],
])
  await verifyInstalledBraces(proof, scope, JSON.parse(await readFile(join(repository, path), 'utf8')));
const braces = require('../vendor/braces-3.0.3');
const rootRequire = createRequire(join(repository, 'package.json'));
const toolRequire = createRequire(join(repository, 'tools/openspec/package.json'));
const candidates = [braces, rootRequire('braces'), toolRequire('braces')];
const limit = 128;
function ast(depth) {
  let node = { type: 'text', value: 'x' };
  for (let i = 0; i < depth; i++) node = { type: 'root', nodes: [node] };
  return node;
}
function boundedFailure(action) {
  assert.throws(action, (error) => error instanceof SyntaxError && error.code === 'ERR_BRACES_MAX_DEPTH');
  assert.deepEqual(braces.expand('{a,b}'), ['a', 'b'], 'a rejected structure must not poison the next call');
}
for (const method of ['compile', 'expand', 'stringify']) {
  test(`${method} rejects an AST just above the structural boundary`, () =>
    boundedFailure(() => braces[method](ast(limit + 1))));
}
test('compile accepts the AST boundary', () => assert.equal(braces.compile(ast(limit)), 'x'));
for (const [open, close] of [
  ['{', '}'],
  ['(', ')'],
]) {
  test(`parse bounds ${open} containers before downstream walkers`, () => {
    boundedFailure(() => braces.parse(open.repeat(limit + 1) + 'x' + close.repeat(limit + 1)));
    assert.equal(
      braces.stringify(open.repeat(limit - 1) + 'x' + close.repeat(limit - 1)),
      open.repeat(limit - 1) + 'x' + close.repeat(limit - 1),
    );
  });
  test(`parse bounds unclosed ${open} containers`, () =>
    boundedFailure(() => braces.parse(open.repeat(limit + 1) + 'x')));
}
test('public create/default paths share the guard', () => {
  const pattern = '{'.repeat(limit + 1) + 'x' + '}'.repeat(limit + 1);
  boundedFailure(() => braces(pattern));
  boundedFailure(() => braces.create(pattern, { expand: true }));
});
test('caller options cannot disable the hard structural boundary', () => {
  for (const maxDepth of [0, false, Infinity, 10000])
    boundedFailure(() => braces.compile(ast(limit + 1), { maxDepth }));
});
test('nested array values cannot bypass the expansion append boundary', () => {
  let value = 'x';
  for (let i = 0; i < limit + 2; i++) value = [value];
  boundedFailure(() =>
    braces.expand({
      type: 'root',
      nodes: [
        { type: 'text', value: 'prefix' },
        { type: 'text', value },
      ],
    }),
  );
});
test('flatten is iterative, preserves ordering/undefined behavior, and rejects array cycles', () => {
  const { flatten } = require('../vendor/braces-3.0.3/lib/utils');
  let value = ['x'];
  for (let i = 0; i < 256; i++) value = [value];
  assert.deepEqual(flatten([1, [2, undefined]], null, value), [1, 2, null, 'x']);
  const cyclic = [];
  cyclic.push(cyclic);
  assert.throws(() => flatten(cyclic), { name: 'TypeError', code: 'ERR_BRACES_ARRAY_CYCLE' });
});

const base = join(repository, 'TestResults/braces-remediation-2026-10-16');
await mkdir(base, { recursive: true });
const referenceRoot = await mkdtemp(join(base, 'compatibility-reference-'));
after(async () => {
  assert.ok(within(base, referenceRoot) && basename(referenceRoot).startsWith('compatibility-reference-'));
  await rm(referenceRoot, { recursive: true, force: true });
});
for (const [name, bytes] of readPackageArchive(await readFile(join(repository, proof.provenance.upstream.archive)))) {
  const path = join(referenceRoot, name);
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, bytes);
}
const pristine = require(join(referenceRoot, 'index.js'));
const outcome = (action) => {
  try {
    return { value: action() };
  } catch (error) {
    return { error: { name: error.name, message: error.message } };
  }
};
const normalizeAst = (value) =>
  JSON.parse(JSON.stringify(value, (key, item) => (['parent', 'prev'].includes(key) ? undefined : item)));
const patterns = [
  '',
  'plain/path',
  '{a,b}',
  '{a,{b,c}}',
  '{01..05}',
  '{5..1..2}',
  'x/{-2..2}/y',
  '{a,,b,a}',
  // biome-ignore lint/suspicious/noTemplateCurlyInString: Upstream preserves shell interpolation syntax as literal input.
  '${a,b}',
  '\\{a,b\\}',
  '[{}]',
  '"{a,b}"',
  "'{a,b}'",
  'a/{b,c',
  'a/b,c}',
  'a/(b/(c))',
  'specs/{flights,support}/**/*.md',
  'src/{a,b}/file.{ts,js}',
  'путь/{а,б}',
];
for (const options of [
  {},
  { expand: true, nodupes: true, noempty: true },
  { escapeInvalid: true, keepQuotes: true },
  { rangeLimit: 2 },
  { maxLength: 8 },
]) {
  test(`ordinary APIs match pristine upstream for options ${JSON.stringify(options)}`, () => {
    for (const pattern of patterns)
      for (const candidate of candidates) {
        for (const method of ['create', 'compile', 'expand', 'stringify'])
          assert.deepEqual(
            outcome(() => candidate[method](pattern, options)),
            outcome(() => pristine[method](pattern, options)),
            `${method}: ${pattern}`,
          );
        assert.deepEqual(
          outcome(() => candidate(pattern, options)),
          outcome(() => pristine(pattern, options)),
        );
        assert.deepEqual(
          outcome(() => normalizeAst(candidate.parse(pattern, options))),
          outcome(() => normalizeAst(pristine.parse(pattern, options))),
        );
      }
  });
}
test('ordinary direct AST APIs preserve upstream behavior', () => {
  for (const method of ['compile', 'expand', 'stringify'])
    for (const candidate of candidates)
      assert.deepEqual(candidate[method](candidate.parse('a/{b,c}/d')), pristine[method](pristine.parse('a/{b,c}/d')));
});
test('both retained fast-glob consumers preserve brace, range and static task semantics', () => {
  for (const req of [rootRequire, toolRequire]) {
    const fg = req('fast-glob');
    const tasks = fg.generateTasks(['specs/{flights,support}/**/*.md', 'assets/{1..3}.json', 'README.md']);
    assert.deepEqual(
      [...new Set(tasks.flatMap((task) => task.positive))].sort(),
      [
        'README.md',
        'assets/1.json',
        'assets/2.json',
        'assets/3.json',
        'specs/flights/**/*.md',
        'specs/support/**/*.md',
      ].sort(),
    );
    assert.ok(tasks.some((task) => !task.dynamic && task.positive.includes('README.md')));
  }
});
test('boundary refusals are identical in every installed copy and normal calls recover', () => {
  for (const candidate of candidates)
    for (const method of ['compile', 'expand', 'stringify']) {
      assert.throws(() => candidate[method](ast(limit + 1)), { name: 'SyntaxError', code: 'ERR_BRACES_MAX_DEPTH' });
      assert.deepEqual(candidate.expand('{a,b}'), ['a', 'b']);
    }
});
