import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, it } from 'vitest';
import { decodeTripPlan, parseTripsJson, tripWrite } from './trips.decoder';

for (const [city, hash] of Object.entries({
  moscow: '93d0ba8a6dd36c565ecb897973ffb9b92e166bb2a54beddd4565f0381749f2f8',
  'saint-petersburg': '3ded6bf53240a3a031a74492380469e24d921bb313e1f85c21df7ce769e3f717',
})) {
  it('reads the exact base-codec ' + city + ' detail without normalizing historical metadata', () => {
    const bytes = readFileSync(
      resolve(import.meta.dirname, '../../../../tests/fixtures/trips-m1-legacy-codec/legacy-' + city + '.detail.json'),
    );
    expect(createHash('sha256').update(bytes).digest('hex')).toBe(hash);
    const raw = parseTripsJson(bytes.toString('utf8'));
    const plan = decodeTripPlan(raw);
    expect(plan).toEqual(raw);
    expect(plan.destination.displayName).toBe('Историческое имя ' + city);
    expect(plan.destination.catalogueVersion).toBe('1');
    expect(plan.revision).toBe('7');
    expect(plan.knownUserEstimateSubtotal).toBe('123.55');
    expect(plan.unknownEstimateCount).toBe(1);
    expect(plan.days.flatMap((d) => d.blocks.map((b) => b.blockId))).toEqual([
      '33333333-3333-4333-8333-333333333333',
      '44444444-4444-4444-8444-444444444444',
      '55555555-5555-4555-8555-555555555555',
    ]);
    expect(tripWrite(plan)).toEqual({ title: plan.title, brief: plan.brief, days: plan.days });
    expect(plan.days[0].blocks[0].note).toBe('Историческая заметка 🧭\nСтрока два');
  });
}
