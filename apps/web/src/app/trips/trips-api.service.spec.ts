import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TripsApiService, tripWorkspaceWrite } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';
// Shared canonical design 4.1 fixture, test only; no module runtime internals.
// eslint-disable-next-line @nx/enforce-module-boundaries
import { tripsExample } from '../../../../../shared/ts/api-client/src/trips.vectors';

describe('Trips API transport', () => {
  let api: TripsApiService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TripsApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  const path = '/api/trips/' + tripsExample.tripId;
  const workspace = path + '/workspace-v2';
  const threeDays = {
    ...tripsExample,
    brief: { ...tripsExample.brief, endDate: '2026-11-16' },
    days: [...tripsExample.days, { date: '2026-11-15', blocks: [] }, { date: '2026-11-16', blocks: [] }],
  };
  const firstStay = { placeName: 'У друзей', checkInDate: '2026-11-14', checkOutDate: '2026-11-15' };
  const secondStay = { placeName: 'Апартаменты', checkInDate: '2026-11-15', checkOutDate: '2026-11-16' };

  it.each([
    [{}, []],
    [{ manualStay: firstStay }, [firstStay]],
    [{ manualStays: [firstStay, secondStay] }, [firstStay, secondStay]],
  ])('reads canonical workspace detail shape %j and writes the full list', async (shape, stays) => {
    const response = firstValueFrom(api.detail(tripsExample.tripId, 'token'));
    http.expectOne(workspace).flush(JSON.stringify({ ...threeDays, ...shape }), { headers: { ETag: '"2"' } });
    const plan = await response;
    const result = firstValueFrom(api.replace(plan.tripId, plan.revision, tripWorkspaceWrite(plan), 'token'));
    const request = http.expectOne(workspace);
    expect(request.request.body).not.toHaveProperty('manualStay');
    expect(request.request.body.manualStays).toEqual(stays);
    request.flush(JSON.stringify({ ...threeDays, ...shape }), { headers: { ETag: '"2"' } });
    expect(tripWorkspaceWrite(await result).manualStays).toEqual(stays);
  });

  it.each([
    { manualStay: null },
    { manualStay: { ...firstStay, confirmed: true } },
    { manualStays: null },
    { manualStays: [] },
    { manualStays: [firstStay] },
    { manualStays: [secondStay, firstStay] },
    { manualStays: [firstStay, { ...secondStay, checkInDate: '2026-11-14' }] },
    { manualStays: [firstStay, { ...secondStay, checkOutDate: '2026-11-17' }] },
    { manualStay: firstStay, manualStays: [firstStay, secondStay] },
  ])('rejects a noncanonical workspace detail %j', async (shape) => {
    const result = firstValueFrom(api.detail(tripsExample.tripId, 'token'));
    http.expectOne(workspace).flush(JSON.stringify({ ...threeDays, ...shape }), { headers: { ETag: '"2"' } });
    await expect(result).rejects.toThrow();
  });

  it('rejects duplicate decoded member names in workspace detail', async () => {
    const result = firstValueFrom(api.detail(tripsExample.tripId, 'token'));
    const raw = JSON.stringify({ ...threeDays, manualStays: [firstStay, secondStay] }).replace(
      '"placeName":"У друзей"',
      '"placeName":"У друзей","place\\u004eame":"Дубликат"',
    );
    http.expectOne(workspace).flush(raw, { headers: { ETag: '"2"' } });
    await expect(result).rejects.toThrow();
  });

  it('requires a non-null full list in writes and keeps POST on the strict legacy contract', async () => {
    for (const shape of [{}, { manualStays: null }, { manualStay: firstStay, manualStays: [] }]) {
      expect(() =>
        api.replace(
          tripsExample.tripId,
          '2',
          { title: threeDays.title, brief: threeDays.brief, days: threeDays.days, ...shape } as never,
          'token',
        ),
      ).toThrow();
    }
    http.expectNone(workspace);
    const result = firstValueFrom(api.create({ title: threeDays.title, brief: threeDays.brief } as never, 'token'));
    const request = http.expectOne('/api/trips');
    expect(request.request.method).toBe('POST');
    request.flush(JSON.stringify({ ...threeDays, manualStays: [firstStay, secondStay] }), {
      status: 201,
      statusText: 'Created',
      headers: { ETag: '"2"', Location: path },
    });
    await expect(result).rejects.toThrow();
  });
  it('sends strong lossless If-Match and only editable fields', async () => {
    const result = firstValueFrom(
      api.replace(tripsExample.tripId, '9223372036854775807', tripWorkspaceWrite(tripsExample as never), 'token'),
    );
    const req = http.expectOne(workspace);
    expect(req.request.headers.get('If-Match')).toBe('"9223372036854775807"');
    expect(req.request.body).not.toHaveProperty('revision');
    expect(req.request.body).not.toHaveProperty('destination');
    req.flush(JSON.stringify(tripsExample), { headers: { ETag: '"2"' } });
    expect((await result).revision).toBe('2');
  });
  it('does not retry a lost mutation and requires exact ETag', async () => {
    const result = firstValueFrom(
      api.replace(tripsExample.tripId, '2', tripWorkspaceWrite(tripsExample as never), 'token'),
    );
    const check = expect(result).rejects.toThrow();
    http.expectOne(workspace).error(new ProgressEvent('error'));
    await check;
    http.expectNone(workspace);
    http.expectNone(path);
    const invalid = firstValueFrom(api.detail(tripsExample.tripId, 'token'));
    const rejected = expect(invalid).rejects.toThrow();
    http.expectOne(workspace).flush(JSON.stringify(tripsExample), { headers: { ETag: 'W/"2"' } });
    await rejected;
  });
});
