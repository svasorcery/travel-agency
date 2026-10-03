import { FormArray, FormControl, FormGroup, type ValidationErrors } from '@angular/forms';
import type { FlightSearchRequest, FlightSearchV2Request } from '@travel/api-client';

export type TripType = 'oneWay' | 'roundTrip' | 'multiLeg';
export type FlightSearchIntent =
  | { kind: 'legacy'; request: FlightSearchRequest }
  | { kind: 'v2'; request: FlightSearchV2Request };
export type FlightLegField = 'origin' | 'destination' | 'departureDate';
export type FlightLegErrors = Partial<Record<FlightLegField, string>>;
export type FlightLegForm = FormGroup<{
  origin: FormControl<string>;
  destination: FormControl<string>;
  departureDate: FormControl<string>;
}>;
type SearchFields = 'origin' | 'destination' | 'departureDate' | 'returnDate' | 'passengerCount';
export type FlightSearchErrors = Partial<Record<SearchFields, string>> & { legs?: FlightLegErrors[] };

export type FlightSearchForm = FormGroup<{
  tripType: FormControl<TripType>;
  origin: FormControl<string>;
  destination: FormControl<string>;
  departureDate: FormControl<string>;
  returnDate: FormControl<string>;
  passengerCount: FormControl<number>;
  legs: FormArray<FlightLegForm>;
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

  if (value.tripType === 'multiLeg') {
    const legErrors = value.legs.map((leg, index) => {
      const row: FlightLegErrors = {};
      const from = normalizeAirport(leg.origin);
      const to = normalizeAirport(leg.destination);
      if (!/^[A-Z]{3}$/.test(from)) row.origin = 'Введите код аэропорта из трёх латинских букв.';
      if (!/^[A-Z]{3}$/.test(to)) row.destination = 'Введите код аэропорта из трёх латинских букв.';
      else if (from === to) row.destination = 'Выберите другой аэропорт прибытия.';
      if (!validDate(leg.departureDate)) row.departureDate = 'Выберите корректную дату вылета.';
      else if (leg.departureDate < today) row.departureDate = 'Дата вылета уже прошла.';
      else if (
        index > 0 &&
        validDate(value.legs[index - 1].departureDate) &&
        leg.departureDate < value.legs[index - 1].departureDate
      )
        row.departureDate = 'Вылет не может быть раньше предыдущего участка.';
      return row;
    });
    if (value.legs.length < 2 || value.legs.length > 4 || legErrors.some((row) => Object.keys(row).length > 0))
      errors.legs = legErrors;
    if (!Number.isInteger(value.passengerCount) || value.passengerCount < 1 || value.passengerCount > 9)
      errors.passengerCount = 'Выберите от 1 до 9 взрослых пассажиров.';
    return errors;
  }

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
    legs: new FormArray<FlightLegForm>([createLeg(), createLeg()]),
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
  if (value.tripType === 'multiLeg') throw new Error('Multi-leg search requires v2.');
  return {
    origin: normalizeAirport(value.origin),
    destination: normalizeAirport(value.destination),
    departureDate: value.departureDate,
    returnDate: value.tripType === 'roundTrip' ? value.returnDate : null,
    passengerCount: value.passengerCount as FlightSearchRequest['passengerCount'],
    cabinClass: 'economy',
  };
}

function createLeg(): FlightLegForm {
  return new FormGroup({
    origin: new FormControl('', { nonNullable: true }),
    destination: new FormControl('', { nonNullable: true }),
    departureDate: new FormControl('', { nonNullable: true }),
  });
}

export function addFlightLeg(form: FlightSearchForm): void {
  if (form.controls.legs.length < 4) form.controls.legs.push(createLeg());
}

export function removeFlightLeg(form: FlightSearchForm, index: number): void {
  if (form.controls.legs.length > 2 && Number.isInteger(index) && index >= 0 && index < form.controls.legs.length)
    form.controls.legs.removeAt(index);
}

export function toFlightSearchIntent(form: FlightSearchForm): FlightSearchIntent {
  form.updateValueAndValidity({ emitEvent: false });
  if (form.invalid) throw new Error('Flight search form is invalid.');
  if (form.controls.tripType.value !== 'multiLeg') return { kind: 'legacy', request: toFlightSearchRequest(form) };
  const value = form.getRawValue();
  return {
    kind: 'v2',
    request: {
      legs: value.legs.map((leg) => ({
        origin: normalizeAirport(leg.origin),
        destination: normalizeAirport(leg.destination),
        departureDate: leg.departureDate,
      })),
      passengerCount: value.passengerCount as FlightSearchRequest['passengerCount'],
      cabinClass: 'economy',
    },
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
  if (form.controls.tripType.value === 'multiLeg') {
    const routes = [
      ['LED', 'DME'],
      ['VKO', 'KZN'],
      ['KZN', 'SVO'],
      ['SVO', 'LED'],
    ];
    form.controls.legs.controls.forEach((leg, index) => {
      leg.patchValue({
        origin: routes[index][0],
        destination: routes[index][1],
        departureDate: addDays(today, 30 + index * 7),
      });
    });
    return;
  }
  form.patchValue({
    origin: 'LED',
    destination: 'DME',
    departureDate: addDays(today, 30),
    returnDate: form.controls.tripType.value === 'roundTrip' ? addDays(today, 37) : '',
  });
}
