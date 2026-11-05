import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { addDays, subDays } from 'date-fns';
import { useAuth } from '../auth/AuthContext';
import { useArchiveStudent, useLessons, useRestoreStudent, useStudent, useStudentProgress, useUpdateStudent } from '../api/hooks';
import type { Lesson } from '../api/types';
import { BookLessonForm } from '../components/BookLessonForm';
import { LessonPanel } from '../components/LessonPanel';
import { StudentForm } from '../components/StudentForm';
import { Dialog, Dots, ErrorAlert, Loading, Status, subjectColor, useToast } from '../components/ui';
import { fmt, hours, percent } from '../lib/time';

export function StudentDetailPage() {
  const { id = '' } = useParams();
  const { user } = useAuth();
  const isTutor = user?.role === 'Tutor';
  const student = useStudent(id);
  const progress = useStudentProgress(id);
  const [range] = useState(() => ({ from: subDays(new Date(), 30).toISOString(), to: addDays(new Date(), 30).toISOString() }));
  const lessons = useLessons(range.from, range.to, id);
  const update = useUpdateStudent(id);
  const archive = useArchiveStudent();
  const restore = useRestoreStudent();
  const toast = useToast();
  const [editing, setEditing] = useState(false);
  const [booking, setBooking] = useState(false);
  const [confirmArchive, setConfirmArchive] = useState(false);
  const [open, setOpen] = useState<Lesson | null>(null);

  if (student.isLoading) return <Loading />;
  if (student.isError || !student.data) return <ErrorAlert error={student.error} />;
  const s = student.data;
  const name = `${s.firstName} ${s.lastName}`;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{name}</h1>
          <p>
            Year {s.yearLevel}{s.school ? `, ${s.school}` : ''}
            {!isTutor && `. Tutor: ${s.tutorName}`}
            {s.isArchived && '. Archived'}
          </p>
        </div>
        {isTutor && (
          <div className="actions">
            {s.isArchived ? (
              <button type="button" className="btn" disabled={restore.isPending}
                onClick={() => restore.mutate(s.id, { onSuccess: () => toast.show(`${s.firstName} restored`) })}>Restore student</button>
            ) : (
              <>
                <button type="button" className="btn secondary" onClick={() => setEditing(true)}>Edit details</button>
                <button type="button" className="btn" onClick={() => setBooking(true)}>Book a lesson</button>
              </>
            )}
          </div>
        )}
      </div>

      <div className="figures">
        <div className="figure"><b>{s.stats.completedLessons}</b><span>lessons completed</span></div>
        <div className="figure"><b>{hours(s.stats.hoursTaught)}</b><span>taught</span></div>
        <div className="figure"><b>{percent(s.stats.attendanceRate)}</b><span>attendance</span></div>
        <div className="figure"><b>{s.stats.upcomingLessons}</b><span>booked ahead</span></div>
      </div>

      <div className="grid-2">
        <div>
          <section className="sheet" aria-labelledby="progress-h">
            <h2 id="progress-h">Progress</h2>
            {progress.data && progress.data.subjects.length > 0 ? (
              <>
                <div className="subject-progress">
                  {progress.data.subjects.map((p) => (
                    <div key={p.subjectId} style={{ ['--subj' as string]: subjectColor(p.subjectId) }}>
                      <strong>{p.subjectName}</strong>
                      <div>Latest <Dots value={p.latestRating} /></div>
                      <div className={`trend ${p.trend.replace(/ /g, '-')}`}>{p.trend === 'not enough data' ? 'Too early to tell' : `Trend: ${p.trend}`}</div>
                      <span className="tag">{p.notes} notes, average {p.averageRating ?? '-'}</span>
                    </div>
                  ))}
                </div>
                <h3 style={{ margin: '24px 0 8px', fontSize: 'var(--step-0)' }}>Lesson notes</h3>
                <ul className="timeline">
                  {progress.data.notes.slice(0, 12).map((n) => (
                    <li key={n.id} style={{ ['--subj' as string]: subjectColor(n.subjectId) }}>
                      <div className="when">{fmt.day(n.lessonStartsAtUtc ?? n.createdAtUtc)}, {n.subjectName} <Dots value={n.understandingRating} /></div>
                      <p>{n.note}</p>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <p className="empty"><strong>No progress notes yet.</strong> Notes you add when recording attendance appear here, grouped by subject.</p>
            )}
          </section>

          <section className="sheet" aria-labelledby="lessons-h">
            <h2 id="lessons-h">Lessons, last 30 days and next 30</h2>
            {lessons.data && lessons.data.length > 0 ? (
              <ul className="lesson-list">
                {lessons.data.map((l) => (
                  <li key={l.id}>
                    <time dateTime={l.startsAtUtc}>{fmt.day(l.startsAtUtc)}</time>
                    <div className="who"><strong>{l.subjectName}</strong><span>{fmt.time(l.startsAtUtc)}, {l.durationMinutes} min</span></div>
                    <div className="actions">
                      <Status status={l.status} />
                      {isTutor && <button type="button" className="btn secondary small" onClick={() => setOpen(l)}>Open</button>}
                    </div>
                  </li>
                ))}
              </ul>
            ) : <p className="empty"><strong>No lessons in this period.</strong></p>}
          </section>
        </div>

        <aside className="sheet" aria-labelledby="contact-h">
          <h2 id="contact-h">Contact</h2>
          <dl className="facts">
            <dt>Parent</dt><dd>{s.parentName ?? '-'}</dd>
            <dt>Phone</dt><dd>{s.parentPhone ? <a href={`tel:${s.parentPhone}`}>{s.parentPhone}</a> : '-'}</dd>
            <dt>Email</dt><dd>{s.parentEmail ? <a href={`mailto:${s.parentEmail}`}>{s.parentEmail}</a> : '-'}</dd>
            <dt>Since</dt><dd>{fmt.day(s.createdAtUtc)}</dd>
          </dl>
          {s.notes && <><h3 style={{ margin: '20px 0 6px', fontSize: 'var(--step-0)' }}>Notes</h3><p style={{ whiteSpace: 'pre-wrap' }}>{s.notes}</p></>}
          {isTutor && !s.isArchived && (
            <div style={{ marginTop: 24 }}>
              <button type="button" className="btn danger small" onClick={() => setConfirmArchive(true)}>Archive student</button>
            </div>
          )}
        </aside>
      </div>

      <Dialog open={editing} onClose={() => setEditing(false)} title={`Edit ${s.firstName}`}>
        {editing && (
          <StudentForm initial={s} submitLabel="Save changes" onCancel={() => setEditing(false)}
            onSubmit={async (input) => {
              await update.mutateAsync({ ...input, version: s.version });
              setEditing(false);
              toast.show('Changes saved');
            }} />
        )}
      </Dialog>

      <Dialog open={booking} onClose={() => setBooking(false)} title={`Book a lesson with ${s.firstName}`}>
        {booking && (
          <BookLessonForm defaults={{ studentId: s.id, date: fmt.isoDate(addDays(new Date(), 1)) }}
            onCancel={() => setBooking(false)} onDone={(m) => { setBooking(false); toast.show(m); }} />
        )}
      </Dialog>

      <Dialog open={confirmArchive} onClose={() => setConfirmArchive(false)} title={`Archive ${s.firstName}?`}>
        <div className="stack">
          <p>{name} will be hidden from your student list and any future lessons will be cancelled. Past lessons and notes are kept, and you can restore them later.</p>
          <ErrorAlert error={archive.error} />
          <div className="dialog-foot">
            <button type="button" className="btn secondary" onClick={() => setConfirmArchive(false)}>Keep student</button>
            <button type="button" className="btn danger" disabled={archive.isPending}
              onClick={() => archive.mutate(s.id, {
                onSuccess: (r) => {
                  setConfirmArchive(false);
                  toast.show(r.cancelledFutureLessons > 0
                    ? `${s.firstName} archived, ${r.cancelledFutureLessons} future lessons cancelled`
                    : `${s.firstName} archived`);
                },
              })}>Archive student</button>
          </div>
        </div>
      </Dialog>

      <Dialog open={!!open} onClose={() => setOpen(null)} title={open ? `${open.subjectName} with ${s.firstName}` : ''}
        subtitle={open ? `${fmt.day(open.startsAtUtc)}, ${fmt.time(open.startsAtUtc)}` : undefined}>
        {open && <LessonPanel lesson={open} onDone={(m) => { setOpen(null); if (m) toast.show(m); }} />}
      </Dialog>
      {toast.node}
    </>
  );
}
