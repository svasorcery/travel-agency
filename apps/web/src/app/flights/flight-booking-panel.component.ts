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
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import type {
  ConfirmedFlightOrderResponse,
  FlightQuoteResponse,
  HeldFlightOrderResponse,
  HoldFlightOrderRequest,
} from '@travel/api-client';
import { FlightOfferComponent } from './flight-offer.component';
import { FlightOrderOperationsService } from './flight-order-operations.service';
import { type BookableOfferView, formatFlightPrice, toFlightOfferView } from './flight-results';
import { localToday } from './flight-search-form';
import { FlightsAuthService } from './flights-auth.service';

type HoldState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked' | 'authRequired';
type ConfirmState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked';
type PassengerField = 'givenName' | 'familyName' | 'dateOfBirth' | 'gender' | 'email' | 'phone';

const PHONE_E164 = /^\+[1-9]\d{7,14}$/;

function trimmedRequired(control: AbstractControl<string>): ValidationErrors | null {
  return control.value.trim().length > 0 ? null : { required: true };
}

@Component({
  selector: 'app-flight-booking-panel',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, FlightOfferComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flight-booking-panel.component.html',
  styleUrl: './flight-booking-panel.component.scss',
})
export class FlightsBookingPanelComponent {
  readonly quote = input.required<FlightQuoteResponse>();
  readonly isDemo = input.required<boolean>();
  readonly quoteAccepted = input(false);
  readonly quoteRefreshRequested = output<void>();
  readonly restartSearchRequested = output<void>();
  readonly confirmed = output<ConfirmedFlightOrderResponse>();
  readonly held = output<HeldFlightOrderResponse>();
  readonly offerView = computed(() => toFlightOfferView(this.quote().offer) as BookableOfferView);
  readonly today = localToday();

  readonly passengerForm = new FormGroup({
    givenName: new FormControl('', { nonNullable: true, validators: [trimmedRequired] }),
    familyName: new FormControl('', { nonNullable: true, validators: [trimmedRequired] }),
    dateOfBirth: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    gender: new FormControl<'male' | 'female' | 'unspecified'>('unspecified', { nonNullable: true }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    phone: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(PHONE_E164)],
    }),
  });

  readonly submitted = signal(false);
  readonly holdState = signal<HoldState>('idle');
  readonly confirmState = signal<ConfirmState>('idle');
  readonly heldOrder = signal<HeldFlightOrderResponse | null>(null);
  readonly confirmedOrder = signal<ConfirmedFlightOrderResponse | null>(null);
  readonly holdMessage = signal<string | null>(null);
  readonly confirmMessage = signal<string | null>(null);
  readonly serverFieldErrors = signal<Partial<Record<PassengerField, string>>>({});

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
      const identity = auth.kind === 'authenticated' ? `${auth.userId}:${this.auth.identityEpoch?.() ?? 0}` : null;
      const quote = this.quote();
      const hold = auth.kind === 'authenticated' ? this.operations.holdOperation(auth.userId) : null;
      untracked(() => {
        if (identity !== this.identity) {
          this.identity = identity;
          this.passengerForm.reset();
          this.heldOrder.set(null);
          this.confirmedOrder.set(null);
          this.holdState.set(identity === null ? 'authRequired' : 'idle');
          this.emittedHold = false;
          this.emittedConfirmation = false;
        }
        if (
          hold === null ||
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
          this.passengerForm.reset();
          this.submitted.set(false);
          if (!this.emittedHold) {
            this.emittedHold = true;
            this.held.emit(hold.result);
          }
        } else if (hold.state === 'rejected') {
          this.holdState.set(
            hold.errorCode === 'flights.offerexpired' ? 'expired' : hold.errorCode === null ? 'authRequired' : 'error',
          );
          this.serverFieldErrors.set(this.mapPassengerErrors(hold.errorCode ?? ''));
          if (this.holdState() !== 'error') this.passengerForm.reset();
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
    this.passengerForm.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.serverFieldErrors.set({}));
    this.destroyRef.onDestroy(() => {
      this.passengerForm.reset();
    });
  }

  fieldError(field: PassengerField): string | null {
    const serverError = this.serverFieldErrors()[field];
    if (serverError) return serverError;
    if (!this.submitted()) return null;
    const control = this.passengerForm.controls[field];
    if (control.hasError('required')) return 'Это поле обязательно.';
    if (field === 'email' && control.hasError('email')) return 'Введите корректный email.';
    if (field === 'phone' && control.invalid) return 'Укажите телефон в формате E.164, например +79161234567.';
    if (field === 'dateOfBirth' && control.value > this.today) return 'Дата рождения не может быть в будущем.';
    return null;
  }

  hold(): void {
    if (
      !this.quoteAccepted() ||
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
      this.passengerForm.reset();
      return;
    }
    this.submitted.set(true);
    this.passengerForm.markAllAsTouched();
    if (this.passengerForm.invalid || this.passengerForm.controls.dateOfBirth.value > this.today) {
      this.focusFirstInvalidField();
      return;
    }

    const value = this.passengerForm.getRawValue();
    const body: HoldFlightOrderRequest = {
      aggregateId: this.quote().aggregateId,
      passengers: [
        {
          givenName: value.givenName.trim(),
          familyName: value.familyName.trim(),
          dateOfBirth: value.dateOfBirth,
          gender: value.gender,
          email: value.email.trim(),
          phone: value.phone.trim(),
        },
      ],
    };
    const auth = this.auth.status();
    if (
      auth.kind === 'authenticated' &&
      this.operations.startHold(body, this.quote(), this.quoteAccepted(), auth.userId, this.isDemo())
    ) {
      this.holdState.set('pending');
      this.holdMessage.set(null);
      this.passengerForm.reset();
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

  private mapPassengerErrors(code: string): Partial<Record<PassengerField, string>> {
    const errors: Partial<Record<PassengerField, string>> = {};
    if (code.includes('givenname')) errors.givenName = 'Проверьте имя пассажира.';
    if (code.includes('familyname')) errors.familyName = 'Проверьте фамилию пассажира.';
    if (code.includes('dateofbirth')) errors.dateOfBirth = 'Проверьте дату рождения.';
    if (code.includes('gender')) errors.gender = 'Выберите допустимое значение.';
    if (code.includes('email')) errors.email = 'Проверьте email.';
    if (code.includes('phone')) errors.phone = 'Проверьте телефон в формате E.164.';
    return errors;
  }

  private focusFirstInvalidField(): void {
    this.element.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }
}
