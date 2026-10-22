import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  Injector,
  inject,
  PLATFORM_ID,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, type SafeResourceUrl } from '@angular/platform-browser';
import { RailApiService, type RailJourney } from '@travel/api-client';
import { firstValueFrom, fromEvent, takeUntil } from 'rxjs';
import { RailState } from './rail-state';
@Component({
  selector: 'app-rail-page',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './rail-page.component.html',
  styleUrl: './rail-page.component.scss',
})
export class RailPageComponent {
  private readonly api = inject(RailApiService);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  readonly state: RailState = new RailState(
    {
      stations: (q, signal) =>
        this.browser
          ? firstValueFrom(this.api.stations(q).pipe(takeUntil(fromEvent(signal, 'abort'))))
          : Promise.reject(),
      search: (request, signal) =>
        this.browser
          ? firstValueFrom(this.api.search(request, this.state.reference!).pipe(takeUntil(fromEvent(signal, 'abort'))))
          : Promise.reject(),
      details: (request, signal) =>
        this.browser
          ? firstValueFrom(this.api.details(request, this.state.reference!).pipe(takeUntil(fromEvent(signal, 'abort'))))
          : Promise.reject(),
    },
    () => this.changes.markForCheck(),
  );
  private lastFrame: string | null = null;
  private safeFrame: SafeResourceUrl | null = null;
  private routeTrigger: HTMLElement | null = null;
  readonly buckets = [
    { value: 'all', label: 'Любое' },
    { value: 'night', label: '00:00–06:00' },
    { value: 'morning', label: '06:00–12:00' },
    { value: 'afternoon', label: '12:00–18:00' },
    { value: 'evening', label: '18:00–24:00' },
  ];
  carrier(value: string): void {
    this.state.setView({
      carrier:
        value === 'all'
          ? { kind: 'all' }
          : value === 'unknown'
            ? { kind: 'unknown' }
            : { kind: 'exact', value: value.slice(6) },
    });
  }
  get carrierValue(): string {
    const c = this.state.view.carrier;
    return c.kind === 'exact' ? `exact:${c.value}` : c.kind;
  }
  openRoute(journey: RailJourney, event: Event): void {
    this.routeTrigger = event.currentTarget as HTMLElement;
    void this.state.openDetails(journey);
    if (this.browser)
      afterNextRender(
        () => {
          const heading = this.document.getElementById('rail-route-title');
          heading?.focus({ preventScroll: true });
          heading?.scrollIntoView({ block: 'start' });
        },
        { injector: this.injector },
      );
  }
  closeRoute(): void {
    const trigger = this.routeTrigger;
    this.state.closeDetails();
    if (trigger?.isConnected) trigger.focus();
    else this.document.getElementById('rail-results')?.focus();
  }
  constructor() {
    const resume = () => {
      if (this.document.visibilityState !== 'hidden') this.state.resume();
    };
    const violation = (event: SecurityPolicyViolationEvent) => {
      if (event.disposition === 'report') return;
      const frame = this.attribution?.frameUrl;
      if (!frame || !['frame-src', 'child-src', 'default-src'].includes(event.effectiveDirective)) return;
      const expected = new URL(frame, this.document.baseURI);
      let blocked: URL;
      try {
        blocked = new URL(event.blockedURI, this.document.baseURI);
      } catch {
        return;
      }
      if (
        blocked.href === expected.href ||
        (expected.origin !== new URL(this.document.baseURI).origin &&
          blocked.origin === expected.origin &&
          blocked.pathname === '/')
      )
        this.state.attributionFailed();
    };
    if (this.browser) {
      this.document.addEventListener('securitypolicyviolation', violation);
      this.document.addEventListener('visibilitychange', resume);
      this.document.defaultView?.addEventListener('pageshow', resume);
    }
    inject(DestroyRef).onDestroy(() => {
      this.document.removeEventListener('securitypolicyviolation', violation);
      this.document.removeEventListener('visibilitychange', resume);
      this.document.defaultView?.removeEventListener('pageshow', resume);
      this.state.dispose();
    });
  }
  get attribution() {
    return this.state.result?.attribution ?? this.state.reference?.attribution ?? null;
  }
  get frame(): SafeResourceUrl | null {
    const url = this.attribution?.frameUrl ?? null;
    const profile = this.state.reference?.profile;
    const valid =
      url ===
      (profile === 'fictional'
        ? '/rail-fictional-banner.html'
        : 'https://yandex.st/rasp/media/apicc/copyright_vert_mono.html');
    if (!valid || url === null) return null;
    if (url !== this.lastFrame) {
      this.lastFrame = url;
      this.safeFrame = this.sanitizer.bypassSecurityTrustResourceUrl(url);
    }
    return this.safeFrame;
  }
  localTime(value: string): string {
    return `${value.slice(0, 10)} ${value.slice(11, 19)} UTC${value.endsWith('Z') ? '+00:00' : value.slice(-6)}`;
  }
  duration(seconds: number): string {
    return `${Math.floor(seconds / 3600)} ч ${Math.floor((seconds % 3600) / 60)} мин`;
  }
  key(event: KeyboardEvent, field: 'origin' | 'destination'): void {
    if (event.key === 'Escape') {
      this.state[field].suggestions = [];
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      document.querySelector<HTMLButtonElement>(`#rail-${field}-options button`)?.focus();
    }
    if (event.key === 'Enter' && this.state[field].suggestions.length > 0) {
      event.preventDefault();
      this.state.select(field, this.state[field].suggestions[0]);
    }
  }
  optionKey(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const current = event.currentTarget as HTMLButtonElement;
      const row = current.parentElement;
      const next = event.key === 'ArrowDown' ? row?.nextElementSibling : row?.previousElementSibling;
      next?.querySelector('button')?.focus();
    }
  }
}
