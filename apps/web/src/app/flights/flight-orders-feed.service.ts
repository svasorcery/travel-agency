import { DOCUMENT } from '@angular/common';
import { DestroyRef, effect, Injectable, inject } from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import type { FlightOrderResponse } from '@travel/api-client';
import { FlightsAuthService } from './flights-auth.service';

export interface FlightOrdersFeedSnapshot {
  items: FlightOrderResponse[];
  nextOffset: number;
  hasMore: boolean;
  anchorId: string | null;
  anchorTop: number;
  scrollY: number;
  focusId: string | null;
}

const listPath = '/flights/orders';
const detailPath = /^\/flights\/orders\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

@Injectable({ providedIn: 'root' })
export class FlightOrdersFeedService {
  private readonly auth = inject(FlightsAuthService);
  private readonly router = inject(Router, { optional: true });
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly history = this.document.defaultView?.history ?? null;
  private readonly originalScrollRestoration = this.history?.scrollRestoration;
  private cached: { ownerUserId: string; snapshot: FlightOrdersFeedSnapshot } | null = null;
  private fromOrder = false;

  constructor() {
    effect(() => this.currentOwner());
    const routerPath = this.router?.url ?? '';
    const initialPath =
      routerPath === listPath || detailPath.test(routerPath)
        ? routerPath
        : (this.document.defaultView?.location.pathname ?? '');
    this.setScrollRestoration(initialPath);
    const navigation = this.router?.events.subscribe((event) => {
      if (!(event instanceof NavigationStart)) return;
      this.currentOwner();
      this.fromOrder = event.url === listPath && detailPath.test(this.router?.url ?? '') && this.cached !== null;
      this.setScrollRestoration(event.url);
    });
    this.destroyRef.onDestroy(() => {
      navigation?.unsubscribe();
      this.restoreNativeScrollRestoration();
      this.clear();
    });
  }

  save(ownerUserId: string, snapshot: FlightOrdersFeedSnapshot): void {
    const owner = this.currentOwner();
    if (owner === null || owner !== ownerUserId.toLowerCase()) return;
    this.cached = { ownerUserId: owner, snapshot: this.cloneSnapshot(snapshot) };
  }

  restore(ownerUserId: string): FlightOrdersFeedSnapshot | null {
    const owner = this.currentOwner();
    if (owner === null || owner !== ownerUserId.toLowerCase() || this.cached === null) return null;
    return this.cloneSnapshot(this.cached.snapshot);
  }

  clear(): void {
    this.cached = null;
    this.fromOrder = false;
  }

  returningFromOrder(): boolean {
    this.currentOwner();
    return this.fromOrder && this.cached !== null;
  }

  private currentOwner(): string | null {
    const status = this.auth.status();
    const owner = status.kind === 'authenticated' ? status.userId.toLowerCase() : null;
    if (owner === null || (this.cached !== null && this.cached.ownerUserId !== owner)) this.clear();
    return owner;
  }

  private setScrollRestoration(path: string): void {
    if (this.history === null || this.originalScrollRestoration === undefined) return;
    this.history.scrollRestoration =
      path === listPath || detailPath.test(path) ? 'manual' : this.originalScrollRestoration;
  }

  private restoreNativeScrollRestoration(): void {
    if (this.history !== null && this.originalScrollRestoration !== undefined) {
      this.history.scrollRestoration = this.originalScrollRestoration;
    }
  }

  private cloneSnapshot(snapshot: FlightOrdersFeedSnapshot): FlightOrdersFeedSnapshot {
    return {
      ...snapshot,
      items: snapshot.items.map((order) => ({
        ...order,
        ticketNumbers: [...order.ticketNumbers],
        itinerary: {
          ...order.itinerary,
          slices: order.itinerary.slices.map((slice) => ({
            ...slice,
            segments: slice.segments.map((segment) => ({ ...segment })),
          })),
        },
      })),
    };
  }
}
