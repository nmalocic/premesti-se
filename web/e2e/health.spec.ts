import { expect, test } from '@playwright/test';

test('SPA calls the real database-backed API and renders healthy', async ({
  page,
}) => {
  const responsePromise = page.waitForResponse((r) =>
    r.url().endsWith('/api/health'),
  );
  await page.goto('/');
  const response = await responsePromise;
  expect(response.status()).toBe(200);
  expect((await response.json()).checks.database.status).toBe('Healthy');
  await expect(page.getByRole('status')).toContainText(
    'API and VeloxDB are healthy',
  );
});

test('shows loading, then unavailable for a failed health response, and can retry', async ({
  page,
}) => {
  let release!: () => void;
  const wait = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route('**/api/health', async (route) => {
    await wait;
    await route.fulfill({
      status: 503,
      json: {
        status: 'Unhealthy',
        checks: { database: { status: 'Unhealthy' } },
      },
    });
  });
  await page.goto('/');
  await expect(page.getByRole('status')).toContainText('Checking');
  release();
  await expect(page.getByRole('status')).toContainText('unavailable');
  await page.unroute('**/api/health');
  await page.getByRole('button', { name: 'Check again' }).click();
  await expect(page.getByRole('status')).toContainText(
    'API and VeloxDB are healthy',
  );
});
