import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FlightsAuthService } from './flights-auth.service';
import type { FlightsAuthStatus } from './flights-auth.types';
import { SavedTravelersPageComponent } from './saved-travelers-page.component';

const id = '11111111-1111-4111-8111-111111111111';
const revision = '22222222-2222-4222-8222-222222222222';
const details = {
  title: 'mr' as const,
  givenName: 'DemoA',
  familyName: 'Traveler',
  dateOfBirth: '2015-04-12',
  gender: 'male' as const,
  email: 'demoa@example.test',
  phone: '+79161234567',
};
describe('saved travelers owner page', () => {
  let http: HttpTestingController;
  let auth: {
    status: WritableSignal<FlightsAuthStatus>;
    identityEpoch: WritableSignal<number>;
    isDemo: boolean;
    accessToken: () => Promise<null>;
    beginLogin: () => Promise<boolean>;
    hasOidcCallback: () => boolean;
  };
  beforeEach(async () => {
    auth = {
      status: signal({ kind: 'authenticated', userId: 'demo-only' }),
      identityEpoch: signal(0),
      isDemo: true,
      accessToken: vi.fn().mockResolvedValue(null),
      beginLogin: vi.fn().mockResolvedValue(true),
      hasOidcCallback: () => false,
    };
    await TestBed.configureTestingModule({
      imports: [SavedTravelersPageComponent],
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
  async function page(items: unknown[] = []) {
    const fixture = TestBed.createComponent(SavedTravelersPageComponent);
    fixture.detectChanges();
    await Promise.resolve();
    http.expectOne('/api/flights/travelers?offset=0').flush({ items, offset: 0, hasMore: false });
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, root: fixture.nativeElement as HTMLElement };
  }
  it('shows an empty page and saves a child profile only after explicit valid submit', async () => {
    const { fixture, component, root } = await page();
    expect(root.textContent).toContain('Пока нет сохранённых пассажиров');
    (root.querySelector('[data-action="traveler-create"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(Object.keys(component.form.controls)).toHaveLength(7);
    component.form.patchValue(details);
    const pending = component.save();
    await Promise.resolve();
    const req = http.expectOne((r) => r.method === 'PUT');
    const createdId = req.request.url.split('/').at(-1) ?? '';
    expect(req.request.headers.get('If-None-Match')).toBe('*');
    fixture.detectChanges();
    expect((root.querySelector('[data-action="traveler-save"]') as HTMLButtonElement).disabled).toBe(true);
    req.flush({ id: createdId, revision }, { status: 201, statusText: 'Created', headers: { ETag: `"${revision}"` } });
    await pending;
    fixture.detectChanges();
    http
      .expectOne('/api/flights/travelers?offset=0')
      .flush({ items: [{ id: createdId, revision, details }], offset: 0, hasMore: false });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(component.form.controls.email.value).toBe('');
    expect(root.textContent).toContain('DemoA');
  });
  it('requires delete confirmation, uses observed revision, and clears the form on auth error', async () => {
    const { fixture, component, root } = await page([{ id, revision, details }]);
    (root.querySelector('[data-action="traveler-delete"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectNone((r) => r.method === 'DELETE');
    (root.querySelector('[data-action="traveler-delete-confirm"]') as HTMLButtonElement).click();
    await Promise.resolve();
    const req = http.expectOne(`/api/flights/travelers/${id}`);
    expect(req.request.headers.get('If-Match')).toBe(`"${revision}"`);
    req.flush('', { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    http.expectOne('/api/flights/travelers?offset=0').flush({ items: [], offset: 0, hasMore: false });
    await fixture.whenStable();
    component.newProfile();
    component.form.patchValue(details);
    auth.status.set({ kind: 'error', message: 'safe' });
    fixture.detectChanges();
    expect(component.form.controls.email.value).toBe('');
    expect(component.profiles.items()).toEqual([]);
  });
  it('does not render backend raw text or resend after malformed mutation success', async () => {
    const { fixture, component, root } = await page();
    component.newProfile();
    component.form.patchValue(details);
    const pending = component.save();
    await Promise.resolve();
    http
      .expectOne((r) => r.method === 'PUT')
      .flush({ detail: 'PRIVATE BACKEND BODY' }, { status: 201, statusText: 'Created' });
    await pending;
    fixture.detectChanges();
    expect(root.textContent).toContain('Результат операции неизвестен');
    expect(root.textContent).not.toContain('PRIVATE BACKEND BODY');
    await component.save();
    http.expectNone((r) => r.method === 'PUT');
  });
  it('erases the editor after an HTTP auth refusal with unchanged token identity', async () => {
    const { fixture, component } = await page();
    component.newProfile();
    component.form.patchValue(details);
    const refresh = component.profiles.load(0);
    await Promise.resolve();
    http.expectOne('/api/flights/travelers?offset=0').flush({}, { status: 401, statusText: 'Unauthorized' });
    await refresh;
    fixture.detectChanges();
    expect(component.form.controls.email.value).toBe('');
    expect(component.editor()).toBe('closed');
  });
});
