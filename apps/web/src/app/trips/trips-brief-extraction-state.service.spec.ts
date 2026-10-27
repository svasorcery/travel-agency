import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  type TripBrief,
  type TripBriefCandidate,
  TripsBriefApiService,
  TripsBriefRequestError,
} from '@travel/api-client';
import { of, Subject, throwError } from 'rxjs';
import { TRIPS_BRIEF_MONOTONIC, TripsBriefExtractionStateService } from './trips-brief-extraction-state.service';
import { TripsStateService } from './trips-state.service';

const policy = '456197be02fdb6164803e7f4394a9434051acc4375a3fd8549696feec0a5e8d9';
const cap = {
  candidateVersion: 1 as const,
  status: 'configured' as const,
  processorId: 'anthropic',
  modelId: 'fictional-model',
  processingPolicyVersion: policy,
};
const original: TripBrief = {
  destinationId: 'moscow',
  originId: 'kaliningrad',
  startDate: '2026-11-01',
  endDate: '2026-11-02',
  adults: 1,
  currency: 'USD',
  totalBudget: '100.00',
  interests: [],
  pace: 'balanced',
};
function candidate(requestId: string): TripBriefCandidate {
  return {
    candidateVersion: 1,
    requestId,
    processingPolicyVersion: policy,
    referenceInstant: '2026-10-27T00:00:00Z',
    referenceZone: 'Europe/Moscow',
    referenceLocalDate: '2026-10-27',
    expiresAt: '2026-10-27T00:10:00Z',
    fields: {
      destinationId: {
        state: 'extracted',
        value: 'saint-petersburg',
        evidence: [{ start: 0, end: 5 }],
        alternatives: [],
      },
      startDate: { state: 'extracted', value: '2026-11-10', evidence: [{ start: 0, end: 5 }], alternatives: [] },
      endDate: { state: 'extracted', value: '2026-11-12', evidence: [{ start: 0, end: 5 }], alternatives: [] },
      durationDays: { state: 'extracted', value: 3, evidence: [{ start: 0, end: 5 }], alternatives: [] },
      adults: { state: 'ambiguous', value: null, evidence: [{ start: 0, end: 5 }], alternatives: [2] },
      interests: { state: 'missing', value: null, evidence: [], alternatives: [] },
      pace: { state: 'missing', value: null, evidence: [], alternatives: [] },
    },
    issues: [],
  };
}
async function setup() {
  let epoch = 0,
    now = 0;
  const scope = signal<object | null>({});
  const editor = {
    itineraryContext: scope,
    auth: { accessToken: vi.fn().mockResolvedValue('token') },
    captureBriefTicket: () => ({ epoch }),
    isBriefTicketCurrent: (ticket: { epoch: number }) => ticket.epoch === epoch && scope() !== null,
    readBriefOwner: vi.fn().mockResolvedValue(true),
  };
  const api = {
    capability: vi.fn().mockReturnValue(of(cap)),
    candidate: vi.fn().mockImplementation((request: { requestId: string }) => of(candidate(request.requestId))),
  };
  TestBed.configureTestingModule({
    providers: [
      TripsBriefExtractionStateService,
      { provide: TripsStateService, useValue: editor },
      { provide: TripsBriefApiService, useValue: api },
      { provide: TRIPS_BRIEF_MONOTONIC, useValue: () => now },
    ],
  });
  const flow = TestBed.inject(TripsBriefExtractionStateService);
  TestBed.tick();
  flow.setDraft(structuredClone(original));
  await flow.refreshCapability();
  flow.textChanged('Питер');
  const transfer = vi.fn();
  flow.transferred.subscribe(transfer);
  return { flow, api, editor, transfer, edit: () => epoch++, clock: (value: number) => (now = value), scope };
}
describe('brief transient epochs and confirmation', () => {
  it('requires a reviewed processor digest, rejects unknown processor even with public metadata', async () => {
    const { flow, api } = await setup();
    api.capability.mockReturnValue(of({ ...cap, processorId: 'unknown' }));
    await flow.refreshCapability();
    expect(flow.capability()).toBeNull();
    expect(flow.canExtract()).toBe(false);
  });
  it('dispatches once, discards a late result after editor changes and never emits', async () => {
    const { flow, api, edit, transfer } = await setup();
    const response = new Subject<TripBriefCandidate>();
    api.candidate.mockReturnValue(response);
    const run = flow.extract();
    await Promise.resolve();
    await flow.extract();
    expect(api.candidate).toHaveBeenCalledTimes(1);
    const id = api.candidate.mock.calls[0][0].requestId;
    edit();
    response.next(candidate(id));
    response.complete();
    await run;
    expect(flow.candidate()).toBeNull();
    expect(transfer).not.toHaveBeenCalled();
  });
  it.each(['text', 'zone', 'draft', 'scope', 'block'] as const)(
    'clears review after %s changes, including same-looking values',
    async (key) => {
      const { flow, scope } = await setup();
      await flow.extract();
      expect(flow.candidate()).not.toBeNull();
      if (key === 'text') flow.textChanged('Питер');
      if (key === 'zone') flow.zoneChanged('Europe/Moscow');
      if (key === 'draft') flow.setDraft(structuredClone(original));
      if (key === 'scope') {
        scope.set({});
        TestBed.tick();
      }
      if (key === 'block') flow.setBlocked(true);
      expect(flow.candidate()).toBeNull();
    },
  );
  it('does not overwrite explicit end on start correction and requires deliberate duration reconciliation', async () => {
    const { flow } = await setup();
    await flow.extract();
    flow.correct('startDate', '2026-11-20');
    expect(flow.review().endDate).toBe('2026-11-12');
    flow.deriveEnd();
    expect(flow.review().endDate).toBe('2026-11-12');
    flow.reconcile(true);
    flow.deriveEnd();
    expect(flow.review().endDate).toBe('2026-11-22');
    expect(flow.corrected().endDate).toBe(true);
  });
  it('blocks atomic dates for overlong, incomplete, reversed or invalid ranges without affecting other selections', async () => {
    const { flow } = await setup();
    await flow.extract();
    for (const [start, end] of [
      ['2026-01-01', '2026-01-15'],
      ['2026-01-01', ''],
      ['2026-01-03', '2026-01-01'],
      ['2026-02-30', '2026-03-01'],
    ]) {
      flow.correct('startDate', start);
      flow.correct('endDate', end);
      flow.reconcile(true);
      expect(flow.valid('dates')).toBe(false);
    }
    expect(flow.valid('destinationId')).toBe(true);
  });
  it('blocks affected conflict slots until explicit correction', async () => {
    const { flow, api } = await setup();
    api.candidate.mockImplementation(({ requestId }) => {
      const value = candidate(requestId);
      value.issues = [{ code: 'destinationChoice', slots: ['destinationId'], evidence: [{ start: 0, end: 5 }] }];
      return of(value);
    });
    await flow.extract();
    expect(flow.valid('destinationId')).toBe(false);
    flow.correct('destinationId', 'kaliningrad');
    expect(flow.valid('destinationId')).toBe(true);
  });
  it.each(['owner', 'policy', 'edit', 'review', 'failure'] as const)(
    'rechecks %s after confirmation await and preserves manual values',
    async (key) => {
      const { flow, api, editor, transfer, edit } = await setup();
      await flow.extract();
      flow.select('destinationId', true);
      const capability = new Subject<typeof cap>();
      api.capability.mockReturnValue(capability);
      if (key === 'owner') editor.readBriefOwner.mockResolvedValue(false);
      if (key === 'failure') editor.readBriefOwner.mockRejectedValue(new Error('PRIVATE ERROR'));
      const run = flow.confirm();
      await Promise.resolve();
      await flow.confirm();
      if (key === 'edit') edit();
      if (key === 'review') flow.correct('destinationId', 'kaliningrad');
      capability.next(key === 'policy' ? { ...cap, modelId: 'changed-model' } : cap);
      capability.complete();
      await run;
      expect(transfer).not.toHaveBeenCalled();
      expect(flow.draft()).toEqual(original);
      expect(flow.message()).not.toContain('PRIVATE');
      expect(api.candidate).toHaveBeenCalledTimes(1);
    },
  );
  it('blocks expired, backwards and uncertain clocks without browser wall-clock authority', async () => {
    for (const elapsed of [600000, -1, Number.NaN]) {
      TestBed.resetTestingModule();
      const { flow, clock, transfer } = await setup();
      await flow.extract();
      flow.select('destinationId', true);
      clock(elapsed);
      await flow.confirm();
      expect(transfer).not.toHaveBeenCalled();
      expect(flow.candidate()).toBeNull();
    }
  });
  it('suspension and owner leave release private references and stale callbacks', async () => {
    const { flow } = await setup();
    await flow.extract();
    flow.suspend();
    expect(flow.text()).toBe('');
    expect(flow.candidate()).toBeNull();
    expect(flow.capability()).toBeNull();
  });
  it('keeps empty successful reviews distinct from typed unavailable without retries', async () => {
    const { flow, api } = await setup();
    api.candidate.mockReturnValue(throwError(() => new TripsBriefRequestError('busy')));
    await flow.extract();
    expect(flow.phase()).toBe('busy');
    expect(flow.candidate()).toBeNull();
    expect(flow.text()).toBe('');
    expect(api.candidate).toHaveBeenCalledTimes(1);
  });
  it('consumes a selected partial transfer once while retaining deferred origin and exact money', async () => {
    const { flow, transfer } = await setup();
    await flow.extract();
    flow.correct('adults', 3);
    flow.select('adults', true);
    await flow.confirm();
    await flow.confirm();
    expect(transfer).toHaveBeenCalledTimes(1);
    expect(transfer).toHaveBeenCalledWith({ ...original, adults: 3 });
    expect(flow.candidate()).toBeNull();
    expect(flow.text()).toBe('');
  });
});

it('reports private edits synchronously across raw text, pending, corrected review and consumed transfer', async () => {
  const { flow, api, transfer } = await setup();
  expect(flow.unsaved()).toBe(true);
  flow.textChanged('');
  expect(flow.unsaved()).toBe(false);
  flow.textChanged('Питер');
  const response = new Subject<TripBriefCandidate>();
  api.candidate.mockReturnValue(response);
  const running = flow.extract();
  await Promise.resolve();
  expect(flow.pending()).toBe(true);
  expect(flow.unsaved()).toBe(true);
  response.next(candidate(api.candidate.mock.calls[0][0].requestId));
  response.complete();
  await running;
  flow.correct('adults', 3);
  flow.select('adults', true);
  expect(flow.unsaved()).toBe(true);
  await flow.confirm();
  expect(transfer).toHaveBeenCalledTimes(1);
  expect(flow.unsaved()).toBe(false);
});
it('explicit discard clears pending private work and prevents a late response from restoring unsaved state', async () => {
  const { flow, api, transfer } = await setup();
  const response = new Subject<TripBriefCandidate>();
  api.candidate.mockReturnValue(response);
  const running = flow.extract();
  await Promise.resolve();
  const id = api.candidate.mock.calls[0][0].requestId;
  flow.leave();
  expect(flow.unsaved()).toBe(false);
  response.next(candidate(id));
  response.complete();
  await running;
  expect(flow.unsaved()).toBe(false);
  expect(flow.candidate()).toBeNull();
  expect(transfer).not.toHaveBeenCalled();
});
