export async function observeResponse<T>(response: Promise<T>, action: () => Promise<unknown>): Promise<T> {
  const [result] = await Promise.all([response, Promise.resolve().then(action)]);
  return result;
}
