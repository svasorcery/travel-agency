import { defineConfig } from 'vitest/config';
export default defineConfig({
  resolve: { tsconfigPaths: true },
  test: {
    globals: true,
    environment: 'jsdom',
    watch: false,
    include: ['apps/web/src/app/auth/travel-frame-policy.spec.ts', 'apps/web/src/app/rail/rail-state.spec.ts'],
  },
});
