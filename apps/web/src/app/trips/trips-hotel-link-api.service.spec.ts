import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TripsHotelLinkApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';

const tripId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const reservationId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const path = `/api/trips/${tripId}/hotel-link`;
function bytes(value: unknown): ArrayBuffer {
  const encoded = new TextEncoder().encode(JSON.stringify(value));
  const data = new ArrayBuffer(encoded.byteLength);
  new Uint8Array(data).set(encoded);
  return data;
}
function setup(platform = 'browser') {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: platform }],
  });
  return { api: TestBed.inject(TripsHotelLinkApiService), http: TestBed.inject(HttpTestingController) };
}

it('uses exact metadata revisions, an id-free remove and private request settings', async () => {
  const { api, http } = setup();
  const reading = firstValueFrom(api.metadata(tripId, 'owner'));
  http.expectOne({ method: 'GET', url: path }).flush(bytes({ hasLink: false, revision: '0' }), {
    headers: { ETag: '"hl-0"' },
  });
  let saved = await reading;
  for (const op of [{ op: 'add', reservationId }, { op: 'remove' }] as const) {
    const response = firstValueFrom(api.patch(tripId, saved, op, 'owner'));
    const req = http.expectOne({ method: 'PATCH', url: path });
    expect(req.request.body).toEqual(op);
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner');
    expect(req.request.headers.get('If-Match')).toBe(`"hl-${saved.revision}"`);
    expect(req.request.cache).toBe('no-store');
    expect(req.request.transferCache).toBe(false);
    const result = { hasLink: op.op === 'add', revision: op.op === 'add' ? '1' : '2' };
    req.flush(bytes(result), { headers: { ETag: `"hl-${result.revision}"` } });
    saved = await response;
    expect(saved).toEqual(result);
  }
  http.verify();
});

it('rejects a weak metadata ETag and private facts on an unavailable response', async () => {
  const { api, http } = setup();
  const reading = expect(firstValueFrom(api.metadata(tripId, 'owner'))).rejects.toMatchObject({ kind: 'contract' });
  http.expectOne(path).flush(bytes({ hasLink: true, revision: '1' }), { headers: { ETag: 'W/"hl-1"' } });
  await reading;
  const status = expect(firstValueFrom(api.status(tripId, 'owner'))).rejects.toMatchObject({ kind: 'contract' });
  const req = http.expectOne(`${path}/status`);
  expect(req.request.headers.has('If-None-Match')).toBe(false);
  req.flush(bytes({ revision: '1', state: 'unavailable', reservationId }));
  await status;
  http.verify();
});

it('does not dispatch private requests during SSR', () => {
  const { api, http } = setup('server');
  expect(() => api.metadata(tripId, 'owner')).toThrow();
  http.verify();
});
