import { describe, expect, it } from 'vitest';
import { decodeHotelExecution, decodeHotelReservation, hotelStatusText } from './hotels.decoder';

describe('Hotels contracts', () => {
  it('retains historical mismatch evidence alongside current actual terms', () => {
    const terms = {
      property: { id: 'acc_fictional', name: 'Fictional', address: null, rating: null, ratingSource: null },
      room: 'Double',
      rateId: 'rat_fictional',
      dates: { checkIn: '2027-01-03', checkOut: '2027-01-05', nights: 2 },
      occupancy: { adults: 1 },
      total: { amount: '120.25', currency: 'GBP' },
      mandatoryLocalPayments: [],
      board: [],
      cancellation: { kind: 'NonRefundable', rules: [] },
      conditions: [],
      eligible: true,
      paymentTiming: 'ImmediateTest',
    };
    const raw = {
      id: '11111111-1111-4111-8111-111111111111',
      quoteRevision: 1,
      accepted: terms,
      validUntil: '2027-01-01T00:00:00Z',
      attemptId: '22222222-2222-4222-8222-222222222222',
      outcome: 'Confirmed',
      reviewReasons: ['TermsMismatch'],
      everReserved: true,
      active: true,
      facts: { bookingId: 'bok_fictional', hotelReference: null, actual: terms },
      createdAt: '2026-10-17T10:00:00Z',
      updatedAt: '2026-10-17T11:00:00Z',
      privateContentAvailable: true,
      firstMismatch: { observedAt: '2026-10-17T10:30:00Z', actual: { ...terms, room: 'Different' } },
    };
    const decoded = decodeHotelReservation(raw);
    expect(decoded).toHaveProperty('firstMismatch.actual.room', 'Different');
    expect(decoded.facts?.actual?.room).toBe('Double');
    expect(decodeHotelReservation({ ...raw, firstMismatch: undefined })).toHaveProperty('firstMismatch', null);
    expect(() => decodeHotelReservation({ ...raw, firstMismatch: { observedAt: 'invalid', actual: terms } })).toThrow();
  });
  it('preserves empty, unsupported, limited and failed meanings', () => {
    for (const kind of ['Complete', 'Unsupported', 'Limited', 'Failed']) {
      expect(decodeHotelExecution({ kind, items: [], reason: kind === 'Complete' ? null : 'Reason' }).kind).toBe(kind);
    }
    expect(() => decodeHotelExecution({ kind: 'Invented', items: [] })).toThrow();
  });
  it('does not describe cancelled as active or unknown as failure', () => {
    expect(hotelStatusText('Cancelled')).toContain('отменена');
    expect(hotelStatusText('Unknown')).toContain('неизвестен');
    expect(hotelStatusText('ReservedAwaitingDetails')).toContain('детали');
  });
});
