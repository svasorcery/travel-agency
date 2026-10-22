import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import {
  decodeTripPlan,
  TripsApiService,
  TripsItineraryApiService,
  TripsItineraryRequestError,
} from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';
import type { TravelAuthStatus } from '../auth/keycloak-session';
import { TripsAuthService } from './trips-auth.service';
import { TripsAvailabilityService } from './trips-availability.service';
import { TripsPageComponent } from './trips-page.component';

async function setup() {
  const auth = {
    status: signal<TravelAuthStatus>({ kind: 'authenticated', userId: 'owner' }),
    identityEpoch: signal(0),
    accessToken: vi.fn().mockResolvedValue('token'),
    hasOidcCallback: () => false,
    initializeFromCallback: vi.fn(),
    beginLogin: vi.fn(),
    deny: vi.fn(),
    logout: vi.fn(async () => {
      auth.identityEpoch.update((e) => e + 1);
      auth.status.set({ kind: 'anonymous' });
    }),
  };
  const plan = decodeTripPlan(structuredClone(tripsExample));
  const api = {
    detail: vi.fn().mockReturnValue(of(plan)),
    list: vi.fn().mockReturnValue(
      of({
        items: [
          {
            tripId: plan.tripId,
            revision: plan.revision,
            title: '<img src=x onerror=alert(1)>',
            destinationId: 'saint-petersburg',
            displayName: 'Санкт-Петербург',
            startDate: '2026-11-14',
            endDate: '2026-11-14',
            updatedAt: plan.updatedAt,
          },
        ],
        nextCursor: null,
      }),
    ),
    create: vi.fn(),
    replace: vi.fn(),
    delete: vi.fn().mockReturnValue(of(undefined)),
  };
  const availability = {
    status: signal('ready'),
    enabled: () => true,
    catalogue: signal({ catalogueVersion: '1', items: [tripsExample.destination] }),
    initialize: vi.fn().mockResolvedValue(undefined),
    clearPrivate: vi.fn(),
  };
  const params = convertToParamMap({ id: plan.tripId });
  TestBed.configureTestingModule({
    imports: [TripsPageComponent],
    providers: [
      provideRouter([]),
      {
        provide: TripsItineraryApiService,
        useValue: { availability: () => throwError(() => new TripsItineraryRequestError('disabled', 404)) },
      },
      { provide: ActivatedRoute, useValue: { paramMap: of(params), snapshot: { paramMap: params } } },
      { provide: TripsAuthService, useValue: auth },
      { provide: TripsAvailabilityService, useValue: availability },
      { provide: TripsApiService, useValue: api },
    ],
  });
  const fixture = TestBed.createComponent(TripsPageComponent);
  await fixture.whenStable();
  return { fixture, page: fixture.componentInstance, api, auth, plan };
}
describe('Trips accessible explicit workspace', () => {
  it('keeps form-only dirty wishes as comparison state when a changed GET arrives', async () => {
    const { page, fixture, api, plan } = await setup();
    page.patchBrief('adults', 3);
    expect(page.state.dirty()).toBe(true);
    api.detail.mockReturnValue(of({ ...plan, revision: '3', brief: { ...plan.brief, adults: 4 } }));
    await page.state.refreshDetail();
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(3);
    expect(page.state.comparison()?.brief.adults).toBe(4);
    expect(page.state.buffer()?.brief.adults).toBe(2);
    expect(page.state.dirty()).toBe(true);
  });
  it('clears form-only dirtiness on actual revert while retaining independent block and title edits', async () => {
    const { page } = await setup();
    page.patchBrief('adults', 3);
    expect(page.state.dirty()).toBe(true);
    page.patchBrief('adults', 2);
    expect(page.state.dirty()).toBe(false);
    page.title('Local title');
    page.patchBrief('adults', 3);
    page.patchBrief('adults', 2);
    expect(page.state.dirty()).toBe(true);
    expect(page.state.buffer()?.title).toBe('Local title');
  });
  it('explicit server reconciliation discards unapplied brief edits from the form and the next PUT', async () => {
    const { page, fixture, api, plan } = await setup();
    page.patchBrief('adults', 3);
    await page.state.refreshDetail();
    await fixture.whenStable();
    const choose = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: unknown) => (b as HTMLElement).textContent?.trim() === 'Заменить редактор сохранённым планом',
    ) as HTMLButtonElement;
    expect(choose).toBeDefined();
    choose.click();
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(2);
    expect(page.briefDirty()).toBe(false);
    page.title('After explicit discard');
    api.replace.mockReturnValue(of({ ...plan, title: 'After explicit discard', revision: '3' }));
    await page.submit();
    expect(api.replace.mock.calls[0][2].brief.adults).toBe(2);
  });
  it('focuses the invalid block time rather than the plan title', async () => {
    const { page, fixture, api, plan } = await setup();
    const block = plan.days[0].blocks[0];
    page.state.updateBlock(block.blockId, { endTime: '09:00' });
    await page.submit();
    await fixture.whenStable();
    expect(api.replace).not.toHaveBeenCalled();
    expect(document.activeElement?.id).toBe('start-' + block.blockId);
    expect(fixture.nativeElement.querySelector('#start-' + block.blockId).getAttribute('aria-invalid')).toBe('true');
  });
  it('renders private markup literally, labels every field and explains partial estimates', async () => {
    const { fixture } = await setup();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('img')).toBeNull();
    expect(root.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(root.textContent).toContain('Прогулочные предложения выключены');
    expect(root.textContent).toContain('500.00 RUB');
    expect(root.textContent).toContain('не полная цена');
    expect(root.querySelector('[aria-live=polite]')).not.toBeNull();
    for (const field of root.querySelectorAll<HTMLInputElement>('input:not([type=checkbox]),select,textarea'))
      expect(root.querySelector(`label[for="${field.id}"]`)).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('#trip-budget')!.type).toBe('text');
    expect(root.querySelector('#generate-itinerary')).toBeNull();
  });
  it('announces invalid dates and focuses the corrective input without losing valid text', async () => {
    const { page, fixture, api } = await setup();
    page.patchBrief('endDate', '2026-11-13');
    page.title('Сохранённый ввод');
    await page.submit();
    await fixture.whenStable();
    expect(api.replace).not.toHaveBeenCalled();
    expect(page.state.buffer()?.title).toBe('Сохранённый ввод');
    expect(page.state.message()).toContain('от 1 до 14');
    expect(fixture.nativeElement.querySelector('#trip-start').getAttribute('aria-invalid')).toBe('true');
    expect(document.activeElement?.id).toBe('trip-start');
  });
  it('requires explicit confirmation before deletion', async () => {
    const { page, fixture, api } = await setup();
    const remove = [...fixture.nativeElement.querySelectorAll('button')].find(
      (b: unknown) => (b as HTMLElement).textContent?.trim() === 'Удалить план',
    ) as HTMLButtonElement;
    remove.click();
    await fixture.whenStable();
    expect(api.delete).not.toHaveBeenCalled();
    expect(page.deleteConfirmation()).toBe(true);
    page.deleteConfirmation.set(false);
    expect(api.delete).not.toHaveBeenCalled();
  });
  it('retains brief B entered while save A is pending', async () => {
    const { page, fixture, api, plan } = await setup();
    page.title('A');
    const pending = new Subject<typeof plan>();
    api.replace.mockReturnValue(pending);
    const saving = page.submit();
    await vi.waitFor(() => expect(api.replace).toHaveBeenCalledTimes(1));
    page.patchBrief('adults', 3);
    pending.next({ ...plan, title: 'A', revision: '3' });
    pending.complete();
    await saving;
    await fixture.whenStable();
    expect(page.briefDraft()?.adults).toBe(3);
    expect(page.briefDirty()).toBe(true);
    expect(page.state.baseline()?.brief.adults).toBe(2);
  });
  it('warns on dirty navigation, retains data when cancelled, and clears on logout', async () => {
    const { page, fixture, auth } = await setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    page.title('Частный черновик');
    expect(page.canLeave()).toBe(false);
    expect(confirm).toHaveBeenCalledOnce();
    expect(page.state.buffer()?.title).toBe('Частный черновик');
    await page.logout();
    await fixture.whenStable();
    expect(page.state.buffer()).toBeNull();
    expect(page.briefDraft()).toBeNull();
    expect(auth.status().kind).toBe('anonymous');
    confirm.mockRestore();
  });
});
