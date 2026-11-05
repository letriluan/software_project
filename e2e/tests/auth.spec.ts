import { expect, test } from '@playwright/test';
import { signIn, TUTOR } from './helpers';

test('wrong password shows a clear message', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email').fill(TUTOR);
  await page.getByLabel('Password').fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByText('Email or password is incorrect.')).toBeVisible();
});

test('session survives a reload via the refresh cookie, and sign out ends it', async ({ page, context }) => {
  await signIn(page, TUTOR);
  const cookie = (await context.cookies()).find((c) => c.name === 'td_refresh');
  expect(cookie?.httpOnly).toBe(true);

  await page.reload();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  await page.goto('/students');
  await expect(page).toHaveURL(/\/login$/);
});

test('tutors cannot open admin pages', async ({ page }) => {
  await signIn(page, TUTOR);
  await page.goto('/admin/tutors');
  await expect(page).toHaveURL(/\/$/);
  const res = await page.request.get('/api/admin/tutors');
  expect(res.status()).toBe(401); // no bearer token on a raw request
});
