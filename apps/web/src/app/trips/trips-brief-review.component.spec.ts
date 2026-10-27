import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  type TripBriefCandidate,
  type TripBriefCapability,
  TripsApiService,
  TripsBriefApiService,
} from '@travel/api-client';
import { of, Subject } from 'rxjs';
import { TRIPS_BRIEF_MONOTONIC, TripsBriefReviewComponent } from './trips-brief-review.component';
import { TripsStateService } from './trips-state.service';

const policy = '456197be02fdb6164803e7f4394a9434051acc4375a3fd8549696feec0a5e8d9';
const brief = {
  destinationId: 'moscow',
  originId: null,
  startDate: '2026-11-01',
  endDate: '2026-11-02',
  adults: 1,
  currency: 'RUB',
  totalBudget: '100.00',
  interests: [],
  pace: 'balanced',
} as const;
function candidate(): TripBriefCandidate {
  return {
    candidateVersion: 1,
    requestId: '11111111-1111-4111-8111-111111111111',
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
      startDate: { state: 'missing', value: null, evidence: [], alternatives: [] },
      endDate: { state: 'missing', value: null, evidence: [], alternatives: [] },
      durationDays: { state: 'extracted', value: 3, evidence: [{ start: 0, end: 5 }], alternatives: [] },
      adults: { state: 'ambiguous', value: null, evidence: [{ start: 0, end: 5 }], alternatives: [2] },
      interests: { state: 'missing', value: null, evidence: [], alternatives: [] },
      pace: { state: 'missing', value: null, evidence: [], alternatives: [] },
    },
    issues: [],
  };
}
async function setup() {
  let current = true,
    now = 0;
  const scope = signal<object | null>({});
  const editor = {
    itineraryContext: scope,
    baseline: signal(null),
    selectedId: signal(null),
    buffer: signal({ brief }),
    auth: { accessToken: vi.fn().mockResolvedValue('token') },
    captureBriefTicket: () => (current ? {} : null),
    isBriefTicketCurrent: () => current,
    readBriefOwner: vi.fn().mockResolvedValue(true),
  };
  const cap: TripBriefCapability = {
    candidateVersion: 1,
    status: 'configured',
    processorId: 'anthropic',
    modelId: 'fictional-model',
    processingPolicyVersion: policy,
  };
  const api = {
    capability: vi.fn().mockReturnValue(of(cap)),
    candidate: vi
      .fn()
      .mockImplementation((request: { requestId: string }) => of({ ...candidate(), requestId: request.requestId })),
  };
  TestBed.configureTestingModule({
    imports: [TripsBriefReviewComponent],
    providers: [
      { provide: TripsStateService, useValue: editor },
      { provide: TripsBriefApiService, useValue: api },
      { provide: TRIPS_BRIEF_MONOTONIC, useValue: () => now },
    ],
  });
  const f = TestBed.createComponent(TripsBriefReviewComponent);
  f.componentRef.setInput('draft', structuredClone(brief));
  await f.whenStable();
  const c = f.componentInstance.flow;
  return {
    f,
    c,
    api,
    editor,
    setCurrent: (value: boolean) => (current = value),
    setNow: (value: number) => (now = value),
    cap,
  };
}
describe('brief separate review', () => {
  it('renders exactly five unchecked choices and keeps a duration-only candidate away from existing dates', async () => {
    const { f, c } = await setup();
    c.capability.set({
      candidateVersion: 1,
      status: 'configured',
      processorId: 'anthropic',
      modelId: 'fictional-model',
      processingPolicyVersion: policy,
    });
    c.textChanged('Питер');
    await c.extract();
    await f.whenStable();
    expect(f.nativeElement.querySelectorAll('[data-transfer]')).toHaveLength(5);
    expect(Object.values(c.selected()).every((x) => !x)).toBe(true);
    expect(c.review()['startDate']).toBe('');
    expect(c.review()['endDate']).toBe('');
    expect(f.nativeElement.querySelector('form')).toBeNull();
  });
  it('transfers only explicitly selected corrections and never invokes editor Apply or Save', async () => {
    const { c, editor } = await setup();
    c.capability.set({
      candidateVersion: 1,
      status: 'configured',
      processorId: 'anthropic',
      modelId: 'fictional-model',
      processingPolicyVersion: policy,
    });
    c.textChanged('Питер');
    await c.extract();
    const emitted = vi.fn();
    c.transferred.subscribe(emitted);
    c.correct('adults', 3);
    c.select('adults', true);
    await c.confirm();
    expect(emitted).toHaveBeenCalledWith(
      expect.objectContaining({ adults: 3, destinationId: 'moscow', startDate: '2026-11-01', totalBudget: '100.00' }),
    );
    expect(editor.readBriefOwner).toHaveBeenCalledTimes(1);
    expect(c.candidate()).toBeNull();
  });
  it('discards late replies after an ABA editor change', async () => {
    const { c, api, setCurrent } = await setup();
    c.capability.set({
      candidateVersion: 1,
      status: 'configured',
      processorId: 'anthropic',
      modelId: 'fictional-model',
      processingPolicyVersion: policy,
    });
    const reply = new Subject<TripBriefCandidate>();
    api.candidate.mockReturnValue(reply);
    c.textChanged('Питер');
    const run = c.extract();
    await Promise.resolve();
    setCurrent(false);
    reply.next(candidate());
    reply.complete();
    await run;
    expect(c.candidate()).toBeNull();
  });
  it('invalidates changed policy, elapsed monotonic TTL and suspension at confirmation', async () => {
    const { c, setNow } = await setup();
    c.capability.set({
      candidateVersion: 1,
      status: 'configured',
      processorId: 'anthropic',
      modelId: 'fictional-model',
      processingPolicyVersion: policy,
    });
    c.textChanged('Питер');
    await c.extract();
    c.select('destinationId', true);
    setNow(600001);
    const emit = vi.fn();
    c.transferred.subscribe(emit);
    await c.confirm();
    expect(emit).not.toHaveBeenCalled();
    expect(c.candidate()).toBeNull();
  });
});

it('lets a user unselect a corrected value that became invalid, preserving partial review', async () => {
  const { f, c } = await setup();
  c.capability.set({
    candidateVersion: 1,
    status: 'configured',
    processorId: 'anthropic',
    modelId: 'fictional-model',
    processingPolicyVersion: policy,
  });
  c.textChanged('Питер');
  await c.extract();
  c.correct('adults', 3);
  c.select('adults', true);
  c.correct('adults', 10);
  await f.whenStable();
  const checkbox = f.nativeElement.querySelector('[data-transfer="adults"]') as HTMLInputElement;
  expect(checkbox.disabled).toBe(false);
});
