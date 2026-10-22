import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { decodeTripItineraryCandidate } from './trips-itinerary.decoder';

// Historical bound real HTTP bytes: regression only, never a new Q_demo/live evaluation.
// No fixture read during import/discovery. Missing fixtures fail each literal test.
const fixtureDirectory = resolve(import.meta.dirname, '../tests/fixtures/trips-m2-q-demo-v1');
const caseSha256 = 'f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c';
const asOfUtc = '2026-10-21T14:16:33.0000000Z';
const expectedIds = [
  'Q01',
  'Q02',
  'Q03',
  'Q04',
  'Q05',
  'Q06',
  'Q07',
  'Q08',
  'Q09',
  'Q10',
  'Q11',
  'Q12',
  'S01',
  'S02',
  'S03',
  'S04',
  'S05',
  'S06',
  'S07',
  'S08',
  'S09',
  'S10',
  'S11',
  'S12',
];
type Exchange = { stage: string; status: number; bodyUtf8Base64: string; bodyLength: number; bodySha256: string };
type RecordRow = {
  schemaVersion: number;
  id: string;
  family: string;
  asOfUtc: string;
  caseSetSha256: string;
  execution: string;
  status: number | null;
  bodyUtf8Base64: string | null;
  bodyLength: number | null;
  bodySha256: string | null;
  outcome: string | null;
  targetStops: number | null;
  stopCount: number;
  stopsPerDay: number[];
  filledTarget: boolean;
  failures: string[];
  exchanges: Exchange[];
  humanScores: null;
  humanQuality: string;
  requestBrief: unknown;
  saveReloadChecked: boolean;
};
function digest(bytes: Uint8Array): string {
  return createHash('sha256').update(bytes).digest('hex');
}
function readCorpus() {
  const responseBytes = readFileSync(resolve(fixtureDirectory, 'responses.json'));
  const bindingBytes = readFileSync(resolve(fixtureDirectory, 'run-binding.json'));
  const manifestBytes = readFileSync(resolve(fixtureDirectory, 'results-manifest.json'));
  // These are recorder envelopes, not candidate bodies. Only the production decoder handles candidate bytes.
  const responses = JSON.parse(responseBytes.toString('utf8')) as { schemaVersion: number; records: RecordRow[] };
  const binding = JSON.parse(bindingBytes.toString('utf8'));
  const manifest = JSON.parse(manifestBytes.toString('utf8'));
  return { responseBytes, bindingBytes, responses, binding, manifest };
}
function rawBody(row: { bodyUtf8Base64: string | null; bodyLength: number | null; bodySha256: string | null }) {
  expect(typeof row.bodyUtf8Base64).toBe('string');
  const bytes = Buffer.from(row.bodyUtf8Base64 as string, 'base64');
  expect(bytes.toString('base64')).toBe(row.bodyUtf8Base64);
  expect(bytes.length).toBe(row.bodyLength);
  expect(digest(bytes)).toBe(row.bodySha256);
  return bytes;
}
function checkQ(id: string, target: number, dayCount: number) {
  const { responses, binding } = readCorpus();
  const matches = responses.records.filter((r) => r.id === id);
  expect(matches).toHaveLength(1);
  const record = matches[0];
  expect(record.status).toBe(200);
  expect(record.family).toBe('real-supported');
  expect(record.outcome).toBe('candidate');
  expect(record.targetStops).toBe(target);
  expect(record.requestBrief).toEqual(binding.qCases.find((c: { id: string }) => c.id === id).brief);
  // decodeTripItineraryCandidate calls the production raw UTF8/duplicate/depth parser itself.
  const decoded = decodeTripItineraryCandidate(rawBody(record));
  expect(decoded.outcome).toBe('candidate');
  expect(decoded.baseRevision).toBe('1');
  expect(new Date(decoded.generatedAt).toISOString()).toBe(new Date(asOfUtc).toISOString());
  if (decoded.outcome === 'unsupported') throw new Error('Q must return a computed candidate');
  expect(decoded.datasetId).toBe(binding.dataset.datasetId);
  expect(decoded.datasetVersion).toBe(binding.dataset.version);
  expect(decoded.datasetHash).toBe(binding.dataset.payloadSha256);
  expect(decoded.days).toHaveLength(dayCount);
  expect(decoded.days.map((d) => d.blocks.length)).toEqual(record.stopsPerDay);
  const blocks = decoded.days.flatMap((d) => d.blocks);
  expect(blocks.length).toBe(record.stopCount);
  expect(new Set(blocks.map((b) => b.proposedBlockId)).size).toBe(blocks.length);
  expect(new Set(blocks.map((b) => b.poiId)).size).toBe(blocks.length);
  expect(record.filledTarget).toBe(
    blocks.length === target && decoded.days.every((d) => d.blocks.length === target / dayCount),
  );
  expect(decoded.humanQuality).toBe('not-evaluated');
  expect(decoded.preparation.humanCurator).toBeNull();
  expect(decoded.preparation.humanReviewer).toBeNull();
  expect(decoded.modelSource.calibrationStatus).toBe('not-evaluated');
  expect(record.humanScores).toBeNull();
}

describe('retained first bound real Moscow demo HTTP corpus', () => {
  it('Q01 actual raw response decodes', () => checkQ('Q01', 2, 1));
  it('Q02 actual raw response decodes', () => checkQ('Q02', 3, 1));
  it('Q03 actual raw response decodes', () => checkQ('Q03', 4, 1));
  it('Q04 actual raw response decodes', () => checkQ('Q04', 4, 2));
  it('Q05 actual raw response decodes', () => checkQ('Q05', 6, 2));
  it('Q06 actual raw response decodes', () => checkQ('Q06', 8, 2));
  it('Q07 actual raw response decodes', () => checkQ('Q07', 6, 3));
  it('Q08 actual raw response decodes', () => checkQ('Q08', 9, 3));
  it('Q09 actual raw response decodes', () => checkQ('Q09', 12, 3));
  it('Q10 actual raw response decodes', () => checkQ('Q10', 3, 1));
  it('Q11 actual raw response decodes', () => checkQ('Q11', 6, 2));
  it('Q12 actual raw response decodes', () => checkQ('Q12', 9, 3));
  it('retains all 24 records and their immutable body/binding linkage', () => {
    const { responseBytes, bindingBytes, responses, binding, manifest } = readCorpus();
    expect(responses.schemaVersion).toBe(1);
    expect(responses.records.map((r) => r.id)).toEqual(expectedIds);
    expect(new Set(responses.records.map((r) => r.id)).size).toBe(24);
    expect(binding.schemaVersion).toBe(1);
    expect(binding.asOfUtc).toBe(asOfUtc);
    expect(binding.caseSetSha256).toBe(caseSha256);
    expect(manifest.responsesSha256).toBe(digest(responseBytes));
    expect(manifest.runBindingSha256).toBe(digest(bindingBytes));
    expect(manifest.sourceManifestSha256).toBe(binding.sourceManifestSha256);
    expect(manifest.binaryManifestSha256).toBe(binding.binaryManifestSha256);
    expect(manifest.recordCount).toBe(24);
    expect(manifest.denominators).toEqual({ q: 12, sEdge: 6, sNegative: 6 });
    for (const key of ['sourceManifestSha256', 'binaryManifestSha256', 'recipeSha256'])
      expect(binding[key]).toMatch(/^[a-f0-9]{64}$/);
    for (const row of responses.records) {
      expect(row.schemaVersion).toBe(1);
      expect(row.asOfUtc).toBe(asOfUtc);
      expect(row.caseSetSha256).toBe(caseSha256);
      expect(row.humanScores).toBeNull();
      expect(row.humanQuality).toBe('not-evaluated');
      expect(row.execution).toBe('completed');
      expect(row.failures).toEqual([]);
      const bytes = rawBody(row);
      const candidateExchange = row.exchanges.filter((e) => e.stage === 'candidate');
      expect(candidateExchange).toHaveLength(1);
      expect(candidateExchange[0].status).toBe(row.status);
      expect(rawBody(candidateExchange[0])).toEqual(bytes);
      for (const exchange of row.exchanges) rawBody(exchange);
    }
    expect(responses.records.filter((r) => r.family === 'real-supported')).toHaveLength(12);
    expect(responses.records.filter((r) => r.family === 'structural-edge')).toHaveLength(6);
    expect(responses.records.filter((r) => r.family === 'structural-negative')).toHaveLength(6);
    expect(responses.records.filter((r) => r.id.startsWith('Q') && r.filledTarget).length).toBeGreaterThanOrEqual(11);
    expect(responses.records.find((r) => r.id === 'Q01')?.saveReloadChecked).toBe(true);
    // Never require historical local build artifacts/paths/SDK to exist on today's CI machine.
  });
});
