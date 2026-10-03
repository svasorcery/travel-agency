import { FormControl, FormGroup, type ValidationErrors } from '@angular/forms';
import type { FlightSearchRequest } from '@travel/api-client';

export type TripType = 'oneWay' | 'roundTrip';
type SearchFields = 'origin' | 'destination' | 'departureDate' | 'returnDate' | 'passengerCount';
export type FlightSearchErrors = Partial<Record<SearchFields, string>>;

export type FlightSearchForm = FormGroup<{
  tripType: FormControl<TripType>;
  origin: FormControl<string>;
  destination: FormControl<string>;
  departureDate: FormControl<string>;
  returnDate: FormControl<string>;
  passengerCount: FormControl<number>;
}>;

function normalizeAirport(code: string): string {
  return code.trim().toUpperCase();
}

function validDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}

function validate(form: FlightSearchForm, today: string): FlightSearchErrors {
  const value = form.getRawValue();
  const origin = normalizeAirport(value.origin);
  const destination = normalizeAirport(value.destination);
  const errors: FlightSearchErrors = {};

  if (!/^[A-Z]{3}$/.test(origin)) errors.origin = 'Введите код аэропорта из трёх латинских букв.';
  if (!/^[A-Z]{3}$/.test(destination)) {
    errors.destination = 'Введите код аэропорта из трёх латинских букв.';
  } else if (origin === destination) {
    errors.destination = 'Выберите другой аэропорт прибытия.';
  }
  if (!validDate(value.departureDate)) {
    errors.departureDate = 'Выберите корректную дату вылета.';
  } else if (value.departureDate < today) {
    errors.departureDate = 'Дата вылета уже прошла.';
  }
  if (value.tripType === 'roundTrip') {
    if (!validDate(value.returnDate)) {
      errors.returnDate = 'Выберите корректную дату возвращения.';
    } else if (validDate(value.departureDate) && value.returnDate < value.departureDate) {
      errors.returnDate = 'Возвращение не может быть раньше вылета.';
    }
  }
  if (!Number.isInteger(value.passengerCount) || value.passengerCount < 1 || value.passengerCount > 9)
    errors.passengerCount = 'Выберите от 1 до 9 взрослых пассажиров.';
  return errors;
}

export function createFlightSearchForm(today: () => string): FlightSearchForm {
  const form: FlightSearchForm = new FormGroup({
    tripType: new FormControl<TripType>('oneWay', { nonNullable: true }),
    origin: new FormControl('', { nonNullable: true }),
    destination: new FormControl('', { nonNullable: true }),
    departureDate: new FormControl('', { nonNullable: true }),
    returnDate: new FormControl('', { nonNullable: true }),
    passengerCount: new FormControl(1, { nonNullable: true }),
  });
  form.addValidators((): ValidationErrors | null => {
    const errors = validate(form, today());
    return Object.keys(errors).length > 0 ? { search: errors } : null;
  });
  form.updateValueAndValidity({ emitEvent: false });
  return form;
}

export function toFlightSearchRequest(form: FlightSearchForm): FlightSearchRequest {
  form.updateValueAndValidity({ emitEvent: false });
  if (form.invalid) throw new Error('Flight search form is invalid.');
  const value = form.getRawValue();
  return {
    origin: normalizeAirport(value.origin),
    destination: normalizeAirport(value.destination),
    departureDate: value.departureDate,
    returnDate: value.tripType === 'roundTrip' ? value.returnDate : null,
    passengerCount: value.passengerCount as FlightSearchRequest['passengerCount'],
    cabinClass: 'economy',
  };
}

export function localToday(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(
    2,
    '0',
  )}`;
}

function addDays(value: string, days: number): string {
  const date = new Date(`${value}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

export function fillFlightSearchExample(form: FlightSearchForm, today: string): void {
  form.patchValue({
    origin: 'LED',
    destination: 'DME',
    departureDate: addDays(today, 30),
    returnDate: form.controls.tripType.value === 'roundTrip' ? addDays(today, 37) : '',
  });
}
