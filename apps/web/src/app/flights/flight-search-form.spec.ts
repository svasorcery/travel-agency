import {
  createFlightSearchForm,
  type FlightSearchForm,
  fillFlightSearchExample,
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
