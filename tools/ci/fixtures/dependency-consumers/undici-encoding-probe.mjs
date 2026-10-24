import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createServer } from 'node:http';
import { createRequire } from 'node:module';

const require = createRequire(new URL('../../../../package.json', import.meta.url));
const builder = createRequire(require.resolve('@angular/build/package.json'));
const { fetch, Agent } = builder('undici');
const dispatcher = new Agent();
let requests = 0;
const server = createServer((request, response) => {
  requests++;
  if (request.url === '/invalid') {
    response.writeHead(200, {
      'content-encoding': 'gzip, gzip, gzip, gzip, gzip, gzip',
      'content-length': '100',
      connection: 'close',
    });
    response.end('x');
  } else response.end('fictional-undici-marker');
});
server.listen(0, '127.0.0.1');
await once(server, 'listening');
const origin = `http://127.0.0.1:${server.address().port}`;
try {
  await assert.rejects(fetch(`${origin}/invalid`, { dispatcher, signal: AbortSignal.timeout(3000) }));
  // Give an orphan stream error a chance to surface in this disposable child.
  await new Promise((done) => setTimeout(done, 200));
  const response = await fetch(`${origin}/valid`, { dispatcher, signal: AbortSignal.timeout(3000) });
  assert.equal(await response.text(), 'fictional-undici-marker');
  assert.equal(requests, 2);
  console.log('undici encoding refusal and recovery passed');
} finally {
  await dispatcher.close();
  await new Promise((done) => {
    server.close(done);
    server.closeAllConnections();
  });
}
