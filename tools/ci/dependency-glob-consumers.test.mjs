import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { basename, join, resolve, sep } from 'node:path';
import test from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';

const require = createRequire(import.meta.url);
async function fixture(work) {
  await mkdir(resolve('TestResults'), { recursive: true });
  const base = await realpath(resolve('TestResults'));
  const directory = await mkdtemp(join(base, 'glob-compat-'));
  try {
    await mkdir(join(directory, 'nested'));
    await writeFile(join(directory, 'marker.json'), '{}');
    await writeFile(join(directory, 'nested', 'entry.ts'), 'export {};');
    await work(directory);
  } finally {
    const actual = await realpath(directory);
    assert.ok(actual.startsWith(base + sep) && basename(actual).startsWith('glob-compat-'));
    await rm(actual, { recursive: true, force: true });
  }
}

for (const consumer of ['@npmcli/package-json', 'cacache']) {
  test(`${consumer} glob stops filesystem traversal when the first directory read is aborted`, {
    timeout: 10000,
  }, async () => {
    const importer = createRequire(require.resolve(consumer));
    const { glob } = importer('glob');
    await fixture(async (cwd) => {
      const controller = new AbortController();
      const reason = new Error('fictional cancellation');
      let reads = 0;
      const observedFs = {
        ...fs,
        readdir(path, options, callback) {
          reads++;
          fs.readdir(path, options, (error, entries) => {
            controller.abort(reason);
            callback(error, entries);
          });
        },
      };
      await assert.rejects(
        glob('**/*', { cwd, fs: observedFs, signal: controller.signal }),
        (error) => error === reason,
      );
      await delay(100);
      assert.equal(reads, 1, 'cancellation must not schedule a read of the nested directory');
      assert.deepEqual((await glob('**/*.ts', { cwd })).map((path) => path.replaceAll('\\', '/')).sort(), [
        'nested/entry.ts',
      ]);
    });
  });
}

test('Nx glob matcher bounds preprocessing amplification while retaining ordinary path matching', () => {
  const nx = createRequire(require.resolve('@nx/devkit'));
  const { Minimatch } = nx('minimatch');
  const matcher = new Minimatch('a/**/../b/**/../c/**/../d/**/../x', { optimizationLevel: 2, braceExpandMax: 4 });
  assert.ok(matcher.set.length <= 4, 'preprocessing must respect the configured expansion budget');
  const normal = new Minimatch('apps/**/!(*.spec).ts', { dot: true });
  assert.equal(normal.match('apps/web/main.ts'), true);
  assert.equal(normal.match('apps/web/main.spec.ts'), false);
});

test('webpack type checker retains callable minimatch when filtering actual issue records', () => {
  const { createIssuePredicateFromIssueMatch } = require('fork-ts-checker-webpack-plugin/lib/issue/issue-match.js');
  const context = resolve('fictional-project');
  const predicate = createIssuePredicateFromIssueMatch(context, { file: 'src/**/*.ts', severity: 'error' });
  assert.equal(predicate({ file: join(context, 'src', 'main.ts'), severity: 'error', code: 'TS123' }), true);
  assert.equal(predicate({ file: join(context, 'other', 'main.ts'), severity: 'error', code: 'TS123' }), false);
  assert.equal(predicate({ file: join(context, 'src', 'main.ts'), severity: 'warning', code: 'TS123' }), false);
});

test('ESLint ESM legacy configuration evaluates file overrides through its real matcher', async () => {
  const { Legacy } = await import('@eslint/eslintrc');
  const factory = new Legacy.ConfigArrayFactory();
  const config = factory.create({ overrides: [{ files: ['**/*.ts'], rules: { semi: 'error' } }] });
  assert.equal(config.extractConfig(resolve('sample.ts')).rules.semi[0], 'error');
  assert.equal(config.extractConfig(resolve('sample.js')).rules.semi, undefined);
});

test('Commitizen finds the nearest retained configuration through glob sync', async () => {
  const findup = require('commitizen/dist/configLoader/findup.js').default;
  await fixture(async (cwd) => {
    assert.equal(
      findup(['marker.json'], { cwd: join(cwd, 'nested'), nocase: true }, () => true),
      join(cwd, 'marker.json'),
    );
  });
});

test('legacy default and defaults helpers preserve matching options alongside modern named exports', async () => {
  const cjs = require('minimatch');
  const esm = await import('minimatch');
  for (const matcher of [cjs, esm.default]) {
    assert.equal(matcher('main.ts', '*.ts'), true);
    assert.equal(matcher('main.js', '*.ts'), false);
    const insensitive = matcher.defaults({ nocase: true });
    assert.equal(insensitive('MAIN.TS', '*.ts'), true);
    assert.equal(new insensitive.Minimatch('*.ts').match('MAIN.TS'), true);
    assert.deepEqual(insensitive.match(['MAIN.TS', 'MAIN.JS'], '*.ts'), ['MAIN.TS']);
  }
  assert.equal(esm.minimatch('MAIN.TS', '*.ts'), false);
  assert.equal(new esm.Minimatch('*.ts').match('main.ts'), true);
});

test('Nx selects projects by wildcard through the real project-matching helper', () => {
  const { findMatchingProjects } = require('nx/src/utils/find-matching-projects');
  const projects = {
    'travel-web': { name: 'travel-web', data: { root: 'apps/web' } },
    'other-app': { name: 'other-app', data: { root: 'apps/other' } },
  };
  assert.deepEqual(findMatchingProjects(['travel-*'], projects), ['travel-web']);
});

test('glob unbundled distribution resolves its compatible modern matcher', async () => {
  const globPackage = createRequire(require.resolve('glob/package.json'));
  const { glob } = globPackage('./dist/commonjs/index.js');
  await fixture(async (cwd) => {
    const actual = (await glob('**/*.{ts,json}', { cwd, posix: true })).sort();
    assert.deepEqual(actual, ['marker.json', 'nested/entry.ts']);
  });
});
