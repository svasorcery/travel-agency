import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
// eslint-disable-next-line @nx/enforce-module-boundaries
import booking from '../../../../../tests/fixtures/flights-booking.json';
import { FlightsBookingPanelComponent } from './flight-booking-panel.component';
import { FlightsAuthService } from './flights-auth.service';
import type { FlightsAuthStatus } from './flights-auth.types';

const idA = '11111111-1111-4111-8111-111111111111';
const idB = '33333333-3333-4333-8333-333333333333';
const revision = '44444444-4444-4444-8444-444444444444';
const details = {
  title: 'mr' as const,
  givenName: 'DemoA',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'male' as const,
  email: 'demoa@example.test',
  phone: '+79161234567',
};
describe('explicit one traveler to one booking slot', () => {
  let http: HttpTestingController;
  let auth: {
    status: WritableSignal<FlightsAuthStatus>;
    identityEpoch: WritableSignal<number>;
    isDemo: boolean;
    accessToken: () => Promise<null>;
    isTestEnvironment: () => boolean;
  };
  beforeEach(async () => {
    auth = {
      status: signal({ kind: 'authenticated', userId: 'demo-only' }),
      identityEpoch: signal(0),
      isDemo: true,
      accessToken: vi.fn().mockResolvedValue(null),
      isTestEnvironment: () => true,
    };
    await TestBed.configureTestingModule({
      imports: [FlightsBookingPanelComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: FlightsAuthService, useValue: auth },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify({ ignoreCancelled: true }));
  function panel() {
    const fixture = TestBed.createComponent(FlightsBookingPanelComponent);
    fixture.componentRef.setInput('quote', booking.oneWay.response);
    fixture.componentRef.setInput('isDemo', true);
    fixture.componentRef.setInput('quoteAccepted', true);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }
  it('copies seven fields into only the current row and preserves its slot ID', async () => {
    const { fixture, component } = panel();
    const row = component.passengers.at(0);
    const slot = row.controls.bookingPassengerId.value;
    component.selectTraveler(row, idA);
    const fill = component.fillTraveler(row);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${idA}`)
      .flush({ id: idA, revision, details }, { headers: { ETag: `"${revision}"` } });
    await fill;
    fixture.detectChanges();
    expect(row.controls.email.value).toBe('demoa@example.test');
    expect(row.controls.bookingPassengerId.value).toBe(slot);
    expect(component.passengers.length).toBe(1);
  });
  it('retains intervening manual edits and discards an older selection response', async () => {
    const { component } = panel();
    const row = component.passengers.at(0);
    component.selectTraveler(row, idA);
    const first = component.fillTraveler(row);
    await Promise.resolve();
    const old = http.expectOne(`/api/flights/travelers/${idA}`);
    component.selectTraveler(row, idB);
    const second = component.fillTraveler(row);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${idB}`)
      .flush(
        { id: idB, revision, details: { ...details, givenName: 'DemoB' } },
        { headers: { ETag: `"${revision}"` } },
      );
    await second;
    old.flush({ id: idA, revision, details }, { headers: { ETag: `"${revision}"` } });
    await first;
    expect(row.controls.givenName.value).toBe('DemoB');
    component.selectTraveler(row, idA);
    const third = component.fillTraveler(row);
    await Promise.resolve();
    const late = http.expectOne(`/api/flights/travelers/${idA}`);
    row.controls.email.setValue('manual@example.test');
    late.flush({ id: idA, revision, details }, { headers: { ETag: `"${revision}"` } });
    await third;
    expect(row.controls.email.value).toBe('manual@example.test');
  });
  it('rejects late fill after same-slot quote revision and auth-error generations', async () => {
    const { fixture, component } = panel();
    const row = component.passengers.at(0);
    component.selectTraveler(row, idA);
    const fill = component.fillTraveler(row);
    await Promise.resolve();
    const req = http.expectOne(`/api/flights/travelers/${idA}`);
    fixture.componentRef.setInput('quote', {
      ...booking.oneWay.response,
      binding: { ...booking.oneWay.response.binding, revision },
    });
    fixture.detectChanges();
    req.flush({ id: idA, revision, details }, { headers: { ETag: `"${revision}"` } });
    await fill;
    expect(row.controls.email.value).toBe('');
    component.selectTraveler(row, idA);
    const late = component.fillTraveler(row);
    await Promise.resolve();
    const req2 = http.expectOne(`/api/flights/travelers/${idA}`);
    auth.status.set({ kind: 'error', message: 'safe' });
    fixture.detectChanges();
    auth.status.set({ kind: 'authenticated', userId: 'demo-only' });
    fixture.detectChanges();
    if (!req2.cancelled) req2.flush({ id: idA, revision, details }, { headers: { ETag: `"${revision}"` } });
    await late;
    expect(row.controls.email.value).toBe('');
  });
  it('revalidates age on departure and blocks copy and save once hold is unknown', async () => {
    const { component } = panel();
    const row = component.passengers.at(0);
    component.selectTraveler(row, idA);
    const fill = component.fillTraveler(row);
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${idA}`)
      .flush(
        { id: idA, revision, details: { ...details, dateOfBirth: '2020-01-01' } },
        { headers: { ETag: `"${revision}"` } },
      );
    await fill;
    expect(row.controls.dateOfBirth.hasError('adult')).toBe(true);
    component.holdState.set('unknown');
    const original = row.getRawValue();
    await component.fillTraveler(row);
    await component.saveTravelerRow(row);
    expect(row.getRawValue()).toEqual(original);
    http.expectNone(() => true);
    expect(component.holdState()).toBe('unknown');
  });
  it('can explicitly save a future adult profile from a row while hold still rejects its age', async () => {
    const { component } = panel();
    const row = component.passengers.at(0);
    row.patchValue({ ...details, dateOfBirth: '2020-01-01' });
    expect(row.controls.dateOfBirth.hasError('adult')).toBe(true);
    const save = component.saveTravelerRow(row);
    await Promise.resolve();
    const req = http.expectOne((r) => r.method === 'PUT');
    expect(req.request.body.dateOfBirth).toBe('2020-01-01');
    expect(req.request.body.bookingPassengerId).toBeUndefined();
    req.flush(
      { id: req.request.url.split('/').at(-1), revision },
      { status: 201, statusText: 'Created', headers: { ETag: `"${revision}"` } },
    );
    await save;
    expect(row.controls.dateOfBirth.hasError('adult')).toBe(true);
    component.hold();
    http.expectNone((r) => r.url.endsWith('/hold'));
  });
  it('clears draft PII on profile authorization refusal even when the token status has not changed', async () => {
    const { fixture, component } = panel();
    const row = component.passengers.at(0);
    row.patchValue(details);
    component.selectTraveler(row, idA);
    const fill = component.fillTraveler(row);
    await Promise.resolve();
    http.expectOne(`/api/flights/travelers/${idA}`).flush({}, { status: 403, statusText: 'Forbidden' });
    await fill;
    fixture.detectChanges();
    expect(row.controls.email.value).toBe('');
    expect(component.selectedTravelers()).toEqual({});
  });
  it('reads current state after a lost profile receipt without changing the row or blocking manual hold', async () => {
    const { component } = panel();
    const row = component.passengers.at(0);
    row.patchValue(details);
    const save = component.saveTravelerRow(row);
    await Promise.resolve();
    const req = http.expectOne((r) => r.method === 'PUT');
    const createdId = req.request.url.split('/').at(-1) ?? '';
    req.error(new ProgressEvent('error'));
    await save;
    expect(component.profiles.mutationState()).toBe('unknown');
    expect(component.holdState()).toBe('idle');
    const review = component.reviewProfileMutation();
    await Promise.resolve();
    http
      .expectOne(`/api/flights/travelers/${createdId}`)
      .flush(
        { id: createdId, revision, details: { ...details, givenName: 'ChangedProfile' } },
        { headers: { ETag: `"${revision}"` } },
      );
    await review;
    expect(row.controls.givenName.value).toBe('DemoA');
    expect(component.profiles.mutationState()).toBe('unknown');
    component.hold();
    const hold = http.expectOne('/api/flights/orders/hold');
    expect(JSON.parse(hold.request.body).passengers[0].givenName).toBe('DemoA');
    hold.error(new ProgressEvent('error'));
  });
});
