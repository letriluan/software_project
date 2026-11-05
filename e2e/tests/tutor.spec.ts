import { expect, test } from '@playwright/test';
import { freeSlot, signIn, TUTOR } from './helpers';

test('add a student, book a weekly series, then cancel the rest of it', async ({ page }) => {
  await signIn(page, TUTOR);
  const first = `E2E${Date.now().toString().slice(-6)}`;

  // Add a student
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Students' }).click();
  await page.getByRole('button', { name: 'Add student' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('First name').fill(first);
  await dialog.getByLabel('Last name').fill('Tester');
  await dialog.getByLabel('Year level').selectOption('10');
  await dialog.getByLabel('Parent email').fill('parent@example.com');
  await dialog.getByRole('button', { name: 'Add student' }).click();
  await expect(page.getByRole('heading', { level: 1, name: `${first} Tester` })).toBeVisible();

  // Book four weekly lessons from the student page
  const slot = freeSlot();
  await page.getByRole('button', { name: 'Book a lesson' }).click();
  const booking = page.getByRole('dialog');
  await booking.getByLabel('Subject').selectOption({ label: 'Physics' });
  await booking.getByLabel('Date').fill(slot.date);
  await booking.getByLabel('Start time').fill(slot.time);
  await booking.getByLabel('Repeat').selectOption('4');
  await booking.getByRole('button', { name: 'Book 4 lessons' }).click();
  await expect(page.getByRole('status').filter({ hasText: '4 weekly lessons booked' })).toBeVisible();
  await expect(page.getByText('booked ahead').locator('..')).toContainText('4');

  // Booking the same slot again is rejected with the clash listed
  await page.getByRole('button', { name: 'Book a lesson' }).click();
  const again = page.getByRole('dialog');
  await again.getByLabel('Date').fill(slot.date);
  await again.getByLabel('Start time').fill(slot.time);
  await again.getByRole('button', { name: 'Book lesson' }).click();
  await expect(again.getByRole('alert')).toContainText('overlap');
  await again.getByRole('button', { name: 'Back' }).click();

  // Open the lesson in the week view and cancel it with the rest of the series
  await page.goto(`/week?week=${slot.date}`);
  await page.getByRole('button', { name: new RegExp(`${first} Tester, Physics`) }).click();
  const lesson = page.getByRole('dialog');
  await lesson.getByRole('button', { name: 'Cancel lesson' }).click();
  await lesson.getByLabel('Reason').fill('Moving to Saturdays');
  await lesson.getByLabel(/later lessons in this weekly series/).check();
  await lesson.getByRole('button', { name: 'Cancel lesson' }).click();
  await expect(page.getByRole('status').filter({ hasText: '4 lessons cancelled' })).toBeVisible();
});

test('record attendance with a progress note from the dashboard', async ({ page }) => {
  await signIn(page, TUTOR);
  const toMark = page.getByRole('region', { name: 'Still to mark' });
  await expect(toMark).toBeVisible();
  const firstRow = toMark.getByRole('listitem').first();
  const student = (await firstRow.locator('.who strong').textContent())!;
  await firstRow.getByRole('button', { name: 'Open' }).click();

  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: 'Record attendance' }).click();
  await dialog.getByLabel('Progress note').fill('E2E: simultaneous equations, ready for the test.');
  await dialog.getByRole('button', { name: '4 out of 5' }).click();
  await dialog.getByRole('button', { name: 'Save attendance' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Lesson marked as completed' })).toBeVisible();

  // The note appears on the student's progress timeline
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Students' }).click();
  await page.getByRole('link', { name: student }).click();
  await expect(page.getByText('E2E: simultaneous equations, ready for the test.')).toBeVisible();
});
