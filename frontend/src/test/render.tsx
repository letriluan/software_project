import type { ReactElement } from 'react';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { AuthProvider } from '../auth/AuthContext';

export function renderWithProviders(ui: ReactElement, { route = '/', withAuth = false } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const tree = (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[route]}>{withAuth ? <AuthProvider>{ui}</AuthProvider> : ui}</MemoryRouter>
    </QueryClientProvider>
  );
  return { user: userEvent.setup(), client, ...render(tree) };
}
