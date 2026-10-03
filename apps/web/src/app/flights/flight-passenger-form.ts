import { type AbstractControl, FormControl, FormGroup, type ValidationErrors, Validators } from '@angular/forms';
import type { FlightPassengerInfo } from '@travel/api-client';
import { localToday } from './flight-search-form';
export type PassengerField = 'title' | 'givenName' | 'familyName' | 'dateOfBirth' | 'gender' | 'email' | 'phone';
const NAME = /^[A-Za-z\u00c0-\u00ff\u0100-\u017f '-]{1,20}$/;
export function passengerDateError(
  value: string,
  departure: string,
  today: string,
): 'date' | 'future' | 'adult' | null {
  const date = new Date(`${value}T00:00:00Z`);
  if (
    !/^\d{4}-\d{2}-\d{2}$/.test(value) ||
    value < '0001-01-01' ||
    !Number.isFinite(date.getTime()) ||
    date.toISOString().slice(0, 10) !== value
  )
    return 'date';
  if (value > today) return 'future';
  const year = Number(value.slice(0, 4)) + 18;
  if (year > 9999) return 'adult';
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const birthday = `${String(year).padStart(4, '0')}-${value.slice(5) === '02-29' && !leap ? '02-28' : value.slice(5)}`;
  return birthday > departure ? 'adult' : null;
}
function name(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value.trim();
  return NAME.test(value) && !/[ÆæĲĳŒœÞð×÷]/.test(value) && /[A-Za-z\u00c0-\u017f]/.test(value) ? null : { name: true };
}
export function createPassengerForm(bookingPassengerId: string, departure: () => string) {
  return new FormGroup({
    bookingPassengerId: new FormControl(bookingPassengerId, { nonNullable: true }),
    title: new FormControl<FlightPassengerInfo['title'] | ''>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^(mr|ms|mrs|miss|dr)$/)],
    }),
    givenName: new FormControl('', { nonNullable: true, validators: [name] }),
    familyName: new FormControl('', { nonNullable: true, validators: [name] }),
    dateOfBirth: new FormControl('', {
      nonNullable: true,
      validators: [
        (c) => {
          const error = passengerDateError(c.value, departure(), localToday());
          return error ? { [error]: true } : null;
        },
      ],
    }),
    gender: new FormControl<FlightPassengerInfo['gender'] | ''>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^(male|female)$/)],
    }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(254)],
    }),
    phone: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^\+[1-9][0-9]{7,14}$/)],
    }),
  });
}
export type PassengerForm = ReturnType<typeof createPassengerForm>;
export interface SafePassengerError {
  bookingPassengerId: string;
  field: PassengerField;
  code: string;
}
const FIELD_CODES: Readonly<Record<PassengerField, readonly string[]>> = {
  title: ['Flights.PassengerTitleInvalid'],
  givenName: ['Flights.PassengerGivenNameInvalid'],
  familyName: ['Flights.PassengerFamilyNameInvalid'],
  dateOfBirth: [
    'Flights.PassengerDateOfBirthInvalid',
    'Flights.PassengerDateOfBirthFutureInvalid',
    'Flights.PassengerAdultRequiredInvalid',
  ],
  gender: ['Flights.PassengerGenderInvalid'],
  email: ['Flights.PassengerEmailInvalid'],
  phone: ['Flights.PassengerPhoneInvalid'],
};
export function safePassengerErrors(value: unknown, ids: readonly string[]): SafePassengerError[] {
  if (!Array.isArray(value) || value.length > 63) return [];
  const members = new Set(ids.map((id) => id.toLowerCase()));
  return value
    .filter(
      (item): item is SafePassengerError =>
        item !== null &&
        typeof item === 'object' &&
        typeof item.bookingPassengerId === 'string' &&
        members.has(item.bookingPassengerId.toLowerCase()) &&
        typeof item.field === 'string' &&
        typeof item.code === 'string' &&
        Object.hasOwn(FIELD_CODES, item.field) &&
        FIELD_CODES[item.field as PassengerField].includes(item.code),
    )
    .map((item) => ({ bookingPassengerId: item.bookingPassengerId.toLowerCase(), field: item.field, code: item.code }));
}
export const PASSENGER_FIELD_COPY: Record<PassengerField, string> = {
  title: 'Выберите обращение.',
  givenName: 'Имя: от 1 до 20 латинских букв, пробел, апостроф или дефис.',
  familyName: 'Фамилия: от 1 до 20 латинских букв, пробел, апостроф или дефис.',
  dateOfBirth: 'Проверьте дату рождения: пассажиру должно быть 18 лет на дату первого вылета.',
  gender: 'Выберите пол.',
  email: 'Введите корректный email, не более 254 символов.',
  phone: 'Укажите телефон в формате E.164, например +79161234567.',
};
