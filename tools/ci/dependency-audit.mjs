import { execFile } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  expectedBracesLock,
  requireBracesProof,
  verifyBracesArtifact,
  verifyBracesLock,
  verifyBracesManifest,
  verifyInstalledBraces,
} from '../security/braces-artifact.mjs';

import {
  expectedDeepmergeLock,
  requireDeepmergeProof,
  verifyDeepmergeArtifact,
  verifyDeepmergeLock,
  verifyDeepmergeManifest,
  verifyInstalledDeepmerge,
} from '../security/deepmerge-artifact.mjs';

const DEEPMERGE = 'https://github.com/advisories/GHSA-gcqx-w48m-w2h4';
const KNOWN = 'https://github.com/advisories/GHSA-vfj7-8cjw-p6xm';
const object = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const equal = (left, right) => JSON.stringify([...left].sort()) === JSON.stringify([...right].sort());
function viaSignature(via) {
  if (typeof via === 'string' && via) return `package:${via}`;
  if (!object(via) || !['name', 'url', 'range', 'severity'].every((key) => typeof via[key] === 'string'))
    throw new Error('Invalid advisory report schema');
  return `advisory:${via.name}:${via.url}:${via.range}:${via.severity}`;
}
export function validateLockProvenance(lock, proof, scope = 'root', deepmergeProof = null) {
  if (!object(lock) || lock.lockfileVersion !== 3 || !object(lock.packages))
    throw new Error('Invalid lock provenance schema');
  for (const [name, entry] of Object.entries(lock.packages)) {
    if (!name) continue;
    if (!object(entry) || entry.link) throw new Error(`Unreviewed package provenance: ${name}`);
    if (name === 'node_modules/braces' && entry.resolved?.startsWith('file:')) {
      const expected = expectedBracesLock(proof, scope);
      if (
        entry.version !== expected.version ||
        entry.resolved !== expected.resolved ||
        entry.integrity !== expected.integrity
      )
        throw new Error('Local backport artifact provenance drift');
      continue;
    }
    if (name === 'node_modules/deepmerge' && entry.resolved?.startsWith('file:')) {
      const expected = expectedDeepmergeLock(deepmergeProof, scope);
      if (
        entry.version !== expected.version ||
        entry.resolved !== expected.resolved ||
        entry.integrity !== expected.integrity
      )
        throw new Error('Local deepmerge backport provenance drift');
      continue;
    }
    if (!entry.resolved && entry.inBundle === true) {
      let parent = name;
      let covered = false;
      while (parent.includes('/node_modules/')) {
        parent = parent.slice(0, parent.lastIndexOf('/node_modules/'));
        const ancestor = lock.packages[parent];
        if (ancestor?.resolved && ancestor.integrity) {
          covered = true;
          break;
        }
      }
      if (!covered) throw new Error(`Bundled package has no covered parent provenance: ${name}`);
      continue;
    }
    let url;
    try {
      url = new URL(entry.resolved);
    } catch {
      throw new Error(`Missing registry provenance: ${name}`);
    }
    if (
      url.protocol !== 'https:' ||
      url.hostname !== 'registry.npmjs.org' ||
      url.username ||
      url.password ||
      url.search ||
      url.hash ||
      (url.port && url.port !== '443')
    )
      throw new Error(`Unreviewed registry provenance: ${name}`);
    if (
      typeof entry.integrity !== 'string' ||
      !entry.integrity.split(' ').every((part) => /^(sha512|sha256|sha1)-[A-Za-z0-9+/=]+$/.test(part))
    )
      throw new Error(`Missing or invalid integrity: ${name}`);
  }
  return true;
}
function resolveLockedDependency(lock, importer, name) {
  let current = importer;
  while (true) {
    const candidate = current ? `${current}/node_modules/${name}` : `node_modules/${name}`;
    if (Object.hasOwn(lock.packages, candidate)) return candidate;
    if (!current) return null;
    const boundary = current.lastIndexOf('/node_modules/');
    current = boundary < 0 ? '' : current.slice(0, boundary);
  }
}
function lockedReachable(lock, start, targets) {
  const pending = [start],
    visited = new Set();
  while (pending.length) {
    const node = pending.pop();
    if (visited.has(node)) continue;
    visited.add(node);
    if (targets.includes(node)) return true;
    const entry = lock.packages[node];
    for (const name of Object.keys({
      ...entry?.dependencies,
      ...entry?.optionalDependencies,
      ...entry?.peerDependencies,
    })) {
      const dependency = resolveLockedDependency(lock, node, name);
      if (dependency) pending.push(dependency);
    }
  }
  return false;
}
export function validateAuditReport(report, lock, remediation, proof, scope = 'root', deepmergeProof = null) {
  requireBracesProof(proof);
  verifyBracesLock(lock, proof, scope);
  validateLockProvenance(lock, proof, scope, deepmergeProof);
  if (deepmergeProof && scope === 'root') verifyDeepmergeLock(lock, deepmergeProof);
  if (
    remediation?.kind !== 'verified-local-security-backport' ||
    remediation.advisory !== KNOWN ||
    remediation.patchRevision !== 1
  )
    throw new Error('Verified local remediation metadata is required');
  if (
    !object(report) ||
    report.error ||
    report.auditReportVersion !== 2 ||
    !object(report.vulnerabilities) ||
    !object(report.metadata?.vulnerabilities)
  )
    throw new Error('Invalid or failed npm audit report schema');
  const entries = Object.entries(report.vulnerabilities);
  if (report.metadata.vulnerabilities.total !== entries.length) throw new Error('Inconsistent audit report total');
  const severities = ['info', 'low', 'moderate', 'high', 'critical'];
  const counts = Object.fromEntries(severities.map((severity) => [severity, 0]));
  for (const [, item] of entries) {
    if (!object(item) || !severities.includes(item.severity))
      throw new Error('Invalid advisory report severity schema');
    counts[item.severity]++;
  }
  for (const severity of severities)
    if (report.metadata.vulnerabilities[severity] !== counts[severity])
      throw new Error('Inconsistent advisory report severity totals');
  for (const [node, version] of Object.entries(remediation?.packages?.braces?.nodes ?? {})) {
    if (!lock.packages[node]) continue;
    if (lock.packages[node].version !== version) throw new Error(`Dependency version/path drift: ${node}`);
  }
  function leaves(start) {
    const pending = [start],
      visited = new Set(),
      result = [];
    while (pending.length) {
      const name = pending.pop();
      if (visited.has(name)) continue;
      visited.add(name);
      const item = report.vulnerabilities[name];
      if (
        !Object.hasOwn(report.vulnerabilities, name) ||
        !object(item) ||
        item.name !== name ||
        !Array.isArray(item.via) ||
        !item.via.length ||
        !Array.isArray(item.nodes) ||
        !item.nodes.length
      )
        throw new Error(`Missing or malformed advisory chain: ${name}`);
      for (const via of item.via) {
        viaSignature(via);
        if (typeof via === 'string') pending.push(via);
        else {
          if (via.name !== name) throw new Error(`Invalid advisory chain leaf: ${name}`);
          result.push(via);
        }
      }
    }
    if (!result.length) throw new Error(`No reachable leaf in cyclic or incomplete advisory chain: ${start}`);
    return result;
  }
  const allowed = [];
  for (const [name, item] of entries) {
    const chain = leaves(name);
    const bracesOnly = chain.every(
      (leaf) => leaf.name === 'braces' && leaf.url === KNOWN && leaf.severity === 'high' && leaf.range === '<=3.0.3',
    );
    const deepmergeOnly = chain.every(
      (leaf) =>
        leaf.name === 'deepmerge' &&
        leaf.url === DEEPMERGE &&
        leaf.severity === 'low' &&
        leaf.range === '>=4.2.0 <=4.3.1',
    );
    if (!bracesOnly && !(deepmergeOnly && deepmergeProof && scope === 'root'))
      throw new Error(`Unapproved advisory in dependency chain: ${name}`);
    if (deepmergeOnly) {
      requireDeepmergeProof(deepmergeProof);
      if (remediation.deepmergeBackport?.advisory !== DEEPMERGE || remediation.deepmergeBackport.patchRevision !== 1)
        throw new Error('Verified deepmerge remediation metadata is required');
    }
    const expected = bracesOnly
      ? remediation.packages?.[name]
      : remediation.deepmergeBackport.scopes.root.packages?.[name];
    if (!expected || item.severity !== expected.severity || !equal(item.nodes, Object.keys(expected.nodes)))
      throw new Error(`Unreviewed or changed dependency chain: ${name}`);
    for (const node of item.nodes)
      if (!lock.packages[node] || lock.packages[node].version !== expected.nodes[node])
        throw new Error(`Dependency version/path drift: ${node}`);
    const leafNodes = report.vulnerabilities[bracesOnly ? 'braces' : 'deepmerge']?.nodes ?? [];
    if (item.nodes.some((node) => !lockedReachable(lock, node, leafNodes)))
      throw new Error(`Unreviewed locked dependency chain to backport leaf: ${name}`);
    for (const via of item.via.filter((value) => typeof value === 'string')) {
      const targets = report.vulnerabilities[via].nodes;
      if (!item.nodes.some((node) => lockedReachable(lock, node, targets)))
        throw new Error(`Unreviewed locked dependency chain edge: ${name} -> ${via}`);
    }
    allowed.push(name);
  }
  return { allowed };
}
async function audit(cwd, npmCli) {
  return new Promise((resolveResult, reject) => {
    execFile(
      process.execPath,
      [npmCli, 'audit', '--package-lock-only', '--ignore-scripts', '--json', '--audit-level=low'],
      { cwd, timeout: 120_000, maxBuffer: 8 * 1024 * 1024, windowsHide: true },
      (error, stdout) => {
        if (error && error.code !== 1)
          return reject(new Error('npm audit did not complete; dependency gate is closed'));
        try {
          resolveResult(JSON.parse(stdout));
        } catch {
          reject(new Error('Malformed npm audit output; dependency gate is closed'));
        }
      },
    );
  });
}
async function main() {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
  const npmCli = process.env.npm_execpath;
  if (!npmCli) throw new Error('Invoke through npm run check:dependencies');
  const args = process.argv.slice(2);
  if (args.some((arg) => arg !== '--artifacts-only') || args.length > 1)
    throw new Error('Unsupported dependency gate arguments');
  const artifactsOnly = args.includes('--artifacts-only');
  const proof = await verifyBracesArtifact(root);
  const deepmergeProof = await verifyDeepmergeArtifact(root);
  const policy = JSON.parse(await readFile(join(root, 'tools/ci/dependency-remediations.json'), 'utf8'));
  const output = join(root, 'TestResults/dependency-audit');
  await mkdir(output, { recursive: true });
  let failed = false;
  for (const [name, cwd] of [
    ['root', root],
    ['openspec', join(root, 'tools/openspec')],
  ]) {
    try {
      const lock = JSON.parse(await readFile(join(cwd, 'package-lock.json'), 'utf8'));
      verifyBracesLock(lock, proof, name);
      validateLockProvenance(lock, proof, name, deepmergeProof);
      if (name === 'root') verifyDeepmergeLock(lock, deepmergeProof);
      const manifest = JSON.parse(await readFile(join(cwd, 'package.json'), 'utf8'));
      verifyBracesManifest(manifest, proof, name);
      if (name === 'root') verifyDeepmergeManifest(manifest, deepmergeProof);
      const installed = artifactsOnly ? null : await verifyInstalledBraces(proof, name, lock);
      const installedDeepmerge =
        name === 'root' && !artifactsOnly ? await verifyInstalledDeepmerge(deepmergeProof, lock) : null;
      const report = await audit(cwd, npmCli);
      await writeFile(join(output, `${name}.json`), `${JSON.stringify(report, null, 2)}\n`);
      const result = validateAuditReport(
        report,
        lock,
        { ...policy, packages: policy.scopes[name]?.packages },
        proof,
        name,
        deepmergeProof,
      );
      console.log(
        `${name}: ${artifactsOnly ? 'LOCK/ARTIFACT ONLY; installed runtime NOT verified' : `${installed.copies} braces and ${installedDeepmerge?.copies ?? 0} deepmerge installed copies verified`}; ` +
          `LOCAL SECURITY BACKPORTS: braces3.0.3 revision1${name === 'root' ? ', deepmerge4.3.1 revision1' : ''}. Raw audit reports ${result.allowed.length} exact proof-bound matching entries; no upstream fixed-release claim or time-based waiver.`,
      );
    } catch (error) {
      failed = true;
      console.error(`${name}: ${error.message}`);
    }
  }
  if (failed) process.exitCode = 1;
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href)
  main().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
