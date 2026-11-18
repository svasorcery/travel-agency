import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TripsFlightLinksApiService } from '@travel/api-client';
import { firstValueFrom } from 'rxjs';

const id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  orderId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const path = '/api/trips/' + id + '/flight-links';
function bytes(raw: string): ArrayBuffer {
  const encoded = new TextEncoder().encode(raw);
  const data = new ArrayBuffer(encoded.byteLength);
  new Uint8Array(data).set(encoded);
  return data;
}
const empty = { tripId: id, links: [], linksRevision: '0' };
function setup(platform = 'browser') {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), { provide: PLATFORM_ID, useValue: platform }],
  });
  return { api: TestBed.inject(TripsFlightLinksApiService), http: TestBed.inject(HttpTestingController) };
}
it('uses the same private URI and exact validated ETag for one explicit add and one remove', async () => {
  const { api, http } = setup();
  const initial = firstValueFrom(api.metadata(id, 'token'));
  const get = http.expectOne(path);
  expect(get.request.method).toBe('GET');
  get.flush(bytes(JSON.stringify(empty)), { headers: { ETag: '"fl-0"' } });
  let saved = await initial;
  for (const op of ['add', 'remove'] as const) {
    const result = firstValueFrom(api.patch(saved, op, orderId, 'token'));
    const req = http.expectOne(path);
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ op, orderId });
    expect(req.request.headers.get('If-Match')).toBe('"fl-' + saved.linksRevision + '"');
    expect(req.request.headers.get('Authorization')).toBe('Bearer token');
    expect(req.request.transferCache).toBe(false);
    expect(req.request.cache).toBe('no-store');
    const next = {
      ...empty,
      linksRevision: op === 'add' ? '1' : '2',
      links: op === 'add' ? [{ orderId, linkedAt: '2026-11-17T00:00:00Z' }] : [],
    };
    req.flush(bytes(JSON.stringify(next)), { headers: { ETag: '"fl-' + next.linksRevision + '"' } });
    saved = await result;
    expect(saved).toEqual(next);
  }
  http.verify();
});
it('rejects oversized or duplicate-key metadata and inconsistent successful mutations without retry', async () => {
  const { api, http } = setup();
  for (const raw of [' '.repeat(16385), JSON.stringify(empty).replace('"links":[]', '"links":[],"links":[]')]) {
    const rejected = expect(firstValueFrom(api.metadata(id, 'token'))).rejects.toMatchObject({ kind: 'contract' });
    http.expectOne(path).flush(bytes(raw), { headers: { ETag: '"fl-0"' } });
    await rejected;
  }
  const malformed = expect(firstValueFrom(api.patch(empty, 'add', orderId, 'token'))).rejects.toMatchObject({
    kind: 'contract',
  });
  http.expectOne(path).flush(bytes(JSON.stringify(empty)), { headers: { ETag: '"fl-0"' } });
  await malformed;
  http.verify();
});
it('rejects private calls during SSR before dispatch', () => {
  const { api, http } = setup('server');
  expect(() => api.metadata(id, 'token')).toThrow();
  http.verify();
});
