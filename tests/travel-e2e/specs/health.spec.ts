import { expect, test } from '@playwright/test';

test.describe('System status', () => {
  test('root redirects to the Flights search shell', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/flights$/);
    await expect(page.getByRole('link', { name: 'Travel Platform' })).toBeVisible();
    await expect(page.getByRole('heading', { name: /Сначала маршрут/ })).toBeVisible();
    await expect(page.getByText('Welcome web')).toHaveCount(0);
  });

  test('status page shows db ok without starter content', async ({ page }) => {
    await page.goto('/status');
    await expect(page.getByRole('link', { name: 'Travel Platform' })).toBeVisible();
    await expect(page.getByText(/db: ok/)).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Welcome web')).toHaveCount(0);
  });
});
