import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import { gzipSync } from 'node:zlib';

const require = createRequire(import.meta.url);
const exec = promisify(execFile);
const spartan = createRequire(require.resolve('@spartan-ng/cli/package.json'));

// GHSA-m5mm-p8h7-m29v: inspect the string only; never evaluate it or render HTML.
test('Webpack copy consumer escapes a script-close prefix at the end of a function source', () => {
  const copy = createRequire(require.resolve('copy-webpack-plugin/package.json'));
  const serialize = copy('serialize-javascript');
  function fictional() {}
  fictional.toString = () => 'function fictional(){ return 1 }//</script';
  const result = serialize({ nested: { fictional }, marker: 'fictional-marker' }, { space: 2 });
  assert.doesNotMatch(result, /<\/script/i);
  assert.match(result, /fictional-marker/);
  assert.match(serialize({ count: 2, text: 'plain' }), /plain/);
});

// GHSA-w74x-v6f9-j3xg: no URI is opened; exercise Ajv's actual fast-uri resolution.
test('Ajv URI consumer does not turn an encoded scheme into a trusted scheme', () => {
  const ajv = createRequire(require.resolve('ajv/package.json'));
  const uri = ajv('fast-uri');
  assert.equal(uri.normalize('https://example.test/path'), 'https://example.test/path');
  for (const value of ['%68ttps://example.test/path', '%6Aavascript:fictional']) {
    assert.ok(!['https', 'javascript'].includes(uri.parse(value).scheme));
  }
});

for (const consumer of ['root', 'sdk']) {
  test(`Express ${consumer} body reader accepts gzip JSON, refuses malformed gzip and remains usable`, {
    timeout: 10000,
  }, async () => {
    const anchor =
      consumer === 'root' ? require : createRequire(require.resolve('@modelcontextprotocol/sdk/server/mcp.js'));
    const express = anchor('express');
    const app = express();
    app.use(express.json({ limit: '1kb' }));
    app.post('/fictional', (request, response) => response.json(request.body));
    app.use((error, _request, response, _next) => response.status(error.status || 500).json({ error: 'invalid-body' }));
    const server = createServer(app);
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const url = `http://127.0.0.1:${server.address().port}/fictional`;
    try {
      for (const malformed of [false, true, false]) {
        const response = await fetch(url, {
          method: 'POST',
          headers: { 'content-type': 'application/json', 'content-encoding': 'gzip' },
          body: malformed ? Buffer.from('not-a-gzip-frame') : gzipSync(JSON.stringify({ marker: 'fictional' })),
          signal: AbortSignal.timeout(3000),
        });
        assert.equal(response.status, malformed ? 400 : 200);
        assert.deepEqual(await response.json(), malformed ? { error: 'invalid-body' } : { marker: 'fictional' });
      }
    } finally {
      await new Promise((done) => {
        server.close(done);
        server.closeAllConnections();
      });
    }
  });
}

test('Angular builder undici refuses excessive encoding without an orphan-stream crash', {
  timeout: 15000,
}, async () => {
  const script = fileURLToPath(new URL('./fixtures/dependency-consumers/undici-encoding-probe.mjs', import.meta.url));
  const { stdout } = await exec(process.execPath, ['--max-old-space-size=128', script], {
    timeout: 10000,
    windowsHide: true,
    maxBuffer: 128 * 1024,
  });
  assert.match(stdout, /undici encoding refusal and recovery passed/);
});

test('Spartan actual config readers preserve both modes, defaults and invalid-field diagnostics', async () => {
  const { createTreeWithEmptyWorkspace } = require('@nx/devkit/testing');
  const config = spartan('./src/utils/config.js');
  const expected = { componentsPath: 'libs/ui', style: 'vega', importAlias: '@spartan-ng/helm' };
  assert.deepEqual(config.AngularCliConfigSchema.parse({ unknown: 'ignored' }), expected);
  assert.deepEqual(config.NXConfigSchema.parse({}), { ...expected, buildable: true, generateAs: 'library' });
  for (const angularCli of [true, false]) {
    const tree = createTreeWithEmptyWorkspace();
    tree.write(
      'components.json',
      JSON.stringify({ componentsPath: 'libs/custom', style: 'vega', importAlias: '@fictional/ui' }),
    );
    assert.equal((await config.loadOrInitConfig(tree, { angularCli })).importAlias, '@fictional/ui');
    assert.equal(await config.getImportAlias(tree, angularCli), '@fictional/ui');
    tree.write(
      'components.json',
      JSON.stringify({ componentsPath: 17, style: 'unsupported', importAlias: '@fictional/ui/' }),
    );
    const original = console.table;
    let rows;
    console.table = (value) => {
      rows = value;
    };
    try {
      await assert.rejects(config.loadOrInitConfig(tree, { angularCli }), /Config validation failed/);
    } finally {
      console.table = original;
    }
    assert.deepEqual(rows.map((row) => row.path).sort(), ['componentsPath', 'importAlias', 'style']);
    assert.ok(rows.every((row) => typeof row.message === 'string' && row.message.length > 0));
  }
});

test('Spartan style-map consumer validates record keys and values while retaining CSS extraction', () => {
  const { styleMapSchema, createStyleMap } = spartan('./src/generators/base/lib/styles/create-style-map.js');
  assert.deepEqual(createStyleMap('.spartan-marker { @apply px-2 text-sm; } .other { @apply ignored; }'), {
    'spartan-marker': 'px-2 text-sm',
  });
  assert.throws(() => styleMapSchema.parse({ foreign: 'px-2' }));
  assert.throws(() => styleMapSchema.parse({ 'spartan-marker': 17 }));
});

test('Spartan real UI generator produces a styled button and path mapping in a virtual workspace', {
  timeout: 30000,
}, async () => {
  const { createTreeWithEmptyWorkspace } = require('@nx/devkit/testing');
  const { readJson } = require('@nx/devkit');
  const tree = createTreeWithEmptyWorkspace();
  tree.write(
    'components.json',
    JSON.stringify({ componentsPath: 'libs/ui', style: 'vega', importAlias: '@fictional/helm' }),
  );
  tree.write('tsconfig.app.json', JSON.stringify({ extends: './tsconfig.base.json', include: ['src/**/*.ts'] }));
  const generator = spartan('./src/generators/ui/generator.js').default;
  const installTask = await generator(tree, { name: 'button', angularCli: true, installPeerDependencies: false });
  assert.equal(typeof installTask, 'function'); // Deliberately do not install anything from the virtual tree.
  const source = tree.read('libs/ui/button/src/lib/hlm-button.ts', 'utf8');
  assert.ok(source, 'actual generator emitted the button source');
  assert.match(source, /export class HlmButton/);
  assert.doesNotMatch(source, /spartan-button/);
  assert.ok(readJson(tree, 'tsconfig.base.json').compilerOptions.paths['@fictional/helm/button']);
  assert.ok(readJson(tree, 'tsconfig.app.json').include.includes('libs/ui/**/src/**/*.ts'));
});
