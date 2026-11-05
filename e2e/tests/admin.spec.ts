import { expect, test } from '@playwright/test';
import { ADMIN, signIn } from './helpers';

test('admin sees the monthly report and downloads the CSV', async ({ page }) => {
  await signIn(page, ADMIN);
  await expect(page).toHaveURL(/\/admin\/report$/);
  const byTutor = page.getByRole('region', { name: 'By tutor' });
  await expect(byTutor.getByRole('row', { name: /Luan Le/ })).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download CSV' }).click();
  expect((await download).suggestedFilename()).toMatch(/^tutordesk-\d{4}-\d{2}\.csv$/);
});

test('admin adds a tutor, who can sign in until deactivated', async ({ page, browser }) => {
  await signIn(page, ADMIN);
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Tutors' }).click();
  const email = `e2e-${Date.now()}@tutordesk.demo`;

  await page.getByRole('button', { name: 'Add tutor' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Full name').fill('E2E Tutor');
  await dialog.getByLabel('Email').fill(email);
  await dialog.getByLabel('Temporary password').fill('Welcome!2026');
  await dialog.getByRole('button', { name: 'Add tutor' }).click();
  const row = page.getByRole('row', { name: new RegExp(email) });
  await expect(row).toBeVisible();

  const tutorCtx = await browser.newContext();
  const tutorPage = await tutorCtx.newPage();
  await signIn(tutorPage, email, 'Welcome!2026');

  await row.getByRole('button', { name: 'Deactivate' }).click();
  await expect(row.getByRole('button', { name: 'Reactivate' })).toBeVisible();

  // The tutor's open session stops working straight away.
  await tutorPage.reload();
  await expect(tutorPage.getByRole('button', { name: 'Sign in' })).toBeVisible();
  await tutorCtx.close();
});
