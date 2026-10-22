import '@angular/compiler';
import { readFileSync } from 'node:fs';
import * as client from './index';

const fixture = (name: string) =>
  new Uint8Array(readFileSync(new URL(`../../../../tests/fixtures/trips-itinerary-v1/${name}.json`, import.meta.url)));
const candidate = () => JSON.parse(new TextDecoder().decode(fixture('candidate')));
const decode = (value: unknown) => client.decodeTripItineraryCandidate(JSON.stringify(value));

describe('Trips M2 strict actual HTTP wire', () => {
  it.each(['candidate', 'partial', 'empty', 'unsupported'])('decodes frozen production %s bytes', (name) => {
    expect(client.decodeTripItineraryCandidate(fixture(name)).candidateVersion).toBe(1);
  });
  it('reads exact ready and RFC7807 vectors', () => {
    expect(client.decodeTripItineraryAvailability(fixture('ready')).status).toBe('ready');
    expect(client.decodeTripItineraryProblem(fixture('error'), 428).code).toBe('Trips.PreconditionRequired');
  });
  it('preserves large revisions/path/anchor strings and unknowns independently of filled', () => {
    const c = candidate();
    c.baseRevision = '9223372036854775807';
    const value = decode(c);
    expect(value.baseRevision).toBe(c.baseRevision);
    if (value.outcome === 'unsupported') throw new Error('computed required');
    expect(value.coverage).toBe('filled');
    expect(value.days[0].blocks[0].facts[1].state).toBe('unknown');
    expect(value.days[0].edges[0].modelledWalk.pathLengthMillimetres).toBe('900000');
  });
  it.each([
    [
      'unknown root',
      (c: ReturnType<typeof candidate>) => {
        c.extra = true;
      },
    ],
    [
      'unknown nested model',
      (c: ReturnType<typeof candidate>) => {
        c.modelSource.extra = true;
      },
    ],
    [
      'unknown evidence',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].extra = true;
      },
    ],
    [
      'unknown fact',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].facts[0].extra = true;
      },
    ],
    [
      'unknown nested',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].extra = true;
      },
    ],
    [
      'wrong revision',
      (c: ReturnType<typeof candidate>) => {
        c.baseRevision = 9007199254740992;
      },
    ],
    [
      'wrong fact scope',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].scopeId = 'p1';
      },
    ],
    [
      'wrong fact kind',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].kind = 'walking';
      },
    ],
    [
      'wrong dates',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].date = '2026-10-23';
      },
    ],
    [
      'wrong adjacency',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].edges[0].fromPoiId = 'p1';
      },
    ],
    [
      'wrong formula',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].edges[0].maximumMinutes = 21;
      },
    ],
    [
      'unsafe URL',
      (c: ReturnType<typeof candidate>) => {
        c.modelSource.sourceUrl = 'https://user:pass@example.test/';
      },
    ],
    [
      'missing model',
      (c: ReturnType<typeof candidate>) => {
        delete c.modelSource;
      },
    ],
    [
      'unknown calibration',
      (c: ReturnType<typeof candidate>) => {
        c.preparation.calibrationStatus = 'evaluated';
      },
    ],
    [
      'invented human',
      (c: ReturnType<typeof candidate>) => {
        c.preparation.humanReviewer = 'reviewer';
      },
    ],
    [
      'invented observation',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[2].observedAt = c.generatedAt;
      },
    ],
    [
      'wrong fields',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].permittedFields = ['walking-duration'];
      },
    ],
    [
      'expired evidence',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].validUntil = c.generatedAt;
      },
    ],
    [
      'wrong path model',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].edges[0].modelledWalk.modelVersion = 'model-v2';
      },
    ],
    [
      'wrong path hash',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].edges[0].modelledWalk.pathHash = 'A'.repeat(64);
      },
    ],
    [
      'unreferenced evidence',
      (c: ReturnType<typeof candidate>) => {
        c.evidence.push({ ...c.evidence[0], id: 'unused' });
      },
    ],
    [
      'wrong attribution',
      (c: ReturnType<typeof candidate>) => {
        c.attribution = 'reviewed';
      },
    ],
    [
      'wrong offer',
      (c: ReturnType<typeof candidate>) => {
        c.derivedDataOffer = 'https://example.test/private';
      },
    ],
    [
      'negative count',
      (c: ReturnType<typeof candidate>) => {
        c.expansionCount = -1;
      },
    ],
    [
      'fractional count',
      (c: ReturnType<typeof candidate>) => {
        c.expansionCount = 1.5;
      },
    ],
    [
      'wrong model input',
      (c: ReturnType<typeof candidate>) => {
        c.modelSource.inputSha256 = 'c'.repeat(64);
      },
    ],
    [
      'missing uncertainty',
      (c: ReturnType<typeof candidate>) => {
        c.uncertainties = c.uncertainties.filter((s: string) => s !== 'openingHoursUnknown');
      },
    ],
    [
      'food covered',
      (c: ReturnType<typeof candidate>) => {
        c.coveredInterests = ['food'];
      },
    ],
    [
      'wrong coverage',
      (c: ReturnType<typeof candidate>) => {
        c.unmetInterests = ['food'];
      },
    ],
    [
      'duplicate UUID',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[1].proposedBlockId = c.days[0].blocks[0].proposedBlockId;
      },
    ],
    [
      'invented time',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].startTime = '09:00';
      },
    ],
    [
      'invented money',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].estimate = { amount: '1.00', currency: 'RUB' };
      },
    ],
    [
      'long note',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].blocks[0].note = '😀'.repeat(501);
      },
    ],
  ])('rejects %s for complete response', (_name, mutate) => {
    const c = candidate();
    mutate(c);
    expect(() => decode(c)).toThrow();
  });
  it.each([
    '{"x":1,"x":2}',
    '{"x":1,"\\u0078":2}',
    '{"nested":{"x":1,"x":2}}',
    '{"x":"\\ud800"}',
    '{"x":"\\udc00"}',
    '{"x":01}',
    '{"x":1e999}',
    '{"x":true,}',
    '\ufeff{}',
    '{}x',
  ])('rejects invalid raw JSON %s', (raw) => {
    expect(() => client.parseTripItineraryJson(raw)).toThrow();
  });
  it('bounds every container depth and actual multibyte UTF8 bytes exactly', () => {
    expect(client.parseTripItineraryJson('['.repeat(16) + '0' + ']'.repeat(16))).toBeDefined();
    expect(() => client.parseTripItineraryJson('['.repeat(17) + '0' + ']'.repeat(17))).toThrow();
    const raw = '"' + 'я'.repeat(65535) + '"';
    expect(new TextEncoder().encode(raw).length).toBe(131072);
    expect(client.parseTripItineraryJson(new TextEncoder().encode(raw))).toBeDefined();
    expect(() => client.parseTripItineraryJson(new TextEncoder().encode(raw + ' '))).toThrow();
    const ready = new TextDecoder().decode(fixture('ready'));
    expect(client.decodeTripItineraryAvailability(ready.padEnd(2048))).toBeDefined();
    expect(() => client.decodeTripItineraryAvailability(ready.padEnd(2049))).toThrow();
  });
  it.each([[0xff], [0xef, 0xbb, 0xbf, 0x7b, 0x7d], [0xc2], [0xe2, 0x82]])(
    'rejects invalid UTF8/BOM bytes %s',
    (...bytes) => {
      expect(() => client.parseTripItineraryJson(new Uint8Array(bytes))).toThrow();
    },
  );
  it('accepts actual Core zero-length same-anchor model with both exact formula branches', () => {
    const c = candidate(),
      path = c.days[0].edges[0].modelledWalk;
    path.pathLengthMillimetres = '0';
    path.toAnchorNodeId = path.fromAnchorNodeId;
    path.crossingCount = 0;
    c.days[0].edges[0].minimumMinutes = 1;
    c.days[0].edges[0].maximumMinutes = 1;
    expect(decode(c).outcome).toBe('candidate');
    path.crossingCount = 2;
    c.days[0].edges[0].maximumMinutes = 2;
    expect(decode(c).outcome).toBe('candidate');
    path.toAnchorNodeId = '2';
    expect(() => decode(c)).toThrow();
  });
  it('retains large anchor and Wikidata revisions losslessly', () => {
    const c = candidate();
    c.days[0].edges[0].modelledWalk.fromAnchorNodeId = '9223372036854775807';
    c.days[0].blocks[0].wikidataRevision = '9223372036854775807';
    c.evidence[0].sourceUrl = 'https://www.wikidata.org/w/index.php?title=Q1&oldid=9223372036854775807';
    const result = decode(c);
    if (result.outcome === 'unsupported') throw new Error('computed');
    expect(result.days[0].blocks[0].wikidataRevision).toBe('9223372036854775807');
    expect(result.days[0].edges[0].modelledWalk.fromAnchorNodeId).toBe('9223372036854775807');
  });
  it('accepts known access separately and rejects inconsistent opening intervals', () => {
    const c = candidate(),
      b = c.days[0].blocks[0];
    b.facts[1] = { kind: 'access', state: 'known', evidenceId: 'access-p0', unknownReason: null };
    b.openingIntervals = [{ day: 4, startMinute: 540, endMinute: 1080 }];
    c.evidence.push({
      ...c.evidence[0],
      id: 'access-p0',
      kind: 'access',
      method: 'source-record',
      rightsBasis: 'explicit-permission',
      sourceUrl: 'https://permission.example.test/hours',
      sourceLabel: 'permission.example.test',
      validUntil: '2026-10-25T00:00:00.0000000Z',
      permittedFields: ['opening-hours'],
    });
    expect(decode(c).outcome).toBe('candidate');
    b.openingIntervals[0].day = 5;
    expect(() => decode(c)).toThrow();
  });
  it.each([
    [
      'one-tick build in future',
      (c: ReturnType<typeof candidate>) => {
        c.preparation.buildAsOfUtc = '2026-10-21T00:00:00.0000001Z';
      },
    ],
    [
      'one-tick expiry over fifteen minutes',
      (c: ReturnType<typeof candidate>) => {
        c.expiresAt = '2026-10-21T00:15:00.0000001Z';
      },
    ],
    [
      'wrong exact expiry',
      (c: ReturnType<typeof candidate>) => {
        c.expiresAt = '2026-10-21T00:14:00.0000000Z';
      },
    ],
    [
      'overlong identity lease',
      (c: ReturnType<typeof candidate>) => {
        c.evidence[0].validUntil = '2027-01-18T00:00:00.0000000Z';
      },
    ],
    [
      'wrong model expiry',
      (c: ReturnType<typeof candidate>) => {
        c.modelSource.validUntil = '2026-11-18T00:00:00.0000000Z';
        c.evidence[2].validUntil = c.modelSource.validUntil;
      },
    ],
    [
      'future trip before processing',
      (c: ReturnType<typeof candidate>) => {
        c.days[0].date = '2026-10-20';
        c.days[0].blocks.forEach((b: ReturnType<typeof candidate>) => {
          b.date = c.days[0].date;
        });
      },
    ],
    [
      'search-limited counter mismatch',
      (c: ReturnType<typeof candidate>) => {
        c.searchLimited = true;
        c.coverage = 'partial';
        c.reasons.push('searchLimited');
      },
    ],
  ])('rejects %s with full timestamp/model precision', (_name, mutate) => {
    const c = candidate();
    mutate(c);
    expect(() => decode(c)).toThrow();
  });
  it('checks escaped duplicate keys at root, nested evidence and path objects', () => {
    const raw = new TextDecoder().decode(fixture('candidate'));
    for (const value of [
      raw.replace('"candidateVersion":1', '"candidateVersion":1,"\\u0063andidateVersion":1'),
      raw.replace('"pathId":"path-e0"', '"pathId":"path-e0","\\u0070athId":"path-e0"'),
      raw.replace('"scopeId":"p0"', '"scopeId":"p0","\\u0073copeId":"p0"'),
    ])
      expect(() => client.decodeTripItineraryCandidate(value)).toThrow();
  });
  it('never constructs a prototype from decoded input keys', () => {
    const result = client.parseTripItineraryJson('{"__proto__":{"polluted":true}}');
    expect(Object.getPrototypeOf(result)).toBeNull();
    expect({}).not.toHaveProperty('polluted');
  });
  it('accepts whole-day lease equality and rejects one tick before local day end', () => {
    const c = candidate();
    c.evidence[0].validUntil = '2026-10-22T21:00:00.0000000Z';
    c.releaseValidUntil = c.evidence[0].validUntil;
    expect(decode(c).outcome).toBe('candidate');
    c.evidence[0].validUntil = '2026-10-22T20:59:59.9999999Z';
    expect(() => decode(c)).toThrow();
  });
  it('rejects package identity whose release outlives referenced identity source', () => {
    const c = candidate();
    c.evidence[0].validUntil = '2026-10-25T00:00:00.0000000Z';
    expect(() => decode(c)).toThrow();
  });
  it.each(['ready', 'candidate', 'partial', 'empty', 'unsupported'])(
    'rejects raw-byte unknown empty root property in %s',
    (name) => {
      const raw = new TextDecoder().decode(fixture(name));
      const bytes = new TextEncoder().encode(raw.replace('{', '{"":{"unvalidated":true},'));
      expect(() =>
        name === 'ready' ? client.decodeTripItineraryAvailability(bytes) : client.decodeTripItineraryCandidate(bytes),
      ).toThrow(client.TripsItineraryContractError);
    },
  );
  it.each([
    ['model source', '"modelSource":{'],
    ['preparation', '"preparation":{'],
    ['day', '"days":[{'],
    ['block', '"blocks":[{'],
    ['identity fact', '"facts":[{'],
    ['unknown access fact', '{"kind":"access","state":"unknown"'],
    ['edge', '"edges":[{'],
    ['modelled path', '"modelledWalk":{'],
    ['evidence', '"evidence":[{'],
  ])('rejects raw-byte unknown empty nested property in %s', (_name, marker) => {
    const raw = new TextDecoder().decode(fixture('candidate'));
    expect(raw).toContain(marker);
    const inserted = marker.endsWith('{')
      ? `${marker}"":{"unvalidated":true},`
      : '{"":{"unvalidated":true},"kind":"access","state":"unknown"';
    const bytes = new TextEncoder().encode(raw.replace(marker, inserted));
    expect(() => client.decodeTripItineraryCandidate(bytes)).toThrow(client.TripsItineraryContractError);
  });
  it('rejects raw-byte unknown empty key in a valid known opening interval', () => {
    const c = candidate(),
      b = c.days[0].blocks[0];
    b.facts[1] = { kind: 'access', state: 'known', evidenceId: 'access-p0', unknownReason: null };
    b.openingIntervals = [{ day: 4, startMinute: 540, endMinute: 1080 }];
    c.evidence.push({
      ...c.evidence[0],
      id: 'access-p0',
      kind: 'access',
      method: 'source-record',
      rightsBasis: 'explicit-permission',
      sourceUrl: 'https://permission.example.test/hours',
      sourceLabel: 'permission.example.test',
      validUntil: '2026-10-25T00:00:00.0000000Z',
      permittedFields: ['opening-hours'],
    });
    const raw = JSON.stringify(c);
    expect(client.decodeTripItineraryCandidate(new TextEncoder().encode(raw)).outcome).toBe('candidate');
    const bytes = new TextEncoder().encode(
      raw.replace('"openingIntervals":[{', '"openingIntervals":[{"":{"unvalidated":true},'),
    );
    expect(() => client.decodeTripItineraryCandidate(bytes)).toThrow(client.TripsItineraryContractError);
  });
  it('keeps generic JSON empty names valid and problem traceId optional', () => {
    const parsed = client.parseTripItineraryJson(new TextEncoder().encode('{"":{"validJson":true}}')) as Record<
      string,
      unknown
    >;
    expect(parsed['']).toEqual({ validJson: true });
    const error = JSON.parse(new TextDecoder().decode(fixture('error')));
    expect(client.decodeTripItineraryProblem(fixture('error'), 428).traceId).toBe(error.traceId);
    delete error.traceId;
    expect(client.decodeTripItineraryProblem(new TextEncoder().encode(JSON.stringify(error)), 428)).not.toHaveProperty(
      'traceId',
    );
    expect(() =>
      client.decodeTripItineraryProblem(new TextEncoder().encode(JSON.stringify({ ...error, '': true })), 428),
    ).toThrow(client.TripsItineraryContractError);
  });
  it('does not add fake package/source to unsupported or accept untyped problems', () => {
    const u = JSON.parse(new TextDecoder().decode(fixture('unsupported')));
    expect(() => decode({ ...u, datasetId: 'fake' })).toThrow();
    expect(() => client.decodeTripItineraryProblem(fixture('error'), 400)).toThrow();
    expect(() =>
      client.decodeTripItineraryProblem('{"type":"about:blank","title":"error","status":400,"code":"arbitrary"}', 400),
    ).toThrow();
  });
});
