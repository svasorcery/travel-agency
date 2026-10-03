import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SavedTravelerContractError, SavedTravelersApiService } from '@travel/api-client';

const id = '11111111-1111-4111-8111-111111111111';
const revision = '22222222-2222-4222-8222-222222222222';
const details = {
  title: 'mr' as const,
  givenName: 'DemoA',
  familyName: 'Traveler',
  dateOfBirth: '1990-04-12',
  gender: 'male' as const,
  email: 'demoa@example.test',
  phone: '+79161234567',
};
describe('saved traveler HTTP client', () => {
  let http: HttpTestingController;
  let api: SavedTravelersApiService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(SavedTravelersApiService);
  });
  afterEach(() => http.verify());
  it('uses exact conditional headers with current bearer and only seven body fields', () => {
    api.create(id, details, 'tokenA').subscribe();
    const create = http.expectOne(`/api/flights/travelers/${id}`);
    expect(create.request.headers.get('Authorization')).toBe('Bearer tokenA');
    expect(create.request.headers.get('If-None-Match')).toBe('*');
    expect(create.request.headers.has('X-Travel-Demo-Owner')).toBe(false);
    expect(Object.keys(create.request.body)).toHaveLength(7);
    create.flush({ id, revision }, { status: 201, statusText: 'Created', headers: { ETag: `"${revision}"` } });
    api.update(id, revision, details, 'tokenB').subscribe();
    const update = http.expectOne(`/api/flights/travelers/${id}`);
    expect(update.request.headers.get('If-Match')).toBe(`"${revision}"`);
    expect(update.request.headers.get('Authorization')).toBe('Bearer tokenB');
    expect(update.request.headers.has('If-None-Match')).toBe(false);
    update.flush({ id, revision }, { headers: { ETag: `"${revision}"` } });
  });
  it.each([
    { status: 200, body: '' },
    { status: 204, body: 'unexpected' },
  ])('rejects malformed delete receipt', ({ status, body }) => {
    let failure: unknown;
    api.delete(id, revision, null, 'demo-only').subscribe({ error: (e) => (failure = e) });
    const req = http.expectOne(`/api/flights/travelers/${id}`);
    expect(req.request.headers.get('If-Match')).toBe(`"${revision}"`);
    req.flush(body, { status, statusText: 'Fixture' });
    expect(failure).toBeInstanceOf(SavedTravelerContractError);
  });
  it('accepts 204 empty delete and checks a selected detail ETag', () => {
    let done = false;
    api.delete(id, revision, 'token').subscribe(() => (done = true));
    http.expectOne(`/api/flights/travelers/${id}`).flush('', { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
    let failure: unknown;
    api.get(id, 'token').subscribe({ error: (e) => (failure = e) });
    http
      .expectOne(`/api/flights/travelers/${id}`)
      .flush({ id, revision, details }, { headers: { ETag: `W/"${revision}"` } });
    expect(failure).toBeInstanceOf(SavedTravelerContractError);
  });
});
