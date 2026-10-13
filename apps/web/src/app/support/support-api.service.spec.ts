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
