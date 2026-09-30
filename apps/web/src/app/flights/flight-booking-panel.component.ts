import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  output,
  signal,
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
import {
  type ConfirmedFlightOrderResponse,
  FlightBookingContractError,
  type FlightQuoteResponse,
  FlightsBookingApiService,
  type HeldFlightOrderResponse,
  type HoldFlightOrderRequest,
} from '@travel/api-client';
import { firstValueFrom, TimeoutError } from 'rxjs';
import { FlightOfferComponent } from './flight-offer.component';
import { type BookableOfferView, formatFlightPrice, toFlightOfferView } from './flight-results';
import { localToday } from './flight-search-form';
import { FlightsAuthService } from './flights-auth.service';

type HoldState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked' | 'authRequired';
type ConfirmState = 'idle' | 'pending' | 'unknown' | 'conflict' | 'error' | 'expired' | 'blocked';
type PassengerField = 'givenName' | 'familyName' | 'dateOfBirth' | 'gender' | 'email' | 'phone';

type HoldAttempt = { body: HoldFlightOrderRequest; idempotencyKey: string; mayHaveSucceeded: boolean };
type ConfirmAttempt = { body: { aggregateId: string }; idempotencyKey: string; mayHaveSucceeded: boolean };

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
  readonly quoteRefreshRequested = output<void>();
  readonly restartSearchRequested = output<void>();
  readonly confirmed = output<ConfirmedFlightOrderResponse>();
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

  private readonly api = inject(FlightsBookingApiService);
  private readonly auth = inject(FlightsAuthService);
  readonly usesTestWallet = computed(() => this.isDemo() || this.auth.isTestEnvironment());
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private holdAttempt: HoldAttempt | null = null;
  private confirmAttempt: ConfirmAttempt | null = null;

  constructor() {
    this.passengerForm.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.serverFieldErrors.set({}));
    this.destroyRef.onDestroy(() => {
      this.passengerForm.reset();
      this.holdAttempt = null;
      this.confirmAttempt = null;
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
    this.holdAttempt = { body, idempotencyKey: crypto.randomUUID(), mayHaveSucceeded: false };
    void this.executeHold(this.holdAttempt);
  }

  retryHold(): void {
    if (this.holdAttempt === null || (this.holdState() !== 'unknown' && this.holdState() !== 'conflict')) return;
    void this.executeHold(this.holdAttempt);
  }

  confirm(): void {
    if (
      (this.confirmState() !== 'idle' && this.confirmState() !== 'error') ||
      this.confirmedOrder() !== null ||
      this.heldOrder() === null
    )
      return;
    this.confirmAttempt = {
      body: { aggregateId: this.quote().aggregateId },
      idempotencyKey: crypto.randomUUID(),
      mayHaveSucceeded: false,
    };
    void this.executeConfirm(this.confirmAttempt);
  }

  retryConfirm(): void {
    if (this.confirmAttempt === null || (this.confirmState() !== 'unknown' && this.confirmState() !== 'conflict'))
      return;
    void this.executeConfirm(this.confirmAttempt);
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

  private async executeHold(attempt: HoldAttempt): Promise<void> {
    this.holdState.set('pending');
    this.holdMessage.set(null);
    let requestSent = false;
    try {
      const accessToken = this.isDemo() ? null : await this.auth.accessToken();
      requestSent = true;
      attempt.mayHaveSucceeded = true;
      const held = await firstValueFrom(this.api.hold(attempt.body, attempt.idempotencyKey, accessToken));
      this.holdAttempt = null;
      this.heldOrder.set(held);
      this.holdState.set('idle');
      this.passengerForm.reset();
      this.submitted.set(false);
    } catch (error) {
      this.handleHoldError(error, requestSent, attempt);
    }
  }

  private handleHoldError(error: unknown, requestSent: boolean, attempt: HoldAttempt): void {
    if (!requestSent && !attempt.mayHaveSucceeded) {
      this.holdAttempt = null;
      this.holdState.set('authRequired');
      this.holdMessage.set(
        'Сеанс входа истёк до отправки. Вернитесь к проверке цены, войдите снова и введите данные заново.',
      );
      this.passengerForm.reset();
      return;
    }
    if (!requestSent && attempt.mayHaveSucceeded) {
      this.holdState.set('unknown');
      this.holdMessage.set(
        'Не удалось обновить токен; предыдущее удержание могло пройти. Не обновляйте страницу. Повторите здесь тот же запрос после восстановления связи; если сеанс окончательно истёк, потребуется ручная проверка заказа.',
      );
      return;
    }
    if (error instanceof HttpErrorResponse) {
      const code = this.problemCode(error);
      if (code === 'flights.offerexpired') {
        this.holdAttempt = null;
        this.holdState.set('expired');
        this.holdMessage.set('Срок предложения истёк. Обновите цену перед новым оформлением.');
        this.passengerForm.reset();
        return;
      }
      if (error.status === 409) {
        if (code === 'flights.idempotencyinflight') {
          this.holdState.set('conflict');
          this.holdMessage.set('Запрос ещё обрабатывается. Подождите и повторите то же действие.');
        } else {
          this.holdState.set('blocked');
          this.holdMessage.set(
            code === 'flights.idempotencyconflict'
              ? 'Ключ запроса уже связан с другим содержимым. Повторять его бесполезно; требуется ручная проверка.'
              : 'Состояние заказа требует проверки. Не создавайте новое удержание и не меняйте ключ.',
          );
        }
        return;
      }
    }
    if (this.isUnknownOutcome(error)) {
      this.holdState.set('unknown');
      this.holdMessage.set('Исход удержания неизвестен. Повторите запрос с тем же ключом и теми же данными.');
      return;
    }

    this.holdAttempt = null;
    this.holdState.set('error');
    this.serverFieldErrors.set(this.mapPassengerErrors(error));
    this.holdMessage.set('Не удалось удержать предложение. Проверьте данные пассажира и попробуйте ещё раз.');
  }

  private async executeConfirm(attempt: ConfirmAttempt): Promise<void> {
    this.confirmState.set('pending');
    this.confirmMessage.set(null);
    let requestSent = false;
    try {
      const accessToken = this.isDemo() ? null : await this.auth.accessToken();
      requestSent = true;
      attempt.mayHaveSucceeded = true;
      const confirmed = await firstValueFrom(this.api.confirm(attempt.body, attempt.idempotencyKey, accessToken));
      this.confirmAttempt = null;
      this.confirmedOrder.set(confirmed);
      this.confirmState.set('idle');
      this.confirmed.emit(confirmed);
    } catch (error) {
      this.handleConfirmError(error, requestSent, attempt);
    }
  }

  private handleConfirmError(error: unknown, requestSent: boolean, attempt: ConfirmAttempt): void {
    if (!requestSent && !attempt.mayHaveSucceeded) {
      this.confirmAttempt = null;
      this.confirmState.set('blocked');
      this.confirmMessage.set(
        'Сеанс входа истёк до подтверждения. Удержание уже существует; нужна ручная проверка заказа перед новым действием.',
      );
      return;
    }
    if (!requestSent && attempt.mayHaveSucceeded) {
      this.confirmState.set('unknown');
      this.confirmMessage.set(
        'Не удалось обновить токен; подтверждение могло пройти. Не обновляйте страницу. Повторите здесь тот же запрос после восстановления связи; если сеанс окончательно истёк, потребуется ручная проверка заказа.',
      );
      return;
    }
    if (error instanceof HttpErrorResponse) {
      const code = this.problemCode(error);
      if (code === 'flights.holdexpired') {
        this.confirmAttempt = null;
        this.confirmState.set('expired');
        this.confirmMessage.set('Срок удержания истёк. Заказ не подтверждён. Начните оформление заново с поиска.');
        return;
      }
      if (error.status === 409) {
        if (code === 'flights.idempotencyinflight') {
          this.confirmState.set('conflict');
          this.confirmMessage.set('Подтверждение ещё обрабатывается. Подождите и повторите то же действие.');
        } else {
          this.confirmState.set('blocked');
          this.confirmMessage.set(
            code === 'flights.idempotencyconflict'
              ? 'Ключ подтверждения связан с другим содержимым. Повторять его бесполезно; требуется ручная проверка.'
              : 'Состояние заказа требует проверки. Не создавайте новое подтверждение и не меняйте ключ.',
          );
        }
        return;
      }
    }
    if (this.isUnknownOutcome(error)) {
      this.confirmState.set('unknown');
      this.confirmMessage.set('Исход подтверждения неизвестен. Повторите запрос с тем же ключом и содержимым.');
      return;
    }

    this.confirmAttempt = null;
    this.confirmState.set('error');
    this.confirmMessage.set('Не удалось подтвердить заказ. Проверьте удержание и попробуйте ещё раз.');
  }

  private isUnknownOutcome(error: unknown): boolean {
    return (
      error instanceof TimeoutError ||
      error instanceof FlightBookingContractError ||
      (error instanceof HttpErrorResponse && (error.status === 0 || error.status === 200 || error.status >= 500))
    );
  }

  private problemCode(error: HttpErrorResponse): string {
    const type = error.error?.type;
    const prefix = 'https://travel.local/errors/';
    return typeof type === 'string' && type.startsWith(prefix) ? type.slice(prefix.length).toLowerCase() : '';
  }

  private mapPassengerErrors(error: unknown): Partial<Record<PassengerField, string>> {
    const code = error instanceof HttpErrorResponse ? this.problemCode(error) : '';
    const detail =
      error instanceof HttpErrorResponse && typeof error.error?.detail === 'string'
        ? error.error.detail.toLowerCase()
        : '';
    const text = `${code} ${detail}`;
    const errors: Partial<Record<PassengerField, string>> = {};
    if (text.includes('givenname')) errors.givenName = 'Проверьте имя пассажира.';
    if (text.includes('familyname')) errors.familyName = 'Проверьте фамилию пассажира.';
    if (text.includes('dateofbirth')) errors.dateOfBirth = 'Проверьте дату рождения.';
    if (text.includes('gender')) errors.gender = 'Выберите допустимое значение.';
    if (text.includes('email')) errors.email = 'Проверьте email.';
    if (text.includes('phone')) errors.phone = 'Проверьте телефон в формате E.164.';
    return errors;
  }

  private focusFirstInvalidField(): void {
    this.element.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
  }
}
