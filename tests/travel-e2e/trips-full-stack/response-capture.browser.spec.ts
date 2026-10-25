import { createServer, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { expect, type Page, test } from '@playwright/test';
import { observeCandidateResponse } from './candidate-response';

const candidatePath = '/api/trips/11111111-1111-4111-8111-111111111111/itinerary-candidate';

test('retains the one original body when the renderer releases its inspector resource', async ({ page }) => {
  const candidate = { marker: 'FICTIONAL', nonce: 1, data: 'x'.repeat(50_000) };
  let posts = 0;
  const server = createServer((request, response) => {
    if (request.url === candidatePath) {
      posts++;
      response.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      response.end(JSON.stringify(candidate));
      return;
    }
    response.writeHead(200, { 'content-type': 'text/html' });
    response.end(`<button id="go">Generate</button><script>
      go.onclick = async () => {
        const value = await (await fetch('${candidatePath}', { method: 'POST', body: '{}' })).json();
        if (value.marker === 'FICTIONAL' && value.nonce === 1 && value.data.length === 50000)
          location.href = 'about:blank#original-consumed';
      };
    </script>`);
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  try {
    await page.goto(origin);
    const delayedObserver = new Int32Array(new SharedArrayBuffer(4));
    page.on('response', (response) => {
      if (response.url() === origin + candidatePath) Atomics.wait(delayedObserver, 0, 0, 100);
    });
    const observed = await observeCandidateResponse(page, origin + candidatePath, () => page.locator('#go').click());
    await expect(page).toHaveURL('about:blank#original-consumed');
    expect(observed.body).toEqual(candidate);
    expect(observed.response.status()).toBe(200);
    expect(posts).toBe(1);
  } finally {
    server.closeAllConnections();
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
});

async function fixture(
  page: Page,
  options: {
    body?: string;
    status?: number;
    duplicate?: boolean;
    held?: boolean;
    redirect?: boolean;
    chunked?: boolean;
  } = {},
) {
  const text = options.body ?? '{\n  "marker": "FICTIONAL Москва 🚶", "nonce": 1\n}\n';
  let posts = 0;
  let released = false;
  let heldResponse: ServerResponse | undefined;
  let markStarted!: () => void;
  const started = new Promise<void>((resolve) => {
    markStarted = resolve;
  });
  const release = () => {
    released = true;
    heldResponse?.end(text);
  };
  const server = createServer((request, response) => {
    if (request.url === candidatePath) {
      posts++;
      if (options.redirect) {
        response.writeHead(302, { location: '/redirected' });
        response.end();
        return;
      }
      response.writeHead(options.status ?? 200, {
        'content-type': 'application/json; charset=utf-8',
        'cache-control': 'no-store',
        'x-fictional-response': 'original',
        ...(options.chunked ? {} : { 'content-length': Buffer.byteLength(text) }),
      });
      if (options.held) {
        response.flushHeaders();
        heldResponse = response;
        if (released) response.end(text);
      } else if (options.chunked) {
        response.write(text.slice(0, 1));
        response.end(text.slice(1));
      } else response.end(text);
      markStarted();
      return;
    }
    if (request.url === '/redirected') {
      response.end('redirect target');
      return;
    }
    response.writeHead(200, { 'content-type': 'text/html' });
    response.end(`<button id="go">Generate</button><script>
      async function candidate() {
        const response = await fetch('${candidatePath}', { method: 'POST', body: '{}' });
        window.observed = { raw: await response.text(), status: response.status,
          cacheControl: response.headers.get('cache-control'), marker: response.headers.get('x-fictional-response') };
      }
      go.onclick = () => ${options.duplicate ? 'Promise.all([candidate(), candidate()])' : 'candidate()'};
    </script>`);
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const origin = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  await page.goto(origin);
  return {
    url: origin + candidatePath,
    text,
    posts: () => posts,
    started,
    release: () => release(),
    close: async () => {
      release();
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
    observed: async () => {
      await page.waitForFunction(() => Boolean((window as unknown as { observed?: unknown }).observed));
      return page.evaluate(
        () =>
          (window as unknown as { observed: { raw: string; status: number; cacheControl: string; marker: string } })
            .observed,
      );
    },
  };
}

test('passes original bytes and headers to the browser with exactly one POST', async ({ page }) => {
  const server = await fixture(page);
  try {
    const result = await observeCandidateResponse(page, server.url, () => page.locator('#go').click());
    expect(result.body).toEqual(JSON.parse(server.text));
    expect(result.response.status()).toBe(200);
    expect(result.response.headers()['x-fictional-response']).toBe('original');
    expect(await server.observed()).toEqual({
      raw: server.text,
      status: 200,
      cacheControl: 'no-store',
      marker: 'original',
    });
    expect(server.posts()).toBe(1);
  } finally {
    await server.close();
  }
});

for (const [name, options] of [
  ['HTTP failure', { status: 503 }],
  ['invalid JSON', { body: 'fictional invalid JSON, never an expected candidate' }],
  ['oversized body', { body: JSON.stringify({ data: 'x'.repeat(128 * 1024) }) }],
  ['oversized chunked body', { body: JSON.stringify({ data: 'x'.repeat(128 * 1024) }), chunked: true }],
  ['duplicate POST', { duplicate: true }],
  ['redirect', { redirect: true }],
] as const) {
  test(`fails closed for ${name} and releases the unmodified response`, async ({ page }) => {
    const server = await fixture(page, options);
    try {
      await expect(observeCandidateResponse(page, server.url, () => page.locator('#go').click())).rejects.toThrow(
        'Trips candidate response observation failed.',
      );
      const observed = await server.observed();
      expect(observed.raw).toBe('redirect' in options ? 'redirect target' : server.text);
      expect(server.posts()).toBe('duplicate' in options ? 2 : 1);
    } finally {
      await server.close();
    }
  });
}

test('preserves action failure after capture and does not replay the request', async ({ page }) => {
  const server = await fixture(page);
  try {
    await expect(
      observeCandidateResponse(page, server.url, async () => {
        await page.locator('#go').click();
        await server.observed();
        throw new Error('fictional action failure');
      }),
    ).rejects.toThrow('fictional action failure');
    expect(server.posts()).toBe(1);
  } finally {
    await server.close();
  }
});

test('releases observation after action failure before any request', async ({ page }) => {
  const server = await fixture(page);
  try {
    await expect(
      observeCandidateResponse(page, server.url, async () => {
        throw new Error('fictional action failure');
      }),
    ).rejects.toThrow('fictional action failure');
    expect(server.posts()).toBe(0);
    await page.locator('#go').click();
    expect((await server.observed()).raw).toBe(server.text);
    expect(server.posts()).toBe(1);
  } finally {
    await server.close();
  }
});

test('times out a held body and releases the original request without retry', async ({ page }) => {
  const server = await fixture(page, { held: true });
  try {
    let failed = false;
    const observation = observeCandidateResponse(page, server.url, () => page.locator('#go').click(), 5_000).catch(
      (error: unknown) => {
        failed = true;
        throw error;
      },
    );
    const rejected = expect(observation).rejects.toThrow('Trips candidate response observation failed.');
    // The deadline must expire while an actual body is held, not during setup/click.
    await Promise.race([
      server.started,
      rejected.then(() => {
        throw new Error('Observation ended before the fixture received its request.');
      }),
    ]);
    expect(failed).toBe(false);
    await rejected;
    server.release();
    expect((await server.observed()).raw).toBe(server.text);
    expect(server.posts()).toBe(1);
  } finally {
    await server.close();
  }
});

test('fixture retains an early release until the delayed request arrives', async ({ page }) => {
  const server = await fixture(page, { held: true });
  try {
    server.release();
    await page.locator('#go').click();
    expect((await server.observed()).raw).toBe(server.text);
    expect(server.posts()).toBe(1);
  } finally {
    await server.close();
  }
});

test('rejects foreign scope before invoking the action', async ({ page }) => {
  let actions = 0;
  await expect(
    observeCandidateResponse(page, `http://example.invalid${candidatePath}`, async () => {
      actions++;
    }),
  ).rejects.toThrow('Invalid Trips candidate observation scope.');
  expect(actions).toBe(0);
});
