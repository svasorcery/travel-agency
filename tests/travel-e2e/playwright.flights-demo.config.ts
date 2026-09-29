import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

const workspaceRoot = resolve(__dirname, '../..');

export default defineConfig({
  testDir: './demo',
  fullyParallel: false,
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? 'github' : 'list',
  use: {
    baseURL: 'http://127.0.0.1:4201',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'node tools/demo/flights-search-api.mjs',
      cwd: workspaceRoot,
      url: 'http://127.0.0.1:5100/',
      timeout: 30_000,
      reuseExistingServer: false,
    },
    {
      command: 'node node_modules/nx/dist/bin/nx.js serve web --configuration=flights-demo',
      cwd: workspaceRoot,
      url: 'http://127.0.0.1:4201/flights',
      timeout: 120_000,
      reuseExistingServer: false,
    },
  ],
});
