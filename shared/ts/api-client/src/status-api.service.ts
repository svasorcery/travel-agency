import { Injectable } from '@angular/core';

export interface StatusResponse {
  version: string;
  db: string;
  timestamp: string;
}

/**
 * Angular service wrapping the status endpoint URL.
 * Consumed by StatusPageComponent via httpResource(() => this.api.statusUrl()).
 */
@Injectable({ providedIn: 'root' })
export class StatusApiService {
  readonly statusUrl = () => '/api/status';
}
