import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { expectedBracesLock, verifyBracesArtifact } from '../security/braces-artifact.mjs';
import { expectedDeepmergeLock, verifyDeepmergeArtifact } from '../security/deepmerge-artifact.mjs';
import { validateAuditReport, validateLockProvenance } from './dependency-audit.mjs';

const url = 'https://github.com/advisories/GHSA-vfj7-8cjw-p6xm';
const proof = await verifyBracesArtifact(fileURLToPath(new URL('../..', import.meta.url)));
const canonical = expectedBracesLock(proof, 'root');
const leaf = { name: 'braces', url, severity: 'high', range: '<=3.0.3' };
function fixture() {
  const report = {
    auditReportVersion: 2,
    vulnerabilities: {
      parent: { name: 'parent', severity: 'high', via: ['braces'], nodes: ['node_modules/parent'] },
      braces: { name: 'braces', severity: 'high', via: [leaf], nodes: ['node_modules/braces'] },
    },
    metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 2, critical: 0, total: 2 } },
  };
  const lock = {
    lockfileVersion: 3,
    packages: {
      '': {},
      'node_modules/parent': {
        version: '1.0.0',
        dependencies: { braces: '^3.0.3' },
        resolved: 'https://registry.npmjs.org/parent/-/parent-1.0.0.tgz',
        integrity: 'sha512-ZmljdGlvbmFs',
      },
      'node_modules/braces': {
        version: '3.0.3',
        resolved: canonical.resolved,
        integrity: canonical.integrity,
      },
    },
  };
  const remediation = {
    advisory: url,
    kind: 'verified-local-security-backport',
    patchRevision: 1,
    packages: {
      parent: { severity: 'high', via: ['package:braces'], nodes: { 'node_modules/parent': '1.0.0' } },
      braces: {
        severity: 'high',
        via: [`advisory:braces:${url}:<=3.0.3:high`],
        nodes: { 'node_modules/braces': '3.0.3' },
      },
    },
  };
  return { report, lock, remediation };
}
test('keeps the exact known leaf and complete parent chain visible as a verified local backport', () => {
  const { report, lock, remediation } = fixture();
  assert.deepEqual(validateAuditReport(report, lock, remediation, proof).allowed.sort(), ['braces', 'parent']);
});
test('rejects mixed known and new leaf advisory even on a previously allowed parent', () => {
  const { report, lock, remediation } = fixture();
  report.vulnerabilities.parent.via.push({
    name: 'parent',
    url: 'https://github.com/advisories/GHSA-new0-new0-new0',
    severity: 'moderate',
    range: '*',
  });
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /advisory|chain/i);
});
test('rejects a new parent chain rather than exempting every future braces consumer', () => {
  const { report, lock, remediation } = fixture();
  report.vulnerabilities.unreviewed = {
    name: 'unreviewed',
    via: ['braces'],
    severity: 'high',
    nodes: ['node_modules/unreviewed'],
  };
  report.metadata.vulnerabilities.total++;
  report.metadata.vulnerabilities.high++;
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /chain|review/i);
});
test('requires unforgeable artifact proof instead of an expiry waiver', () => {
  const { report, lock, remediation } = fixture();
  for (const fake of [undefined, {}, { provenance: proof.provenance }])
    assert.throws(() => validateAuditReport(report, lock, remediation, fake), /proof|backport/i);
  lock.packages['node_modules/braces'].resolved = 'https://registry.npmjs.org/braces/-/braces-3.0.3.tgz';
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /artifact|backport/i);
});

test('rejects version/path drift within an allowed package', () => {
  const { report, lock, remediation } = fixture();
  lock.packages['node_modules/braces'].version = '3.0.2';
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /version|drift/i);
});
test('rejects network/error reports, missing schema and inconsistent totals', () => {
  const { report, lock, remediation } = fixture();
  for (const invalid of [
    { error: { code: 'ENETUNREACH' } },
    {},
    { ...report, metadata: {} },
    { ...report, metadata: { vulnerabilities: { total: 0 } } },
    { ...report, metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 1, critical: 1, total: 2 } } },
  ])
    assert.throws(() => validateAuditReport(invalid, lock, remediation, proof), /report|schema|total/i);
});
test('rejects missing transitive references and cyclic metadata', () => {
  const { report, lock, remediation } = fixture();
  report.vulnerabilities.parent.via = ['missing'];
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /missing|chain/i);
  report.vulnerabilities.parent.via = ['braces'];
  report.vulnerabilities.braces.via = ['parent'];
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /cycl|chain/i);
});
test('does not accept a known GHSA attached to a different leaf', () => {
  const { report, lock, remediation } = fixture();
  report.vulnerabilities.braces.via = [{ ...leaf, name: 'other-package' }];
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /advisory|chain/i);
});
test('permits metadata edge variation only through reviewed actual lock carriers', () => {
  const { report, lock, remediation } = fixture();
  const peerPath = 'node_modules/reviewed-peer';
  lock.packages[peerPath] = { ...lock.packages['node_modules/parent'], dependencies: { braces: '^3.0.3' } };
  lock.packages['node_modules/parent'].dependencies = { 'reviewed-peer': '1.0.0' };
  report.vulnerabilities['reviewed-peer'] = {
    name: 'reviewed-peer',
    severity: 'high',
    nodes: [peerPath],
    via: ['braces'],
  };
  remediation.packages['reviewed-peer'] = { severity: 'high', nodes: { [peerPath]: '1.0.0' }, via: ['package:braces'] };
  report.metadata.vulnerabilities.total++;
  report.metadata.vulnerabilities.high++;
  assert.equal(validateAuditReport(report, lock, remediation, proof).allowed.length, 3);
  report.vulnerabilities.parent.via = ['reviewed-peer'];
  assert.equal(validateAuditReport(report, lock, remediation, proof).allowed.length, 3);
  lock.packages['node_modules/parent'].dependencies = { braces: '^3.0.3' };
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /chain|review/i);
});
test('a retained dependency cycle requires a real lock path and a complete approved leaf exit', () => {
  const { report, lock, remediation } = fixture();
  const peerPath = 'node_modules/reviewed-peer';
  lock.packages[peerPath] = { ...lock.packages['node_modules/parent'], dependencies: { parent: '1.0.0' } };
  lock.packages['node_modules/parent'].dependencies['reviewed-peer'] = '1.0.0';
  report.vulnerabilities.parent.via.push('reviewed-peer');
  report.vulnerabilities['reviewed-peer'] = {
    name: 'reviewed-peer',
    severity: 'high',
    nodes: [peerPath],
    via: ['parent'],
  };
  remediation.packages['reviewed-peer'] = { severity: 'high', nodes: { [peerPath]: '1.0.0' }, via: ['package:parent'] };
  report.metadata.vulnerabilities.total++;
  report.metadata.vulnerabilities.high++;
  assert.equal(validateAuditReport(report, lock, remediation, proof).allowed.length, 3);
  report.vulnerabilities['reviewed-peer'].via.push({
    name: 'reviewed-peer',
    url: 'https://github.com/advisories/GHSA-new0-new0-new0',
    range: '*',
    severity: 'moderate',
  });
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /advisory|chain/i);
});
test('a closed no-leaf cycle fails even while another report node has the approved leaf', () => {
  const { report, lock, remediation } = fixture();
  for (const [name, other] of [
    ['orphan-a', 'orphan-b'],
    ['orphan-b', 'orphan-a'],
  ]) {
    const path = `node_modules/${name}`;
    lock.packages[path] = { ...lock.packages['node_modules/parent'], dependencies: { [other]: '1.0.0' } };
    report.vulnerabilities[name] = { name, severity: 'high', nodes: [path], via: [other] };
    remediation.packages[name] = { severity: 'high', nodes: { [path]: '1.0.0' } };
    report.metadata.vulnerabilities.total++;
    report.metadata.vulnerabilities.high++;
  }
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /cycl|chain/i);
});
test('a clean raw report still requires the actual fixed artifact and every locked copy', () => {
  const { report, lock, remediation } = fixture();
  report.vulnerabilities = {};
  report.metadata.vulnerabilities.high = 0;
  report.metadata.vulnerabilities.total = 0;
  assert.deepEqual(validateAuditReport(report, lock, remediation, proof).allowed, []);
  lock.packages['node_modules/parent/node_modules/braces'] = { ...lock.packages['node_modules/braces'] };
  assert.throws(() => validateAuditReport(report, lock, remediation, proof), /copy|locked/i);
});

test('requires registry HTTPS and an integrity value for every fetched package', () => {
  const { lock } = fixture();
  assert.equal(validateLockProvenance(lock, proof), true);
  lock.packages['node_modules/braces'].resolved = 'https://unreviewed.example/braces.tgz';
  assert.throws(() => validateLockProvenance(lock, proof), /registry|provenance/i);
  lock.packages['node_modules/braces'].resolved = 'https://registry.npmjs.org/braces/-/braces-3.0.3.tgz';
  delete lock.packages['node_modules/braces'].integrity;
  assert.throws(() => validateLockProvenance(lock, proof), /integrity/i);
});
test('bundled children require a covered trusted parent tarball', () => {
  const { lock } = fixture();
  lock.packages['node_modules/parent/node_modules/child'] = { version: '1.0.0', inBundle: true };
  assert.equal(validateLockProvenance(lock, proof), true);
  delete lock.packages['node_modules/parent'].integrity;
  assert.throws(() => validateLockProvenance(lock, proof), /integrity|provenance/i);
});
test('actual npm11/npm12 reports resolve the same approved leaf through retained lock carriers', () => {
  const load = (name) =>
    JSON.parse(readFileSync(new URL(`./fixtures/dependency-audit/${name}`, import.meta.url), 'utf8'));
  // The lock fixture retains the actual reverse closure to braces, pruning unrelated edges.
  const lock = load('retained-lock-closure.json');
  const snapshot = load('remediation-snapshot.json');
  Object.assign(lock.packages['node_modules/braces'], { resolved: canonical.resolved, integrity: canonical.integrity });
  for (const name of ['npm11-root.json', 'npm12-root.json']) {
    const report = load(name);
    const result = validateAuditReport(
      report,
      lock,
      {
        advisory: url,
        kind: 'verified-local-security-backport',
        patchRevision: 1,
        packages: snapshot.packages,
      },
      proof,
    );
    assert.equal(result.allowed.length, report.metadata.vulnerabilities.total);
  }
});

const deepProof = await verifyDeepmergeArtifact(fileURLToPath(new URL('../..', import.meta.url)));
function deepFixture() {
  const f = fixture(),
    e = expectedDeepmergeLock(deepProof);
  f.lock.packages['node_modules/deepmerge'] = { version: e.version, resolved: e.resolved, integrity: e.integrity };
  f.lock.packages['node_modules/fork-ts-checker-webpack-plugin'] = {
    version: '9.1.0',
    dependencies: { deepmerge: '^4.2.2' },
    resolved: 'https://registry.npmjs.org/fork-ts-checker-webpack-plugin/-/fork-ts-checker-webpack-plugin-9.1.0.tgz',
    integrity: 'sha512-ZmljdGlvbmFs',
  };
  f.report.vulnerabilities = {
    deepmerge: {
      name: 'deepmerge',
      severity: 'low',
      via: [
        {
          name: 'deepmerge',
          url: 'https://github.com/advisories/GHSA-gcqx-w48m-w2h4',
          range: '>=4.2.0 <=4.3.1',
          severity: 'low',
        },
      ],
      nodes: ['node_modules/deepmerge'],
    },
    'fork-ts-checker-webpack-plugin': {
      name: 'fork-ts-checker-webpack-plugin',
      severity: 'low',
      via: ['deepmerge'],
      nodes: ['node_modules/fork-ts-checker-webpack-plugin'],
    },
  };
  f.report.metadata.vulnerabilities = { info: 0, low: 2, moderate: 0, high: 0, critical: 0, total: 2 };
  f.remediation.deepmergeBackport = {
    advisory: 'https://github.com/advisories/GHSA-gcqx-w48m-w2h4',
    patchRevision: 1,
    scopes: {
      root: {
        packages: {
          deepmerge: { severity: 'low', nodes: { 'node_modules/deepmerge': '4.3.1' } },
          'fork-ts-checker-webpack-plugin': {
            severity: 'low',
            nodes: { 'node_modules/fork-ts-checker-webpack-plugin': '9.1.0' },
          },
        },
      },
    },
  };
  return f;
}
test('deepmerge chain requires its own actual artifact proof, exact nodes and unchanged advisory', () => {
  const f = deepFixture();
  assert.deepEqual(validateAuditReport(f.report, f.lock, f.remediation, proof, 'root', deepProof).allowed.sort(), [
    'deepmerge',
    'fork-ts-checker-webpack-plugin',
  ]);
  assert.throws(() => validateAuditReport(f.report, f.lock, f.remediation, proof), /advisory|proof|provenance/i);
  for (const value of ['https://github.com/advisories/GHSA-new0-new0-new0', 'changed']) {
    const changed = structuredClone(f.report);
    changed.vulnerabilities.deepmerge.via[0].url = value;
    assert.throws(
      () => validateAuditReport(changed, f.lock, f.remediation, proof, 'root', deepProof),
      /advisory|chain/i,
    );
  }
  const changed = structuredClone(f.lock);
  changed.packages['node_modules/fork-ts-checker-webpack-plugin'].dependencies = {};
  assert.throws(() => validateAuditReport(f.report, changed, f.remediation, proof, 'root', deepProof), /chain|edge/i);
  assert.throws(
    () => validateAuditReport(f.report, f.lock, f.remediation, proof, 'openspec', deepProof),
    /scope|proof|provenance|drift/i,
  );
});
