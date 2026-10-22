import { expect, it } from 'vitest';
import { sourceManifest } from './source-manifest.mjs';

it('binds M2 to its explicit fresh base and retains the new shared Rail detail fixture in source identity', () => {
  const manifest = sourceManifest('a8721effd9056459d217966083816455fbea599d');
  expect(manifest.requestedBase).toBe('a8721effd9056459d217966083816455fbea599d');
  expect(
    manifest.files.some(
      (file) =>
        file.path === 'shared/ts/api-client/tests/rail-details-fixture.ts' && /^[a-f0-9]{64}$/.test(file.sha256),
    ),
  ).toBe(true);
});
