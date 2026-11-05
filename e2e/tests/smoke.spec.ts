import { expect, test } from '@playwright/test';
import { signIn, TUTOR } from './helpers';

test('health endpoint and login page are up', async ({ page, request }) => {
  const health = await request.get('/health/ready');
  expect(health.ok()).toBeTruthy();
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
});

test('tutor can sign in and see today', async ({ page }) => {
  await signIn(page, TUTOR);
  await expect(page.getByRole('heading', { name: /Good (morning|afternoon|evening), Luan/ })).toBeVisible();
});
