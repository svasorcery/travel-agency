import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { basename, join, resolve, sep } from 'node:path';
import test from 'node:test';

for (const [name, headers, sensitiveHeaders] of [
  ['object credential headers', { Authorization: 'fictional-auth', Cookie: 'fictional-cookie' }, []],
  ['array credential headers', ['Authorization', 'fictional-auth', 'Cookie', 'fictional-cookie'], []],
  [
    'custom credential headers with regex characters',
    { 'X-Private^Token': 'fictional-one', 'X-Private|Token': 'fictional-two' },
    ['X-Private^Token', 'X-Private|Token'],
  ],
]) {
  test(`Nx redirect consumer strips ${name} across hosts and preserves them on the same host`, {
    timeout: 15_000,
  }, async () => {
    const require = createRequire(import.meta.url);
    const nx = createRequire(require.resolve('nx/package.json'));
    const transport = nx('follow-redirects').http;
    const seen = [];
    const destination = createServer((request, response) => {
      seen.push(request.headers);
      response.end('fictional-destination');
    });
    const origin = createServer((request, response) => {
      if (request.url === '/same-destination') {
        seen.push(request.headers);
        response.end('fictional-destination');
      } else {
        seen.push(request.headers);
        response.writeHead(302, {
          Location:
            request.url === '/same'
              ? '/same-destination'
              : `http://127.0.0.1:${destination.address().port}/destination`,
        });
        response.end();
      }
    });
    try {
      for (const server of [destination, origin]) {
        server.listen(0, '127.0.0.1');
        await once(server, 'listening');
      }
      const protectedNames = Array.isArray(headers)
        ? headers.filter((_, index) => index % 2 === 0)
        : Object.keys(headers);
      for (const sameHost of [false, true]) {
        seen.length = 0;
        const requestHeaders = Array.isArray(headers)
          ? [...headers, 'Host', `localhost:${origin.address().port}`, 'X-Safe', 'fictional-public']
          : { ...headers, 'X-Safe': 'fictional-public' };
        const body = await new Promise((done, reject) => {
          const request = transport.get(
            {
              hostname: 'localhost',
              port: origin.address().port,
              family: 4,
              path: sameHost ? '/same' : '/cross',
              headers: requestHeaders,
              sensitiveHeaders,
              signal: AbortSignal.timeout(5000),
            },
            (response) => {
              let text = '';
              response.setEncoding('utf8');
              response.on('data', (chunk) => {
                text += chunk;
              });
              response.on('end', () => done(text));
              response.on('error', reject);
            },
          );
          request.on('error', reject);
        });
        assert.equal(body, 'fictional-destination');
        assert.equal(seen.length, 2);
        assert.equal(seen[1]['x-safe'], 'fictional-public');
        for (const header of protectedNames) {
          const key = header.toLowerCase();
          assert.ok(seen[0][key], `origin received ${key}`);
          assert.equal(seen[1][key], sameHost ? seen[0][key] : undefined, `${key}, sameHost=${sameHost}`);
        }
      }
    } finally {
      for (const server of [origin, destination]) {
        if (server.listening) {
          await new Promise((done) => {
            server.close(done);
            server.closeAllConnections();
          });
        }
      }
    }
  });
}

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
