import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

// Failure DOM prompts may contain private login fields. The dedicated reporter also
// deliberately discards raw Playwright errors, attachments and child output.
process.env['PLAYWRIGHT_NO_COPY_PROMPT'] = '1';

export default defineConfig({
  testDir: './trips-full-stack',
  testMatch: 'trips-full-stack.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 480_000,
  expect: { timeout: 20_000 },
  forbidOnly: true,
  captureGitInfo: { commit: false, diff: false },
  outputDir: resolve(__dirname, '../../TestResults/trips-full-stack-runner'),
  preserveOutput: 'never',
  reporter: [['./trips-full-stack/safe-reporter.ts']],
  use: {
    baseURL: 'http://127.0.0.1:4206',
    actionTimeout: 30_000,
    navigationTimeout: 45_000,
    trace: 'off',
    video: 'off',
    screenshot: 'off',
    serviceWorkers: 'block',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
