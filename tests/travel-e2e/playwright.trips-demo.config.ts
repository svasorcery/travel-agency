import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';
export default defineConfig({
  testDir: './trips-demo',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: process.env['CI'] ? 'github' : 'list',
  use: { baseURL: 'http://127.0.0.1:4203', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'node node_modules/nx/dist/bin/nx.js serve web --configuration=trips-demo',
    cwd: resolve(__dirname, '../..'),
    url: 'http://127.0.0.1:4203/trips',
    timeout: 120000,
    reuseExistingServer: false,
  },
});
