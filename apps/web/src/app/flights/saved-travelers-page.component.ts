import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import type { DemoTravelerOwner, SavedTraveler, SavedTravelerField, SavedTravelerReceipt } from '@travel/api-client';
import { PASSENGER_FIELD_COPY } from './flight-passenger-form';
import { FlightsAuthService } from './flights-auth.service';
import { canonicalTravelerDetails, createSavedTravelerForm, TRAVELER_FIELDS } from './saved-traveler-form';
import { SavedTravelersState } from './saved-travelers-state.service';

interface DemoOwnerSwitch {
  switchDemoOwner(owner: DemoTravelerOwner): void;
}
@Component({
  selector: 'app-saved-travelers-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  providers: [SavedTravelersState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './saved-travelers-page.component.html',
  styleUrl: './saved-travelers-page.component.scss',
})
export class SavedTravelersPageComponent {
  readonly profiles = inject(SavedTravelersState);
  readonly auth = inject(FlightsAuthService);
  readonly form = createSavedTravelerForm();
  readonly fields = TRAVELER_FIELDS;
  readonly today = new Date().toISOString().slice(0, 10);
  readonly editor = signal<'closed' | 'create' | 'edit'>('closed');
  readonly submitted = signal(false);
  readonly deletion = signal<SavedTravelerReceipt | null>(null);
  readonly reviewRequired = signal(false);
  readonly reviewPending = signal(false);
  readonly labels: Record<SavedTravelerField, string> = {
    title: 'Обращение',
    givenName: 'Имя',
    familyName: 'Фамилия',
    dateOfBirth: 'Дата рождения',
    gender: 'Пол',
    email: 'Email',
    phone: 'Телефон',
  };
  private editing: SavedTravelerReceipt | null = null;
  private generation = 0;
  private identity: string | null = null;
  private privacyGeneration = -1;
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  get demoOwnerSwitch(): DemoOwnerSwitch | null {
    const auth = this.auth as FlightsAuthService & Partial<DemoOwnerSwitch>;
    return this.auth.isDemo && typeof auth.switchDemoOwner === 'function'
      ? (auth as FlightsAuthService & DemoOwnerSwitch)
      : null;
  }
  constructor() {
    effect(() => {
      const auth = this.auth.status();
      const epoch = this.auth.identityEpoch?.() ?? 0;
      const privacyGeneration = this.profiles.privacyGeneration();
      const identity = auth.kind === 'authenticated' ? `${auth.userId.toLowerCase()}:${epoch}` : null;
      untracked(() => {
        if (identity === this.identity && privacyGeneration === this.privacyGeneration) return;
        const identityChanged = identity !== this.identity;
        this.privacyGeneration = privacyGeneration;
        this.identity = identity;
        this.clearEditor();
        this.deletion.set(null);
        if (identityChanged && identity !== null && !this.profiles.accessDenied()) void this.profiles.load(0);
      });
    });
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event instanceof NavigationStart) this.clearEditor();
      });
    inject(DestroyRef).onDestroy(() => this.clearEditor());
    if (!this.auth.isDemo && this.auth.hasOidcCallback()) void this.auth.initializeFromCallback();
  }
  async login(): Promise<void> {
    if (await this.auth.beginLogin('/flights/travelers')) {
      this.profiles.reauthorize();
      void this.profiles.load(0);
    }
  }
  switchDemoOwner(value: string): void {
    if (value !== 'demo-only' && value !== 'demo-other') return;
    this.clearEditor();
    this.demoOwnerSwitch?.switchDemoOwner(value);
  }
  demoOwner(): string {
    const auth = this.auth.status();
    return auth.kind === 'authenticated' && auth.userId === 'demo-other' ? 'demo-other' : 'demo-only';
  }
  refresh(): void {
    if (this.profiles.mutationState() !== 'pending') void this.profiles.load(0);
  }
  loadMore(): void {
    if (this.profiles.hasMore()) void this.profiles.load(this.profiles.offset() + 20);
  }
  newProfile(): void {
    if (!this.profiles.canMutate()) return;
    this.clearEditor();
    this.editor.set('create');
  }
  private clearEditor(): void {
    this.generation++;
    this.form.reset();
    this.editing = null;
    this.editor.set('closed');
    this.submitted.set(false);
    this.reviewRequired.set(false);
    this.reviewPending.set(false);
  }
  cancel(): void {
    if (this.profiles.mutationState() !== 'pending') this.clearEditor();
  }
  async edit(profile: SavedTraveler): Promise<void> {
    if (!this.profiles.canMutate()) return;
    this.clearEditor();
    const generation = this.generation;
    const session = this.profiles.session();
    const current = await this.profiles.read(profile.id);
    if (session === null || !this.profiles.current(session) || generation !== this.generation || current === null)
      return;
    this.editing = { id: current.id, revision: current.revision };
    this.form.setValue(current.details);
    this.editor.set('edit');
  }
  fieldError(field: SavedTravelerField): string | null {
    if (this.profiles.fieldErrors().some((error) => error.field === field)) return this.fieldCopy(field);
    if (!this.submitted() || this.form.controls[field].valid) return null;
    return this.fieldCopy(field);
  }
  private fieldCopy(field: SavedTravelerField): string {
    return field === 'dateOfBirth'
      ? 'Введите существующую дату рождения, не позднее сегодняшнего дня (UTC).'
      : PASSENGER_FIELD_COPY[field];
  }
  async save(): Promise<void> {
    if (this.editor() === 'closed' || this.reviewRequired() || !this.profiles.canMutate()) return;
    this.submitted.set(true);
    this.form.markAllAsTouched();
    this.form.controls.dateOfBirth.updateValueAndValidity();
    if (this.form.invalid) {
      this.focusInvalid();
      return;
    }
    const generation = this.generation;
    const session = this.profiles.session();
    const details = canonicalTravelerDetails(this.form.getRawValue());
    const editing = this.editing;
    if (this.editor() === 'edit' && editing === null) return;
    const receipt =
      this.editor() === 'create'
        ? await this.profiles.create(details)
        : editing === null
          ? null
          : await this.profiles.update(editing.id, editing.revision, details);
    if (session === null || !this.profiles.current(session) || generation !== this.generation) return;
    if (receipt !== null) {
      this.clearEditor();
      void this.profiles.load(0);
    } else if (this.profiles.mutationState() === 'validation') this.focusInvalid();
  }
  askDelete(profile: SavedTraveler): void {
    if (this.profiles.canMutate()) this.deletion.set({ id: profile.id, revision: profile.revision });
  }
  cancelDelete(): void {
    if (this.profiles.mutationState() !== 'pending') this.deletion.set(null);
  }
  async confirmDelete(): Promise<void> {
    const deletion = this.deletion();
    if (deletion === null || !this.profiles.canMutate()) return;
    const session = this.profiles.session();
    const result = await this.profiles.delete(deletion.id, deletion.revision);
    if (session === null || !this.profiles.current(session)) return;
    this.deletion.set(null);
    if (result !== null) {
      this.clearEditor();
      void this.profiles.load(0);
    }
  }
  async review(): Promise<void> {
    const id = this.profiles.mutationId();
    if (id === null || this.profiles.mutationState() === 'pending' || this.reviewPending()) return;
    this.clearEditor();
    const generation = this.generation;
    const session = this.profiles.session();
    this.reviewPending.set(true);
    const current = await this.profiles.read(id);
    if (session === null || !this.profiles.current(session) || generation !== this.generation) return;
    this.reviewPending.set(false);
    if (current !== null) {
      this.editing = { id: current.id, revision: current.revision };
      this.form.setValue(current.details);
      this.editor.set('edit');
    }
    if (current !== null || this.profiles.loadState() === 'missing') this.reviewRequired.set(true);
  }
  acceptCurrent(): void {
    if (this.reviewRequired()) {
      this.profiles.acceptReview();
      this.reviewRequired.set(false);
    }
  }
  inputType(field: SavedTravelerField): string {
    return field === 'dateOfBirth' ? 'date' : field === 'email' ? 'email' : field === 'phone' ? 'tel' : 'text';
  }
  maxLength(field: SavedTravelerField): number | null {
    return field === 'givenName' || field === 'familyName'
      ? 20
      : field === 'email'
        ? 254
        : field === 'phone'
          ? 16
          : null;
  }
  private focusInvalid(): void {
    const field = this.fields.find(
      (field) =>
        this.form.controls[field].invalid || this.profiles.fieldErrors().some((error) => error.field === field),
    );
    if (field)
      setTimeout(() => this.element.nativeElement.querySelector<HTMLElement>(`#traveler-${field}`)?.focus(), 0);
  }
}
