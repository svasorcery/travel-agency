import { defineConfig, devices } from '@playwright/test';

// Synthetic local HTTP fixtures only; real Host/identity/restart remain in the full-stack suite.
export default defineConfig({
  testDir: './trips-full-stack',
  testMatch: 'response-capture.browser.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 30_000,
  reporter: [['list']],
  outputDir: '../../TestResults/trips-response-runner',
  use: { trace: 'off', video: 'off', screenshot: 'off', serviceWorkers: 'block' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
