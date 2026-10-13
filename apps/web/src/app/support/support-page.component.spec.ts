import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import type { SupportCase, SupportClassificationSuggestion } from '@travel/api-client';
import { BehaviorSubject } from 'rxjs';
import { SupportPageComponent } from './support-page.component';
import { SupportStateService } from './support-state.service';

const id = '11111111-1111-4111-8111-111111111111',
  date = '2030-01-01T00:00:00Z';
const row: SupportCase = {
  caseId: id,
  ownerId: id,
  title: '<img src=x onerror=alert(1)>',
  state: 'Open',
  revision: 3,
  category: 'Unclassified',
  priority: 'Normal',
  triageSource: 'Default',
  orderId: null,
  orderVerifiedAt: null,
  orderStreamVersion: null,
  createdAt: date,
  updatedAt: date,
  resolvedAt: null,
  purgeAt: null,
};
function setup(mode: 'customer' | 'operator', detail: boolean, orderId?: string) {
  const status = signal<{ kind: 'authenticated'; userId: string } | { kind: 'error'; message: string }>({
    kind: 'authenticated',
    userId: id,
  });
  const auth = { isDemo: false, status, identityEpoch: signal(0), beginLogin: vi.fn(), logout: vi.fn() };
  const state = {
    auth,
    caseId: signal(detail ? id : null),
    authorized: () => status().kind === 'authenticated',
    selected: signal<SupportCase | null>(detail ? row : null),
    cases: signal(detail ? [] : [row]),
    messages: signal(
      detail
        ? [{ caseId: id, messageId: id, actorKind: 'Operator', body: '<script>alert(1)</script>', createdAt: date }]
        : [],
    ),
    privateGeneration: signal(0),
    error: signal<string | null>(null),
    notice: signal(null),
    reviewRequired: signal(false),
    orderLinkRejected: signal(false),
    unresolved: signal([]),
    loading: signal(false),
    nextCases: signal(null),
    nextMessages: signal(null),
    writeBlocked: signal(false),
    enter: vi.fn().mockResolvedValue(undefined),
    leave: vi.fn(),
    refresh: vi.fn(),
    reply: vi.fn().mockResolvedValue(false),
    create: vi.fn(),
    createWithoutOrder: vi.fn(),
    triage: vi.fn(),
    suggestion: signal<SupportClassificationSuggestion | null>(null),
    classificationPending: signal(false),
    classificationNotice: signal<string | null>(null),
    classificationGeneration: signal(0),
    classificationAvailable: signal(true),
    requestClassification: vi.fn(),
    discardClassification: vi.fn(),
    resolve: vi.fn(),
  };
  const params = new BehaviorSubject(convertToParamMap(detail ? { id } : {}));
  TestBed.configureTestingModule({
    imports: [SupportPageComponent],
    providers: [
      provideRouter([]),
      { provide: SupportStateService, useValue: state },
      {
        provide: ActivatedRoute,
        useValue: {
          paramMap: params,
          snapshot: { data: { supportMode: mode }, queryParamMap: convertToParamMap(orderId ? { orderId } : {}) },
        },
      },
    ],
  });
  return { state, status, fixture: TestBed.createComponent(SupportPageComponent) };
}
describe('SupportPageComponent', () => {
  it('preserves only the validated order resource in a separate customer login return', async () => {
    const { fixture, state, status } = setup('customer', false, id);
    status.set({ kind: 'error', message: 'Login required' });
    fixture.detectChanges();
    await fixture.whenStable();
    state.auth.beginLogin.mockResolvedValue(false);
    await fixture.componentInstance.login();
    expect(state.auth.beginLogin).toHaveBeenCalledWith('/support?orderId=' + id);
  });
  it('renders untrusted title/body as escaped text with clear operator provenance', async () => {
    const { fixture } = setup('customer', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('<script>alert(1)</script>');
    expect(element.textContent).toContain('Оператор Travel');
    expect(element.querySelector('img,script')).toBeNull();
    expect(element.querySelector('#support-reply-body')?.getAttribute('aria-describedby')).toBe('reply-help');
  });
  it('clears drafts and hides private content when the same subject loses access', async () => {
    const { fixture, state, status } = setup('customer', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.body.set('Private draft');
    state.auth.identityEpoch.update((v) => v + 1);
    status.set({ kind: 'error', message: 'Denied' });
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.body()).toBe('');
    expect(fixture.nativeElement.textContent).not.toContain(row.title);
    expect(fixture.nativeElement.querySelector('#support-reply-body')).toBeNull();
  });
  it('shows honest queue errors and exposes operator controls only in operator context', async () => {
    const { fixture, state } = setup('operator', false);
    state.cases.set([]);
    state.error.set('Сервис недоступен');
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Сервис недоступен');
    expect(fixture.nativeElement.textContent).not.toContain('пока нет обращений');
    expect(fixture.nativeElement.querySelector('[data-testid="support-create"]')).toBeNull();
  });
  it('retains a failed order-link draft and requires an explicit new unlinked action', async () => {
    const { fixture, state } = setup('customer', false);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.title.set('Question');
    fixture.componentInstance.body.set('Private draft');
    fixture.componentInstance.orderId.set(id);
    state.orderLinkRejected.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.body()).toBe('Private draft');
    expect(fixture.nativeElement.querySelector('[data-testid="support-create"]').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="support-create-unlinked"]')).not.toBeNull();
    expect(state.createWithoutOrder).not.toHaveBeenCalled();
  });
});

describe('Support operator reviewed suggestion draft', () => {
  function candidate() {
    return {
      caseId: id,
      requestId: id,
      capturedRevision: 3,
      generatedAt: new Date().toISOString(),
      expiresAt: new Date(Date.now() + 120000).toISOString(),
      status: 'Candidate',
      reason: 'Ready',
      category: 'Refund',
      queue: 'Refund',
      servingProfileId: 'a'.repeat(64),
      questionVersion: 'support-category-v1',
      urgency: {
        kind: 'High',
        nextScheduledDepartureAt: new Date(Date.now() + 3600000).toISOString(),
        observedAt: new Date().toISOString(),
        streamVersion: 4,
        ruleVersion: 'support-urgency-v1',
      },
    } as const;
  }
  it('prefills category only and requires a separate manual save, with no employee assignment or accuracy claim', async () => {
    const { fixture, state } = setup('operator', true);
    fixture.detectChanges();
    await fixture.whenStable();
    state.suggestion.set(candidate());
    fixture.detectChanges();
    await fixture.whenStable();
    expect(state.requestClassification).not.toHaveBeenCalled();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Фильтр очереди');
    expect(element.textContent).toContain('не назначает сотрудника');
    (element.querySelector('[data-testid="support-suggestion-prefill"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.category()).toBe('Refund');
    expect(fixture.componentInstance.priority()).toBe('Normal');
    expect(state.triage).not.toHaveBeenCalled();
    (element.querySelector('[data-testid="support-triage"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(state.triage).toHaveBeenCalledWith('Refund', 'Normal');
  });
  it('restores an unedited recommendation prefill to saved values on invalidation', async () => {
    const { fixture, state } = setup('operator', true);
    fixture.detectChanges();
    await fixture.whenStable();
    state.suggestion.set(candidate());
    fixture.detectChanges();
    fixture.componentInstance.prefillSuggestion();
    state.suggestion.set(null);
    state.classificationGeneration.update((v) => v + 1);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.category()).toBe('Unclassified');
    expect(fixture.componentInstance.priority()).toBe('Normal');
  });
  it('retains an explicitly corrected draft after expiry but requires review before save', async () => {
    const { fixture, state } = setup('operator', true);
    fixture.detectChanges();
    await fixture.whenStable();
    state.suggestion.set(candidate());
    fixture.detectChanges();
    fixture.componentInstance.prefillSuggestion();
    fixture.componentInstance.editCategory('Payment');
    state.suggestion.set(null);
    state.classificationGeneration.update((v) => v + 1);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.category()).toBe('Payment');
    expect(fixture.componentInstance.draftReviewRequired()).toBe(true);
    expect(fixture.nativeElement.querySelector('[data-testid="support-triage"]')?.disabled).toBe(true);
    await fixture.componentInstance.saveTriage();
    expect(state.triage).not.toHaveBeenCalled();
    fixture.componentInstance.reviewDraft();
    await fixture.componentInstance.saveTriage();
    expect(state.triage).toHaveBeenCalledWith('Payment', 'Normal');
  });
  it('does not expose classification controls to customer views', async () => {
    const { fixture } = setup('customer', true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[data-testid="support-classify"]')).toBeNull();
  });
});

it('preserves an independent manual draft when a classification request or response changes', async () => {
  const { fixture, state } = setup('operator', true);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.componentInstance.editCategory('Payment');
  fixture.componentInstance.editPriority('High');
  state.classificationGeneration.update((v) => v + 1);
  fixture.detectChanges();
  await fixture.whenStable();
  expect(fixture.componentInstance.category()).toBe('Payment');
  expect(fixture.componentInstance.priority()).toBe('High');
  expect(fixture.componentInstance.draftReviewRequired()).toBe(false);
});

it('prefilling category preserves an already explicit priority choice and reviews it after expiry', async () => {
  const { fixture, state } = setup('operator', true);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.componentInstance.editPriority('High');
  state.suggestion.set({
    caseId: id,
    requestId: id,
    capturedRevision: 3,
    generatedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 120000).toISOString(),
    status: 'Candidate',
    reason: 'Ready',
    category: 'Refund',
    queue: 'Refund',
    servingProfileId: 'a'.repeat(64),
    questionVersion: 'support-category-v1',
    urgency: {
      kind: 'Unknown',
      nextScheduledDepartureAt: null,
      observedAt: null,
      streamVersion: null,
      ruleVersion: 'support-urgency-v1',
    },
  });
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.componentInstance.prefillSuggestion();
  expect(fixture.componentInstance.priority()).toBe('High');
  state.suggestion.set(null);
  state.classificationGeneration.update((v) => v + 1);
  fixture.detectChanges();
  await fixture.whenStable();
  expect(fixture.componentInstance.priority()).toBe('High');
  expect(fixture.componentInstance.draftReviewRequired()).toBe(true);
});

it.each([true, false])(
  'requires a new review for a newer revision of a retained edited draft (render=%s)',
  async (renderAfterRefresh) => {
    const { fixture, state } = setup('operator', true);
    state.selected.set({ ...row, revision: 5 });
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;
    state.suggestion.set({
      caseId: id,
      requestId: id,
      capturedRevision: 5,
      generatedAt: new Date().toISOString(),
      expiresAt: new Date(Date.now() + 120000).toISOString(),
      status: 'Candidate',
      reason: 'Ready',
      category: 'Refund',
      queue: 'Refund',
      servingProfileId: 'a'.repeat(64),
      questionVersion: 'support-category-v1',
      urgency: {
        kind: 'Unknown',
        nextScheduledDepartureAt: null,
        observedAt: null,
        streamVersion: null,
        ruleVersion: 'support-urgency-v1',
      },
    });
    fixture.detectChanges();
    await fixture.whenStable();
    component.prefillSuggestion();
    component.editCategory('Payment');
    state.suggestion.set(null);
    state.classificationGeneration.update((v) => v + 1);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(component.draftReviewRequired()).toBe(true);
    component.reviewDraft();
    expect(component.draftReviewRequired()).toBe(false);
    state.refresh.mockImplementation(async () => {
      state.classificationGeneration.update((v) => v + 1);
      state.selected.set({ ...row, revision: 6, category: 'Ticketing', priority: 'High', triageSource: 'Operator' });
    });
    await state.refresh();
    if (renderAfterRefresh) {
      fixture.detectChanges();
      await fixture.whenStable();
      expect(fixture.nativeElement.querySelector('[data-testid="support-triage"]')?.disabled).toBe(true);
    }
    expect(component.category()).toBe('Payment');
    expect(component.priority()).toBe('Normal');
    await component.saveTriage();
    expect(state.triage).not.toHaveBeenCalled();
    expect(component.draftReviewRequired()).toBe(true);
    component.reviewDraft();
    expect(component.draftReviewRequired()).toBe(false);
    await component.saveTriage();
    expect(state.triage).toHaveBeenCalledWith('Payment', 'Normal');
  },
);
