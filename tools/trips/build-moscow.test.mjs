import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, realpathSync, rmSync, statSync, truncateSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import test, { after } from 'node:test';
import { gzipSync } from 'node:zlib';

// FICTIONAL graph and identities; these prove CLI/data integrity, never Moscow coverage.
const helper = resolve('tools/trips/build-moscow.cs');
const fixedDay = new Date();
fixedDay.setUTCHours(0, 0, 0, 0);
const publication = new Date(fixedDay.getTime() - 3 * 86400000).toISOString();
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const tempRoot = realpathSync(tmpdir());
const dir = mkdtempSync(join(tempRoot, 'travel-m2-builder-'));
after(() => {
  if (dirname(dir) !== tempRoot || !basename(dir).startsWith('travel-m2-builder-'))
    throw new Error('Refusing cleanup outside owned fixture directory');
  rmSync(dir, { recursive: true, force: false });
});
let sequence = 0;
function fixture({
  fixtureDay = fixedDay,
  count = 18,
  wayTags = '<tag k="highway" v="footway"/>',
  nodeTags = '',
  gzip = false,
  mutateInventory,
  mutateManifest,
  xml,
} = {}) {
  const fixtureAsOf = fixtureDay.toISOString();
  const path = join(dir, String(sequence++));
  const points = Array.from({ length: count }, (_, index) => ({
    id: index + 1,
    lat: 55.745 + index * 0.0001,
    lon: 37.61,
  }));
  const inventory = points.map((point) => ({
    id: `fictional-${String(point.id).padStart(2, '0')}`,
    title: `FICTIONAL ${point.id}`,
    tags: ['architecture', 'culture', 'history', 'nature'],
    dwellMinutes: 20,
    wikidataEntityId: `Q${point.id}`,
    wikidataRevision: 1,
    retrievedAtUtc: fixtureAsOf,
    latitude: point.lat,
    longitude: point.lon,
    anchorNodeId: point.id,
  }));
  mutateInventory?.(inventory);
  const body =
    xml ??
    `<osm version="0.6">${points.map((point) => `<node id="${point.id}" lat="${point.lat}" lon="${point.lon}">${typeof nodeTags === 'function' ? nodeTags(point.id) : nodeTags}</node>`).join('')}${points
      .slice(1)
      .map(
        (point) =>
          `<way id="${100 + point.id}"><nd ref="${point.id - 1}"/><nd ref="${point.id}"/>${typeof wayTags === 'function' ? wayTags(point.id) : wayTags}</way>`,
      )
      .join('')}</osm>`;
  const bytes = gzip ? gzipSync(body) : Buffer.from(body);
  const input = `${path}.osm${gzip ? '.gz' : ''}`;
  const inputManifest = `${path}.input.json`;
  const inventoryPath = `${path}.inventory.json`;
  const manifest = {
    sourceUrl: 'https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz',
    sha256: sha(bytes),
    bytes: bytes.length,
    snapshotPublishedAtUtc: new Date(fixtureDay.getTime() - 3 * 86400000).toISOString(),
    downloadedAtUtc: fixtureAsOf,
  };
  mutateManifest?.(manifest);
  writeFileSync(input, bytes);
  writeFileSync(inputManifest, JSON.stringify(manifest));
  writeFileSync(inventoryPath, JSON.stringify(inventory));
  return { input, inputManifest, inventoryPath, output: `${path}.output`, inventory, manifest, asOf: fixtureAsOf };
}
function run(input, extra = []) {
  return spawnSync(
    'dotnet',
    [
      'run',
      '--file',
      helper,
      '--verbosity',
      'quiet',
      '--',
      '--osm',
      input.input,
      '--inventory',
      input.inventoryPath,
      '--input-manifest',
      input.inputManifest,
      '--as-of',
      input.asOf,
      '--dataset-id',
      'moscow-outdoor',
      '--version',
      '1',
      '--output',
      input.output,
      ...extra,
    ],
    { encoding: 'utf8', timeout: 90000, maxBuffer: 512 * 1024, windowsHide: true },
  );
}
function ready(input) {
  const result = run(input);
  assert.equal(result.status, 0, result.stderr + result.stdout);
  return {
    payload: JSON.parse(readFileSync(join(input.output, 'moscow-v1.json'))),
    evidence: JSON.parse(readFileSync(join(input.output, 'walking-evidence.json'))),
    manifest: JSON.parse(readFileSync(join(input.output, 'moscow-v1.manifest.json'))),
  };
}
function rejected(input, reason) {
  const result = run(input);
  assert.notEqual(result.status, 0, 'Invalid input was accepted');
  assert.match(result.stderr + result.stdout, reason);
  assert.equal(statSync(input.output, { throwIfNoEntry: false }), undefined, 'Failed build published output');
}

test('offline fixture emits 18 identities/90 selected paths with truthful model semantics and exact byte hashes', () => {
  const input = fixture({
    gzip: true,
    nodeTags: (id) => (id === 1 || id === 2 ? '<tag k="highway" v="crossing"/>' : ''),
  });
  const { payload, evidence, manifest } = ready(input);
  assert.equal(payload.pois.length, 18);
  assert.equal(payload.edges.length, 90);
  assert.equal(payload.dataMode, 'osm-modelled-demo');
  assert.equal(payload.humanCurator, null);
  assert.equal(payload.humanReviewer, null);
  assert.equal(payload.humanQuality, 'not-evaluated');
  assert.equal(new Date(payload.releaseValidUntil).getTime(), new Date(publication).getTime() + 30 * 86400000);
  for (const edge of payload.edges) {
    assert.equal(edge.method, 'modelled');
    assert.equal(edge.rightsBasis, 'odbl');
    assert.equal(edge.observedAt, null);
    assert.equal(edge.calibrationStatus, 'not-evaluated');
    assert.equal(edge.minimumMinutes, Math.max(1, Math.ceil(edge.pathLengthMillimetres / 75000)));
    assert.equal(
      edge.maximumMinutes,
      Math.max(edge.minimumMinutes, Math.ceil(edge.pathLengthMillimetres / 45000) + edge.crossingCount),
    );
    assert.equal(edge.sourceSha256, input.manifest.sha256);
  }
  const first = payload.edges.find((edge) => edge.fromPoiId === 'fictional-01' && edge.toPoiId === 'fictional-02');
  assert.equal(first.crossingCount, 2, 'Endpoints count, distinct crossing nodes only');
  assert.equal(evidence.paths.length, 90);
  for (const [name, digest] of Object.entries(manifest.files)) {
    const bytes = readFileSync(join(input.output, name));
    assert.equal(sha(bytes), digest);
    assert.notEqual(bytes[0], 0xef);
    assert.equal(bytes.includes(13), false);
  }
  assert.ok(statSync(join(input.output, 'moscow-v1.json')).size <= 256 * 1024);
  assert.ok(statSync(join(input.output, 'moscow-v1.manifest.json')).size <= 2048);
  assert.ok(statSync(join(input.output, 'walking-evidence.json')).size <= 4 * 1024 * 1024);
  assert.match(readFileSync(join(input.output, 'ATTRIBUTION.txt'), 'utf8'), /OpenStreetMap contributors.*ODbL/s);
});

test('same bytes and frozen metadata reproduce exact artifact hashes', () => {
  const input = fixture();
  const first = ready(input);
  input.output += '-repeat';
  const second = ready(input);
  assert.deepEqual(first.manifest.files, second.manifest.files);
});

test('48 connected anchors select at most five outgoing each and stable distance/ID ordering', () => {
  const input = fixture({ count: 48 });
  const first = ready(input);
  assert.equal(first.payload.edges.length, 240);
  const shuffled = fixture({ count: 48, mutateInventory: (pois) => pois.reverse() });
  const second = ready(shuffled);
  assert.deepEqual(first.evidence.paths, second.evidence.paths);
});

test('directed foot permission overrides road one-way without inventing reverse paths', () => {
  const forward = ready(fixture({ wayTags: '<tag k="highway" v="footway"/><tag k="oneway:foot" v="yes"/>' }));
  assert.equal(forward.payload.edges.length, 75);
  assert.ok(forward.evidence.paths.every((path) => path.nodeIds[0] < path.nodeIds.at(-1)));
  const reverse = ready(fixture({ wayTags: '<tag k="highway" v="footway"/><tag k="oneway:foot" v="-1"/>' }));
  assert.equal(reverse.payload.edges.length, 75);
  assert.ok(reverse.evidence.paths.every((path) => path.nodeIds[0] > path.nodeIds.at(-1)));
  assert.equal(
    ready(fixture({ wayTags: '<tag k="highway" v="footway"/><tag k="oneway" v="yes"/>' })).payload.edges.length,
    90,
  );
});

test('denied, conditional and unknown bridge permissions never create a connection across the gap', () => {
  for (const tags of [
    '<tag k="access" v="private"/><tag k="foot" v="yes"/>',
    '<tag k="access:conditional" v="yes @ (sunrise-sunset)"/>',
    '<tag k="foot" v="unknown"/>',
    '<tag k="oneway:foot" v="unknown"/>',
    '<tag k="area" v="yes"/>',
  ]) {
    const result = ready(fixture({ wayTags: (id) => `<tag k="highway" v="footway"/>${id === 10 ? tags : ''}` }));
    assert.ok(
      result.evidence.paths.every(
        (path) => !(path.nodeIds.some((id) => id < 10) && path.nodeIds.some((id) => id >= 10)),
      ),
      tags,
    );
  }
});

test('crossing ways alone add no delay and node-plus-way representations do not double count', () => {
  const result = ready(
    fixture({
      wayTags: '<tag k="highway" v="footway"/><tag k="footway" v="crossing"/>',
      nodeTags: (id) => (id === 2 ? '<tag k="highway" v="crossing"/>' : ''),
    }),
  );
  const edge = result.payload.edges.find(
    (item) => item.fromPoiId === 'fictional-01' && item.toPoiId === 'fictional-03',
  );
  assert.equal(edge.crossingCount, 1);
});

test('unsafe nodes, missing anchors, sparse coverage and stale/unbound metadata fail without release', () => {
  for (const tags of [
    '<tag k="barrier" v="gate"/>',
    '<tag k="barrier" v="wall"/>',
    '<tag k="crossing" v="no"/>',
    '<tag k="foot" v="private"/>',
  ])
    rejected(fixture({ nodeTags: (id) => (id === 1 ? tags : '') }), /anchor|18|coverage/i);
  rejected(
    fixture({
      mutateInventory: (pois) => {
        pois[0].anchorNodeId = 999;
      },
    }),
    /anchor/i,
  );
  rejected(
    fixture({
      mutateInventory: (pois) => {
        pois[0].longitude += 0.01;
      },
    }),
    /anchor/i,
  );
  rejected(fixture({ count: 17 }), /18|inventory/i);
  rejected(
    fixture({
      mutateManifest: (manifest) => {
        manifest.sha256 = '0'.repeat(64);
      },
    }),
    /hash|sha/i,
  );
  rejected(
    fixture({
      mutateManifest: (manifest) => {
        delete manifest.snapshotPublishedAtUtc;
      },
    }),
    /publication|snapshotPublished/i,
  );
  rejected(
    fixture({
      mutateManifest: (manifest) => {
        manifest.snapshotPublishedAtUtc = futurePublication(manifest.downloadedAtUtc);
      },
    }),
    /publication|timestamp|order/i,
  );
  rejected(
    fixture({
      mutateManifest: (manifest) => {
        manifest.snapshotPublishedAtUtc = '2026-09-01T00:00:00Z';
      },
    }),
    /expired|stale/i,
  );
});

function futurePublication(downloadedAtUtc) {
  return new Date(Date.parse(downloadedAtUtc) + 86400000).toISOString();
}

test('invalid future publication stays after the fixture clock across UTC days and years', () => {
  for (const day of ['2026-10-21', '2026-10-22', '2026-10-23', '2027-01-01', '2028-02-29']) {
    const input = fixture({
      fixtureDay: new Date(day + 'T00:00:00Z'),
      mutateManifest: (manifest) => {
        manifest.snapshotPublishedAtUtc = futurePublication(manifest.downloadedAtUtc);
      },
    });
    assert.equal(input.asOf, day + 'T00:00:00.000Z');
    assert.equal(input.manifest.downloadedAtUtc, input.asOf);
    assert.equal(input.inventory[0].retrievedAtUtc, input.asOf);
    assert.equal(Date.parse(input.manifest.snapshotPublishedAtUtc) - Date.parse(input.asOf), 86400000, day);
  }
});

test('XML entities, corrupted gzip and compressed input cap are rejected before publishing', () => {
  rejected(fixture({ xml: '<!DOCTYPE osm [<!ENTITY bad "external">]><osm>&bad;</osm>' }), /DTD|XML|entity/i);
  const corrupt = fixture({ gzip: true });
  const bytes = Buffer.from('not gzip');
  writeFileSync(corrupt.input, bytes);
  corrupt.manifest.sha256 = sha(bytes);
  corrupt.manifest.bytes = bytes.length;
  writeFileSync(corrupt.inputManifest, JSON.stringify(corrupt.manifest));
  rejected(corrupt, /gzip|archive|compression|corrupt/i);
  const huge = fixture();
  truncateSync(huge.input, 200 * 1024 * 1024 + 1);
  rejected(huge, /200|compressed|size/i);
});
test('future publication/download/build cannot become valid by supplying a future frozen clock', () => {
  const input = fixture({
    mutateManifest: (manifest) => {
      manifest.snapshotPublishedAtUtc = '2099-10-18T00:00:00Z';
      manifest.downloadedAtUtc = '2099-10-21T00:00:00Z';
    },
    mutateInventory: (pois) => {
      for (const poi of pois) poi.retrievedAtUtc = '2099-10-21T00:00:00Z';
    },
  });
  const result = run(input, ['--as-of', '2099-10-21T00:00:00Z']);
  // Repeating a CLI argument is independently rejected. Use one future frozen input to test clock binding itself.
  assert.notEqual(result.status, 0);
  const args = [
    'run',
    '--file',
    helper,
    '--verbosity',
    'quiet',
    '--',
    '--osm',
    input.input,
    '--inventory',
    input.inventoryPath,
    '--input-manifest',
    input.inputManifest,
    '--as-of',
    '2099-10-21T00:00:00Z',
    '--dataset-id',
    'moscow-outdoor',
    '--version',
    '1',
    '--output',
    input.output,
  ];
  const future = spawnSync('dotnet', args, { encoding: 'utf8', timeout: 90000, windowsHide: true });
  assert.notEqual(future.status, 0, 'Future publication was accepted by moving the build clock');
  assert.match(future.stderr + future.stdout, /future|publication|timestamp/i);
});

test('profile branches keep explicit feet/sidewalks and eligible barriers, exclude unsupported roads and tags', () => {
  for (const highway of ['residential', 'living_street', 'service', 'tertiary', 'unclassified', 'cycleway', 'track']) {
    assert.equal(
      ready(fixture({ wayTags: `<tag k="highway" v="${highway}"/><tag k="sidewalk" v="both"/>` })).payload.edges.length,
      90,
    );
  }
  for (const foot of ['designated', 'permissive'])
    assert.equal(
      ready(fixture({ wayTags: `<tag k="highway" v="service"/><tag k="foot" v="${foot}"/>` })).payload.edges.length,
      90,
    );
  for (const highway of ['pedestrian', 'path', 'steps'])
    assert.equal(ready(fixture({ wayTags: `<tag k="highway" v="${highway}"/>` })).payload.edges.length, 90);
  for (const direction of ['no', '0'])
    assert.equal(
      ready(fixture({ wayTags: `<tag k="highway" v="footway"/><tag k="oneway:foot" v="${direction}"/>` })).payload.edges
        .length,
      90,
    );
  for (const direction of ['1', 'true'])
    assert.equal(
      ready(fixture({ wayTags: `<tag k="highway" v="footway"/><tag k="oneway:foot" v="${direction}"/>` })).payload.edges
        .length,
      75,
    );
  for (const barrier of ['gate', 'lift_gate', 'kissing_gate', 'cycle_barrier', 'stile'])
    assert.equal(
      ready(fixture({ nodeTags: `<tag k="barrier" v="${barrier}"/><tag k="foot" v="yes"/>` })).payload.edges.length,
      90,
    );
  assert.equal(ready(fixture({ nodeTags: '<tag k="barrier" v="bollard"/>' })).payload.edges.length, 90);
  for (const tags of [
    '<tag k="highway" v="primary"/>',
    '<tag k="highway" v="residential"/>',
    '<tag k="highway" v="footway"/><tag k="foot:forward" v="yes"/>',
    '<tag k="highway" v="footway"/><tag k="foot:backward" v="yes"/>',
    '<tag k="highway" v="footway"/><tag k="construction" v="no"/>',
    '<tag k="highway" v="footway"/><tag k="proposed" v="no"/>',
    '<tag k="highway" v="footway"/><tag k="oneway:foot:conditional" v="no"/>',
    '<tag k="highway" v="footway"/><tag k="foot:conditional" v="no"/>',
    '<tag k="highway" v="footway"/><tag k="access" v="destination"/>',
  ])
    rejected(fixture({ wayTags: tags }), /anchor|coverage/i);
});
test('irrelevant contributor/editorial tags are ignored without changing modelled paths', () => {
  const extra = Array.from({ length: 300 }, (_, index) => `<tag k="irrelevant:${index}" v="metadata"/>`).join('');
  const base = ready(fixture());
  const tagged = ready(fixture({ nodeTags: extra, wayTags: `<tag k="highway" v="footway"/>${extra}` }));
  assert.deepEqual(base.evidence.paths, tagged.evidence.paths);
});
test('raw coordinate staging remains separate from the capped eligible graph', () => {
  const base = fixture();
  let xml = readFileSync(base.input, 'utf8');
  const unused = Array.from(
    { length: 100001 },
    (_, index) => `<node id="${1000 + index}" lat="55.74" lon="37.6"/>`,
  ).join('');
  xml = xml.replace('<way ', `${unused}<way `);
  assert.equal(ready(fixture({ xml })).payload.edges.length, 90);
  const extra = '<tag k="irrelevant" v="ignored"/>'.repeat(300);
  const disconnected = ready(
    fixture({
      wayTags: (id) => `<tag k="highway" v="footway"/>${id === 10 ? `${extra}<tag k="foot:conditional" v="no"/>` : ''}`,
    }),
  );
  assert.ok(
    disconnected.evidence.paths.every(
      (path) => !(path.nodeIds.some((id) => id < 10) && path.nodeIds.some((id) => id >= 10)),
    ),
  );
  rejected(
    fixture({ wayTags: '<tag k="highway" v="footway"/><tag k="foot" v="yes"/><tag k="foot" v="no"/>' }),
    /duplicate/i,
  );
});

test('raw-index and actual retained-graph overflows are distinct failures without a published package', () => {
  const nodes = (count) =>
    Array.from({ length: count }, (_, index) => `<node id="${index + 1}" lat="55.745" lon="37.61"/>`).join('');
  rejected(fixture({ xml: `<osm>${nodes(750001)}</osm>` }), /raw-coordinate.*750000/i);
  const references = Array.from({ length: 100001 }, (_, index) => index + 1);
  // Multiple bounded ways overlap at endpoints; no individual-way-size failure masks the graph cap.
  const ways = Array.from(
    { length: 100 },
    (_, index) =>
      `<way id="${index + 1}">${references
        .slice(index * 1000, index * 1000 + 1001)
        .map((id) => `<nd ref="${id}"/>`)
        .join('')}<tag k="highway" v="footway"/></way>`,
  ).join('');
  rejected(fixture({ xml: `<osm>${nodes(100001)}${ways}</osm>` }), /retained eligible graph.*100000/i);
});

test('expanded gzip data exceeding 2 GiB is rejected while streaming without allocating the expanded document', () => {
  const input = fixture({ gzip: true });
  // Concatenated gzip members: small bounded buffers, >2 GiB of valid XML whitespace after expansion.
  const members = [gzipSync('<osm>')];
  const chunk = gzipSync(' '.repeat(16 * 1024 * 1024));
  for (let index = 0; index < 129; index++) members.push(chunk);
  members.push(gzipSync('</osm>'));
  const bytes = Buffer.concat(members);
  writeFileSync(input.input, bytes);
  input.manifest.bytes = bytes.length;
  input.manifest.sha256 = sha(bytes);
  writeFileSync(input.inputManifest, JSON.stringify(input.manifest));
  rejected(input, /2 GiB|MaxCharactersInDocument|limit/i);
});

test('operator may tighten time/memory budgets but never raise hard ceilings, failures publish no package', () => {
  for (const [flag, value, reason] of [
    ['--max-wall-ms', '1', /wall|time/i],
    ['--max-memory-mib', '1', /memory|working-set/i],
    ['--max-wall-ms', '300001', /budget|limit/i],
    ['--max-memory-mib', '1025', /budget|limit/i],
  ]) {
    const input = fixture();
    const result = run(input, [flag, value]);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr + result.stdout, reason);
    assert.equal(statSync(input.output, { throwIfNoEntry: false }), undefined);
  }
});
