import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { FlightAncillaryCatalog, FlightQuoteResponse } from '@travel/api-client';
import { FlightsAncillariesApiService, FlightsQuoteApiService } from '@travel/api-client';
import { of } from 'rxjs';
// eslint-disable-next-line @nx/enforce-module-boundaries
import fixtures from '../../../../../tests/fixtures/flights-booking.json';
import { FlightAncillariesService } from './flight-ancillaries.service';
import { FlightsAuthService } from './flights-auth.service';

describe('Optional service draft', () => {
  it('permits explicit removal after the selected key disappeared from the catalog', async () => {
    const { service } = setup();
    await service.load(quote, false);
    service.setQuantity('ase_bag', 2);
    service.catalog.set({ ...catalog, services: [] });
    expect(service.setQuantity('ase_bag', 0)).toBe(true);
    expect(service.draft()).toEqual([]);
  });
  const quote = fixtures.oneWay.response as FlightQuoteResponse;
  const passenger = quote.binding.slots[0].bookingPassengerId;
  const catalog: FlightAncillaryCatalog = {
    aggregateId: quote.aggregateId,
    quoteRevision: quote.binding.revision,
    services: [
      {
        selectionKey: 'ase_bag',
        kind: 'checked-baggage',
        bookingPassengerIds: [passenger],
        segments: [{ leg: 0, segment: 0 }],
        unitPrice: { amount: '10.00', currency: 'RUB' },
        maximumQuantity: 2,
        selectable: true,
        reason: null,
        name: null,
        seatDesignator: null,
        physicalSeat: null,
        disclosures: null,
        baggage: null,
      },
    ],
    allowances: [],
    seatMaps: [],
    baseFare: { amount: '100', currency: 'RUB' },
    expiresAt: quote.offer.expiresAt,
    seatsUnavailable: false,
    unsupportedPricing: false,
  };
  function setup() {
    const auth = {
      status: signal({ kind: 'authenticated', userId: 'fictional-owner' }),
      identityEpoch: signal(0),
      isDemo: true,
      accessToken: vi.fn(async (): Promise<string | null> => 'fictional-token'),
    };
    const api = { getCatalog: vi.fn(() => of(catalog)) };
    const quotes = { quote: vi.fn(() => of(quote)) };
    TestBed.configureTestingModule({
      providers: [
        FlightAncillariesService,
        { provide: FlightsAuthService, useValue: auth },
        { provide: FlightsAncillariesApiService, useValue: api },
        { provide: FlightsQuoteApiService, useValue: quotes },
      ],
    });
    return { service: TestBed.inject(FlightAncillariesService), auth, api, quotes };
  }
  it.each(['catalog', 'quote'])(
    'does not send a stale %s after the identity changed while obtaining a token',
    async (operation) => {
      const { service, auth, api, quotes } = setup();
      auth.isDemo = false;
      let release!: (token: string) => void;
      auth.accessToken.mockImplementation(
        () =>
          new Promise<string>((resolve) => {
            release = resolve;
          }),
      );
      service.bind(quote);
      const pending = operation === 'catalog' ? service.load(quote, false) : service.review(quote);
      auth.identityEpoch.set(1);
      release('fictional-token');
      await pending;
      expect(api.getCatalog).not.toHaveBeenCalled();
      expect(quotes.quote).not.toHaveBeenCalled();
    },
  );
  it('preserves a draft beyond two minutes and quote refresh, while refusing stale unavailable choices', async () => {
    const { service } = setup();
    await service.load(quote, false);
    expect(service.setQuantity('ase_bag', 2)).toBe(true);
    vi.useFakeTimers();
    vi.advanceTimersByTime(180_000);
    const refreshed = { ...quote, binding: { ...quote.binding, revision: '33333333-3333-4333-8333-333333333333' } };
    service.bind(refreshed);
    expect(service.draft()).toEqual([{ selectionKey: 'ase_bag', quantity: 2 }]);
    expect(service.matchesQuote(refreshed)).toBe(false);
    expect(service.setQuantity('ase_missing', 1)).toBe(false);
    vi.useRealTimers();
  });
  it('identity epoch clears prior owner choices even for A to B to A', async () => {
    const { service, auth } = setup();
    await service.load(quote, false);
    service.setQuantity('ase_bag', 1);
    auth.identityEpoch.set(1);
    service.bind(quote);
    expect(service.draft()).toEqual([]);
  });
});
