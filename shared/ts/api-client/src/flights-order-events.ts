export interface OrderChangeHint {
  orderId: string;
  streamVersion: number;
}

export class FlightOrderEventsError extends Error {
  constructor(
    readonly code: 'auth' | 'http' | 'disabled' | 'protocol' | 'network',
    readonly status: number | null = null,
    readonly retryAfterMs: number | null = null,
  ) {
    super(`Order stream ${code}`);
    this.name = 'FlightOrderEventsError';
  }
}

const events = new Set(['OrderConfirmed', 'OrderTicketed', 'OrderCancelled', 'OrderChanged']);
const guid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;
const maximumFrameBytes = 64 * 1024;

/** Parses the existing order endpoint protocol. Event bodies are never business snapshots. */
export class OrderEventParser {
  private readonly decoder = new TextDecoder();
  private line = '';
  private data: string[] = [];
  private event = 'message';
  private bytes = 0;
  private afterCr = false;

  constructor(
    private readonly orderId: string,
    private readonly hint: (value: OrderChangeHint) => void,
  ) {}

  push(chunk: Uint8Array): void {
    for (const char of this.decoder.decode(chunk, { stream: true })) {
      if (this.afterCr && char === '\n') {
        this.afterCr = false;
        continue;
      }
      this.afterCr = char === '\r';
      const point = char.codePointAt(0)!;
      this.bytes += point <= 0x7f ? 1 : point <= 0x7ff ? 2 : point <= 0xffff ? 3 : 4;
      if (this.bytes > maximumFrameBytes) throw new FlightOrderEventsError('protocol');
      if (char === '\r' || char === '\n') {
        this.consumeLine();
        this.line = '';
      } else this.line += char;
    }
  }

  private consumeLine(): void {
    if (this.line === '') {
      if (this.data.length > 0 && events.has(this.event)) {
        let value: unknown;
        try {
          value = JSON.parse(this.data.join('\n'));
        } catch {
          throw new FlightOrderEventsError('protocol');
        }
        if (typeof value !== 'object' || value === null) throw new FlightOrderEventsError('protocol');
        const body = value as Record<string, unknown>;
        if (
          body['type'] !== this.event ||
          typeof body['orderId'] !== 'string' ||
          body['orderId'].toLowerCase() !== this.orderId.toLowerCase() ||
          !Number.isSafeInteger(body['streamVersion']) ||
          (body['streamVersion'] as number) <= 0
        )
          throw new FlightOrderEventsError('protocol');
        this.hint({ orderId: this.orderId.toLowerCase(), streamVersion: body['streamVersion'] as number });
      }
      this.data = [];
      this.event = 'message';
      this.bytes = 0;
      return;
    }
    if (this.line.startsWith(':')) return;
    const colon = this.line.indexOf(':');
    const field = colon < 0 ? this.line : this.line.slice(0, colon);
    let value = colon < 0 ? '' : this.line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') this.event = value;
    else if (field === 'data') this.data.push(value);
  }
}

export interface OpenOrderEventsOptions {
  aggregateId: string;
  accessToken: string | null;
  signal: AbortSignal;
  onOpen: () => void;
  onHint: (hint: OrderChangeHint) => void;
  onActivity?: () => void;
  allowFictionalAnonymous?: boolean;
  fetcher?: typeof fetch;
}

function retryAfter(value: string | null): number | null {
  if (value === null) return null;
  const ms = /^\d+$/.test(value) ? Number(value) * 1000 : Date.parse(value) - Date.now();
  return Number.isFinite(ms) && ms >= 0 ? Math.min(ms, 300_000) : null;
}

export async function openFlightOrderEvents(options: OpenOrderEventsOptions): Promise<void> {
  if (!guid.test(options.aggregateId) || /^0{8}(?:-0{4}){3}-0{12}$/.test(options.aggregateId))
    throw new FlightOrderEventsError('protocol');
  if ((!options.accessToken || !options.accessToken.trim()) && !options.allowFictionalAnonymous)
    throw new FlightOrderEventsError('auth');
  if (options.signal.aborted) return;
  const headers = new Headers({ Accept: 'text/event-stream' });
  if (options.accessToken !== null) headers.set('Authorization', `Bearer ${options.accessToken}`);
  let reader: ReadableStreamDefaultReader<Uint8Array> | null = null;
  const abort = () => {
    void reader?.cancel().catch(() => undefined);
  };
  try {
    const response = await (options.fetcher ?? fetch)(`/events/flights/orders/${options.aggregateId.toLowerCase()}`, {
      method: 'GET',
      headers,
      signal: options.signal,
      cache: 'no-store',
      credentials: 'omit',
      redirect: 'error',
    });
    if (response.status !== 200) {
      await response.body?.cancel();
      throw new FlightOrderEventsError(
        response.status === 204 ? 'disabled' : response.status === 401 || response.status === 403 ? 'auth' : 'http',
        response.status,
        retryAfter(response.headers.get('Retry-After')),
      );
    }
    if (
      response.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase() !== 'text/event-stream' ||
      !response.body
    ) {
      await response.body?.cancel();
      throw new FlightOrderEventsError('protocol');
    }
    reader = response.body.getReader();
    options.signal.addEventListener('abort', abort, { once: true });
    if (options.signal.aborted) {
      abort();
      return;
    }
    const parser = new OrderEventParser(options.aggregateId, options.onHint);
    options.onOpen();
    while (!options.signal.aborted) {
      const next = await reader.read();
      if (options.signal.aborted || next.done) break;
      options.onActivity?.();
      parser.push(next.value);
    }
  } catch (error) {
    if (options.signal.aborted) return;
    if (error instanceof FlightOrderEventsError) throw error;
    throw new FlightOrderEventsError('network');
  } finally {
    options.signal.removeEventListener('abort', abort);
    if (reader) {
      await reader.cancel().catch(() => undefined);
      reader.releaseLock();
    }
  }
}
