import { signal } from '@angular/core';
import {
  type FlightSearchPreview,
  type FlightSearchPreviewField,
  type FlightSearchPreviewLocation,
  flightPreviewLocations,
} from '@travel/api-client';
import type { FlightSearchForm, TripType } from './flight-search-form';

type LocationField = 'origin' | 'destination';
type DateField = 'departureDate' | 'returnDate';
type ReviewValues = Pick<ReturnType<FlightSearchForm['getRawValue']>, FlightSearchPreviewField>;
type ReviewField = keyof ReviewValues | 'request';

interface Choice {
  value: string | number;
  label: string;
  source: 'manual' | 'preview';
  location?: FlightSearchPreviewLocation;
  suggestedDate?: boolean;
}

interface Question {
  text: string;
  choices?: readonly Choice[];
}

const defaults: ReviewValues = {
  tripType: 'oneWay',
  origin: '',
  destination: '',
  departureDate: '',
  returnDate: '',
  passengerCount: 1,
};

const fields = Object.keys(defaults) as FlightSearchPreviewField[];
const questions: Record<FlightSearchPreviewField, string> = {
  tripType: 'В одну сторону или туда и обратно?',
  origin: 'Из какого города вылетаете?',
  destination: 'В какой город летите?',
  departureDate: 'Когда вылетаете?',
  returnDate: 'Когда возвращаетесь?',
  passengerCount: 'Сколько взрослых летит?',
};

const tripLabels: Record<TripType, string> = {
  oneWay: 'В одну сторону',
  roundTrip: 'Туда и обратно',
  multiLeg: 'Несколько перелётов',
};

function locationLabel(location: FlightSearchPreviewLocation): string {
  return location.name === location.code ? location.code : `${location.name} (${location.code})`;
}

function code(input: string): string {
  return input.trim().toUpperCase();
}

function hasAnswer(field: FlightSearchPreviewField, value: string | number | undefined): boolean {
  if (field === 'origin' || field === 'destination') return typeof value === 'string' && /^[A-Z]{3}$/.test(code(value));
  if (field === 'passengerCount') return typeof value === 'number' && value >= 1 && value <= 9;
  return Boolean(value);
}

export class FlightSearchReview {
  readonly active = signal(false);
  readonly questions = signal<Partial<Record<ReviewField, Question>>>({});
  readonly labels = signal<Partial<Record<LocationField, FlightSearchPreviewLocation>>>({});
  readonly suggestedDates = signal<Partial<Record<DateField, boolean>>>({});
  private manual: Partial<ReviewValues> = {};
  private manualLabels: Partial<Record<LocationField, FlightSearchPreviewLocation>> = {};
  private previous: ReturnType<FlightSearchForm['getRawValue']>;
  private applying = false;

  constructor(private readonly form: FlightSearchForm) {
    this.previous = form.getRawValue();
  }

  observeEdit(): void {
    const next = this.form.getRawValue();
    const previous = this.previous;
    this.previous = next;
    if (this.applying) return;

    const labels = { ...this.labels() };
    const dates = { ...this.suggestedDates() };
    const pending = { ...this.questions() };
    for (const field of fields) {
      if (next[field] === previous[field]) continue;
      Object.assign(this.manual, { [field]: next[field] });
      if (field === 'origin' || field === 'destination') {
        delete labels[field];
        delete this.manualLabels[field];
      }
      if (field === 'departureDate' || field === 'returnDate') delete dates[field];
      if (hasAnswer(field, next[field])) delete pending[field];
    }
    if (next.tripType !== previous.tripType) {
      this.updateReturnQuestion(pending);
      if (this.active() && next.tripType === 'oneWay') {
        delete this.manual.returnDate;
        this.form.controls.returnDate.setValue('', { emitEvent: false });
        this.previous = { ...next, returnDate: '' };
        delete dates.returnDate;
      }
    }
    this.labels.set(labels);
    this.suggestedDates.set(dates);
    this.questions.set(pending);
  }

  apply(draft: FlightSearchPreview): void {
    const proposed = {
      tripType: draft.tripType,
      origin: draft.origin?.code,
      destination: draft.destination?.code,
      departureDate: draft.departureDate?.value,
      returnDate: draft.returnDate?.value,
      passengerCount: draft.passengerCount,
    };
    const manual = this.manual;
    const values: ReviewValues = { ...defaults, passengerCount: 0, ...manual };
    const pending: Partial<Record<ReviewField, Question>> = {};
    const labels = { ...this.manualLabels };
    const dates: Partial<Record<DateField, boolean>> = {};

    for (const field of fields) {
      const suggestion = proposed[field];
      if (suggestion == null) continue;
      const current = manual[field];
      const location = field === 'origin' || field === 'destination' ? draft[field] : null;
      const suggestedDate =
        (field === 'departureDate' || field === 'returnDate') && draft[field]?.source === 'suggested';
      const comparableCurrent = location && typeof current === 'string' ? code(current) : current;
      if (current !== undefined && hasAnswer(field, current) && comparableCurrent !== suggestion) {
        pending[field] = {
          text: questions[field],
          choices: [
            { label: this.format(field, current), value: current, source: 'manual' },
            {
              label: location ? locationLabel(location) : this.format(field, suggestion),
              value: suggestion,
              source: 'preview',
              location: location ?? undefined,
              suggestedDate,
            },
          ],
        };
      } else {
        Object.assign(values, { [field]: suggestion });
        if (location) Object.assign(labels, { [field]: location });
        if (suggestedDate && !hasAnswer(field, current)) dates[field as DateField] = true;
      }
    }

    for (const issue of draft.issues) {
      if (issue.field === 'request') {
        pending.request = {
          text:
            issue.reason === 'unsupported'
              ? 'Форма поддерживает до девяти взрослых, перелёт в одну сторону или туда и обратно, экономкласс и цены в RUB.'
              : 'Уточните условия перелёта или заполните форму вручную.',
        };
      } else if (issue.reason !== 'missing' || !hasAnswer(issue.field, manual[issue.field])) {
        pending[issue.field] ??= { text: questions[issue.field] };
      }
    }

    for (const field of ['origin', 'destination', 'departureDate', 'passengerCount'] as const) {
      if (!hasAnswer(field, values[field])) pending[field] ??= { text: questions[field] };
    }
    if (values.tripType === 'roundTrip' && !values.returnDate) {
      pending.returnDate ??= { text: questions.returnDate };
    } else if (values.tripType === 'oneWay' && !pending.tripType?.choices) {
      if (pending.returnDate) pending.tripType ??= { text: questions.tripType };
      delete pending.returnDate;
      if (draft.tripType === 'oneWay' && !pending.tripType) values.returnDate = '';
    }

    this.active.set(true);
    this.labels.set(labels);
    this.suggestedDates.set(dates);
    this.questions.set(pending);
    this.patch(values);
  }

  confirmTripType(tripType: 'oneWay' | 'roundTrip'): void {
    if (this.form.controls.tripType.value !== tripType || this.questions().tripType?.choices) return;
    this.manual.tripType = tripType;
    if (!this.active()) return;
    if (tripType === 'oneWay') delete this.manual.returnDate;
    const pending = { ...this.questions() };
    delete pending.tripType;
    this.updateReturnQuestion(pending);
    this.questions.set(pending);
  }

  use(field: FlightSearchPreviewField, choice: Choice): void {
    const choseOneWay = field === 'tripType' && choice.value === 'oneWay';
    if (choseOneWay) delete this.manual.returnDate;
    if (choice.source === 'preview') {
      delete this.manual[field];
      this.patch(choseOneWay ? { tripType: 'oneWay', returnDate: '' } : { [field]: choice.value });
      if (field === 'origin' || field === 'destination') {
        delete this.manualLabels[field];
        this.labels.update((labels) => ({ ...labels, [field]: choice.location }));
      }
      if (field === 'departureDate' || field === 'returnDate') {
        this.suggestedDates.update((dates) => ({ ...dates, [field]: choice.suggestedDate }));
      }
    } else if (choseOneWay) {
      this.patch({ returnDate: '' });
    }
    if (choseOneWay) this.suggestedDates.update((dates) => ({ ...dates, returnDate: false }));

    const pending = { ...this.questions() };
    delete pending[field];
    if (field === 'tripType') this.updateReturnQuestion(pending);
    this.questions.set(pending);
  }

  manualMode(): void {
    if (!this.active()) return;
    this.active.set(false);
    this.questions.set({});
    this.labels.set({ ...this.manualLabels });
    this.suggestedDates.set({});
    this.patch({ ...defaults, ...this.manual });
  }

  locationOptions(field: LocationField): readonly FlightSearchPreviewLocation[] {
    const text = this.form.controls[field].value.trim().toLowerCase();
    if (!text || /^[a-z]{1,3}$/.test(text)) return [];
    return flightPreviewLocations.filter((place) =>
      place.aliases.some((alias) => alias.toLowerCase().startsWith(text)),
    );
  }

  selectLocation(field: LocationField, location: FlightSearchPreviewLocation): void {
    this.form.controls[field].setValue(location.code);
    this.manualLabels[field] = location;
    this.labels.update((labels) => ({ ...labels, [field]: location }));
  }

  displayLocation(location: FlightSearchPreviewLocation): string {
    return locationLabel(location);
  }

  question(field: ReviewField): Question | undefined {
    return this.questions()[field];
  }

  private format(field: FlightSearchPreviewField, value: string | number): string {
    if (field === 'tripType') return tripLabels[value as TripType];
    if (field === 'origin' || field === 'destination') {
      const manualLabel = this.manualLabels[field];
      return manualLabel ? locationLabel(manualLabel) : code(String(value));
    }
    return String(value);
  }

  private updateReturnQuestion(pending: Partial<Record<ReviewField, Question>>): void {
    if (this.form.controls.tripType.value !== 'roundTrip') delete pending.returnDate;
    else if (this.active() && !this.form.controls.returnDate.value) {
      pending.returnDate ??= { text: questions.returnDate };
    }
  }

  private patch(values: Partial<ReviewValues>): void {
    this.applying = true;
    try {
      this.form.patchValue(values);
    } finally {
      this.applying = false;
    }
  }
}
