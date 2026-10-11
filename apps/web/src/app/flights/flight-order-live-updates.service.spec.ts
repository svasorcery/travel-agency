import { TestBed } from '@angular/core/testing';
import { FlightOrderEventsError, type OpenOrderEventsOptions } from '@travel/api-client';
import { FlightOrderLiveUpdatesService, type OrderLiveContext } from './flight-order-live-updates.service';

describe('Order page live observation', () => {
  let service: FlightOrderLiveUpdatesService;
  let context: OrderLiveContext;
  let streams: OpenOrderEventsOptions[];
  const settle = async () => {
    for (let i = 0; i < 12; i++) await Promise.resolve();
  };
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [FlightOrderLiveUpdatesService] });
    service = TestBed.inject(FlightOrderLiveUpdatesService);
    streams = [];
    const read = () => vi.fn().mockResolvedValue({ outcome: 'ok', version: 10 });
    context = {
      key: 'order:owner:0:1',
      aggregateId: '11111111-1111-1111-1111-111111111111',
      getToken: vi.fn().mockResolvedValue('token'),
      isCurrent: () => true,
      canStream: () => true,
      readers: { creation: read(), order: read(), cancellation: read(), refund: read() },
      onPause: vi.fn(),
      onAuthorizationFailure: vi.fn(),
      transport: (options) =>
        new Promise<void>((resolve) => {
          streams.push(options);
          options.onOpen();
          options.signal.addEventListener('abort', () => resolve(), { once: true });
        }),
    };
  });
  afterEach(() => {
    service.stop();
    TestBed.resetTestingModule();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });
  it('same identity start does not rotate session; satisfied duplicate does not read again', async () => {
    service.start(context);
    await settle();
    const count = vi.mocked(context.readers.refund).mock.calls.length;
    service.start({ ...context });
    streams[0].onHint({ orderId: context.aggregateId, streamVersion: 10 });
    await settle();
    expect(streams).toHaveLength(1);
    expect(context.readers.refund).toHaveBeenCalledTimes(count);
  });
  it('queues only one follow-up for a new hint received during an older GET', async () => {
    let finish!: (value: { outcome: 'ok'; version: number }) => void;
    context.readers.refund = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            finish = resolve;
          }),
      )
      .mockResolvedValue({ outcome: 'ok', version: 11 });
    service.start(context);
    await settle();
    for (let i = 0; i < 100; i++) streams[0].onHint({ orderId: context.aggregateId, streamVersion: 11 });
    finish({ outcome: 'ok', version: 10 });
    await settle();
    expect(context.readers.refund).toHaveBeenCalledTimes(2);
  });
  it('falls back on a healthy stream and stops every timer/callback on close', async () => {
    service.start(context);
    await settle();
    await vi.advanceTimersByTimeAsync(30_000);
    expect(vi.mocked(context.readers.refund).mock.calls.length).toBeGreaterThan(1);
    service.stop();
    const count = vi.mocked(context.readers.refund).mock.calls.length;
    await vi.advanceTimersByTimeAsync(120_000);
    streams[0].onHint({ orderId: context.aggregateId, streamVersion: 99 });
    await settle();
    expect(context.readers.refund).toHaveBeenCalledTimes(count);
    expect(streams[0].signal.aborted).toBe(true);
  });
  it('ignores token completion after stop and preserves the existing mutation lifetime on pause', async () => {
    let finish!: (token: string) => void;
    context.getToken = () =>
      new Promise((resolve) => {
        finish = resolve;
      });
    service.start(context);
    service.stop();
    finish('late-token');
    await settle();
    expect(streams).toHaveLength(0);
    expect(context.readers.order).not.toHaveBeenCalled();
    expect(context.onPause).toHaveBeenCalledWith(true);
  });
  it('a zero Retry-After cannot create a reconnect loop', async () => {
    let calls = 0;
    context.transport = async () => {
      if (++calls <= 4) throw new FlightOrderEventsError('http', 429, 0);
      return new Promise(() => undefined);
    };
    service.start(context);
    await settle();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(calls).toBe(1);
  });
  it('an old healthy connection does not reset backoff of later failed attempts', async () => {
    let finish!: () => void;
    let calls = 0;
    context.transport = (options) => {
      if (++calls === 1) {
        options.onOpen();
        return new Promise<void>((resolve) => {
          finish = resolve;
          options.signal.addEventListener('abort', () => resolve());
        });
      }
      return Promise.reject(new FlightOrderEventsError('network'));
    };
    service.start(context);
    await settle();
    await vi.advanceTimersByTimeAsync(30_001);
    finish();
    await settle();
    await vi.advanceTimersByTimeAsync(1_000);
    expect(calls).toBe(2);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(calls).toBe(2);
    await vi.advanceTimersByTimeAsync(1_000);
    expect(calls).toBe(3);
  });
  it('retained checkpoint from a stale GET does not acknowledge freshness', async () => {
    context.readers.order = vi.fn().mockResolvedValue({ outcome: 'stale', version: 10 });
    service.start(context);
    await settle();
    expect(service.synchronizing()).toBe(true);
    expect(service.prerequisitesReady('order')).toBe(false);
    await vi.advanceTimersByTimeAsync(30_001);
    const reads = vi.mocked(context.readers.order).mock.calls.length;
    for (let i = 0; i < 100; i++) streams[0].onHint({ orderId: context.aggregateId, streamVersion: 10 });
    await vi.advanceTimersByTimeAsync(5000);
    expect(context.readers.order).toHaveBeenCalledTimes(reads);
  });
  it('post-connect gate cannot be satisfied by an earlier pending read', async () => {
    let complete!: (value: { outcome: 'ok'; version: number }) => void;
    context.readers.refund = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            complete = resolve;
          }),
      )
      .mockImplementationOnce(() => new Promise(() => undefined));
    service.start(context);
    await settle();
    expect(service.prerequisitesReady('refund')).toBe(false);
    complete({ outcome: 'ok', version: 10 });
    await settle();
    expect(context.readers.refund).toHaveBeenCalledTimes(2);
    expect(service.prerequisitesReady('refund')).toBe(false);
  });
  it('token acquisition timeout settles observation without inventing authorization loss', async () => {
    context.getToken = () => new Promise(() => undefined);
    service.start(context);
    await vi.advanceTimersByTimeAsync(15_001);
    expect(context.onAuthorizationFailure).not.toHaveBeenCalled();
    expect(service.message()).toContain('Не удалось обновить данные');
    expect(context.readers.order).not.toHaveBeenCalled();
    service.stop();
    await settle();
    expect(vi.getTimerCount()).toBe(0);
  });
  it('pauses every read and stream while hidden, ignores late results, and resumes one coalesced cycle', async () => {
    let complete!: (value: { outcome: 'ok'; version: number }) => void;
    context.readers.refund = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            complete = resolve;
          }),
      )
      .mockResolvedValue({ outcome: 'ok', version: 10 });
    const hidden = vi.spyOn(document, 'hidden', 'get').mockReturnValue(false);
    service.start(context);
    await settle();
    const before = vi.mocked(context.readers.refund).mock.calls.length;
    hidden.mockReturnValue(true);
    document.dispatchEvent(new Event('visibilitychange'));
    complete({ outcome: 'ok', version: 99 });
    await vi.advanceTimersByTimeAsync(200_000);
    expect(context.readers.refund).toHaveBeenCalledTimes(before);
    expect(service.targetVersion()).toBe(10);
    expect(service.prerequisitesReady('refund')).toBe(false);
    expect(streams[0].signal.aborted).toBe(true);
    hidden.mockReturnValue(false);
    document.dispatchEvent(new Event('visibilitychange'));
    document.dispatchEvent(new Event('online'));
    await settle();
    await settle();
    expect(streams).toHaveLength(2);
    const resumed = vi.mocked(context.readers.refund).mock.calls.length - before;
    expect(resumed).toBeGreaterThanOrEqual(1);
    expect(resumed).toBeLessThanOrEqual(2);
    expect(service.prerequisitesReady('refund')).toBe(true);
  });
  it('one failed panel cannot suppress a successful independent refund read', async () => {
    context.readers.creation = vi.fn().mockRejectedValue(new Error('Unavailable'));
    context.readers.order = vi.fn().mockResolvedValue({ outcome: 'error', version: 10 });
    service.start(context);
    await settle();
    expect(service.prerequisitesReady('refund')).toBe(true);
    expect(service.prerequisitesReady('creation', 'order')).toBe(false);
    expect(context.readers.refund).toHaveBeenCalled();
    expect(service.message()).toContain('Не удалось обновить данные');
  });
  it('reads the first owned projection when source-only creation becomes Held during a cycle', async () => {
    let applicable = false;
    let materialized = false;
    context.orderApplicable = () => applicable;
    context.canStream = () => materialized;
    context.readers.order = vi.fn(async () => {
      if (!applicable) return { outcome: 'notApplicable' as const, version: null };
      materialized = true;
      return { outcome: 'ok' as const, version: 11 };
    });
    service.start(context);
    await settle();
    expect(streams).toHaveLength(0);
    context.readers.creation = vi.fn(async () => {
      await Promise.resolve();
      applicable = true;
      return { outcome: 'ok' as const, version: 11 };
    });
    context.readers.cancellation = vi.fn().mockResolvedValue({ outcome: 'ok', version: 11 });
    context.readers.refund = vi.fn().mockResolvedValue({ outcome: 'ok', version: 11 });
    service.requestRefresh('workflow');
    await settle();
    await settle();
    expect(materialized).toBe(true);
    expect(streams).toHaveLength(1);
    expect(service.prerequisitesReady('order')).toBe(true);
  });
  it.each([401, 403])('revokes observation on an authoritative GET denial (%s)', async (status) => {
    context.readers.refund = vi.fn().mockResolvedValue({ outcome: 'auth', status, version: null });
    service.start(context);
    await settle();
    expect(context.onAuthorizationFailure).toHaveBeenCalledWith(status);
    expect(service.active()).toBe(false);
    expect(vi.getTimerCount()).toBe(0);
    const count = vi.mocked(context.readers.refund).mock.calls.length;
    await vi.advanceTimersByTimeAsync(200_000);
    expect(context.readers.refund).toHaveBeenCalledTimes(count);
  });
  it.each([null, 10])(
    'stops an all404 resource only when no same-owner checkpoint was retained (%s)',
    async (retained) => {
      context.canStream = () => false;
      for (const name of ['creation', 'order', 'cancellation', 'refund'] as const)
        context.readers[name] = vi.fn().mockResolvedValue({
          outcome: 'missing',
          version: name === 'cancellation' ? retained : null,
        });
      service.start(context);
      await settle();
      expect(service.active()).toBe(retained !== null);
      expect(service.prerequisitesReady('cancellation')).toBe(false);
      expect(streams).toHaveLength(0);
    },
  );
  it('keeps bounded fallback for a proven legacy resource without treating its null version as fresh', async () => {
    context.canStream = () => false;
    const legacy = { ...context, hasRetainedEvidence: () => true };
    for (const name of ['creation', 'order', 'cancellation', 'refund'] as const)
      context.readers[name] = vi.fn().mockResolvedValue({ outcome: 'missing', version: null });
    service.start(legacy);
    await settle();
    expect(service.active()).toBe(true);
    expect(service.targetVersion()).toBe(0);
    expect(service.prerequisitesReady('order')).toBe(false);
    const before = vi.mocked(context.readers.order).mock.calls.length;
    await vi.advanceTimersByTimeAsync(29_999);
    expect(context.readers.order).toHaveBeenCalledTimes(before);
    await vi.advanceTimersByTimeAsync(1);
    expect(context.readers.order).toHaveBeenCalledTimes(before + 1);
    expect(streams).toHaveLength(0);
  });
});
