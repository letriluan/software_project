import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ApiError } from '../api/client';
import type { StudentDetail, StudentInput } from '../api/types';
import { ErrorAlert, Field } from './ui';

const optional = (max: number) => z.string().trim().max(max).optional().or(z.literal(''));

export const studentSchema = z.object({
  firstName: z.string().trim().min(1, 'Enter a first name').max(60),
  lastName: z.string().trim().min(1, 'Enter a last name').max(60),
  yearLevel: z.coerce.number({ invalid_type_error: 'Choose a year level' }).int().min(1, 'Year 1 to 12').max(12, 'Year 1 to 12'),
  school: optional(120),
  parentName: optional(120),
  parentEmail: z.string().trim().email('Enter a valid email address').max(254).optional().or(z.literal('')),
  parentPhone: z.string().trim().regex(/^[+\d\s()-]{6,30}$/, 'Enter a valid phone number').optional().or(z.literal('')),
  notes: optional(2000),
});
type StudentFormValues = z.infer<typeof studentSchema>;

const FIELDS: (keyof StudentFormValues)[] = ['firstName', 'lastName', 'yearLevel', 'school', 'parentName', 'parentEmail', 'parentPhone', 'notes'];

export function StudentForm({ initial, submitLabel, onSubmit, onCancel }: {
  initial?: StudentDetail;
  submitLabel: string;
  onSubmit: (input: StudentInput) => Promise<unknown>;
  onCancel: () => void;
}) {
  const [error, setError] = useState<unknown>(null);
  const { register, handleSubmit, setError: setFieldError, formState: { errors, isSubmitting } } =
    useForm<StudentFormValues>({
      resolver: zodResolver(studentSchema) as never,
      defaultValues: {
        firstName: initial?.firstName ?? '', lastName: initial?.lastName ?? '', yearLevel: initial?.yearLevel,
        school: initial?.school ?? '', parentName: initial?.parentName ?? '', parentEmail: initial?.parentEmail ?? '',
        parentPhone: initial?.parentPhone ?? '', notes: initial?.notes ?? '',
      },
    });

  const submit = handleSubmit(async (v) => {
    setError(null);
    const blankToNull = (s?: string) => (s && s.trim() ? s.trim() : null);
    try {
      await onSubmit({
        firstName: v.firstName, lastName: v.lastName, yearLevel: v.yearLevel,
        school: blankToNull(v.school), parentName: blankToNull(v.parentName), parentEmail: blankToNull(v.parentEmail),
        parentPhone: blankToNull(v.parentPhone), notes: blankToNull(v.notes),
      });
    } catch (e) {
      if (e instanceof ApiError) {
        let mapped = false;
        for (const f of FIELDS) {
          const msg = e.field(f);
          if (msg) { setFieldError(f, { message: msg }); mapped = true; }
        }
        if (mapped) return;
      }
      setError(e);
    }
  });

  const err = (f: keyof StudentFormValues) => errors[f]?.message;
  return (
    <form onSubmit={submit} noValidate>
      <div className="form-grid">
        {!!error && (
          <div className="wide">
            {error instanceof ApiError && error.status === 409
              ? <div className="alert" role="alert">Someone else changed this student while you were editing. Close this form and open it again to see their changes.</div>
              : <ErrorAlert error={error} />}
          </div>
        )}
        <Field label="First name" htmlFor="firstName" error={err('firstName')}>
          <input id="firstName" autoComplete="off" aria-invalid={!!errors.firstName} {...register('firstName')} />
        </Field>
        <Field label="Last name" htmlFor="lastName" error={err('lastName')}>
          <input id="lastName" autoComplete="off" aria-invalid={!!errors.lastName} {...register('lastName')} />
        </Field>
        <Field label="Year level" htmlFor="yearLevel" error={err('yearLevel')}>
          <select id="yearLevel" aria-invalid={!!errors.yearLevel} {...register('yearLevel')}>
            <option value="">Choose</option>
            {Array.from({ length: 12 }, (_, i) => i + 1).map((y) => <option key={y} value={y}>Year {y}</option>)}
          </select>
        </Field>
        <Field label="School" htmlFor="school" error={err('school')}>
          <input id="school" {...register('school')} />
        </Field>
        <Field label="Parent or guardian" htmlFor="parentName" error={err('parentName')}>
          <input id="parentName" {...register('parentName')} />
        </Field>
        <Field label="Parent phone" htmlFor="parentPhone" error={err('parentPhone')}>
          <input id="parentPhone" type="tel" aria-invalid={!!errors.parentPhone} {...register('parentPhone')} />
        </Field>
        <Field label="Parent email" htmlFor="parentEmail" error={err('parentEmail')} wide>
          <input id="parentEmail" type="email" aria-invalid={!!errors.parentEmail} {...register('parentEmail')} />
        </Field>
        <Field label="Notes" htmlFor="notes" error={err('notes')} wide hint="Goals, learning needs, anything to remember">
          <textarea id="notes" {...register('notes')} />
        </Field>
      </div>
      <div className="dialog-foot">
        <button type="button" className="btn secondary" onClick={onCancel}>Cancel</button>
        <button type="submit" className="btn" disabled={isSubmitting}>{isSubmitting ? 'Saving…' : submitLabel}</button>
      </div>
    </form>
  );
}
