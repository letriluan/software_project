import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ApiError } from '../api/client';
import { useChangePassword } from '../api/hooks';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert, Field, useToast } from '../components/ui';

const schema = z.object({
  currentPassword: z.string().min(1, 'Enter your current password'),
  newPassword: z.string().min(8, 'At least 8 characters')
    .regex(/[A-Z]/, 'Include an uppercase letter').regex(/[a-z]/, 'Include a lowercase letter')
    .regex(/\d/, 'Include a number'),
  confirm: z.string(),
}).refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: "Passwords don't match" });
type Values = z.infer<typeof schema>;

export function AccountPage() {
  const { user } = useAuth();
  const change = useChangePassword();
  const toast = useToast();
  const { register, handleSubmit, reset, setError, formState: { errors, isSubmitting } } =
    useForm<Values>({ resolver: zodResolver(schema) });

  const onSubmit = handleSubmit(async (v) => {
    try {
      await change.mutateAsync({ currentPassword: v.currentPassword, newPassword: v.newPassword });
      reset();
      toast.show('Password changed. Other devices have been signed out.');
    } catch (e) {
      if (e instanceof ApiError && e.field('currentPassword')) setError('currentPassword', { message: e.field('currentPassword') });
      else if (e instanceof ApiError && e.field('newPassword')) setError('newPassword', { message: e.field('newPassword') });
    }
  });

  return (
    <>
      <div className="page-head">
        <div><h1>Account</h1><p>{user?.fullName}, {user?.email}</p></div>
      </div>
      <section className="sheet" style={{ maxWidth: 520 }} aria-labelledby="pw-h">
        <h2 id="pw-h">Change password</h2>
        <form className="stack" onSubmit={onSubmit} noValidate>
          {change.error instanceof ApiError && !change.error.field('currentPassword') && !change.error.field('newPassword') &&
            <ErrorAlert error={change.error} />}
          <Field label="Current password" htmlFor="currentPassword" error={errors.currentPassword?.message}>
            <input id="currentPassword" type="password" autoComplete="current-password" aria-invalid={!!errors.currentPassword} {...register('currentPassword')} />
          </Field>
          <Field label="New password" htmlFor="newPassword" error={errors.newPassword?.message} hint="8+ characters with upper and lower case letters and a number">
            <input id="newPassword" type="password" autoComplete="new-password" aria-invalid={!!errors.newPassword} {...register('newPassword')} />
          </Field>
          <Field label="Confirm new password" htmlFor="confirm" error={errors.confirm?.message}>
            <input id="confirm" type="password" autoComplete="new-password" aria-invalid={!!errors.confirm} {...register('confirm')} />
          </Field>
          <div><button type="submit" className="btn" disabled={isSubmitting}>{isSubmitting ? 'Saving…' : 'Change password'}</button></div>
        </form>
      </section>
      {toast.node}
    </>
  );
}
