import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  HostListener,
  Injector,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  type TripBlock,
  type TripBrief,
  type TripCurrency,
  type TripInterest,
  type TripPace,
  tripCurrencies,
  tripInterests,
  tripMoney,
  tripPaces,
  tripText,
} from '@travel/api-client';
import { TripsStateService } from './trips-state.service';

const labels: Record<TripInterest, string> = {
  architecture: 'Архитектура',
  culture: 'Культура',
  food: 'Еда',
  nature: 'Природа',
  history: 'История',
};
@Component({
  selector: 'app-trips-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-page.component.html',
  styleUrl: './trips-page.component.scss',
})
export class TripsPageComponent {
  readonly state = inject(TripsStateService);
  readonly auth = this.state.auth;
  readonly availability = this.state.availability;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly destroy = inject(DestroyRef);
  readonly currencies = tripCurrencies;
  readonly interests = tripInterests;
  readonly paces = tripPaces;
  readonly interestLabels = labels;
  readonly paceLabels: Record<TripPace, string> = { relaxed: 'Спокойный', balanced: 'Умеренный', active: 'Активный' };
  readonly deleteConfirmation = signal(false);
  readonly briefDraft = signal<TripBrief | null>(null);
  readonly errorField = signal<string | null>(null);
  readonly timeZone = computed(
    () =>
      this.state.baseline()?.destination.timeZone ??
      this.availability.catalogue()?.items.find((x) => x.destinationId === this.state.buffer()?.brief.destinationId)
        ?.timeZone ??
      'местное время',
  );
  readonly briefDirty = computed(() => {
    const current = this.state.buffer();
    return current !== null && JSON.stringify(current.brief) !== JSON.stringify(this.briefDraft());
  });
  private briefSource: TripBrief | null = null;
  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.deleteConfirmation.set(false);
      this.errorField.set(null);
      void this.state.enter(params.get('id'));
    });
    effect(() => {
      const buffer = this.state.buffer();
      untracked(() => {
        if (buffer?.brief !== this.briefSource) {
          const prior = this.briefSource;
          this.briefSource = buffer?.brief ?? null;
          if (!buffer) this.briefDraft.set(null);
          else if (prior === null || JSON.stringify(prior) !== JSON.stringify(buffer.brief))
            this.briefDraft.set(structuredClone(buffer.brief));
        }
      });
    });
    this.destroy.onDestroy(() => {
      this.briefDraft.set(null);
      this.state.leave();
    });
  }
  canLeave(): boolean {
    return (
      !(this.state.dirty() || this.briefDirty()) ||
      this.document.defaultView?.confirm('Есть несохранённые изменения. Покинуть план и удалить их из редактора?') ===
        true
    );
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.state.dirty() || this.briefDirty()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  async login(): Promise<void> {
    if (await this.auth.beginLogin(this.router.url)) await this.state.enter(this.route.snapshot.paramMap.get('id'));
  }
  async logout(): Promise<void> {
    this.briefDraft.set(null);
    await this.state.logout();
  }
  patchBrief<K extends keyof TripBrief>(key: K, value: TripBrief[K]): void {
    const b = this.briefDraft();
    if (b) {
      const next = { ...b, [key]: value };
      this.state.touch(JSON.stringify(next) !== JSON.stringify(this.state.buffer()?.brief));
      this.briefDraft.set(next);
    }
  }
  toggleInterest(interest: TripInterest, checked: boolean): void {
    const b = this.briefDraft();
    if (b)
      this.patchBrief('interests', checked ? [...b.interests, interest] : b.interests.filter((x) => x !== interest));
  }
  applyBrief(): boolean {
    const b = this.briefDraft();
    if (!b) return false;
    const success = this.state.changeBrief(b);
    const current = this.state.buffer();
    const hasBlocks = current?.days.some((day) => day.blocks.length);
    const field =
      hasBlocks && b.destinationId !== current?.brief.destinationId
        ? 'trip-destination'
        : hasBlocks && b.currency !== current?.brief.currency
          ? 'trip-currency'
          : 'trip-start';
    this.errorField.set(success ? null : field);
    if (!success) this.focus(field);
    return success;
  }
  title(value: string): void {
    const d = this.state.buffer();
    if (d) this.state.edit({ ...d, title: value });
  }
  async submit(): Promise<void> {
    if (this.briefDirty() && !this.applyBrief()) return;
    if (this.state.selectedId()) await this.state.save();
    else await this.state.create();
    if (this.state.status() === 'invalid') {
      const field = this.validationTarget();
      this.errorField.set(field);
      this.focus(field);
    } else if (
      !this.state.dirty() &&
      !this.briefDirty() &&
      this.state.selectedId() &&
      this.route.snapshot.paramMap.get('id') !== this.state.selectedId()
    ) {
      await this.router.navigate(['/trips', this.state.selectedId()]);
    }
  }
  block(block: TripBlock, key: 'title' | 'note' | 'startTime' | 'endTime', value: string): void {
    this.state.updateBlock(block.blockId, { [key]: value === '' && key !== 'title' ? null : value });
  }
  estimate(block: TripBlock, value: string, currency: TripCurrency): void {
    this.state.updateBlock(block.blockId, { estimate: value === '' ? null : { amount: value, currency } });
  }
  addBlock(date: string): void {
    this.state.addBlock(date);
    const last = this.state
      .buffer()
      ?.days.find((x) => x.date === date)
      ?.blocks.at(-1);
    if (last) this.focus('block-' + last.blockId);
  }
  removeBlock(blockId: string, date: string): void {
    this.state.removeBlock(blockId);
    this.focus('add-' + date);
  }
  reorder(blockId: string, date: string, index: number): void {
    this.state.moveBlock(blockId, date, index);
    this.focus('block-' + blockId);
  }
  move(blockId: string, date: string): void {
    this.state.moveBlock(blockId, date, 8);
    this.focus('block-' + blockId);
  }
  async newPlan(): Promise<void> {
    if (!this.canLeave()) return;
    if (this.route.snapshot.paramMap.has('id')) await this.router.navigate(['/trips']);
    else this.state.newPlan();
  }
  async deletePlan(): Promise<void> {
    await this.state.deletePlan();
    this.deleteConfirmation.set(false);
    if (this.state.selectedId() === null) await this.router.navigate(['/trips']);
  }
  resetBriefDraft(): void {
    const brief = this.state.buffer()?.brief ?? null;
    this.briefSource = brief;
    this.briefDraft.set(brief ? structuredClone(brief) : null);
    this.state.touch(false);
    this.errorField.set(null);
  }
  useCurrentServer(): void {
    this.state.useCurrentServer();
    this.resetBriefDraft();
  }
  private validationTarget(): string {
    const d = this.state.buffer();
    if (!d) return 'trip-title';
    try {
      tripText(d.title, 100);
    } catch {
      return 'trip-title';
    }
    if (!Number.isInteger(d.brief.adults) || d.brief.adults < 1 || d.brief.adults > 9) return 'trip-adults';
    try {
      if (d.brief.totalBudget !== null) tripMoney(d.brief.totalBudget);
    } catch {
      return 'trip-budget';
    }
    for (const day of d.days)
      for (const b of day.blocks) {
        try {
          tripText(b.title, 100);
        } catch {
          return 'block-' + b.blockId;
        }
        try {
          if (b.note !== null) tripText(b.note, 500, true);
        } catch {
          return 'note-' + b.blockId;
        }
        if (
          (b.startTime === null) !== (b.endTime === null) ||
          (b.startTime !== null &&
            (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(b.startTime) ||
              !/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(b.endTime!) ||
              b.endTime! <= b.startTime))
        )
          return 'start-' + b.blockId;
        try {
          if (b.estimate !== null) tripMoney(b.estimate.amount);
        } catch {
          return 'estimate-' + b.blockId;
        }
      }
    return 'trip-title';
  }
  confirmDelete(): void {
    this.deleteConfirmation.set(true);
    this.focus('confirm-delete');
  }
  cancelDelete(): void {
    this.deleteConfirmation.set(false);
    this.focus('delete-plan');
  }
  private focus(id: string): void {
    afterNextRender(() => this.document.getElementById(id)?.focus(), { injector: this.injector });
  }
}
