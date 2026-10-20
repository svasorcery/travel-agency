import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TripsApiService } from '@travel/api-client';
import { of, throwError } from 'rxjs';
import { TripsAvailabilityService } from './trips-availability.service';

const catalogue = { catalogueVersion: '1', items: [] };
describe('Trips authenticated capability discovery', () => {
  it('accepts the actual RFC7807 code extension through the real TS HTTP transport', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const availability = TestBed.inject(TripsAvailabilityService),
      http = TestBed.inject(HttpTestingController);
    const read = availability.initialize('token');
    http
      .expectOne('/api/trips/destinations')
      .flush(
        JSON.stringify({ type: 'about:blank', title: 'Service Unavailable', status: 503, code: 'Trips.Unavailable' }),
        { status: 503, statusText: 'Service Unavailable' },
      );
    await read;
    http.verify();
    expect(availability.status()).toBe('unavailable');
    expect(availability.enabled()).toBe(true);
  });
  it.each([
    [404, 'disabled', false],
    [401, 'authentication-required', true],
    [503, 'unavailable', true],
    [0, 'error', false],
  ] as const)('maps %s without treating authentication or unavailable as disabled', async (code, status, enabled) => {
    const api = {
      destinations: vi.fn().mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: code,
              error: { type: 'about:blank', status: code, code: 'Trips.Unavailable' },
            }),
        ),
      ),
    };
    TestBed.configureTestingModule({ providers: [{ provide: TripsApiService, useValue: api }] });
    const service = TestBed.inject(TripsAvailabilityService);
    await service.initialize();
    expect(service.status()).toBe(status);
    expect(service.enabled()).toBe(enabled);
    expect(service.catalogue()).toBeNull();
  });
  it('requires a typed unavailable result and does not invent enabled on a generic proxy503', async () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: TripsApiService,
          useValue: {
            destinations: () => throwError(() => new HttpErrorResponse({ status: 503, error: 'proxy failed' })),
          },
        },
      ],
    });
    const s = TestBed.inject(TripsAvailabilityService);
    await s.initialize();
    expect(s.status()).toBe('error');
    expect(s.enabled()).toBe(false);
  });
  it('never requests authenticated metadata during SSR', async () => {
    const api = { destinations: vi.fn().mockReturnValue(of(catalogue)) };
    TestBed.configureTestingModule({
      providers: [
        { provide: TripsApiService, useValue: api },
        { provide: PLATFORM_ID, useValue: 'server' },
      ],
    });
    const s = TestBed.inject(TripsAvailabilityService);
    await s.initialize('token');
    expect(api.destinations).not.toHaveBeenCalled();
    expect(s.status()).toBe('unknown');
  });
});
