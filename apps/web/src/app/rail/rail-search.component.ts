import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  output,
  PLATFORM_ID,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, type SafeResourceUrl } from '@angular/platform-browser';
import { RailApiService, type RailJourney } from '@travel/api-client';
import { firstValueFrom, fromEvent, takeUntil } from 'rxjs';
import { type RailSelection, RailState } from './rail-state';
@Component({
  selector: 'app-rail-search',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './rail-search.component.html',
  styleUrl: './rail-search.component.scss',
})
export class RailSearchComponent {
  readonly selectable = input(false);
  readonly journeySelected = output<RailSelection>();
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly api = inject(RailApiService);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly document = inject(DOCUMENT);
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
    },
    () => this.changes.markForCheck(),
  );
  private lastFrame: string | null = null;
  private safeFrame: SafeResourceUrl | null = null;
  constructor() {
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
    if (this.browser) this.document.addEventListener('securitypolicyviolation', violation);
    inject(DestroyRef).onDestroy(() => {
      this.document.removeEventListener('securitypolicyviolation', violation);
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
  choose(journey: RailJourney): void {
    if (!this.selectable()) return;
    const selection = this.state.captureSelection(journey);
    if (selection) this.journeySelected.emit(selection);
    else this.state.message = this.selectionReason(journey);
  }
  selectionReason(journey: RailJourney): string {
    return this.state.result?.profile === 'real'
      ? 'Данные реального источника доступны только для просмотра: условия источника не разрешают перенос в план.'
      : this.state.selectionAvailable(journey)
        ? ''
        : 'Выбор недоступен: нужны свежие неповреждённые данные и сведения об источнике. Обновите поиск или выберите станции вручную.';
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
      this.element.nativeElement.querySelector<HTMLButtonElement>(`#rail-${field}-options button`)?.focus();
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
