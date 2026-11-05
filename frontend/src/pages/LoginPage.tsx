import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { ErrorAlert, Field, Logo } from '../components/ui';
import { useState } from 'react';

const schema = z.object({
  email: z.string().trim().min(1, 'Enter your email').email('Enter a valid email address'),
  password: z.string().min(1, 'Enter your password'),
});
type LoginForm = z.infer<typeof schema>;

const DEMO = [
  { label: 'tutor', email: 'tutor@tutordesk.demo' },
  { label: 'admin', email: 'admin@tutordesk.demo' },
];

export function LoginPage() {
  const { user, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [error, setError] = useState<unknown>(null);
  const { register, handleSubmit, setValue, formState: { errors, isSubmitting } } =
    useForm<LoginForm>({ resolver: zodResolver(schema) });

  if (user) return <Navigate to="/" replace />;

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setError(null);
    try {
      const signedIn = await login(email, password);
      const from = (location.state as { from?: string } | null)?.from;
      navigate(from && from !== '/login' ? from : signedIn.role === 'Admin' ? '/admin/report' : '/', { replace: true });
    } catch (e) {
      setError(e);
    }
  });

  return (
    <div className="login">
      <div className="login-card">
        <h1><Logo size={32} /> TutorDesk</h1>
        <p>Lessons, students and attendance for independent tutors.</p>
        <form onSubmit={onSubmit} noValidate className="stack">
          <ErrorAlert error={error} />
          <Field label="Email" htmlFor="email" error={errors.email?.message}>
            <input id="email" type="email" autoComplete="username" aria-invalid={!!errors.email} {...register('email')} />
          </Field>
          <Field label="Password" htmlFor="password" error={errors.password?.message}>
            <input id="password" type="password" autoComplete="current-password" aria-invalid={!!errors.password} {...register('password')} />
          </Field>
          <button className="btn" type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
        <div className="demo-accounts">
          Trying the demo? Sign in as the{' '}
          {DEMO.map((d, i) => (
            <span key={d.email}>
              {i > 0 && ' or '}
              <button type="button" onClick={() => { setValue('email', d.email); setValue('password', 'Demo!2345'); }}>
                {d.label}
              </button>
            </span>
          ))}
          . Password: Demo!2345
        </div>
      </div>
    </div>
  );
}
