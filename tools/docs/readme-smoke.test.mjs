import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import test from 'node:test';
import { runSmoke } from './readme-smoke.mjs';

async function withServer(handler, run) {
  const server = createServer(handler);
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  try {
    return await run('http://127.0.0.1:' + server.address().port);
  } finally {
    await new Promise((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
  }
}

test('smoke calls only status, OpenAPI and invalid search', async () => {
  const calls = [];
  await withServer(
    async (req, res) => {
      calls.push(req.method + ' ' + req.url);
      res.setHeader('content-type', 'application/json');
      if (req.url === '/api/status') {
        res.end(JSON.stringify({ db: 'ok' }));
      } else if (req.url === '/openapi/v1.json') {
        res.end(
          JSON.stringify({
            paths: Object.fromEntries(
              [
                '/api/flights/search',
                '/api/flights/search/nl',
                '/api/flights/orders/quote',
                '/api/flights/orders/hold',
                '/api/flights/orders/confirm',
                '/events/flights/orders/{orderId}',
              ].map((path) => [path, { get: {}, post: {} }]),
            ),
          }),
        );
      } else if (req.url === '/api/flights/search') {
        const body = await new Promise((resolve) => {
          let text = '';
          req.on('data', (part) => {
            text += part;
          });
          req.on('end', () => resolve(JSON.parse(text)));
        });
        assert.equal(body.origin, 'LE');
        res.statusCode = 400;
        res.setHeader('content-type', 'application/problem+json');
        res.end(JSON.stringify({ status: 400, errors: [{ code: 'IataCode.Length' }] }));
      } else {
        res.statusCode = 500;
        res.end('{}');
      }
    },
    async (baseUrl) => {
      assert.deepEqual(await runSmoke({ baseUrl }), ['status', 'openapi', 'validation']);
    },
  );
  assert.deepEqual(calls, ['GET /api/status', 'GET /openapi/v1.json', 'POST /api/flights/search']);
});

test('smoke rejects missing OpenAPI route and malformed problem response', async () => {
  await withServer(
    async (req, res) => {
      res.setHeader('content-type', 'application/json');
      if (req.url === '/api/status') res.end('{"db":"ok"}');
      else res.end('{"paths":{}}');
    },
    async (baseUrl) => {
      await assert.rejects(() => runSmoke({ baseUrl }), /OpenAPI|route/i);
    },
  );
  await withServer(
    async (req, res) => {
      if (req.url === '/api/status') res.end('{"db":"ok"}');
      else if (req.url === '/openapi/v1.json')
        res.end(
          JSON.stringify({
            paths: Object.fromEntries(
              [
                '/api/flights/search',
                '/api/flights/search/nl',
                '/api/flights/orders/quote',
                '/api/flights/orders/hold',
                '/api/flights/orders/confirm',
                '/events/flights/orders/{orderId}',
              ].map((path) => [path, { get: {}, post: {} }]),
            ),
          }),
        );
      else {
        res.statusCode = 400;
        res.end('{"status":400}');
      }
    },
    async (baseUrl) => {
      await assert.rejects(() => runSmoke({ baseUrl }), /ProblemDetails|content-type/i);
    },
  );
});

test('smoke fails closed on wrong status and malformed OpenAPI JSON', async () => {
  await withServer(
    (_req, res) => {
      res.statusCode = 503;
      res.end('{"db":"ok"}');
    },
    async (baseUrl) => {
      await assert.rejects(() => runSmoke({ baseUrl }), /status must return 200/);
    },
  );
  await withServer(
    (req, res) => {
      if (req.url === '/api/status') res.end('{"db":"ok"}');
      else res.end('{');
    },
    async (baseUrl) => {
      await assert.rejects(() => runSmoke({ baseUrl }), /OpenAPI returned malformed JSON/);
    },
  );
});

test('smoke fails on redirect, unreachable host and bounded timeout', async () => {
  await withServer(
    (_req, res) => {
      res.statusCode = 302;
      res.setHeader('location', '/somewhere-else');
      res.end();
    },
    async (baseUrl) => {
      await assert.rejects(() => runSmoke({ baseUrl }), /fetch failed|redirect/i);
    },
  );
  await assert.rejects(
    () =>
      runSmoke({
        fetchImpl: () => Promise.reject(new Error('offline')),
      }),
    /offline/,
  );
  const keepAlive = setTimeout(() => {}, 50);
  try {
    await assert.rejects(
      () =>
        runSmoke({
          timeoutMs: 5,
          fetchImpl: (_url, options) =>
            new Promise((_resolve, reject) => {
              options.signal.addEventListener('abort', () => reject(options.signal.reason));
            }),
        }),
      /timeout|aborted/i,
    );
  } finally {
    clearTimeout(keepAlive);
  }
});
