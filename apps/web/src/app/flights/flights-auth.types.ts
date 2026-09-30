export type FlightsAuthStatus =
  | { kind: 'anonymous' }
  | { kind: 'redirecting' }
  | { kind: 'authenticated'; userId: string }
  | { kind: 'unavailable'; message: string }
  | { kind: 'error'; message: string };
