import type { Page, Request, Response } from '@playwright/test';

const maximumBytes = 128 * 1024;

/** Read the single original response before Chromium can release its inspector resource. */
export async function observeCandidateResponse<T>(
  page: Page,
  url: string,
  action: () => Promise<unknown>,
  timeoutMs = 30_000,
  decode?: (bytes: Uint8Array) => T,
): Promise<{ response: Response; body: T }> {
  const target = new URL(url);
  if (
    target.protocol !== 'http:' ||
    target.hostname !== '127.0.0.1' ||
    target.username ||
    target.password ||
    target.search ||
    target.hash ||
    !/^\/api\/trips\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\/itinerary-candidate$/i.test(target.pathname) ||
    !Number.isFinite(timeoutMs) ||
    timeoutMs <= 0 ||
    timeoutMs > 30_000
  )
    throw new Error('Invalid Trips candidate observation scope.');

  const session = await page.context().newCDPSession(page);
  let rejectFailure!: (error: Error) => void;
  const failure = new Promise<never>((_, reject) => {
    rejectFailure = reject;
  });
  // Failures can precede setup completion. Keep them handled until the main race is installed.
  void failure.catch(() => undefined);
  const fail = () => rejectFailure(new Error('Trips candidate response observation failed.'));
  const timer = setTimeout(fail, timeoutMs);
  let stopped = false;
  let requestCount = 0;
  let responseCount = 0;
  let pausedCount = 0;
  let resolveResponse!: (response: Response) => void;
  let resolveBody!: (body: T) => void;
  const response = new Promise<Response>((resolve) => {
    resolveResponse = resolve;
  });
  const body = new Promise<T>((resolve) => {
    resolveBody = resolve;
  });
  const onRequest = (request: Request) => {
    if (request.url() !== url) return;
    requestCount++;
    if (
      requestCount !== 1 ||
      request.method() !== 'POST' ||
      request.frame() !== page.mainFrame() ||
      request.redirectedFrom() !== null
    )
      fail();
  };
  const onResponse = (observed: Response) => {
    if (observed.url() !== url) return;
    responseCount++;
    if (
      responseCount !== 1 ||
      observed.request().method() !== 'POST' ||
      observed.frame() !== page.mainFrame() ||
      observed.status() !== 200
    )
      fail();
    else resolveResponse(observed);
  };
  page.on('request', onRequest);
  page.on('response', onResponse);
  page.on('close', fail);
  session.on('close', fail);

  try {
    const operation = async () => {
      const { frameTree } = await session.send('Page.getFrameTree');
      if (stopped) throw new Error('Trips candidate observation ended during setup.');
      session.on('Fetch.requestPaused', (event) => {
        void (async () => {
          let captured: T | undefined;
          let valid = false;
          try {
            pausedCount++;
            const headers = event.responseHeaders ?? [];
            const lengths = headers.filter((header) => header.name.toLowerCase() === 'content-length');
            if (
              pausedCount !== 1 ||
              event.request.url !== url ||
              event.request.method !== 'POST' ||
              event.frameId !== frameTree.frame.id ||
              event.redirectedRequestId ||
              event.responseErrorReason ||
              event.responseStatusCode !== 200 ||
              lengths.length > 1 ||
              lengths.some((header) => !/^\d+$/.test(header.value) || Number(header.value) > maximumBytes)
            )
              throw new Error('Unexpected candidate response.');
            const encoded = await session.send('Fetch.getResponseBody', { requestId: event.requestId });
            if (encoded.body.length > 4 * Math.ceil(maximumBytes / 3))
              throw new Error('Candidate body exceeds its bound.');
            const bytes = Buffer.from(encoded.body, encoded.base64Encoded ? 'base64' : 'utf8');
            if (bytes.length === 0 || bytes.length > maximumBytes) throw new Error('Candidate body exceeds its bound.');
            captured = decode
              ? decode(bytes)
              : (JSON.parse(new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(bytes)) as T);
            valid = true;
          } catch {
            valid = false;
          } finally {
            // No fulfill, replay or header/status/body override: the browser gets the same response.
            await session.send('Fetch.continueResponse', { requestId: event.requestId });
          }
          if (valid) resolveBody(captured as T);
          else fail();
        })().catch(fail);
      });
      await session.send('Fetch.enable', { patterns: [{ urlPattern: url, requestStage: 'Response' }] });
      if (stopped) throw new Error('Trips candidate observation ended during setup.');
      const [observed, captured] = await Promise.all([
        response,
        body,
        Promise.resolve().then(() => {
          if (stopped) throw new Error('Trips candidate observation ended before action.');
          return action();
        }),
      ]);
      if (requestCount !== 1 || responseCount !== 1 || pausedCount !== 1)
        throw new Error('Trips candidate observation was not singular.');
      return { response: observed, body: captured };
    };
    return await Promise.race([operation(), failure]);
  } finally {
    stopped = true;
    clearTimeout(timer);
    page.off('request', onRequest);
    page.off('response', onResponse);
    page.off('close', fail);
    session.off('close', fail);
    // Detach also releases a paused request on failure/timeout; no second request is made.
    await session.detach().catch(() => {
      if (!page.isClosed()) throw new Error('Trips candidate observer cleanup failed.');
    });
  }
}
