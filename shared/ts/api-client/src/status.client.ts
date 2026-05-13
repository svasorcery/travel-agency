export interface StatusResponse {
  version: string;
  db: string;
  timestamp: string;
}

export async function getStatus(baseUrl: string): Promise<StatusResponse> {
  const response = await fetch(`${baseUrl}/api/status`);
  if (!response.ok) throw new Error(`Status request failed: ${response.status}`);
  return (await response.json()) as StatusResponse;
}
