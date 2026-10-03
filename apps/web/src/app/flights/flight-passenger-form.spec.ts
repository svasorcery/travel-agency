import { createPassengerForm, passengerDateError, safePassengerErrors } from './flight-passenger-form';

describe('group adult validation', () => {
  it('matches DateOnly eighteenth birthday clamping', () => {
    expect(passengerDateError('2012-02-29', '2030-02-28', '2026-10-03')).toBeNull();
    expect(passengerDateError('2012-02-29', '2030-02-27', '2026-10-03')).toBe('adult');
    expect(passengerDateError('2012-02-30', '2030-02-28', '2026-10-03')).toBe('date');
  });
  it('requires explicit title and gender, restricted names, and bounded contacts', () => {
    const form = createPassengerForm('22222222-2222-4222-8222-222222222222', () => '2030-06-01');
    form.patchValue({
      givenName: 'Demo',
      familyName: 'Traveler',
      dateOfBirth: '1990-04-12',
      email: 'demo@example.test',
      phone: '+79161234567',
    });
    expect(form.invalid).toBe(true);
    form.patchValue({ title: 'dr', gender: 'female' });
    expect(form.valid).toBe(true);
    for (const givenName of ['Ægir', 'Œil', 'Demo123', 'a'.repeat(21)]) {
      form.patchValue({ givenName });
      expect(form.invalid).toBe(true);
    }
  });
});

describe('bounded safe passenger errors', () => {
  const id = '22222222-2222-4222-8222-222222222222';
  it('keeps own fixed fields/codes and drops foreign identifiers and mismatched codes', () => {
    const valid = { bookingPassengerId: id, field: 'email', code: 'Flights.PassengerEmailInvalid' };
    expect(
      safePassengerErrors(
        [
          valid,
          { ...valid, bookingPassengerId: '33333333-3333-4333-8333-333333333333' },
          { ...valid, field: 'detail' },
          { ...valid, code: 'Flights.PassengerPhoneInvalid' },
          { ...valid, field: ['email'] },
        ],
        [id],
      ),
    ).toEqual([valid]);
    expect(safePassengerErrors(Array(64).fill(valid), [id])).toEqual([]);
  });
});
