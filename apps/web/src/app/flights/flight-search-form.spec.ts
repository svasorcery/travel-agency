import {
  addFlightLeg,
  createFlightSearchForm,
  type FlightSearchForm,
  fillFlightSearchExample,
  removeFlightLeg,
  toFlightSearchIntent,
  toFlightSearchRequest,
} from './flight-search-form';

function filledForm(today: () => string = () => '2030-06-01'): FlightSearchForm {
  const form = createFlightSearchForm(today);
  form.patchValue({
    origin: ' led ',
    destination: 'dme',
    departureDate: '2030-06-10',
  });
  return form;
}

describe('flight search form', () => {
  it('keeps independent route rows, bounds and errors attached to remaining rows', () => {
    const form = filledForm();
    form.controls.tripType.setValue('multiLeg');
    expect(form.controls.legs.length).toBe(2);
    addFlightLeg(form);
    addFlightLeg(form);
    addFlightLeg(form);
    expect(form.controls.legs.length).toBe(4);
    const last = form.controls.legs.at(3);
    last.patchValue({ origin: 'KZN', destination: 'SVO', departureDate: '2030-06-09' });
    form.controls.legs.at(0).patchValue({ origin: 'LED', destination: 'DME', departureDate: '2030-06-10' });
    form.controls.legs.at(1).patchValue({ origin: 'DME', destination: 'VKO', departureDate: '2030-06-11' });
    removeFlightLeg(form, 2);
    expect(form.controls.legs.at(2)).toBe(last);
    expect(form.errors?.['search'].legs[2].departureDate).toContain('раньше');
    removeFlightLeg(form, 2);
    removeFlightLeg(form, 1);
    expect(form.controls.legs.length).toBe(2);
  });
  it('submits v2 with independent ordered airports and equal local dates, never flattens it to v1', () => {
    const form = filledForm();
    form.controls.tripType.setValue('multiLeg');
    form.controls.legs.at(0).patchValue({ origin: ' led ', destination: 'dme', departureDate: '2030-06-10' });
    form.controls.legs.at(1).patchValue({ origin: 'VKO', destination: 'KZN', departureDate: '2030-06-10' });
    expect(toFlightSearchIntent(form)).toEqual({
      kind: 'v2',
      request: {
        legs: [
          { origin: 'LED', destination: 'DME', departureDate: '2030-06-10' },
          { origin: 'VKO', destination: 'KZN', departureDate: '2030-06-10' },
        ],
        passengerCount: 1,
        cabinClass: 'economy',
      },
    });
    expect(() => toFlightSearchRequest(form)).toThrow();
    form.controls.tripType.setValue('oneWay');
    expect(toFlightSearchIntent(form).kind).toBe('legacy');
  });
  it('normalizes airport codes and sends a null return date for one-way', () => {
    const form = filledForm();
    expect(toFlightSearchRequest(form)).toEqual({
      origin: 'LED',
      destination: 'DME',
      departureDate: '2030-06-10',
      returnDate: null,
      passengerCount: 1,
      cabinClass: 'economy',
    });
  });

  it('sends the return date for round-trip, then clears it after switching to one-way', () => {
    const form = filledForm();
    form.patchValue({ tripType: 'roundTrip', returnDate: '2030-06-17' });
    expect(toFlightSearchRequest(form).returnDate).toBe('2030-06-17');
    form.patchValue({ tripType: 'oneWay' });
    expect(toFlightSearchRequest(form).returnDate).toBeNull();
  });

  it.each([
    [{ origin: 'LED', destination: 'LED' }, 'destination'],
    [{ origin: 'AB1' }, 'origin'],
    [{ departureDate: '2030-02-30' }, 'departureDate'],
    [{ departureDate: '2030-05-31' }, 'departureDate'],
    [{ tripType: 'roundTrip', returnDate: '' }, 'returnDate'],
    [{ tripType: 'roundTrip', returnDate: '2030-06-09' }, 'returnDate'],
  ] as const)('rejects invalid trip data at %s', (patch, field) => {
    const form = filledForm();
    form.patchValue(patch);
    form.updateValueAndValidity();
    expect(form.invalid).toBe(true);
    expect(form.errors?.['search']?.[field]).toBeTruthy();
    expect(() => toFlightSearchRequest(form)).toThrow();
  });

  it('rechecks today when an open tab passes midnight', () => {
    let current = '2030-06-10';
    const form = filledForm(() => current);
    expect(form.valid).toBe(true);
    current = '2030-06-11';
    form.updateValueAndValidity();
    expect(form.errors?.['search']?.['departureDate']).toBeTruthy();
  });

  it('fills a useful example without submitting anything', () => {
    const form = createFlightSearchForm(() => '2030-06-01');
    fillFlightSearchExample(form, '2030-06-01');
    expect(form.value).toMatchObject({
      origin: 'LED',
      destination: 'DME',
      departureDate: '2030-07-01',
      returnDate: '',
    });
  });
});

describe('adult group count', () => {
  it.each([1, 2, 9])('submits an immutable count %s', (count) => {
    const form = createFlightSearchForm(() => '2030-01-01');
    form.patchValue({ origin: 'LED', destination: 'DME', departureDate: '2030-06-01', passengerCount: count });
    const request = toFlightSearchRequest(form);
    form.patchValue({ passengerCount: 1 });
    expect(request.passengerCount).toBe(count);
  });
  it.each([0, 10, 1.5, NaN])('rejects count %s', (passengerCount) => {
    const form = createFlightSearchForm(() => '2030-01-01');
    form.patchValue({ origin: 'LED', destination: 'DME', departureDate: '2030-06-01', passengerCount });
    expect(form.invalid).toBe(true);
  });
});
