import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import type { SupportCase } from '@travel/api-client';
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
