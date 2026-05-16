import { expect, test } from '@playwright/test';

test.describe('System status', () => {
  test('status page shows db ok', async ({ page }) => {
    await page.goto('/status');
    await expect(page.getByText(/db: ok/)).toBeVisible({ timeout: 10000 });
  });
});
