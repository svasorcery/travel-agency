import { DOCUMENT } from '@angular/common';
import { afterNextRender, Component, effect, HostListener, Injector, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  type TripBrief,
  type TripBriefSlot,
  tripBriefSlots,
  tripCities,
  tripInterests,
  tripPaces,
} from '@travel/api-client';
import { TripsBriefExtractionStateService } from './trips-brief-extraction-state.service';

export { TRIPS_BRIEF_MONOTONIC } from './trips-brief-extraction-state.service';
@Component({
  selector: 'app-trips-brief-review',
  standalone: true,
  imports: [FormsModule],
  providers: [TripsBriefExtractionStateService],
  templateUrl: './trips-brief-review.component.html',
  styles: [
    `:host{display:block;margin-block:1.5rem} section{border:1px solid #8a9299;border-radius:.5rem;padding:1rem}textarea,input,select{display:block;max-width:100%;margin-block:.3rem .7rem}input[type=checkbox]{display:inline}li{margin-block:.6rem}pre{white-space:pre-wrap;overflow-wrap:anywhere}button{margin:.3rem}small{display:block}label{margin-right:.4rem}`,
  ],
})
export class TripsBriefReviewComponent {
  readonly draft = input.required<TripBrief>();
  readonly blocked = input(false);
  readonly transferred = output<TripBrief>();
  readonly flow = inject(TripsBriefExtractionStateService);
  readonly slots = tripBriefSlots;
  readonly cities = tripCities;
  readonly interests = tripInterests;
  readonly paces = tripPaces;
  readonly labels: Record<TripBriefSlot, string> = {
    destinationId: 'Город',
    startDate: 'Первый день',
    endDate: 'Последний день',
    durationDays: 'Длительность (только для проверки)',
    adults: 'Взрослые',
    interests: 'Интересы',
    pace: 'Темп',
  };
  readonly states = {
    extracted: 'Извлечено',
    missing: 'Не указано',
    ambiguous: 'Неоднозначно',
    unsupported: 'Не поддерживается',
    invalid: 'Недопустимо',
  };
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  constructor() {
    effect(() => this.flow.setDraft(this.draft()));
    effect(() => this.flow.setBlocked(this.blocked()));
    this.flow.transferred.subscribe((value) => this.transferred.emit(value));
  }
  async extract(): Promise<void> {
    await this.flow.extract();
    if (this.flow.phase() !== 'stale') this.focus(this.flow.candidate() ? 'brief-review-title' : 'brief-status');
  }
  async confirm(): Promise<void> {
    await this.flow.confirm();
    this.focus('brief-status');
  }
  private focus(id: string): void {
    const scope = this.flow.editor.itineraryContext();
    const draft = this.draft();
    const ticket = this.flow.editor.captureBriefTicket();
    afterNextRender(
      () => {
        if (
          scope === this.flow.editor.itineraryContext() &&
          draft === this.draft() &&
          ticket &&
          this.flow.editor.isBriefTicketCurrent(ticket)
        )
          this.document.getElementById(id)?.focus();
      },
      { injector: this.injector },
    );
  }
  display(value: unknown): string {
    return value === null || value === undefined || value === ''
      ? 'не указано'
      : Array.isArray(value)
        ? value.length
          ? value.join(', ')
          : 'пустой список'
        : String(value);
  }
  retained(key: TripBriefSlot): string {
    const draft = this.draft();
    if (key === 'durationDays') return 'Нет поля в форме';
    return this.display(draft[key]);
  }
  @HostListener('document:visibilitychange') visibility(): void {
    if (this.document.visibilityState === 'hidden') this.flow.suspend();
  }
  @HostListener('document:freeze') freeze(): void {
    this.flow.suspend();
  }
  @HostListener('document:resume') resume(): void {
    this.flow.suspend();
  }
  @HostListener('window:pagehide') pagehide(): void {
    this.flow.suspend();
  }
  @HostListener('window:pageshow', ['$event']) pageshow(event: PageTransitionEvent): void {
    if (event.persisted) this.flow.suspend();
  }
}
