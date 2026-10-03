import { createSavedTravelerForm } from './saved-traveler-form';

describe('slot-free saved traveler form', () => {
  it('allows a child profile while retaining seven fields and real UTC dates', () => {
    const form = createSavedTravelerForm(() => '2026-10-03');
    form.patchValue({
      title: 'mr',
      givenName: 'Demo',
      familyName: 'Traveler',
      dateOfBirth: '2015-04-12',
      gender: 'male',
      email: 'demo@example.test',
      phone: '+79161234567',
    });
    expect(form.valid).toBe(true);
    expect(Object.keys(form.controls)).toHaveLength(7);
    for (const dateOfBirth of ['0001-01-01', '2015-02-30', '2026-10-04']) {
      form.patchValue({ dateOfBirth });
      expect(form.invalid).toBe(true);
    }
  });
  it('checks raw name bounds before canonical trimming', () => {
    const form = createSavedTravelerForm();
    form.controls.givenName.setValue(`${' '.repeat(20)}A`);
    expect(form.controls.givenName.invalid).toBe(true);
    form.controls.givenName.setValue(' Demo ');
    expect(form.controls.givenName.valid).toBe(true);
  });
});
