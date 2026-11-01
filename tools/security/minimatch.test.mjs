import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
for (const [format, module] of [
  ['CommonJS', require('minimatch')],
  ['ESM', await import('minimatch')],
]) {
  test(format + ': preprocessing caps amplification without changing ordinary matches', () => {
    const { Minimatch } = module;
    const bounded = new Minimatch('a/**/../b/**/../c/**/../d/**/../x', { optimizationLevel: 2, braceExpandMax: 4 });
    assert.ok(bounded.set.length <= 4);
    const small = new Minimatch('**/../a/b/**/../c/d', { optimizationLevel: 2 });
    assert.equal(small.set.length, 4);
    assert.equal(small.match('../a/b/../c/d'), true);
    assert.equal(small.match('x/a/b/y/c/d'), true);
  });
  test(format + ': large pattern sets skip quadratic deduplication and still match', () => {
    const matcher = new module.Minimatch('{x,*}/{0..999}', { optimizationLevel: 2 });
    assert.equal(matcher.set.length, 2000);
    assert.equal(matcher.match('x/5'), true);
    assert.equal(matcher.match('y/999'), true);
    assert.equal(matcher.match('.y/5'), false);
  });
}
