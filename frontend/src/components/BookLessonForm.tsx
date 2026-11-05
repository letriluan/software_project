import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ApiError } from '../api/client';
import { useBookLesson, useRescheduleLesson, useStudents, useSubjects } from '../api/hooks';
import type { BookingResult, Lesson, LessonConflict } from '../api/types';
import { adelaideIso, fmt } from '../lib/time';
import { ErrorAlert, Field } from './ui';

export const DURATIONS = [30, 45, 60, 75, 90, 120];

const schema = z.object({
  studentId: z.string().min(1, 'Choose a student'),
  subjectId: z.coerce.number().int().positive('Choose a subject'),
  date: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Choose a date'),
  time: z.string().regex(/^\d{2}:\d{2}$/, 'Choose a start time'),
  durationMinutes: z.coerce.number().int(),
  mode: z.enum(['InPerson', 'Online']),
  location: z.string().max(200).optional(),
  repeatWeekly: z.coerce.number().int().min(1).max(12),
});
export type BookingForm = z.infer<typeof schema>;

function conflictText(c: LessonConflict) {
  return `${fmt.dateTime(c.requestedStartUtc)} clashes with a lesson at ${fmt.time(c.conflictingStartUtc)}` +
    (c.conflictsWith === 'student' ? ' for this student' : '');
}

/**
 * Books a new lesson (optionally weekly), or reschedules `lesson` when given.
 * Server-side rules (business hours, overlaps, past times) come back as field errors / 409s.
 */
export function BookLessonForm({ defaults, lesson, onDone, onCancel }: {
  defaults?: Partial<BookingForm>;
  lesson?: Lesson;
  onDone: (message: string, result?: BookingResult | Lesson) => void;
  onCancel: () => void;
}) {
  const students = useStudents({ pageSize: 100 });
  const subjects = useSubjects();
  const book = useBookLesson();
  const reschedule = useRescheduleLesson();
  const [error, setError] = useState<unknown>(null);

  const editing = !!lesson;
  const { register, handleSubmit, watch, setError: setFieldError, formState: { errors, isSubmitting } } =
    useForm<BookingForm>({
      resolver: zodResolver(schema) as never,
      defaultValues: {
        studentId: lesson?.studentId ?? defaults?.studentId ?? '',
        subjectId: lesson?.subjectId ?? defaults?.subjectId ?? 1,
        date: lesson ? fmt.isoDate(new Date(lesson.startsAtUtc)) : defaults?.date ?? '',
        time: lesson ? new Intl.DateTimeFormat('en-GB', { timeZone: 'Australia/Adelaide', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(lesson.startsAtUtc)) : defaults?.time ?? '16:00',
        durationMinutes: lesson?.durationMinutes ?? defaults?.durationMinutes ?? 60,
        mode: lesson?.mode ?? defaults?.mode ?? 'InPerson',
        location: lesson?.location ?? '',
        repeatWeekly: 1,
      },
    });
  const repeat = Number(watch('repeatWeekly'));

  const onSubmit = handleSubmit(async (v) => {
    setError(null);
    const startsAt = adelaideIso(v.date, v.time);
    try {
      if (lesson) {
        const updated = await reschedule.mutateAsync({
          id: lesson.id, subjectId: v.subjectId, startsAt, durationMinutes: v.durationMinutes,
          mode: v.mode, location: v.location || null, version: lesson.version,
        });
        onDone('Lesson rescheduled', updated);
      } else {
        const result = await book.mutateAsync({
          studentId: v.studentId, subjectId: v.subjectId, startsAt, durationMinutes: v.durationMinutes,
          mode: v.mode, location: v.location || null, repeatWeekly: v.repeatWeekly,
        });
        onDone(result.lessons.length > 1 ? `${result.lessons.length} weekly lessons booked` : 'Lesson booked', result);
      }
    } catch (e) {
      if (e instanceof ApiError) {
        const startErr = e.field('startsAt');
        if (startErr) setFieldError('time', { message: startErr });
        const durErr = e.field('durationMinutes');
        if (durErr) setFieldError('durationMinutes', { message: durErr });
        if (startErr || durErr) return;
      }
      setError(e);
    }
  });

  const conflicts = error instanceof ApiError ? (error.body.conflicts as LessonConflict[] | undefined) : undefined;
  const activeStudents = students.data?.items.filter((s) => !s.isArchived) ?? [];

  return (
    <form onSubmit={onSubmit} noValidate>
      <div className="form-grid">
        {conflicts ? (
          <div className="alert wide" role="alert">
            <strong>{(error as ApiError).message}</strong>
            <ul>{conflicts.map((c) => <li key={c.requestedStartUtc + c.conflictingLessonId}>{conflictText(c)}</li>)}</ul>
          </div>
        ) : (
          error ? <div className="wide"><ErrorAlert error={error} /></div> : null
        )}

        <Field label="Student" htmlFor="studentId" error={errors.studentId?.message} wide>
          <select id="studentId" disabled={editing} aria-invalid={!!errors.studentId} {...register('studentId')}>
            <option value="">Choose a student</option>
            {(editing ? [{ id: lesson.studentId, firstName: lesson.studentName, lastName: '' }] : activeStudents).map((s) => (
              <option key={s.id} value={s.id}>{`${s.firstName} ${s.lastName}`.trim()}</option>
            ))}
          </select>
        </Field>

        <Field label="Subject" htmlFor="subjectId" error={errors.subjectId?.message}>
          <select id="subjectId" {...register('subjectId')}>
            {subjects.data?.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </Field>

        <Field label="Length" htmlFor="durationMinutes" error={errors.durationMinutes?.message}>
          <select id="durationMinutes" aria-invalid={!!errors.durationMinutes} {...register('durationMinutes')}>
            {DURATIONS.map((d) => <option key={d} value={d}>{d < 60 ? `${d} min` : `${d / 60} h`.replace('.5 h', 'h 30')}</option>)}
          </select>
        </Field>

        <Field label="Date" htmlFor="date" error={errors.date?.message}>
          <input id="date" type="date" aria-invalid={!!errors.date} {...register('date')} />
        </Field>

        <Field label="Start time" htmlFor="time" error={errors.time?.message} hint="Adelaide time, 6am to 10pm">
          <input id="time" type="time" step={900} aria-invalid={!!errors.time} {...register('time')} />
        </Field>

        <Field label="Where" htmlFor="mode">
          <select id="mode" {...register('mode')}>
            <option value="InPerson">In person</option>
            <option value="Online">Online</option>
          </select>
        </Field>

        <Field label="Location or link" htmlFor="location" error={errors.location?.message}>
          <input id="location" placeholder="e.g. Student's home, Zoom" {...register('location')} />
        </Field>

        {!editing && (
          <Field label="Repeat" htmlFor="repeatWeekly" wide
            hint={repeat > 1 ? `Books ${repeat} lessons, same time each week. If any week clashes, nothing is booked.` : undefined}>
            <select id="repeatWeekly" {...register('repeatWeekly')}>
              <option value={1}>Just this once</option>
              {[2, 4, 6, 8, 10, 12].map((n) => <option key={n} value={n}>Weekly for {n} weeks</option>)}
            </select>
          </Field>
        )}
      </div>
      <div className="dialog-foot">
        <button type="button" className="btn secondary" onClick={onCancel}>Back</button>
        <button type="submit" className="btn" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : editing ? 'Save new time' : repeat > 1 ? `Book ${repeat} lessons` : 'Book lesson'}
        </button>
      </div>
    </form>
  );
}
