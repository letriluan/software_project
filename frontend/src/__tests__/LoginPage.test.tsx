import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';
import { LoginPage } from '../pages/LoginPage';
import { renderWithProviders } from '../test/render';
import { server } from '../test/server';

function setup() {
  return renderWithProviders(
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<p>Tutor home</p>} />
    </Routes>,
    { route: '/login', withAuth: true },
  );
}

describe('LoginPage', () => {
  it('validates before calling the API', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Enter your email')).toBeInTheDocument();
    expect(screen.getByText('Enter your password')).toBeInTheDocument();
  });

  it('signs in and goes to the tutor home', async () => {
    const { user } = setup();
    await user.type(await screen.findByLabelText('Email'), 'tutor@tutordesk.demo');
    await user.type(screen.getByLabelText('Password'), 'Demo!2345');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Tutor home')).toBeInTheDocument();
  });

  it('shows the server message for wrong credentials', async () => {
    server.use(http.post('/api/auth/login', () =>
      HttpResponse.json({ title: 'Unauthorized', detail: 'Email or password is incorrect.' }, { status: 401 })));
    const { user } = setup();
    await user.type(await screen.findByLabelText('Email'), 'tutor@tutordesk.demo');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Email or password is incorrect.')).toBeInTheDocument();
  });

  it('fills the demo tutor account', async () => {
    const { user } = setup();
    await user.click(await screen.findByRole('button', { name: 'tutor' }));
    expect(screen.getByLabelText('Email')).toHaveValue('tutor@tutordesk.demo');
  });
});
