import { TZDate } from '@date-fns/tz';
import { addDays, format, startOfWeek } from 'date-fns';

/** Every date in the UI is shown in Adelaide time, wherever the browser is. */
export const ZONE = 'Australia/Adelaide';

export function inAdelaide(iso: string | Date): TZDate {
  return new TZDate(typeof iso === 'string' ? new Date(iso) : iso, ZONE);
}

export function nowInAdelaide(): TZDate {
  return TZDate.tz(ZONE);
}

/** Monday 00:00 (Adelaide) of the week containing `date`. */
export function weekStart(date: Date): TZDate {
  return startOfWeek(inAdelaide(date), { weekStartsOn: 1 }) as TZDate;
}

export function weekDays(monday: TZDate): TZDate[] {
  return Array.from({ length: 7 }, (_, i) => addDays(monday, i) as TZDate);
}

export const fmt = {
  time: (iso: string) => format(inAdelaide(iso), 'h:mmaaa'),
  day: (iso: string | Date) => format(inAdelaide(iso), 'EEE d MMM'),
  longDay: (iso: string | Date) => format(inAdelaide(iso), 'EEEE d MMMM'),
  dateTime: (iso: string) => format(inAdelaide(iso), "EEE d MMM, h:mmaaa"),
  isoDate: (d: Date) => format(inAdelaide(d), 'yyyy-MM-dd'),
};

/**
 * Builds an ISO-8601 timestamp with Adelaide's offset for that date
 * (e.g. "2026-11-10T16:00:00.000+10:30"), from form inputs "2026-11-10" and "16:00".
 */
export function adelaideIso(date: string, time: string): string {
  const [y, m, d] = date.split('-').map(Number);
  const [hh, mm] = time.split(':').map(Number);
  return new TZDate(y!, m! - 1, d!, hh!, mm!, ZONE).toISOString();
}

/** Minutes since local midnight (Adelaide) - used to position lessons on the calendar. */
export function minutesIntoDay(iso: string): number {
  const d = inAdelaide(iso);
  return d.getHours() * 60 + d.getMinutes();
}

export function sameAdelaideDay(a: Date | string, b: Date | string): boolean {
  return fmt.isoDate(new Date(a)) === fmt.isoDate(new Date(b));
}

export function hours(n: number): string {
  return `${Number.isInteger(n) ? n : n.toFixed(1)}h`;
}

export function percent(rate: number | null): string {
  return rate === null ? '-' : `${Math.round(rate * 100)}%`;
}
