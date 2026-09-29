import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { FlightSearchContractError, type FlightSearchRequest, FlightsSearchApiService } from '@travel/api-client';
import { TravelButton } from '@travel/ui-kit';
import { catchError, map, of, Subject, startWith, switchMap, TimeoutError } from 'rxjs';
import { FlightOfferComponent } from './flight-offer.component';
import { type FlightOfferView, summarizeSearchResponse } from './flight-results';
import {
  createFlightSearchForm,
  type FlightSearchErrors,
  fillFlightSearchExample,
  localToday,
  toFlightSearchRequest,
} from './flight-search-form';
import { isDemoSource } from './flights-source-mode';

type PageState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'ready'; offers: FlightOfferView[]; partial: boolean; currencyMismatch: boolean }
  | { kind: 'error'; message: string };

function userError(error: unknown): string {
  if (error instanceof FlightSearchContractError) {
    return 'Не удалось прочитать результаты. Повторите поиск.';
  }
  if (error instanceof TimeoutError) return 'Поиск занял слишком много времени. Повторите попытку.';
  if (error instanceof HttpErrorResponse) {
    if (error.status === 200) return 'Не удалось прочитать результаты. Повторите поиск.';
    if (error.status === 400) return 'Проверьте условия поиска и повторите попытку.';
    if (error.status === 401 || error.status === 403) {
      return 'Поиск сейчас недоступен из-за настройки доступа.';
    }
    if (error.status === 0) return 'Нет соединения с поиском. Проверьте подключение и повторите попытку.';
  }
  return 'Сервис поиска временно недоступен. Повторите попытку.';
}

@Component({
  selector: 'app-flights-page',
  standalone: true,
  imports: [ReactiveFormsModule, TravelButton, FlightOfferComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './flights-page.component.html',
  styleUrl: './flights-page.component.scss',
})
export class FlightsPageComponent {
  private readonly api = inject(FlightsSearchApiService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly commands = new Subject<FlightSearchRequest | null>();

  readonly form = createFlightSearchForm(localToday);
  readonly today = localToday;
  readonly submitted = signal(false);
  readonly state = signal<PageState>({ kind: 'idle' });
  readonly isDemo = isDemoSource();

  constructor() {
    this.commands
      .pipe(
        switchMap((request) =>
          request === null
            ? of<PageState>({ kind: 'idle' })
            : this.api.search(request).pipe(
                map((response): PageState => ({ kind: 'ready', ...summarizeSearchResponse(response) })),
                catchError((error: unknown) => of<PageState>({ kind: 'error', message: userError(error) })),
                startWith<PageState>({ kind: 'loading' }),
              ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((value) => this.state.set(value));

    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.submitted.set(false);
      this.commands.next(null);
    });
  }

  fieldError(field: keyof FlightSearchErrors): string | null {
    if (!this.submitted()) return null;
    const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
    return errors?.[field] ?? null;
  }

  submit(): void {
    if (this.state().kind === 'loading') return;
    this.submitted.set(true);
    this.form.updateValueAndValidity({ emitEvent: false });
    if (this.form.invalid) {
      const errors = this.form.errors?.['search'] as FlightSearchErrors | undefined;
      const first = errors ? Object.keys(errors)[0] : undefined;
      if (first) {
        this.element.nativeElement.querySelector<HTMLInputElement>(`[formControlName="${first}"]`)?.focus();
      }
      return;
    }
    this.commands.next(toFlightSearchRequest(this.form));
  }

  retry(): void {
    this.submit();
  }

  example(): void {
    fillFlightSearchExample(this.form, localToday());
  }
}
