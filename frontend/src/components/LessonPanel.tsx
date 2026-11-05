import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useCancelLesson, useMarkAttendance } from '../api/hooks';
import type { AttendanceOutcome, Lesson } from '../api/types';
import { fmt } from '../lib/time';
import { BookLessonForm } from './BookLessonForm';
import { Dots, ErrorAlert, Field, Status } from './ui';

type Mode = 'view' | 'attendance' | 'cancel' | 'reschedule';

/** Everything a tutor can do with one lesson, inside the lesson dialog. */
export function LessonPanel({ lesson, onDone, now = new Date() }: {
  lesson: Lesson; onDone: (message?: string) => void; now?: Date;
}) {
  const [mode, setMode] = useState<Mode>('view');
  const started = new Date(lesson.startsAtUtc) <= now;
  const scheduled = lesson.status === 'Scheduled';

  if (mode === 'attendance') return <AttendanceForm lesson={lesson} onDone={onDone} onBack={() => setMode('view')} />;
  if (mode === 'cancel') return <CancelForm lesson={lesson} onDone={onDone} onBack={() => setMode('view')} />;
  if (mode === 'reschedule')
    return <BookLessonForm lesson={lesson} onDone={(m) => onDone(m)} onCancel={() => setMode('view')} />;

  return (
    <div className="stack">
      <dl className="facts">
        <dt>Student</dt><dd><Link to={`/students/${lesson.studentId}`}>{lesson.studentName}</Link></dd>
        <dt>When</dt><dd>{fmt.longDay(lesson.startsAtUtc)}, {fmt.time(lesson.startsAtUtc)} to {fmt.time(lesson.endsAtUtc)}</dd>
        <dt>Where</dt><dd>{lesson.mode === 'Online' ? 'Online' : 'In person'}{lesson.location ? `, ${lesson.location}` : ''}</dd>
        <dt>Status</dt><dd><Status status={lesson.status} />{lesson.cancellationReason ? `: ${lesson.cancellationReason}` : ''}</dd>
        {lesson.seriesId && <><dt>Series</dt><dd>Part of a weekly series</dd></>}
        {lesson.progress && (
          <>
            <dt>Notes</dt><dd>{lesson.progress.note}</dd>
            <dt>Understanding</dt><dd><Dots value={lesson.progress.understandingRating} /></dd>
          </>
        )}
      </dl>

      {scheduled && started && (
        <div className="alert info"><span className="marker">This lesson has started.</span> Record how it went.</div>
      )}

      {scheduled && (
        <div className="dialog-foot">
          <button type="button" className="btn danger" onClick={() => setMode('cancel')}>Cancel lesson</button>
          {!started && <button type="button" className="btn secondary" onClick={() => setMode('reschedule')}>Reschedule</button>}
          {started && <button type="button" className="btn" onClick={() => setMode('attendance')}>Record attendance</button>}
        </div>
      )}
    </div>
  );
}

function AttendanceForm({ lesson, onDone, onBack }: { lesson: Lesson; onDone: (m: string) => void; onBack: () => void }) {
  const mark = useMarkAttendance();
  const [outcome, setOutcome] = useState<AttendanceOutcome>('Completed');
  const [note, setNote] = useState('');
  const [rating, setRating] = useState<number | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    await mark.mutateAsync({
      id: lesson.id, outcome, version: lesson.version,
      progressNote: outcome === 'Completed' && note.trim() ? note.trim() : undefined,
      understandingRating: outcome === 'Completed' ? rating : null,
    });
    onDone(outcome === 'Completed' ? 'Lesson marked as completed' : 'Marked as a no-show');
  };

  return (
    <form className="stack" onSubmit={(e) => void submit(e).catch(() => undefined)}>
      <ErrorAlert error={mark.error} />
      <div className="segmented" role="group" aria-label="Did the lesson happen?">
        <button type="button" aria-pressed={outcome === 'Completed'} onClick={() => setOutcome('Completed')}>Lesson happened</button>
        <button type="button" aria-pressed={outcome === 'NoShow'} onClick={() => setOutcome('NoShow')}>Student didn't come</button>
      </div>
      {outcome === 'Completed' && (
        <>
          <Field label="Progress note" htmlFor="note" hint={`Saved to ${lesson.studentName.split(' ')[0]}'s ${lesson.subjectName} progress`}>
            <textarea id="note" value={note} maxLength={2000} onChange={(e) => setNote(e.target.value)}
              placeholder="What you covered, what clicked, what to practise" />
          </Field>
          <div className="field">
            <span id="rating-label" style={{ fontWeight: 700, fontSize: 'var(--step--1)' }}>How well did they understand it?</span>
            <div className="rating" role="group" aria-labelledby="rating-label">
              {[1, 2, 3, 4, 5].map((n) => (
                <button key={n} type="button" aria-pressed={rating === n} aria-label={`${n} out of 5`}
                  onClick={() => setRating(rating === n ? null : n)}>{n}</button>
              ))}
            </div>
            <span className="hint">1 = lost, 5 = could teach it. Optional.</span>
          </div>
        </>
      )}
      <div className="dialog-foot">
        <button type="button" className="btn secondary" onClick={onBack}>Back</button>
        <button type="submit" className="btn" disabled={mark.isPending}>{mark.isPending ? 'Saving…' : 'Save attendance'}</button>
      </div>
    </form>
  );
}

function CancelForm({ lesson, onDone, onBack }: { lesson: Lesson; onDone: (m: string) => void; onBack: () => void }) {
  const cancel = useCancelLesson();
  const [reason, setReason] = useState('');
  const [following, setFollowing] = useState(false);
  const tooShort = reason.trim().length < 3;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (tooShort) return;
    const res = await cancel.mutateAsync({
      id: lesson.id, reason: reason.trim(), version: lesson.version, includeFollowingInSeries: following,
    });
    onDone(res.cancelledCount > 1 ? `${res.cancelledCount} lessons cancelled` : 'Lesson cancelled');
  };

  return (
    <form className="stack" onSubmit={(e) => void submit(e).catch(() => undefined)} noValidate>
      <ErrorAlert error={cancel.error} />
      <Field label="Reason" htmlFor="reason" hint="Shown on the lesson and in reports">
        <input id="reason" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Student unwell" />
      </Field>
      {lesson.seriesId && (
        <label className="check">
          <input type="checkbox" checked={following} onChange={(e) => setFollowing(e.target.checked)} />
          Also cancel the later lessons in this weekly series
        </label>
      )}
      <div className="dialog-foot">
        <button type="button" className="btn secondary" onClick={onBack}>Keep lesson</button>
        <button type="submit" className="btn danger" disabled={tooShort || cancel.isPending}>
          {cancel.isPending ? 'Cancelling…' : 'Cancel lesson'}
        </button>
      </div>
    </form>
  );
}
