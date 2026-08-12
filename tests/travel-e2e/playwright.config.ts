import { defineConfig, devices } from '@playwright/test';

const { CI: isCi } = process.env;

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!isCi,
  retries: isCi ? 2 : 0,
  reporter: isCi ? 'github' : 'list',
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
