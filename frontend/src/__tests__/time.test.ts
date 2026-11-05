import { adelaideIso, fmt, minutesIntoDay, weekStart } from '../lib/time';

describe('Adelaide time helpers', () => {
  it('uses +10:30 in daylight saving and +9:30 outside it', () => {
    expect(adelaideIso('2026-11-10', '16:00')).toBe('2026-11-10T16:00:00.000+10:30');
    expect(adelaideIso('2027-04-05', '16:00')).toBe('2027-04-05T16:00:00.000+09:30');
  });

  it('positions a UTC instant at its Adelaide wall-clock minute', () => {
    expect(minutesIntoDay('2026-11-10T05:30:00Z')).toBe(16 * 60); // 4:00pm ACDT
  });

  it('starts the week on Monday in Adelaide even when it is still Sunday in UTC', () => {
    // 20:00 UTC Sunday 1 Nov = 6:30am Monday 2 Nov in Adelaide
    expect(fmt.isoDate(weekStart(new Date('2026-11-01T20:00:00Z')))).toBe('2026-11-02');
  });

  it('formats times compactly', () => {
    expect(fmt.time('2026-11-10T05:30:00Z')).toBe('4:00pm');
    expect(fmt.day('2026-11-10T05:30:00Z')).toBe('Tue 10 Nov');
  });
});
