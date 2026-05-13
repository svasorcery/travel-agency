// Re-export the canonical StatusResponse type from the Angular service layer
export type { StatusResponse } from './status-api.service';

/**
 * Standalone fetch helper for non-Angular consumers (SSR scripts, CLI tools, etc.)
 * Angular components should use StatusApiService + httpResource instead.
 */
export async function getStatus(baseUrl: string): Promise<import('./status-api.service').StatusResponse> {
  const response = await fetch(`${baseUrl}/api/status`);
  if (!response.ok) throw new Error(`Status request failed: ${response.status}`);
  return (await response.json()) as import('./status-api.service').StatusResponse;
}
