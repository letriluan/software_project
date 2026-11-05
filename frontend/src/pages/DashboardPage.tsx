import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useDashboard } from '../api/hooks';
import type { Lesson } from '../api/types';
import { LessonPanel } from '../components/LessonPanel';
import { Dialog, ErrorAlert, Loading, Status, useToast } from '../components/ui';
import { fmt, hours, nowInAdelaide, percent } from '../lib/time';

function greeting() {
  const h = nowInAdelaide().getHours();
  return h < 12 ? 'Good morning' : h < 17 ? 'Good afternoon' : 'Good evening';
}

function LessonRow({ lesson, onOpen, showDay }: { lesson: Lesson; onOpen: () => void; showDay?: boolean }) {
  return (
    <li>
      <time dateTime={lesson.startsAtUtc}>{showDay ? fmt.day(lesson.startsAtUtc) : fmt.time(lesson.startsAtUtc)}</time>
      <div className="who">
        <strong>{lesson.studentName}</strong>
        <span>{lesson.subjectName}, {lesson.durationMinutes} min{showDay ? ` at ${fmt.time(lesson.startsAtUtc)}` : ''}</span>
      </div>
      <div className="actions">
        <Status status={lesson.status} />
        <button type="button" className="btn secondary small" onClick={onOpen}>Open</button>
      </div>
    </li>
  );
}

export function DashboardPage() {
  const { user } = useAuth();
  const { data, isLoading, error } = useDashboard();
  const [open, setOpen] = useState<Lesson | null>(null);
  const toast = useToast();

  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorAlert error={error} />;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{greeting()}, {user?.fullName.split(' ')[0]}</h1>
          <p>{fmt.longDay(new Date())}</p>
        </div>
        <Link to="/week" className="btn">Open the week</Link>
      </div>

      <div className="figures">
        <div className="figure"><b>{data.todaysLessons.length}</b><span>lessons today</span></div>
        <div className="figure"><b>{data.upcomingThisWeek}</b><span>coming up this week</span></div>
        <div className="figure"><b>{hours(data.hoursThisMonth)}</b><span>taught this month</span></div>
        <div className="figure"><b>{percent(data.attendanceRateThisMonth)}</b><span>attendance this month</span></div>
        <div className="figure"><b>{data.activeStudents}</b><span>active students</span></div>
      </div>

      <div className="grid-2">
        <section className="sheet" aria-labelledby="today-h">
          <h2 id="today-h">Today</h2>
          {data.todaysLessons.length === 0 ? (
            <p className="empty"><strong>No lessons today.</strong> Book one from the week view.</p>
          ) : (
            <ul className="lesson-list">
              {data.todaysLessons.map((l) => <LessonRow key={l.id} lesson={l} onOpen={() => setOpen(l)} />)}
            </ul>
          )}
        </section>

        <section className="sheet" aria-labelledby="mark-h">
          <h2 id="mark-h">{data.needsAttendance.length > 0 ? <span className="marker">Still to mark</span> : 'Still to mark'}</h2>
          {data.needsAttendance.length === 0 ? (
            <p className="empty"><strong>All caught up.</strong> Every past lesson has attendance recorded.</p>
          ) : (
            <ul className="lesson-list">
              {data.needsAttendance.map((l) => <LessonRow key={l.id} lesson={l} showDay onOpen={() => setOpen(l)} />)}
            </ul>
          )}
        </section>
      </div>

      <Dialog open={!!open} onClose={() => setOpen(null)}
        title={open ? `${open.subjectName} with ${open.studentName.split(' ')[0]}` : ''}
        subtitle={open ? `${fmt.day(open.startsAtUtc)}, ${fmt.time(open.startsAtUtc)}` : undefined}>
        {open && <LessonPanel lesson={open} onDone={(m) => { setOpen(null); if (m) toast.show(m); }} />}
      </Dialog>
      {toast.node}
    </>
  );
}
