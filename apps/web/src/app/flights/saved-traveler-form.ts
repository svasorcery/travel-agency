import { FormControl, FormGroup, Validators } from '@angular/forms';
import { type SavedTravelerDetails, travelerDateError, validTravelerName } from '@travel/api-client';
export const TRAVELER_FIELDS = ['title', 'givenName', 'familyName', 'dateOfBirth', 'gender', 'email', 'phone'] as const;
export function createSavedTravelerForm(today = () => new Date().toISOString().slice(0, 10)) {
  return new FormGroup({
    title: new FormControl<SavedTravelerDetails['title'] | ''>('', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(/^(mr|ms|mrs|miss|dr)$/)],
    }),
    givenName: new FormControl('', {
      nonNullable: true,
      validators: [(c) => (validTravelerName(c.value) ? null : { name: true })],
    }),
    familyName: new FormControl('', {
      nonNullable: true,
      validators: [(c) => (validTravelerName(c.value) ? null : { name: true })],
    }),
    dateOfBirth: new FormControl('', {
      nonNullable: true,
      validators: [
        (c) => {
          const error = travelerDateError(c.value, today());
          return error ? { [error]: true } : null;
        },
      ],
    }),
    gender: new FormControl<SavedTravelerDetails['gender'] | ''>('', {
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
export function canonicalTravelerDetails(
  value: ReturnType<ReturnType<typeof createSavedTravelerForm>['getRawValue']>,
): SavedTravelerDetails {
  return {
    ...value,
    title: value.title as SavedTravelerDetails['title'],
    gender: value.gender as SavedTravelerDetails['gender'],
    givenName: value.givenName.trim(),
    familyName: value.familyName.trim(),
    email: value.email.trim(),
    phone: value.phone.trim(),
  };
}
