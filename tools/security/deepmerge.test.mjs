import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import test from 'node:test';
import { runInNewContext } from 'node:vm';
import { readDeepmergeArchive } from './deepmerge-artifact.mjs';

const require = createRequire(import.meta.url);
const consumer = createRequire(require.resolve('fork-ts-checker-webpack-plugin/package.json'));
const merge = consumer('deepmerge');
const root = dirname(consumer.resolve('deepmerge/package.json'));

for (const [name, build] of [
  ['consumer CommonJS', merge],
  ['UMD', runInNewContext(`${readFileSync(join(root, 'dist/umd.js'), 'utf8')}; deepmerge`)],
]) {
  test(`${name}: null-prototype target cannot poison returned object`, () => {
    const result = build(Object.create(null), JSON.parse('{"__proto__":{"fictionalFlag":true}}'));
    assert.equal(result.fictionalFlag, undefined);
    assert.equal(Object.hasOwn(result, 'fictionalFlag'), false);
    assert.equal({}.fictionalFlag, undefined);
  });
  test(`${name}: nested null/primitive placeholders cannot inherit attacker flags`, () => {
    for (const session of [null, undefined, false, 0, 'fictional']) {
      const result = build({ session }, JSON.parse('{"session":{"__proto__":{"fictionalFlag":true}}}'));
      assert.equal(result.session.fictionalFlag, undefined);
      assert.equal(Object.hasOwn(result.session, 'fictionalFlag'), false);
    }
  });
  test(`${name}: copying own __proto__ from target cannot change destination prototype`, () => {
    const target = JSON.parse('{"__proto__":{"fictionalFlag":true},"ordinary":1}');
    const result = build(target, { ordinary: 2 });
    assert.equal(result.fictionalFlag, undefined);
    assert.equal(result.ordinary, 2);
  });
}

test('retained consumer preserves ordinary merge, cloning, symbols and custom array policy', () => {
  const marker = Symbol('fictional');
  const a = { nested: { one: 1 }, list: [{ left: true }], [marker]: { one: 1 } };
  const result = merge(a, { nested: { two: 2 }, list: [{ right: true }], [marker]: { two: 2 } });
  assert.deepEqual(result.nested, { one: 1, two: 2 });
  assert.deepEqual(result.list, [{ left: true }, { right: true }]);
  assert.deepEqual(result[marker], { one: 1, two: 2 });
  assert.notEqual(result.list[0], a.list[0]);
  assert.deepEqual(merge({ list: [1] }, { list: [2] }, { arrayMerge: (_a, b) => b }), { list: [2] });
  assert.deepEqual(merge.all([{ one: 1 }, { two: 2 }]), { one: 1, two: 2 });
});

test('patched CJS and UMD preserve ordinary upstream results and options', () => {
  const archive = readDeepmergeArchive(
    readFileSync(new URL('../vendor/deepmerge-4.3.1-upstream.tgz', import.meta.url)),
  );
  for (const file of ['dist/cjs.js', 'dist/umd.js']) {
    const baseline = file.includes('cjs')
      ? runInNewContext(`${archive.get(file).toString()}; module.exports`, { module: { exports: {} } })
      : runInNewContext(`${archive.get(file).toString()}; deepmerge`);
    const patched = file.includes('cjs')
      ? merge
      : runInNewContext(`${readFileSync(join(root, file), 'utf8')}; deepmerge`);
    const cases = [
      [{ one: 1 }, { two: 2 }],
      [
        { nested: { one: 1 }, list: [1, 2] },
        { nested: { two: 2 }, list: [3] },
      ],
      [{ slot: null }, { slot: { enabled: true } }],
      [{ slot: { one: 1 } }, { slot: false }],
      [
        [1, 2],
        [3, 4],
      ],
      [{ list: [1] }, { list: [2] }, { arrayMerge: (_a, b) => b }],
      [{ nested: { one: 1 } }, { nested: { two: 2 } }, { clone: false }],
      [
        { slot: { one: 1 } },
        { slot: { two: 2 } },
        { customMerge: (key) => (key === 'slot' ? (_a, b) => b : undefined) },
      ],
    ];
    for (const args of cases)
      assert.deepEqual(JSON.parse(JSON.stringify(patched(...args))), JSON.parse(JSON.stringify(baseline(...args))));
  }
});
