import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SupportApiService, SupportContractError } from '@travel/api-client';

const id = '11111111-1111-4111-8111-111111111111',
  action = '22222222-2222-4222-8222-222222222222';
const receipt = {
  caseId: id,
  actionId: action,
  kind: 'OperatorReply',
  appliedRevision: 2,
  appliedState: 'WaitingForCustomer',
  appliedAt: '2030-01-01T00:00:00+00:00',
  messageId: action,
};
describe('SupportApiService routes and mutation dispatch', () => {
  let api: SupportApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SupportApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('uses the operator path and bearer token once and decodes a bound receipt', () => {
    const result = vi.fn();
    api
      .reply('operator', id, action, { body: 'Plain text', expectedRevision: 1, kind: 'Reply' }, 'operator-token')
      .subscribe(result);
    const request = http.expectOne('/api/support/operator/cases/' + id + '/messages/' + action);
    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('Authorization')).toBe('Bearer operator-token');
    expect(request.request.body).toEqual({ body: 'Plain text', expectedRevision: 1, kind: 'Reply' });
    request.flush(receipt);
    expect(result).toHaveBeenCalledWith(receipt);
  });
  it('reads direct actor receipt without interpreting list absence or resubmitting', () => {
    const failure = vi.fn();
    api.receipt('operator', id, action, 'OperatorReply', 'token').subscribe({ error: failure });
    const request = http.expectOne('/api/support/operator/cases/' + id + '/actions/' + action);
    expect(request.request.method).toBe('GET');
    request.flush({ type: 'Support.NotFound' }, { status: 404, statusText: 'Not found' });
    expect(failure).toHaveBeenCalled();
  });
  it('rejects invalid reply mode or text before dispatch', () => {
    expect(() =>
      api.reply('operator', id, action, { body: 'Text', expectedRevision: 1, kind: 'ReplyAndReopen' }, 'token'),
    ).toThrow(SupportContractError);
    expect(() => api.create(id, { messageId: action, title: 'Title', body: '\ud800' }, 'token')).toThrow(
      SupportContractError,
    );
    expect(() => api.list('customer', { state: 'foreign' as never }, null, 'token')).toThrow(SupportContractError);
  });
});

describe('Support classification request', () => {
  it('posts only request identity/current revision on the operator route and does not retry', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(SupportApiService),
      http = TestBed.inject(HttpTestingController),
      error = vi.fn();
    api.classificationSuggestion(id, action, 3, 'operator-token').subscribe({ error });
    const request = http.expectOne('/api/support/operator/cases/' + id + '/classification-suggestion');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe('Bearer operator-token');
    expect(request.request.body).toEqual({ requestId: action, expectedRevision: 3 });
    request.flush({ type: 'Support.ClassificationBusy' }, { status: 429, statusText: 'Busy' });
    expect(error).toHaveBeenCalledTimes(1);
    http.verify();
  });
  it('rejects invalid UUIDv4 request IDs and revisions before transport', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const api = TestBed.inject(SupportApiService);
    expect(() => api.classificationSuggestion(id, action.replace('-4222-', '-1222-'), 3, 'token')).toThrow(
      SupportContractError,
    );
    expect(() => api.classificationSuggestion(id, action, 0, 'token')).toThrow(SupportContractError);
    TestBed.inject(HttpTestingController).verify();
  });
});

it('bounds classification transport to 15 seconds without resubmission', async () => {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const api = TestBed.inject(SupportApiService),
    http = TestBed.inject(HttpTestingController),
    failed = vi.fn();
  vi.useFakeTimers();
  try {
    api.classificationSuggestion(id, action, 3, 'token').subscribe({ error: failed });
    const request = http.expectOne('/api/support/operator/cases/' + id + '/classification-suggestion');
    await vi.advanceTimersByTimeAsync(15000);
    expect(failed.mock.calls[0][0].name).toBe('TimeoutError');
    expect(request.cancelled).toBe(true);
    http.verify();
  } finally {
    vi.useRealTimers();
  }
});
it('checks response request and captured revision before revealing a candidate', () => {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const api = TestBed.inject(SupportApiService),
    http = TestBed.inject(HttpTestingController),
    failed = vi.fn();
  api.classificationSuggestion(id, action, 3, 'token').subscribe({ error: failed });
  const request = http.expectOne('/api/support/operator/cases/' + id + '/classification-suggestion');
  request.flush({
    caseId: id,
    requestId: action,
    capturedRevision: 4,
    generatedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 120000).toISOString(),
    status: 'Disabled',
    reason: 'Disabled',
    category: null,
    queue: null,
    servingProfileId: null,
    questionVersion: null,
    urgency: {
      kind: 'Unknown',
      nextScheduledDepartureAt: null,
      observedAt: null,
      streamVersion: null,
      ruleVersion: 'support-urgency-v1',
    },
  });
  expect(failed.mock.calls[0][0]).toBeInstanceOf(SupportContractError);
  http.verify();
});
