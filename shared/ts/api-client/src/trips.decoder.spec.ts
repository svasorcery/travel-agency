import {
  decodeTripCatalogue,
  decodeTripPage,
  decodeTripPlan,
  parseTripsJson,
  tripDate,
  tripMoney,
  tripRevision,
  tripText,
  tripWrite,
} from './trips.decoder';
import { tripsExample } from './trips.vectors';

describe('Trips strict wire contract', () => {
  it('round trips design 4.1 without floating money, narrowing revisions or moving block IDs', () => {
    const plan = decodeTripPlan(tripsExample);
    expect(plan.days[0].blocks[0].blockId).toBe('22222222-2222-4222-8222-222222222222');
    expect(plan.knownUserEstimateSubtotal).toBe('500.00');
    expect(tripRevision('9223372036854775807')).toBe('9223372036854775807');
    expect(Object.keys(tripWrite(plan))).toEqual(['title', 'brief', 'days']);
    expect(tripWrite(plan)).not.toHaveProperty('revision');
  });
  it.each(['-1', '01', '1.2', '1e2', '1000000000.00', '0.001', '1\n', 1])('rejects invalid money %s', (value) => {
    expect(() => tripMoney(value)).toThrow();
  });
  it.each(['0', '01', '9223372036854775808', 2, '1e2', '2\n'])('rejects invalid revision %s', (value) => {
    expect(() => tripRevision(value)).toThrow();
  });
  it.each(['2026-02-29', '2026-04-31', '0000-01-01', '2026-01-01T00:00:00Z'])('rejects invalid date %s', (value) => {
    expect(() => tripDate(value)).toThrow();
  });
  it('checks Unicode scalars, controls and UTF8 payload bytes', () => {
    expect(tripText('😀'.repeat(100), 100)).toBe('😀'.repeat(100));
    for (const text of ['😀'.repeat(101), '\ud800', '\udc00', 'a\u0000', 'a\u007f', 'a\u0085', 'a\nb'])
      expect(() => tripText(text, 100)).toThrow();
    expect(tripText('a\nb', 500, true)).toBe('a\nb');
    expect(() => parseTripsJson(' '.repeat(131073))).toThrow();
  });
  it('rejects extra fields, wrong subtotal, duplicate ids, wrong day range and invalid time pairs', () => {
    const mutate = (change: (p: typeof tripsExample) => void) => {
      const p = structuredClone(tripsExample);
      change(p);
      expect(() => decodeTripPlan(p)).toThrow();
    };
    expect(() => decodeTripPlan({ ...tripsExample, ownerId: 'forged' })).toThrow();
    mutate((p) => {
      p.knownUserEstimateSubtotal = '0.00';
    });
    mutate((p) => {
      p.days[0].blocks.push(p.days[0].blocks[0]);
    });
    mutate((p) => {
      p.days[0].date = '2026-11-15';
    });
    mutate((p) => {
      p.days[0].blocks[0].endTime = '09:00';
    });
    expect(() => decodeTripPlan({ ...tripsExample, brief: { ...tripsExample.brief, timeZone: 'UTC' } })).toThrow();
  });
  it('bounds summaries, cursor and catalogue and forbids private summary fields', () => {
    const { tripId, revision, title, updatedAt, brief, destination } = tripsExample;
    const item = {
      tripId,
      revision,
      title,
      updatedAt,
      destinationId: brief.destinationId,
      displayName: destination.displayName,
      startDate: brief.startDate,
      endDate: brief.endDate,
    };
    expect(decodeTripPage({ items: [item], nextCursor: null }).items).toHaveLength(1);
    expect(() => decodeTripPage({ items: [{ ...item, brief }], nextCursor: null })).toThrow();
    expect(() => decodeTripPage({ items: Array(21).fill(item), nextCursor: null })).toThrow();
    expect(() => decodeTripPage({ items: [], nextCursor: 'a'.repeat(257) })).toThrow();
    expect(decodeTripCatalogue({ catalogueVersion: '1', items: [destination] }).items[0].timeZone).toBe(
      'Europe/Moscow',
    );
    expect(() =>
      decodeTripCatalogue({ catalogueVersion: '1', items: [{ ...destination, timeZone: 'UTC' }] }),
    ).toThrow();
  });
});
