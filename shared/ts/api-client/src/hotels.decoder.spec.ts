import { describe, expect, it } from 'vitest';
import { decodeHotelExecution, hotelStatusText } from './hotels.decoder';

describe('Hotels contracts', () => {
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
