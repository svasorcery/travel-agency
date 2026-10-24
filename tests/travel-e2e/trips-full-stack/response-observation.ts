export interface JsonResponse {
  json(): Promise<unknown>;
}

export async function observeResponse<T>(response: Promise<T>, action: () => Promise<unknown>): Promise<T> {
  const [result] = await Promise.all([response, Promise.resolve().then(action)]);
  return result;
}

export async function observeJsonResponse<T, R extends JsonResponse = JsonResponse>(
  response: Promise<R>,
  action: () => Promise<unknown>,
): Promise<{ response: R; body: T }> {
  // Start reading as soon as the response arrives, before the action can finish
  // a navigation that releases Chromium's response body. Observe each operation once.
  const captured = response.then(async (observed) => ({
    response: observed,
    body: (await observed.json()) as T,
  }));
  return observeResponse(captured, action);
}
