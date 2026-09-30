import { takeFlightsBookingDraft, writeFlightsBookingDraft } from './flights-booking-draft';

describe('Flights booking redirect draft', () => {
  const aggregateId = '88b83d41-0194-2098-c1f6-fe7351d41cf2';

  it('stores only the short-lived opaque offer intent and consumes it once', () => {
    const storage = new MemoryStorage();
    writeFlightsBookingDraft(
      storage,
      {
        provider: 'duffel',
        providerOfferRef: 'off_fixture_ow_2030-06-10',
        aggregateId,
      },
      1_000,
    );

    const stored = JSON.parse(storage.getItem('travel.flights.booking.v1') ?? '{}') as Record<string, unknown>;
    expect(Object.keys(stored).sort()).toEqual(['aggregateId', 'expiresAt', 'provider', 'providerOfferRef', 'version']);
    expect(takeFlightsBookingDraft(storage, 1_001)).toEqual({
      provider: 'duffel',
      providerOfferRef: 'off_fixture_ow_2030-06-10',
      aggregateId,
    });
    expect(storage.getItem('travel.flights.booking.v1')).toBeNull();
    expect(takeFlightsBookingDraft(storage, 1_002)).toBeNull();
  });

  it('discards expired or malformed drafts', () => {
    const storage = new MemoryStorage();
    writeFlightsBookingDraft(
      storage,
      {
        provider: 'duffel',
        providerOfferRef: 'off_fixture_ow_2030-06-10',
        aggregateId,
      },
      1_000,
    );
    expect(takeFlightsBookingDraft(storage, 400_000)).toBeNull();

    storage.setItem('travel.flights.booking.v1', '{broken');
    expect(takeFlightsBookingDraft(storage, 1_000)).toBeNull();
    expect(storage.getItem('travel.flights.booking.v1')).toBeNull();
  });
});

class MemoryStorage implements Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
  removeItem(key: string): void {
    this.values.delete(key);
  }
}
