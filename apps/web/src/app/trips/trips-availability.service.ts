import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { computed, Injectable, inject, PLATFORM_ID, signal } from '@angular/core';
import { type TripCatalogue, TripsApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
import { tripsProblemCode } from './trips-problem';
export type TripsAvailability = 'unknown' | 'disabled' | 'authentication-required' | 'ready' | 'unavailable' | 'error';
@Injectable({ providedIn: 'root' })
export class TripsAvailabilityService {
  private readonly api = inject(TripsApiService);
  private readonly platform = inject(PLATFORM_ID);
  readonly status = signal<TripsAvailability>('unknown');
  readonly enabled = computed(
    () => this.status() === 'ready' || this.status() === 'unavailable' || this.status() === 'authentication-required',
  );
  readonly catalogue = signal<TripCatalogue | null>(null);
  private request = 0;
  private pending: Promise<void> | null = null;
  initialize(token: string | null = null): Promise<void> {
    if (!isPlatformBrowser(this.platform)) return Promise.resolve();
    if (this.pending && token === null) return this.pending;
    const epoch = ++this.request;
    const task = this.load(epoch, token).finally(() => {
      if (this.pending === task) this.pending = null;
    });
    this.pending = task;
    return task;
  }
  clearPrivate(): void {
    this.request++;
    this.pending = null;
    this.catalogue.set(null);
    if (this.status() === 'ready') this.status.set('authentication-required');
  }
  private async load(epoch: number, token: string | null): Promise<void> {
    try {
      const catalogue = await firstValueFrom(this.api.destinations(token));
      if (epoch !== this.request) return;
      this.catalogue.set(catalogue);
      this.status.set('ready');
    } catch (error) {
      if (epoch !== this.request) return;
      this.catalogue.set(null);
      const code = error instanceof HttpErrorResponse ? error.status : 0;
      const typed = tripsProblemCode(error) === 'Trips.Unavailable';
      this.status.set(
        code === 404
          ? 'disabled'
          : code === 401
            ? 'authentication-required'
            : code === 503 && typed
              ? 'unavailable'
              : 'error',
      );
    }
  }
}
