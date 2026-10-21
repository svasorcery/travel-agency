import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdir, mkdtemp, realpath, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';
import { basename, join, resolve, sep } from 'node:path';
import test from 'node:test';
import { pathToFileURL } from 'node:url';

test('Angular CLI SDK retains tool registration and calls over a fictional in-memory connection', {
  timeout: 15_000,
}, async () => {
  const require = createRequire(import.meta.url);
  const cli = createRequire(require.resolve('@angular/cli/package.json'));
  const { McpServer } = cli('@modelcontextprotocol/sdk/server/mcp.js');
  const { Client } = cli('@modelcontextprotocol/sdk/client/index.js');
  const { InMemoryTransport } = cli('@modelcontextprotocol/sdk/inMemory.js');
  const server = new McpServer({ name: 'fictional-server', version: '1.0.0' });
  const client = new Client({ name: 'fictional-client', version: '1.0.0' });
  server.registerTool('fictional_marker', { description: 'Local compatibility marker' }, async () => ({
    content: [{ type: 'text', text: 'fictional-sdk-marker' }],
  }));
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  try {
    await server.connect(serverTransport);
    await client.connect(clientTransport);
    const tools = await client.listTools();
    assert.deepEqual(
      tools.tools.map((tool) => tool.name),
      ['fictional_marker'],
    );
    const result = await client.callTool({ name: 'fictional_marker', arguments: {} });
    assert.equal(result.content[0].text, 'fictional-sdk-marker');
  } finally {
    await client.close();
    await server.close();
  }
});

test('Angular CLI SDK refuses redirects without forwarding a fictional custom header or POST body', {
  timeout: 15_000,
}, async () => {
  const require = createRequire(import.meta.url);
  const cli = createRequire(require.resolve('@angular/cli/package.json'));
  const { StreamableHTTPClientTransport } = cli('@modelcontextprotocol/sdk/client/streamableHttp.js');
  const requests = [];
  let forwarded = 0;
  const destination = createServer((request, response) => {
    forwarded++;
    request.resume();
    response.writeHead(202).end();
  });
  const origin = createServer(async (request, response) => {
    let body = '';
    for await (const chunk of request) body += chunk;
    requests.push({ body, key: request.headers['x-fictional-key'] });
    if (request.url === '/redirect') {
      response.writeHead(307, { Location: `http://127.0.0.1:${destination.address().port}/other` }).end();
    } else {
      response.writeHead(202).end();
    }
  });
  const transports = [];
  try {
    for (const server of [destination, origin]) {
      server.listen(0, '127.0.0.1');
      await once(server, 'listening');
    }
    const message = { jsonrpc: '2.0', method: 'notifications/fictional', params: { text: 'fictional-body' } };
    for (const path of ['/direct', '/redirect']) {
      const transport = new StreamableHTTPClientTransport(new URL(`http://127.0.0.1:${origin.address().port}${path}`), {
        requestInit: { headers: { 'X-Fictional-Key': 'fictional-key' } },
        fetch: (url, init) =>
          fetch(url, { ...init, signal: AbortSignal.any([init.signal, AbortSignal.timeout(5000)]) }),
      });
      transports.push(transport);
      await transport.start();
      if (path === '/direct') await transport.send(message);
      else await assert.rejects(transport.send(message));
    }
    assert.equal(forwarded, 0);
    assert.deepEqual(requests, [
      { body: JSON.stringify(message), key: 'fictional-key' },
      { body: JSON.stringify(message), key: 'fictional-key' },
    ]);
  } finally {
    for (const transport of transports) await transport.close();
    for (const server of [origin, destination]) {
      if (server.listening)
        await new Promise((done) => {
          server.close(done);
          server.closeAllConnections();
        });
    }
  }
});

test('Angular builder Vite serves a fictional ES module through its retained dev-server API', {
  timeout: 30_000,
}, async () => {
  const require = createRequire(import.meta.url);
  const builder = createRequire(require.resolve('@angular/build/package.json'));
  const { createServer: createViteServer } = await import(pathToFileURL(builder.resolve('vite')).href);
  await mkdir(resolve('TestResults'), { recursive: true });
  const base = await realpath(resolve('TestResults'));
  const temporary = await mkdtemp(join(base, 'dependency-vite-'));
  let server;
  try {
    await writeFile(join(temporary, 'entry.js'), 'export const marker = "fictional-vite-marker";\n');
    server = await createViteServer({
      root: temporary,
      configFile: false,
      envFile: false,
      logLevel: 'silent',
      appType: 'custom',
      server: { host: '127.0.0.1', port: 0, strictPort: true, hmr: false, watch: null },
      optimizeDeps: { noDiscovery: true, include: [] },
    });
    await server.listen();
    const response = await fetch(`http://127.0.0.1:${server.httpServer.address().port}/entry.js`, {
      signal: AbortSignal.timeout(5000),
    });
    assert.equal(response.status, 200);
    assert.match(await response.text(), /fictional-vite-marker/);
  } finally {
    if (server) await server.close();
    const actual = await realpath(temporary);
    assert.ok(actual.startsWith(base + sep) && basename(actual).startsWith('dependency-vite-'));
    await rm(actual, { recursive: true, force: true });
  }
});

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
