import { format } from 'date-fns';
import type { TZDate } from '@date-fns/tz';
import type { Lesson } from '../api/types';
import { fmt, minutesIntoDay, sameAdelaideDay } from '../lib/time';
import { subjectColor } from './ui';

export const DAY_START_HOUR = 6;
export const DAY_END_HOUR = 22;
const HOUR_PX = 56;

/**
 * The ruled-page week view. Lessons are positioned by their Adelaide start time; clicking an
 * empty slot (rounded to the half hour) starts a booking there.
 */
export function WeekCalendar({ days, lessons, now, onSelect, onSlot }: {
  days: TZDate[];
  lessons: Lesson[];
  now: Date;
  onSelect: (lesson: Lesson) => void;
  onSlot: (date: string, time: string) => void;
}) {
  const top = (minutes: number) => ((minutes - DAY_START_HOUR * 60) / 60) * HOUR_PX;
  const nowMinutes = minutesIntoDay(now.toISOString());

  const handleSlot = (day: TZDate, e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target !== e.currentTarget) return; // clicked a lesson
    const y = e.clientY - e.currentTarget.getBoundingClientRect().top;
    const minutes = DAY_START_HOUR * 60 + Math.floor((y / HOUR_PX) * 2) * 30;
    const hh = String(Math.floor(minutes / 60)).padStart(2, '0');
    const mm = String(minutes % 60).padStart(2, '0');
    onSlot(fmt.isoDate(day), `${hh}:${mm}`);
  };

  return (
    <div className="week" role="grid" aria-label="Week timetable">
      <div className="week-head" role="row">
        <div aria-hidden="true" />
        {days.map((d) => (
          <div key={d.toISOString()} role="columnheader" className={sameAdelaideDay(d, now) ? 'is-today' : undefined}>
            <span className="dow">{format(d, 'EEE')}</span>
            <span className="dom">{format(d, 'd')}</span>
          </div>
        ))}
      </div>

      <div className="hours" aria-hidden="true">
        {Array.from({ length: DAY_END_HOUR - DAY_START_HOUR }, (_, i) => DAY_START_HOUR + i).slice(1).map((h) => (
          <span key={h} style={{ top: (h - DAY_START_HOUR) * HOUR_PX }}>
            {h === 12 ? '12pm' : h > 12 ? `${h - 12}pm` : `${h}am`}
          </span>
        ))}
      </div>

      {days.map((day) => {
        const dayLessons = lessons.filter((l) => sameAdelaideDay(l.startsAtUtc, day));
        const isToday = sameAdelaideDay(day, now);
        const isPast = fmt.isoDate(day) < fmt.isoDate(now);
        const weekend = day.getDay() === 0 || day.getDay() === 6;
        return (
          <div
            key={day.toISOString()}
            role="gridcell"
            aria-label={fmt.longDay(day)}
            className={`day${weekend ? ' is-weekend' : ''}${isPast ? ' is-past' : ''}`}
            onClick={(e) => !isPast && handleSlot(day, e)}
          >
            {isToday && nowMinutes >= DAY_START_HOUR * 60 && nowMinutes <= DAY_END_HOUR * 60 && (
              <div className="now-line" style={{ top: top(nowMinutes) }} aria-label="Now" />
            )}
            {dayLessons.map((l) => {
              const start = minutesIntoDay(l.startsAtUtc);
              const needsMarking = l.status === 'Scheduled' && new Date(l.startsAtUtc) <= now;
              return (
                <button
                  type="button"
                  key={l.id}
                  className={`lesson-block ${l.status}${needsMarking ? ' needs-marking' : ''}`}
                  style={{
                    top: top(start) + 1,
                    height: Math.max((l.durationMinutes / 60) * HOUR_PX - 3, 22),
                    ['--subj' as string]: subjectColor(l.subjectId),
                  }}
                  onClick={() => onSelect(l)}
                  aria-label={`${l.studentName}, ${l.subjectName}, ${fmt.time(l.startsAtUtc)}, ${needsMarking ? 'needs attendance' : l.status}`}
                >
                  <strong>{l.studentName}</strong>
                  <span className="meta">{fmt.time(l.startsAtUtc)}, {l.subjectName}</span>
                </button>
              );
            })}
          </div>
        );
      })}
    </div>
  );
}
