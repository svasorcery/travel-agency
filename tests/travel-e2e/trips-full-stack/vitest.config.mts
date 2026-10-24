import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    watch: false,
    include: [
      'apps/web/src/app/auth/travel-web-auth-config.spec.ts',
      'tests/travel-e2e/trips-full-stack/safe-reporter.spec.ts',
    ],
  },
});
