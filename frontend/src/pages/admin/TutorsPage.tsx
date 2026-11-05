import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ApiError } from '../../api/client';
import { useCreateTutor, useSetTutorStatus, useTutors } from '../../api/hooks';
import type { TutorSummary } from '../../api/types';
import { Dialog, ErrorAlert, Field, Loading, useToast } from '../../components/ui';
import { fmt, hours } from '../../lib/time';

const schema = z.object({
  fullName: z.string().trim().min(2, 'Enter their full name').max(120),
  email: z.string().trim().email('Enter a valid email address'),
  temporaryPassword: z.string().min(8, 'At least 8 characters')
    .regex(/[A-Z]/, 'Include an uppercase letter').regex(/[a-z]/, 'Include a lowercase letter').regex(/\d/, 'Include a number'),
});
type Values = z.infer<typeof schema>;

function AddTutorForm({ onDone, onCancel }: { onDone: (t: TutorSummary) => void; onCancel: () => void }) {
  const create = useCreateTutor();
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<Values>({ resolver: zodResolver(schema) });
  const submit = handleSubmit(async (v) => {
    try {
      onDone(await create.mutateAsync(v));
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) setError('email', { message: e.message });
      else if (e instanceof ApiError && e.field('temporaryPassword')) setError('temporaryPassword', { message: e.field('temporaryPassword') });
    }
  });
  return (
    <form className="stack" onSubmit={submit} noValidate>
      {create.error instanceof ApiError && create.error.status >= 500 && <ErrorAlert error={create.error} />}
      <Field label="Full name" htmlFor="fullName" error={errors.fullName?.message}>
        <input id="fullName" aria-invalid={!!errors.fullName} {...register('fullName')} />
      </Field>
      <Field label="Email" htmlFor="email" error={errors.email?.message}>
        <input id="email" type="email" aria-invalid={!!errors.email} {...register('email')} />
      </Field>
      <Field label="Temporary password" htmlFor="temporaryPassword" error={errors.temporaryPassword?.message}
        hint="Share it with the tutor; they can change it under Account">
        <input id="temporaryPassword" type="text" autoComplete="off" aria-invalid={!!errors.temporaryPassword} {...register('temporaryPassword')} />
      </Field>
      <div className="dialog-foot">
        <button type="button" className="btn secondary" onClick={onCancel}>Cancel</button>
        <button type="submit" className="btn" disabled={isSubmitting}>{isSubmitting ? 'Adding…' : 'Add tutor'}</button>
      </div>
    </form>
  );
}

export function TutorsPage() {
  const tutors = useTutors();
  const setStatus = useSetTutorStatus();
  const [adding, setAdding] = useState(false);
  const toast = useToast();

  return (
    <>
      <div className="page-head">
        <div><h1>Tutors</h1><p>{tutors.data ? `${tutors.data.filter((t) => t.isActive).length} active` : ' '}</p></div>
        <button type="button" className="btn" onClick={() => setAdding(true)}>Add tutor</button>
      </div>
      <div className="sheet">
        {tutors.isLoading ? <Loading /> : tutors.isError ? <ErrorAlert error={tutors.error} /> : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Tutor</th><th className="num">Active students</th><th className="num">Booked ahead</th>
                  <th className="num">Hours this month</th><th>Last lesson</th><th><span className="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {tutors.data?.map((t) => (
                  <tr key={t.id} className={t.isActive ? undefined : 'dim'}>
                    <td><strong>{t.fullName}</strong><br /><span className="tag">{t.email}{!t.isActive && ', deactivated'}</span></td>
                    <td className="num">{t.activeStudents}</td>
                    <td className="num">{t.upcomingLessons}</td>
                    <td className="num">{hours(t.hoursThisMonth)}</td>
                    <td>{t.lastLessonUtc ? fmt.day(t.lastLessonUtc) : '-'}</td>
                    <td className="num">
                      <button type="button" className={`btn small ${t.isActive ? 'danger' : 'secondary'}`} disabled={setStatus.isPending}
                        onClick={() => setStatus.mutate({ id: t.id, isActive: !t.isActive }, {
                          onSuccess: () => toast.show(t.isActive ? `${t.fullName} deactivated and signed out` : `${t.fullName} reactivated`),
                        })}>
                        {t.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      <Dialog open={adding} onClose={() => setAdding(false)} title="Add a tutor">
        {adding && <AddTutorForm onCancel={() => setAdding(false)} onDone={(t) => { setAdding(false); toast.show(`${t.fullName} added`); }} />}
      </Dialog>
      {toast.node}
    </>
  );
}
