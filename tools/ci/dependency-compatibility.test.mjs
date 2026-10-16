import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { basename, join, resolve, sep } from 'node:path';
import test from 'node:test';

test('Storybook resolves patched middleware and its retained Webpack API serves an asset', {
  timeout: 30_000,
}, async () => {
  const require = createRequire(import.meta.url);
  const storybook = createRequire(require.resolve('@storybook/builder-webpack5'));
  const middlewareFactory = storybook('webpack-dev-middleware');
  assert.equal(storybook('webpack-dev-middleware/package.json').version, '7.4.5');
  const webpack = require('webpack');
  const express = require('express');
  await mkdir(resolve('TestResults'), { recursive: true });
  const base = await realpath(resolve('TestResults'));
  const temporary = await mkdtemp(join(base, 'dependency-compat-'));
  let compiler, middleware, server;
  try {
    const entry = join(temporary, 'entry.js');
    await writeFile(entry, 'console.log("dependency-compatibility-marker");\n');
    compiler = webpack({
      mode: 'development',
      devtool: false,
      entry,
      output: { path: join(temporary, 'output'), filename: 'main.js', publicPath: '/assets' },
    });
    // Same options and callback used by the retained Storybook builder.
    middleware = middlewareFactory(compiler, { publicPath: '/assets', writeToDisk: true, stats: 'errors-only' });
    const stats = await new Promise((done) => middleware.waitUntilValid(done));
    assert.equal(stats.hasErrors(), false, stats.toString('errors-only'));
    const app = express();
    app.use(middleware);
    server = app.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const response = await fetch(`http://127.0.0.1:${server.address().port}/assets/main.js`, {
      signal: AbortSignal.timeout(5000),
    });
    assert.equal(response.status, 200);
    assert.match(await response.text(), /dependency-compatibility-marker/);
  } finally {
    if (server)
      await new Promise((done) => {
        server.close(done);
        server.closeAllConnections();
      });
    if (middleware) await new Promise((done) => middleware.close(done));
    if (compiler) await new Promise((done) => compiler.close(done));
    const actual = await realpath(temporary);
    assert.ok(actual.startsWith(base + sep) && basename(actual).startsWith('dependency-compat-'));
    await rm(actual, { recursive: true, force: true });
  }
});
