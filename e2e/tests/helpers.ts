import { expect, type Page } from '@playwright/test';

export const PASSWORD = 'Demo!2345';
export const TUTOR = 'tutor@tutordesk.demo';
export const ADMIN = 'admin@tutordesk.demo';

export async function signIn(page: Page, email: string, password = PASSWORD) {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
}

/** A weekday 3-6 weeks ahead (yyyy-mm-dd) and a morning time that won't clash with demo lessons. */
export function freeSlot(): { date: string; time: string } {
  const d = new Date();
  d.setDate(d.getDate() + 21 + Math.floor(Math.random() * 21));
  while (d.getDay() === 0 || d.getDay() === 6) d.setDate(d.getDate() + 1);
  const hour = 7 + Math.floor(Math.random() * 6);
  const minute = [0, 15, 30, 45][Math.floor(Math.random() * 4)]!;
  const pad = (n: number) => String(n).padStart(2, '0');
  return { date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`, time: `${pad(hour)}:${pad(minute)}` };
}
