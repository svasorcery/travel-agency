import { describe, expect, it, vi } from 'vitest';
import { type JsonResponse, observeJsonResponse, observeResponse } from './response-observation';

describe('real response observation', () => {
  it('retains response metadata when navigation makes the browser body unavailable', async () => {
    const json = vi.fn().mockRejectedValue(new Error('No resource with given identifier found'));
    const response = { status: 201, location: '/api/trips/fictional-id', json };
    let finishAction!: () => void;
    const action = new Promise<void>((resolve) => {
      finishAction = resolve;
    });
    let completed = false;
    const observation = observeResponse(Promise.resolve(response), () => action).then((result) => {
      completed = true;
      return result;
    });
    await new Promise<void>((resolve) => setImmediate(resolve));
    expect(completed).toBe(false);
    finishAction();
    expect(await observation).toBe(response);
    expect(json).not.toHaveBeenCalled();
  });

  it('reads the original body before the click completes navigation and releases it', async () => {
    let deliver!: (response: JsonResponse) => void;
    const response = new Promise<JsonResponse>((resolve) => {
      deliver = resolve;
    });
    let released = false;
    const payload = { revision: '1', days: [{ date: '2026-10-25', blocks: [] }] };
    const json = vi.fn(async () => {
      if (released) throw new Error('Response body is unavailable');
      return payload;
    });
    const original = { json };
    const click = vi.fn(async () => {
      deliver(original);
      await Promise.resolve();
      released = true;
    });
    const result = await observeJsonResponse<typeof payload>(response, click);
    expect(result.response).toBe(original);
    expect(result.body).toBe(payload);
    expect(click).toHaveBeenCalledTimes(1);
    expect(json).toHaveBeenCalledTimes(1);
  });

  it('propagates a body failure without retrying the action or substituting data', async () => {
    const error = new Error('body unavailable');
    const json = vi.fn().mockRejectedValue(error);
    const click = vi.fn().mockResolvedValue(undefined);
    await expect(observeJsonResponse(Promise.resolve({ json }), click)).rejects.toBe(error);
    expect(click).toHaveBeenCalledTimes(1);
    expect(json).toHaveBeenCalledTimes(1);
  });

  it('does not report success when the UI action fails despite receiving a body', async () => {
    const error = new Error('click failed');
    const click = vi.fn().mockRejectedValue(error);
    await expect(observeJsonResponse(Promise.resolve({ json: async () => ({ revision: '1' }) }), click)).rejects.toBe(
      error,
    );
    expect(click).toHaveBeenCalledTimes(1);
  });
});
