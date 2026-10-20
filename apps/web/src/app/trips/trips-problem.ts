import { HttpErrorResponse } from '@angular/common/http';
// Trips RFC7807 uses a code extension; type is the ordinary about:blank URI.
export function tripsProblemCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  try {
    const raw: unknown = error.error;
    if (typeof raw === 'string' && new TextEncoder().encode(raw).length > 131072) return null;
    const body: unknown = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (!body || typeof body !== 'object' || Array.isArray(body)) return null;
    const code = (body as Record<string, unknown>)['code'];
    return typeof code === 'string' && /^Trips\.[A-Za-z]+$/.test(code) ? code : null;
  } catch {
    return null;
  }
}
