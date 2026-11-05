import { useMemo, useState } from 'react';
import { addDays, format } from 'date-fns';
import { useSearchParams } from 'react-router-dom';
import { useLessons } from '../api/hooks';
import type { Lesson } from '../api/types';
import { BookLessonForm, type BookingForm } from '../components/BookLessonForm';
import { LessonPanel } from '../components/LessonPanel';
import { Dialog, ErrorAlert, useToast } from '../components/ui';
import { WeekCalendar } from '../components/WeekCalendar';
import { fmt, hours, inAdelaide, weekDays, weekStart } from '../lib/time';

export function WeekPage() {
  const [params, setParams] = useSearchParams();
  const now = new Date();
  const monday = useMemo(() => weekStart(params.get('week') ? inAdelaide(`${params.get('week')}T12:00:00+10:00`) : now),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [params]);
  const days = weekDays(monday);
  const from = monday.toISOString();
  const to = addDays(monday, 7).toISOString();
  const lessons = useLessons(from, to);
  const toast = useToast();

  const [selected, setSelected] = useState<Lesson | null>(null);
  const [booking, setBooking] = useState<Partial<BookingForm> | null>(null);

  const goTo = (offsetWeeks: number) => {
    const target = offsetWeeks === 0 ? weekStart(now) : addDays(monday, 7 * offsetWeeks);
    setParams(offsetWeeks === 0 ? {} : { week: fmt.isoDate(target) });
  };

  const live = (lessons.data ?? []).filter((l) => l.status !== 'Cancelled');
  const planned = live.reduce((sum, l) => sum + l.durationMinutes, 0) / 60;
  const toMark = live.filter((l) => l.status === 'Scheduled' && new Date(l.startsAtUtc) <= now).length;
  const label = `${format(monday, 'd MMM')} to ${format(addDays(monday, 6), 'd MMM yyyy')}`;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Week</h1>
          <p>
            {live.length} lessons, {hours(planned)} planned
            {toMark > 0 && <>, <span className="marker">{toMark} to mark</span></>}
          </p>
        </div>
        <button type="button" className="btn" onClick={() => setBooking({ date: fmt.isoDate(now) })}>Book a lesson</button>
      </div>

      <div className="week-toolbar">
        <div className="actions">
          <button type="button" className="btn secondary small" onClick={() => goTo(-1)} aria-label="Previous week">‹</button>
          <button type="button" className="btn secondary small" onClick={() => goTo(0)}>This week</button>
          <button type="button" className="btn secondary small" onClick={() => goTo(1)} aria-label="Next week">›</button>
        </div>
        <h2 aria-live="polite">{label}</h2>
      </div>

      {lessons.isError ? <ErrorAlert error={lessons.error} /> : (
        <WeekCalendar
          days={days}
          lessons={lessons.data ?? []}
          now={now}
          onSelect={setSelected}
          onSlot={(date, time) => setBooking({ date, time })}
        />
      )}
      <div className="legend" aria-hidden="true">
        <span><i style={{ background: 'var(--highlight)' }} />Needs attendance</span>
        <span><i style={{ background: '#f3f8f4', border: '1px solid var(--rule-strong)' }} />Completed</span>
        <span><i style={{ borderTop: '2px solid var(--margin)', height: 0 }} />Now</span>
        <span>Click an empty slot to book it</span>
      </div>

      <Dialog open={!!selected} onClose={() => setSelected(null)}
        title={selected ? `${selected.subjectName} with ${selected.studentName.split(' ')[0]}` : ''}
        subtitle={selected ? `${fmt.day(selected.startsAtUtc)}, ${fmt.time(selected.startsAtUtc)}` : undefined}>
        {selected && (
          <LessonPanel lesson={selected} onDone={(m) => { setSelected(null); if (m) toast.show(m); }} />
        )}
      </Dialog>

      <Dialog open={!!booking} onClose={() => setBooking(null)} title="Book a lesson">
        {booking && (
          <BookLessonForm defaults={booking} onCancel={() => setBooking(null)}
            onDone={(m) => { setBooking(null); toast.show(m); }} />
        )}
      </Dialog>
      {toast.node}
    </>
  );
}
