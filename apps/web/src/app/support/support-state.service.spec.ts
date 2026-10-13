import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SupportApiService, type SupportCase, type SupportReceipt } from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
import { SupportAuthService } from './support-auth.service';
import { SupportStateService } from './support-state.service';

const owner = '11111111-1111-4111-8111-111111111111',
  id = '44444444-4444-4444-8444-444444444444',
  date = '2030-01-01T00:00:00Z';
const row: SupportCase = {
  caseId: id,
  ownerId: owner,
  title: 'Private title',
  state: 'Open',
  revision: 5,
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
function setup() {
  const auth = {
    isDemo: false,
    mode: signal<'customer' | 'operator'>('customer'),
    status: signal<{ kind: 'authenticated'; userId: string } | { kind: 'error'; message: string }>({
      kind: 'authenticated',
      userId: owner,
    }),
    identityEpoch: signal(0),
    selectMode: vi.fn(),
    hasOidcCallback: () => false,
    accessToken: vi.fn().mockResolvedValue('token'),
    deny: vi.fn((epoch: number) => {
      if (epoch === auth.identityEpoch()) {
        auth.identityEpoch.update((v) => v + 1);
        auth.status.set({ kind: 'error', message: 'Denied' });
      }
    }),
  };
  const api = {
    list: vi.fn().mockReturnValue(of({ items: [row], nextCursor: null })),
    detail: vi.fn().mockReturnValue(of(row)),
    messages: vi.fn().mockReturnValue(of({ caseId: id, items: [], nextCursor: null })),
    reply: vi.fn(),
    control: vi.fn(),
    create: vi.fn(),
    receipt: vi.fn(),
    classificationSuggestion: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      { provide: SupportAuthService, useValue: auth },
      { provide: SupportApiService, useValue: api },
    ],
  });
  return { auth, api, state: TestBed.inject(SupportStateService) };
}
describe('Support private state and unknown writes', () => {
  it('hides a private view synchronously when its identity generation changes', async () => {
    const { auth, state } = setup();
    await state.enter('customer', id);
    expect(state.authorized()).toBe(true);
    auth.identityEpoch.update((v) => v + 1);
    expect(state.authorized()).toBe(false);
    TestBed.tick();
    expect(state.selected()).toBeNull();
  });
  it('retains a lost reply barrier after negative receipt reads and another message page', async () => {
    const { api, state } = setup();
    await state.enter('customer', id);
    api.reply.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await state.reply('Private draft', false);
    expect(state.unresolved().length).toBe(1);
    for (const status of [404, 503]) {
      api.receipt.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
      await state.reconcile(state.unresolved()[0]);
      expect(state.unresolved().length).toBe(1);
    }
    await state.refresh();
    await state.reply('Do not repeat', false);
    expect(api.reply).toHaveBeenCalledTimes(1);
  });
  it('acknowledges an old positive receipt without replacing a newer snapshot', async () => {
    const { api, state } = setup();
    await state.enter('customer', id);
    api.reply.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await state.reply('Question', false);
    const action = state.unresolved()[0];
    api.receipt.mockReturnValue(
      of({
        caseId: id,
        actionId: action.actionId,
        kind: 'CustomerReply',
        appliedRevision: 2,
        appliedState: 'Open',
        appliedAt: date,
        messageId: action.actionId,
      } satisfies SupportReceipt),
    );
    await state.reconcile(action);
    expect(state.unresolved()).toEqual([]);
    expect(state.selected()?.revision).toBe(5);
  });
  it('hides missing or purged content while preserving unknown action metadata', async () => {
    const { api, state } = setup();
    await state.enter('customer', id);
    api.reply.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await state.reply('Question', false);
    api.detail.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    await state.refresh();
    expect(state.selected()).toBeNull();
    expect(state.messages()).toEqual([]);
    expect(state.unresolved()).toHaveLength(1);
  });
  it.each([
    [404, 'Support.OrderLinkNotFound'],
    [503, 'Support.OrderLinkUnavailable'],
  ])('allows explicit fresh unlinked creation only for pre-commit order-link rejection %s', async (status, code) => {
    const { api, state } = setup();
    await state.enter('customer', null);
    api.create.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({ status: status as number, error: { type: 'https://travel.local/errors/' + code } }),
      ),
    );
    await state.create('Title', 'Private draft', id);
    expect(state.orderLinkRejected()).toBe(true);
    expect(state.unresolved()).toEqual([]);
    expect(api.create).toHaveBeenCalledTimes(1);
    await state.create('Title', 'Private draft', id);
    expect(api.create).toHaveBeenCalledTimes(1);
    api.create.mockImplementation((caseId: string, body: { messageId: string }) =>
      of({
        caseId,
        actionId: body.messageId,
        kind: 'Create',
        appliedRevision: 1,
        appliedState: 'Open',
        appliedAt: date,
        messageId: body.messageId,
      }),
    );
    await state.createWithoutOrder('Title', 'Private draft');
    expect(api.create).toHaveBeenCalledTimes(2);
    expect(api.create.mock.calls[1][0]).not.toBe(api.create.mock.calls[0][0]);
    expect(api.create.mock.calls[1][1].orderId).toBeNull();
    expect(api.create.mock.calls[1][1].body).toBe('Private draft');
  });
  it.each([404, 503])(
    'does not treat a generic create failure %s as permission to create without a link',
    async (status) => {
      const { api, state } = setup();
      await state.enter('customer', null);
      api.create.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status, error: { type: 'Support.Unavailable' } })),
      );
      await state.create('Title', 'Draft', id);
      expect(state.orderLinkRejected()).toBe(false);
      expect(state.unresolved()).toHaveLength(1);
      await state.createWithoutOrder('Title', 'Draft');
      expect(api.create).toHaveBeenCalledTimes(1);
    },
  );
  it('erases private context on current 403 but ignores an old session 403', async () => {
    const { auth, api, state } = setup();
    await state.enter('customer', id);
    const old = new Subject<SupportCase>();
    api.detail.mockReturnValueOnce(old);
    const read = state.refresh();
    await Promise.resolve();
    auth.identityEpoch.update((v) => v + 1);
    await state.enter('customer', id);
    old.error(new HttpErrorResponse({ status: 403 }));
    await read;
    expect(auth.deny).not.toHaveBeenCalled();
    expect(state.selected()?.title).toBe('Private title');
    api.detail.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    await state.refresh();
    expect(state.selected()).toBeNull();
    expect(state.messages()).toEqual([]);
    expect(auth.deny).toHaveBeenCalledTimes(1);
  });
  it('does not accept late private reads after navigation', async () => {
    const { api, state } = setup(),
      late = new Subject<SupportCase>();
    api.detail.mockReturnValue(late);
    const read = state.enter('customer', id);
    await Promise.resolve();
    await state.enter('customer', null);
    late.next(row);
    late.complete();
    await read;
    expect(state.selected()).toBeNull();
  });
  it('erases private context when a parallel current read denies after another read fails', async () => {
    const { auth, api, state } = setup();
    await state.enter('customer', id);
    const messages = new Subject();
    api.detail.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    api.messages.mockReturnValue(messages);
    await state.refresh();
    expect(state.selected()).not.toBeNull();
    messages.error(new HttpErrorResponse({ status: 403 }));
    await Promise.resolve();
    await Promise.resolve();
    expect(auth.deny).toHaveBeenCalledTimes(1);
    expect(state.selected()).toBeNull();
  });
});

describe('Support explicit private classification', () => {
  async function operator() {
    const context = setup();
    context.auth.mode.set('operator');
    await context.state.enter('operator', id);
    return context;
  }
  function suggestion(requestId: string) {
    return {
      caseId: id,
      requestId,
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
    } as const;
  }
  it('never requests on reads or in customer mode, and only classifies an explicit operator click', async () => {
    const { api, state } = await operator();
    await state.refresh();
    expect(api.classificationSuggestion).not.toHaveBeenCalled();
    api.classificationSuggestion.mockImplementation((_id: string, requestId: string) => of(suggestion(requestId)));
    await state.requestClassification();
    expect(state.suggestion()?.category).toBe('Refund');
    expect(state.selected()?.category).toBe('Unclassified');
    expect(state.unresolved()).toEqual([]);
    await state.enter('customer', id);
    await state.requestClassification();
    expect(api.classificationSuggestion).toHaveBeenCalledTimes(1);
  });
  it('discards late completion after even a same-revision refresh and allows manual triage during inference', async () => {
    const { api, state } = await operator();
    const late = new Subject();
    api.classificationSuggestion.mockReturnValue(late);
    const request = state.requestClassification();
    await Promise.resolve();
    expect(state.classificationPending()).toBe(true);
    expect(state.writeBlocked()).toBe(false);
    await state.refresh();
    late.next(suggestion(api.classificationSuggestion.mock.calls[0][1]));
    late.complete();
    await request;
    expect(state.suggestion()).toBeNull();
    expect(state.classificationPending()).toBe(false);
  });
  it.each(['leave', 'epoch', 'revision'])('rejects old inference after %s changes', async (kind) => {
    const { api, auth, state } = await operator();
    const late = new Subject();
    api.classificationSuggestion.mockReturnValue(late);
    const request = state.requestClassification();
    await Promise.resolve();
    if (kind === 'leave') state.leave();
    if (kind === 'epoch') auth.identityEpoch.update((v) => v + 1);
    if (kind === 'revision') state.selected.set({ ...row, revision: 6 });
    late.next(suggestion(api.classificationSuggestion.mock.calls[0][1]));
    late.complete();
    await request;
    expect(state.suggestion()).toBeNull();
  });
  it('clears a candidate at TTL without automatic replay', async () => {
    const { api, state } = await operator();
    vi.useFakeTimers();
    try {
      api.classificationSuggestion.mockImplementation((_id: string, requestId: string) => of(suggestion(requestId)));
      await state.requestClassification();
      expect(state.suggestion()).not.toBeNull();
      vi.advanceTimersByTime(120001);
      expect(state.suggestion()).toBeNull();
      expect(api.classificationSuggestion).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
  it.each([0, 503])('keeps manual actions available and does not retry an uncertain inference %s', async (status) => {
    const { api, state } = await operator();
    api.classificationSuggestion.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
    await state.requestClassification();
    expect(state.classificationNotice()).toContain('могло выполниться');
    expect(state.writeBlocked()).toBe(false);
    expect(api.classificationSuggestion).toHaveBeenCalledTimes(1);
    expect(state.unresolved()).toEqual([]);
  });
  it('does not release unknown S1 writes and disables classification while a write is pending', async () => {
    const { api, state } = await operator();
    const pending = new Subject<SupportReceipt>();
    api.reply.mockReturnValue(pending);
    const reply = state.reply('Manual answer', false);
    await Promise.resolve();
    await state.requestClassification();
    expect(api.classificationSuggestion).not.toHaveBeenCalled();
    pending.error(new HttpErrorResponse({ status: 0 }));
    await reply;
    await state.requestClassification();
    expect(api.classificationSuggestion).not.toHaveBeenCalled();
    expect(state.unresolved()).toHaveLength(1);
  });
  it.each([401, 403])('erases private state on current classification denial %s', async (status) => {
    const { api, state, auth } = await operator();
    api.classificationSuggestion.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
    await state.requestClassification();
    expect(state.selected()).toBeNull();
    expect(auth.deny).toHaveBeenCalledTimes(1);
  });
  it('refuses resolved cases', async () => {
    const { api, state } = await operator();
    state.selected.set({ ...row, state: 'Resolved' });
    await state.requestClassification();
    expect(api.classificationSuggestion).not.toHaveBeenCalled();
  });
});

describe('Support classification supersession and manual independence', () => {
  async function operator() {
    const context = setup();
    context.auth.mode.set('operator');
    await context.state.enter('operator', id);
    return context;
  }
  function candidate(requestId: string, category: 'Refund' | 'Payment' = 'Refund') {
    return {
      caseId: id,
      requestId,
      capturedRevision: 5,
      generatedAt: new Date().toISOString(),
      expiresAt: new Date(Date.now() + 120000).toISOString(),
      status: 'Candidate',
      reason: 'Ready',
      category,
      queue: category,
      servingProfileId: 'a'.repeat(64),
      questionVersion: 'support-category-v1',
      urgency: {
        kind: 'Unknown',
        nextScheduledDepartureAt: null,
        observedAt: null,
        streamVersion: null,
        ruleVersion: 'support-urgency-v1',
      },
    } as const;
  }
  it('does not let an older completion replace an explicit newer request', async () => {
    const { state, api } = await operator();
    const old = new Subject();
    api.classificationSuggestion
      .mockReturnValueOnce(old)
      .mockImplementation((_id: string, requestId: string) => of(candidate(requestId, 'Payment')));
    const first = state.requestClassification();
    await Promise.resolve();
    state.discardClassification();
    await state.requestClassification();
    old.next(candidate(api.classificationSuggestion.mock.calls[0][1]));
    old.complete();
    await first;
    expect(state.suggestion()?.category).toBe('Payment');
    expect(api.classificationSuggestion).toHaveBeenCalledTimes(2);
    expect(api.classificationSuggestion.mock.calls[0][1]).not.toBe(api.classificationSuggestion.mock.calls[1][1]);
  });
  it('never releases an uncertain S1 action when already-running inference finishes', async () => {
    const { state, api } = await operator();
    const inference = new Subject();
    api.classificationSuggestion.mockReturnValue(inference);
    const classification = state.requestClassification();
    await Promise.resolve();
    api.reply.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await state.reply('Manual operator answer', false);
    expect(state.unresolved()).toHaveLength(1);
    inference.next(candidate(api.classificationSuggestion.mock.calls[0][1]));
    inference.complete();
    await classification;
    expect(state.suggestion()).toBeNull();
    expect(state.unresolved()).toHaveLength(1);
    expect(state.classificationAvailable()).toBe(false);
    expect(state.writeBlocked()).toBe(true);
  });
  it('ignores an old-session classifier denial after a fresh same-subject login', async () => {
    const { state, api, auth } = await operator();
    const old = new Subject();
    api.classificationSuggestion.mockReturnValue(old);
    const request = state.requestClassification();
    await Promise.resolve();
    auth.identityEpoch.update((v) => v + 1);
    await state.enter('operator', id);
    old.error(new HttpErrorResponse({ status: 403 }));
    await request;
    expect(auth.deny).not.toHaveBeenCalled();
    expect(state.selected()?.caseId).toBe(id);
  });
  it.each([429, 409])('handles classifier rejection %s without S1 mutation barriers', async (status) => {
    const { state, api } = await operator();
    api.classificationSuggestion.mockReturnValue(throwError(() => new HttpErrorResponse({ status })));
    await state.requestClassification();
    expect(state.unresolved()).toEqual([]);
    expect(state.reviewRequired()).toBe(status === 409);
    expect(state.writeBlocked()).toBe(status === 409);
  });
  it('keeps malformed candidate text out of private preview and notices', async () => {
    const { state, api } = await operator();
    api.classificationSuggestion.mockImplementation((_id: string, requestId: string) =>
      of({ ...candidate(requestId), category: '<private response>' }),
    );
    await state.requestClassification();
    expect(state.suggestion()).toBeNull();
    expect(state.classificationNotice()).not.toContain('<private response>');
    expect(state.writeBlocked()).toBe(false);
  });
});
