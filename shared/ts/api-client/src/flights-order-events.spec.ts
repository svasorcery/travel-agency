import { FlightOrderEventsError, OrderEventParser, openFlightOrderEvents } from './flights-order-events';

const id = '11111111-1111-1111-1111-111111111111';
const frame = (version = 12, type = 'OrderChanged') =>
  `event: ${type}\ndata: ${JSON.stringify({ type, orderId: id, streamVersion: version, payload: { status: 'Refunded' } })}\n\n`;

describe('Order SSE framing', () => {
  it.each(['\n', '\r\n', '\r'])('parses byte-split frames, comments and UTF8 with %j', (newline) => {
    const hints: unknown[] = [];
    const parser = new OrderEventParser(id, (hint) => hints.push(hint));
    const bytes = new TextEncoder().encode(
      `\uFEFF: connected${newline}${newline}: ✓${newline}${newline}${frame().replaceAll('\n', newline)}`,
    );
    for (const byte of bytes) parser.push(new Uint8Array([byte]));
    expect(hints).toEqual([{ orderId: id, streamVersion: 12 }]);
  });
  it('handles multiline JSON, several frames and discards incomplete EOF', () => {
    const hints: unknown[] = [];
    const parser = new OrderEventParser(id, (hint) => hints.push(hint));
    parser.push(
      new TextEncoder().encode(
        `event: OrderChanged\ndata: {"type":"OrderChanged",\ndata: "orderId":"${id}","streamVersion":4}\n\n${frame(5)}${frame(6).trimEnd()}`,
      ),
    );
    expect(hints).toEqual([
      { orderId: id, streamVersion: 4 },
      { orderId: id, streamVersion: 5 },
    ]);
  });
  it('ignores unknown kinds/fields and replay instructions', () => {
    const hints: unknown[] = [];
    const parser = new OrderEventParser(id, (hint) => hints.push(hint));
    parser.push(
      new TextEncoder().encode(`id: private\nretry: 0\nunknown: ignored\n${frame(2, 'Unrelated')}${frame(3)}`),
    );
    expect(hints).toEqual([{ orderId: id, streamVersion: 3 }]);
  });
  it.each([0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1])('rejects malformed checkpoint %s', (version) => {
    const parser = new OrderEventParser(id, vi.fn());
    expect(() => parser.push(new TextEncoder().encode(frame(version)))).toThrow(FlightOrderEventsError);
  });
  it('rejects mismatched booking/type and oversized incomplete buffers', () => {
    for (const value of [
      frame().replace(id, '22222222-2222-2222-2222-222222222222'),
      frame().replace('"type":"OrderChanged"', '"type":"OrderTicketed"'),
      `data: ${'x'.repeat(65_536)}`,
    ]) {
      const parser = new OrderEventParser(id, vi.fn());
      expect(() => parser.push(new TextEncoder().encode(value))).toThrow(FlightOrderEventsError);
    }
  });
});

describe('Order SSE fetch', () => {
  it('uses header authentication and validates a stream before announcing open', async () => {
    const hints: unknown[] = [];
    const onOpen = vi.fn();
    const fetcher = vi.fn<typeof fetch>(
      async () => new Response(frame(), { headers: { 'Content-Type': 'text/event-stream; charset=utf-8' } }),
    );
    await openFlightOrderEvents({
      aggregateId: id,
      accessToken: 'memory-token',
      signal: new AbortController().signal,
      onOpen,
      onHint: (v) => hints.push(v),
      fetcher,
    });
    expect(fetcher).toHaveBeenCalledWith(
      `/events/flights/orders/${id}`,
      expect.objectContaining({ method: 'GET', cache: 'no-store', redirect: 'error', credentials: 'omit' }),
    );
    const headers = new Headers(fetcher.mock.calls[0][1]?.headers);
    expect(headers.get('Authorization')).toBe('Bearer memory-token');
    expect(onOpen).toHaveBeenCalledOnce();
    expect(hints).toEqual([{ orderId: id, streamVersion: 12 }]);
  });
  it.each([401, 403, 404, 429, 500, 204])('returns safe classified HTTP failure %s', async (status) => {
    const fetcher = vi.fn(
      async () => new Response(status === 204 ? null : 'private body', { status, headers: { 'Retry-After': '600' } }),
    );
    const options = {
      aggregateId: id,
      accessToken: 'token',
      signal: new AbortController().signal,
      onOpen: vi.fn(),
      onHint: vi.fn(),
      fetcher,
    };
    await expect(openFlightOrderEvents(options)).rejects.toMatchObject({ status });
    expect(options.onOpen).not.toHaveBeenCalled();
  });
  it('rejects HTML and refuses anonymous production transport', async () => {
    const fetcher = vi.fn(async () => new Response('<html>app</html>', { headers: { 'Content-Type': 'text/html' } }));
    const options = {
      aggregateId: id,
      accessToken: 'token' as string | null,
      signal: new AbortController().signal,
      onOpen: vi.fn(),
      onHint: vi.fn(),
      fetcher,
    };
    await expect(openFlightOrderEvents(options)).rejects.toMatchObject({ code: 'protocol' });
    fetcher.mockClear();
    await expect(openFlightOrderEvents({ ...options, accessToken: null })).rejects.toMatchObject({ code: 'auth' });
    expect(fetcher).not.toHaveBeenCalled();
  });
  it('settles an aborted reader even when the test fetch does not handle its signal', async () => {
    const controller = new AbortController();
    const cancelled = vi.fn();
    const stream = new ReadableStream({ cancel: cancelled });
    const task = openFlightOrderEvents({
      aggregateId: id,
      accessToken: 'token',
      signal: controller.signal,
      onOpen: () => controller.abort(),
      onHint: vi.fn(),
      fetcher: async () => new Response(stream, { headers: { 'Content-Type': 'text/event-stream' } }),
    });
    await task;
    expect(cancelled).toHaveBeenCalledOnce();
  });
});
