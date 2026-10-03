import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import type {
  ConfirmedFlightOrderResponse,
  FlightQuoteResponse,
  HeldFlightOrderResponse,
  HoldFlightOrderRequest,
  SavedTraveler,
} from '@travel/api-client';
import { isTravelerGuid } from '@travel/api-client';
import { FlightOfferComponent } from './flight-offer.component';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import {
  createPassengerForm,
  PASSENGER_FIELD_COPY,
  type PassengerField,
  type PassengerForm,
} from './flight-passenger-form';
import { type BookableOfferView, formatFlightPrice, toFlightOfferView } from './flight-results';
import { localToday } from './flight-search-form';
import { FlightsAuthService } from './flights-auth.service';
import { canonicalTravelerDetails, createSavedTravelerForm } from './saved-traveler-form';
import { SavedTravelersState } from './saved-travelers-state.service';

type HoldState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked' | 'authRequired';
type ConfirmState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked';
@Component({
  selector: 'app-flight-booking-panel',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, FlightOfferComponent],
  providers: [SavedTravelersState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-booking-panel.component.html',
  styleUrl: './flight-booking-panel.component.scss',
})
export class FlightsBookingPanelComponent {
  readonly profileReview = signal<SavedTraveler | null>(null);
  readonly profileReviewReady = signal(false);
  readonly profileReviewPending = signal(false);
  private profileReviewGeneration = 0;
  async reviewProfileMutation(): Promise<void> {
    const id = this.profiles.mutationId();
    const session = this.profiles.session();
    if (id === null || session === null || this.profileReviewPending() || this.profileActionsLocked()) return;
    const generation = ++this.profileReviewGeneration;
    this.profileReviewPending.set(true);
    this.profileReview.set(null);
    this.profileReviewReady.set(false);
    try {
      const current = await this.profiles.read(id);
      if (!this.profiles.current(session) || generation !== this.profileReviewGeneration || this.profileActionsLocked())
        return;
      this.profileReview.set(current);
      this.profileReviewReady.set(current !== null || this.profiles.loadState() === 'missing');
    } finally {
      if (generation === this.profileReviewGeneration) this.profileReviewPending.set(false);
    }
  }
  acceptProfileReview(): void {
    if (this.profileReviewReady() && !this.profileActionsLocked()) {
      this.profiles.acceptReview();
      this.profileReviewReady.set(false);
      this.profileReview.set(null);
    }
  }
  readonly profiles = inject(SavedTravelersState);
  readonly selectedTravelers = signal<Record<string, string>>({});
  readonly fillingTravelers = signal<Record<string, boolean>>({});
  private readonly rowEdits = new WeakMap<PassengerForm, number>();
  private readonly selections = new Map<string, number>();
  profileActionsLocked(): boolean {
    return (
      this.auth.status().kind !== 'authenticated' ||
      this.profiles.accessDenied() ||
      this.quoteReviewPending() ||
      this.heldOrder() !== null ||
      this.confirmedOrder() !== null ||
      !['idle', 'error'].includes(this.holdState()) ||
      this.operations.blocksBookingInSession()
    );
  }
  loadTravelers(): void {
    if (!this.profileActionsLocked()) void this.profiles.load(0);
  }
  moreTravelers(): void {
    if (!this.profileActionsLocked() && this.profiles.hasMore()) void this.profiles.load(this.profiles.offset() + 20);
  }
  selectTraveler(row: PassengerForm, id: string): void {
    if (this.profileActionsLocked() || !this.passengers.controls.includes(row)) return;
    const slot = row.controls.bookingPassengerId.value;
    this.selections.set(slot, (this.selections.get(slot) ?? 0) + 1);
    this.selectedTravelers.update((selected) => ({ ...selected, [slot]: isTravelerGuid(id) ? id.toLowerCase() : '' }));
    this.fillingTravelers.update((filling) => ({ ...filling, [slot]: false }));
  }
  async fillTraveler(row: PassengerForm): Promise<void> {
    const slot = row.controls.bookingPassengerId.value;
    const id = this.selectedTravelers()[slot];
    const session = this.profiles.session();
    if (this.profileActionsLocked() || session === null || !id || !this.passengers.controls.includes(row)) return;
    const selection = (this.selections.get(slot) ?? 0) + 1;
    this.selections.set(slot, selection);
    const quoteRevision = this.quote().binding.revision;
    const edits = this.rowEdits.get(row) ?? 0;
    this.fillingTravelers.update((filling) => ({ ...filling, [slot]: true }));
    const profile = await this.profiles.read(id);
    if (!this.profiles.current(session) || this.selections.get(slot) !== selection) return;
    this.fillingTravelers.update((filling) => ({ ...filling, [slot]: false }));
    if (
      profile === null ||
      profile.id !== id ||
      this.profileActionsLocked() ||
      this.quote().binding.revision !== quoteRevision ||
      !this.passengers.controls.includes(row) ||
      row.controls.bookingPassengerId.value !== slot ||
      this.selectedTravelers()[slot] !== id ||
      (this.rowEdits.get(row) ?? 0) !== edits
    )
      return;
    row.patchValue({ ...profile.details });
    row.controls.dateOfBirth.updateValueAndValidity({ emitEvent: false });
    this.submitted.set(true);
  }
  async saveTravelerRow(row: PassengerForm): Promise<void> {
    if (this.profileActionsLocked() || !this.profiles.canMutate() || !this.passengers.controls.includes(row)) return;
    this.submitted.set(true);
    row.markAllAsTouched();
    row.controls.dateOfBirth.updateValueAndValidity({ emitEvent: false });
    const { bookingPassengerId: _slot, ...details } = row.getRawValue();
    void _slot;
    const profileForm = createSavedTravelerForm();
    profileForm.setValue(details);
    if (profileForm.invalid) {
      profileForm.reset();
      this.focusFirstInvalidField();
      return;
    }
    profileForm.reset();
    const session = this.profiles.session();
    const edits = this.rowEdits.get(row) ?? 0;
    const quoteRevision = this.quote().binding.revision;
    await this.profiles.create(canonicalTravelerDetails(details));
    if (
      session !== null &&
      this.profiles.current(session) &&
      this.passengers.controls.includes(row) &&
      this.quote().binding.revision === quoteRevision &&
      (this.rowEdits.get(row) ?? 0) === edits &&
      this.profiles.mutationState() === 'validation'
    ) {
      const errors = Object.fromEntries(
        this.profiles.fieldErrors().map((error) => [error.field, PASSENGER_FIELD_COPY[error.field]]),
      );
      this.serverFieldErrors.update((current) => ({ ...current, [row.controls.bookingPassengerId.value]: errors }));
      this.focusFirstInvalidField(true);
    }
  }
  readonly quote = input.required<FlightQuoteResponse>();
  readonly isDemo = input.required<boolean>();
  readonly quoteAccepted = input(false);
  readonly quoteReviewPending = input(false);
  readonly quoteRefreshRequested = output<void>();
  readonly restartSearchRequested = output<void>();
  readonly confirmed = output<ConfirmedFlightOrderResponse>();
  readonly held = output<HeldFlightOrderResponse>();
  readonly offerView = computed(() => toFlightOfferView(this.quote().offer) as BookableOfferView);
  readonly today = localToday();

  readonly passengers = new FormArray<PassengerForm>([]);
  readonly partyForm = new FormGroup({ passengers: this.passengers });
  private bindingIds = '';
  private quoteRevision: string | null = null;
  private resetDraft(): void {
    this.profileReviewGeneration++;
    this.profileReview.set(null);
    this.profileReviewReady.set(false);
    this.profileReviewPending.set(false);
    this.selectedTravelers.set({});
    this.fillingTravelers.set({});
    this.selections.clear();
    for (const row of this.passengers.controls)
      row.reset({ bookingPassengerId: row.controls.bookingPassengerId.value });
  }
  private syncBinding(): void {
    const ids = this.quote().binding.slots.map((slot) => slot.bookingPassengerId.toLowerCase());
    const key = [...ids].sort().join(',');
    if (key !== this.bindingIds) {
      this.resetDraft();
      this.passengers.clear();
      this.bindingIds = key;
      for (const id of ids) {
        const row = createPassengerForm(id, () => this.quote().binding.firstDepartureLocalDate);
        this.rowEdits.set(row, 0);
        row.valueChanges
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe(() => this.rowEdits.set(row, (this.rowEdits.get(row) ?? 0) + 1));
        this.passengers.push(row);
      }
      this.serverFieldErrors.set({});
      this.submitted.set(false);
    }
    for (const row of this.passengers.controls) row.controls.dateOfBirth.updateValueAndValidity({ emitEvent: false });
  }
  fieldId(row: PassengerForm, field: PassengerField): string {
    return `passenger-${row.controls.bookingPassengerId.value}-${field}`;
  }
  readonly submitted = signal(false);
  readonly holdState = signal<HoldState>('idle');
  readonly confirmState = signal<ConfirmState>('idle');
  readonly heldOrder = signal<HeldFlightOrderResponse | null>(null);
  readonly confirmedOrder = signal<ConfirmedFlightOrderResponse | null>(null);
  readonly holdMessage = signal<string | null>(null);
  readonly confirmMessage = signal<string | null>(null);
  readonly serverFieldErrors = signal<Record<string, Partial<Record<PassengerField, string>>>>({});

  private readonly auth = inject(FlightsAuthService);
  readonly usesTestWallet = computed(() => this.isDemo() || this.auth.isTestEnvironment());
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly operations = inject(FlightOrderOperationsService);
  private emittedConfirmation = false;
  private emittedHold = false;
  private identity: string | null = null;

  constructor() {
    effect(() => {
      const auth = this.auth.status();
      const profileAccessDenied = this.profiles.accessDenied();
      const identity = auth.kind === 'authenticated' ? `${auth.userId}:${this.auth.identityEpoch?.() ?? 0}` : null;
      const quote = this.quote();
      const hold = auth.kind === 'authenticated' ? this.operations.holdOperation(auth.userId) : null;
      untracked(() => {
        this.syncBinding();
        if (profileAccessDenied) this.resetDraft();
        if (quote.binding.revision !== this.quoteRevision) {
          this.quoteRevision = quote.binding.revision;
          if (this.heldOrder() === null && !this.operations.blocksBookingInSession()) {
            this.holdState.set(auth.kind === 'authenticated' ? 'idle' : 'authRequired');
            this.holdMessage.set(null);
            this.serverFieldErrors.set({});
          }
        }
        if (identity !== this.identity) {
          this.identity = identity;
          this.resetDraft();
          this.heldOrder.set(null);
          this.confirmedOrder.set(null);
          this.holdState.set(identity === null ? 'authRequired' : 'idle');
          this.emittedHold = false;
          this.emittedConfirmation = false;
        }
        if (
          hold === null ||
          (hold.state === 'rejected' && hold.quoteRevision !== quote.binding.revision.toLowerCase()) ||
          (hold.aggregateId !== quote.aggregateId &&
            hold.state !== 'pending' &&
            hold.state !== 'unknown' &&
            hold.state !== 'conflict')
        )
          return;
        this.holdMessage.set(hold.message || null);
        if (hold.state === 'success' && hold.result !== null && hold.aggregateId === quote.aggregateId) {
          this.heldOrder.set(hold.result);
          this.holdState.set('idle');
          this.resetDraft();
          this.submitted.set(false);
          if (!this.emittedHold) {
            this.emittedHold = true;
            this.held.emit(hold.result);
          }
        } else if (hold.state === 'rejected') {
          this.holdState.set(
            hold.errorCode === 'flights.offerexpired' ? 'expired' : hold.errorCode === null ? 'authRequired' : 'error',
          );
          const errors: Record<string, Partial<Record<PassengerField, string>>> = {};
          for (const error of hold.passengerErrors)
            errors[error.bookingPassengerId] = {
              ...errors[error.bookingPassengerId],
              [error.field]: PASSENGER_FIELD_COPY[error.field],
            };
          this.serverFieldErrors.set(errors);
          if (hold.passengerErrors.length) setTimeout(() => this.focusFirstInvalidField(true), 0);
          if (this.holdState() !== 'error') this.resetDraft();
        } else {
          this.holdState.set(
            hold.errorCode === 'flights.idempotencyconflict' || hold.errorCode === 'flights.invalidstate'
              ? 'blocked'
              : hold.state === 'conflict'
                ? 'unknown'
                : hold.state === 'success'
                  ? 'idle'
                  : hold.state,
          );
        }
      });
    });
    effect(() => {
      const auth = this.auth.status();
      const quote = this.quote();
      const operation =
        auth.kind === 'authenticated' ? this.operations.operation(quote.aggregateId, auth.userId) : null;
      if (operation?.kind !== 'confirm') return;
      untracked(() => {
        this.confirmMessage.set(operation.message || null);
        this.confirmState.set(
          operation.state === 'success'
            ? 'idle'
            : operation.state === 'rejected'
              ? operation.message.includes('удержания истёк')
                ? 'expired'
                : 'error'
              : operation.state === 'conflict' && !operation.retryable
                ? 'blocked'
                : operation.state,
        );
        if (operation.state === 'success' && operation.result !== null && !this.emittedConfirmation) {
          this.emittedConfirmation = true;
          const confirmed = operation.result as ConfirmedFlightOrderResponse;
          this.confirmedOrder.set(confirmed);
          this.confirmed.emit(confirmed);
        }
      });
    });
    this.partyForm.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.serverFieldErrors.set({}));
    this.destroyRef.onDestroy(() => {
      this.resetDraft();
    });
  }

  fieldError(field: PassengerField, row: PassengerForm): string | null {
    const serverError = this.serverFieldErrors()[row.controls.bookingPassengerId.value.toLowerCase()]?.[field];
    if (serverError) return serverError;
    if (!this.submitted()) return null;
    const control = row.controls[field];
    if (control.hasError('future')) return 'Дата рождения не может быть в будущем.';
    if (control.invalid) return PASSENGER_FIELD_COPY[field];
    return null;
  }

  hold(): void {
    if (
      !this.quoteAccepted() ||
      this.quoteReviewPending() ||
      this.auth.status().kind !== 'authenticated' ||
      this.holdState() === 'pending' ||
      this.holdState() === 'unknown' ||
      this.holdState() === 'conflict' ||
      this.holdState() === 'expired' ||
      this.holdState() === 'blocked' ||
      this.holdState() === 'authRequired' ||
      this.heldOrder() !== null
    )
      return;
    if (Date.parse(this.quote().offer.expiresAt) <= Date.now()) {
      this.holdState.set('expired');
      this.holdMessage.set('Предложение истекло. Обновите цену перед удержанием.');
      this.resetDraft();
      return;
    }
    this.submitted.set(true);
    this.partyForm.markAllAsTouched();
    for (const row of this.passengers.controls) row.controls.dateOfBirth.updateValueAndValidity({ emitEvent: false });
    if (this.partyForm.invalid) {
      this.focusFirstInvalidField();
      return;
    }

    const body: HoldFlightOrderRequest = {
      aggregateId: this.quote().aggregateId,
      quoteRevision: this.quote().binding.revision,
      passengers: this.quote().binding.slots.map((slot) => {
        const row = this.passengers.controls.find(
          (candidate) =>
            candidate.controls.bookingPassengerId.value.toLowerCase() === slot.bookingPassengerId.toLowerCase(),
        );
        if (!row) throw new Error('Missing local passenger slot');
        const value = row.getRawValue();
        return {
          bookingPassengerId: slot.bookingPassengerId,
          title: value.title as 'mr' | 'ms' | 'mrs' | 'miss' | 'dr',
          givenName: value.givenName.trim(),
          familyName: value.familyName.trim(),
          dateOfBirth: value.dateOfBirth,
          gender: value.gender as 'male' | 'female',
          email: value.email.trim(),
          phone: value.phone.trim(),
        };
      }),
    };
    const auth = this.auth.status();
    if (
      auth.kind === 'authenticated' &&
      this.operations.startHold(body, this.quote(), this.quoteAccepted(), auth.userId, this.isDemo())
    ) {
      this.holdState.set('pending');
      this.holdMessage.set(null);
      this.resetDraft();
    }
  }

  retryHold(): void {
    // An unknown supplier write cannot safely be repeated, including with its original key.
  }

  confirm(): void {
    const auth = this.auth.status();
    if (auth.kind !== 'authenticated' || this.heldOrder() === null || this.confirmedOrder() !== null) return;
    if (this.operations.startConfirm(this.quote().aggregateId, auth.userId)) this.confirmState.set('pending');
  }

  retryConfirm(): void {
    const auth = this.auth.status();
    if (auth.kind === 'authenticated' && this.operations.retry(this.quote().aggregateId, auth.userId))
      this.confirmState.set('pending');
  }

  formatDeadline(value: string): string {
    return new Intl.DateTimeFormat('ru-RU', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      timeZoneName: 'short',
    }).format(new Date(value));
  }

  formatPrice(amount: number, currency: string): string {
    return formatFlightPrice(amount, currency);
  }

  private focusFirstInvalidField(preferServer = false): void {
    if (preferServer) {
      for (const row of this.passengers.controls) {
        const field = Object.keys(this.serverFieldErrors()[row.controls.bookingPassengerId.value] ?? {})[0] as
          | PassengerField
          | undefined;
        if (field) {
          this.element.nativeElement.querySelector<HTMLElement>(`#${this.fieldId(row, field)}`)?.focus();
          return;
        }
      }
    }
    for (const row of this.passengers.controls) {
      for (const field of ['title', 'givenName', 'familyName', 'dateOfBirth', 'gender', 'email', 'phone'] as const) {
        if (row.controls[field].invalid || this.serverFieldErrors()[row.controls.bookingPassengerId.value]?.[field]) {
          this.element.nativeElement.querySelector<HTMLElement>(`#${this.fieldId(row, field)}`)?.focus();
          return;
        }
      }
    }
  }
}
