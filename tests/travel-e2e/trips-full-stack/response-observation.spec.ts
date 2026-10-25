import { describe, expect, it, vi } from 'vitest';
import { observeResponse } from './response-observation';

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
});
