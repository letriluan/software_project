import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { App } from '../App';
import { renderWithProviders } from '../test/render';
import { authResponse, server } from '../test/server';

describe('App routing', () => {
  it('sends signed-out visitors to the login page', async () => {
    renderWithProviders(<App />, { route: '/students', withAuth: true });
    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('restores the session from the refresh cookie and shows the tutor menu', async () => {
    server.use(http.post('/api/auth/refresh', () => HttpResponse.json(authResponse)));
    renderWithProviders(<App />, { route: '/', withAuth: true });

    expect(await screen.findByRole('heading', { name: /Luan/ })).toBeInTheDocument();
    const nav = screen.getByRole('navigation', { name: 'Main' });
    expect(nav).toHaveTextContent('Week');
    expect(nav).not.toHaveTextContent('Monthly report');
  });

  it('keeps tutors out of admin pages', async () => {
    server.use(http.post('/api/auth/refresh', () => HttpResponse.json(authResponse)));
    renderWithProviders(<App />, { route: '/admin/tutors', withAuth: true });
    expect(await screen.findByRole('heading', { name: /Luan/ })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Tutors' })).not.toBeInTheDocument();
  });
});
