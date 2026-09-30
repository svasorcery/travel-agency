export interface FlightsBookingIntent {
  provider: string;
  providerOfferRef: string;
  aggregateId: string;
}

interface StoredFlightsBookingDraft extends FlightsBookingIntent {
  version: 1;
  expiresAt: number;
}

type DraftStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

const STORAGE_KEY = 'travel.flights.booking.v1';
const LIFETIME_MS = 5 * 60 * 1000;
const GUID = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

export function writeFlightsBookingDraft(storage: DraftStorage, intent: FlightsBookingIntent, now = Date.now()): void {
  const draft: StoredFlightsBookingDraft = {
    version: 1,
    provider: intent.provider,
    providerOfferRef: intent.providerOfferRef,
    aggregateId: intent.aggregateId,
    expiresAt: now + LIFETIME_MS,
  };
  storage.setItem(STORAGE_KEY, JSON.stringify(draft));
}

export function takeFlightsBookingDraft(storage: DraftStorage, now = Date.now()): FlightsBookingIntent | null {
  const serialized = storage.getItem(STORAGE_KEY);
  if (serialized === null) return null;
  storage.removeItem(STORAGE_KEY);

  try {
    const value: unknown = JSON.parse(serialized);
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const draft = value as Partial<StoredFlightsBookingDraft>;
    if (
      draft.version !== 1 ||
      typeof draft.provider !== 'string' ||
      draft.provider.trim() === '' ||
      typeof draft.providerOfferRef !== 'string' ||
      draft.providerOfferRef.trim() === '' ||
      typeof draft.aggregateId !== 'string' ||
      !GUID.test(draft.aggregateId) ||
      draft.aggregateId === '00000000-0000-0000-0000-000000000000' ||
      typeof draft.expiresAt !== 'number' ||
      !Number.isFinite(draft.expiresAt) ||
      draft.expiresAt <= now
    ) {
      return null;
    }

    return {
      provider: draft.provider,
      providerOfferRef: draft.providerOfferRef,
      aggregateId: draft.aggregateId,
    };
  } catch {
    return null;
  }
}
