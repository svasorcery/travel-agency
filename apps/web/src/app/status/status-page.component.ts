import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { StatusApiService, type StatusResponse } from '@travel/api-client';

@Component({
  selector: 'app-status-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="p-8">
      <h1 class="text-2xl font-bold">System Status</h1>
      @if (status.isLoading()) {
        <p>Loading…</p>
      } @else if (status.error()) {
        <p class="text-red-600">Error: {{ status.error()!.message }}</p>
      } @else if (status.value(); as s) {
        <ul class="mt-4 space-y-2">
          <li>db: {{ s.db }}</li>
          <li>version: {{ s.version }}</li>
          <li>timestamp: {{ s.timestamp }}</li>
        </ul>
      }
    </main>
  `,
})
export class StatusPageComponent {
  private readonly api = inject(StatusApiService);
  status = httpResource<StatusResponse>(() => this.api.statusUrl());
}
