import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { decodeTripItineraryCandidateV2 } from './trips-itinerary.decoder';

// Recorded original HTTP responses from the first bound SPb run. Regression only.
const directory = resolve(import.meta.dirname, '../tests/fixtures/trips-m22-spb-q-demo-v1');
const sha = (bytes: Uint8Array) => createHash('sha256').update(bytes).digest('hex');
const load = () => {
  const bytes = readFileSync(resolve(directory, 'responses.json'));
  const bindingBytes = readFileSync(resolve(directory, 'run-binding.json'));
  const manifest = JSON.parse(readFileSync(resolve(directory, 'results-manifest.json'), 'utf8'));
  expect(sha(bytes)).toBe(manifest.responsesSha256);
  expect(sha(bindingBytes)).toBe(manifest.bindingSha256);
  return { records: JSON.parse(bytes.toString()).records, binding: JSON.parse(bindingBytes.toString()), manifest };
};
describe('SPb original Q_demo responses through the production v2 raw decoder', () => {
  it('retains the complete first-run denominator, binding and blank human scores', () => {
    const { records, binding, manifest } = load();
    expect(records.map((r: { id: string }) => r.id)).toEqual(
      Array.from({ length: 12 }, (_, i) => 'Q' + String(i + 1).padStart(2, '0')),
    );
    expect(binding.caseHash).toBe('fc12bc22c49ebfa7498cd4f4a865be297c22aef73081aecd11eef1846fb9409b');
    expect(binding.applicationAsOfUtc).toBe('2026-10-27T12:00:00Z');
    expect(manifest.summary).toMatchObject({
      total: 12,
      passed: 12,
      failed: 0,
      humanScores: null,
      humanQuality: 'not-evaluated',
    });
  });
  for (let index = 1; index <= 12; index++)
    it('decodes original Q' + String(index).padStart(2, '0'), () => {
      const { records, binding } = load();
      const r = records[index - 1];
      const bytes = Buffer.from(r.bodyUtf8Base64, 'base64');
      expect(bytes.length).toBe(r.bodyLength);
      expect(sha(bytes)).toBe(r.bodySha256);
      const value = decodeTripItineraryCandidateV2(bytes);
      expect(value.outcome).toBe('candidate');
      if (value.outcome === 'unsupported') throw Error('Expected computed response');
      expect(value.candidateVersion).toBe(2);
      expect(value.destinationId).toBe('saint-petersburg');
      expect(value.datasetHash).toBe('dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12');
      expect(new Date(value.generatedAt).toISOString()).toBe(new Date(binding.applicationAsOfUtc).toISOString());
      expect(value.days.map((d) => d.blocks.length)).toEqual(r.stopsPerDay);
      expect(value.days.flatMap((d) => d.blocks)).toHaveLength(r.targetStops);
      expect(value.coverage).toBe(index === 10 || index === 11 ? 'partial' : 'filled');
      expect(value.unmetInterests).toEqual(index === 10 || index === 11 ? ['food'] : []);
      expect(r.failures).toEqual([]);
      expect(r.humanScores).toBeNull();
      expect(value.preparation.humanCurator).toBeNull();
      expect(value.preparation.humanReviewer).toBeNull();
      expect(value.humanQuality).toBe('not-evaluated');
      expect(value.modelSource.calibrationStatus).toBe('not-evaluated');
    });
});
