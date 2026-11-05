import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { LessonPanel } from '../components/LessonPanel';
import { renderWithProviders } from '../test/render';
import { lesson, server } from '../test/server';

describe('LessonPanel', () => {
  it('offers reschedule (not attendance) before the lesson starts', () => {
    renderWithProviders(<LessonPanel lesson={lesson()} onDone={() => {}} now={new Date('2026-11-09T00:00:00Z')} />);
    expect(screen.getByRole('button', { name: 'Reschedule' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Record attendance' })).not.toBeInTheDocument();
  });

  it('records a completed lesson with a note and rating', async () => {
    let body: Record<string, unknown> = {};
    server.use(http.post('/api/lessons/l1/attendance', async ({ request }) => {
      body = (await request.json()) as Record<string, unknown>;
      return HttpResponse.json(lesson({ status: 'Completed' }));
    }));
    const onDone = vi.fn();
    const { user } = renderWithProviders(
      <LessonPanel lesson={lesson()} onDone={onDone} now={new Date('2026-11-10T06:00:00Z')} />);

    await user.click(screen.getByRole('button', { name: 'Record attendance' }));
    await user.type(screen.getByLabelText('Progress note'), 'Factorising - nearly there');
    await user.click(screen.getByRole('button', { name: '4 out of 5' }));
    await user.click(screen.getByRole('button', { name: 'Save attendance' }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith('Lesson marked as completed'));
    expect(body).toEqual({ outcome: 'Completed', version: 7, progressNote: 'Factorising - nearly there', understandingRating: 4 });
  });

  it('sends no note or rating for a no-show', async () => {
    let body: Record<string, unknown> = {};
    server.use(http.post('/api/lessons/l1/attendance', async ({ request }) => {
      body = (await request.json()) as Record<string, unknown>;
      return HttpResponse.json(lesson({ status: 'NoShow' }));
    }));
    const { user } = renderWithProviders(
      <LessonPanel lesson={lesson()} onDone={() => {}} now={new Date('2026-11-10T06:00:00Z')} />);

    await user.click(screen.getByRole('button', { name: 'Record attendance' }));
    await user.click(screen.getByRole('button', { name: "Student didn't come" }));
    expect(screen.queryByLabelText('Progress note')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Save attendance' }));

    await waitFor(() => expect(body).toEqual({ outcome: 'NoShow', version: 7, understandingRating: null }));
  });

  it('needs a reason to cancel and offers the rest of the series', async () => {
    const { user } = renderWithProviders(
      <LessonPanel lesson={lesson({ seriesId: 'series-1' })} onDone={() => {}} now={new Date('2026-11-09T00:00:00Z')} />);
    await user.click(screen.getByRole('button', { name: 'Cancel lesson' }));
    const confirm = screen.getByRole('button', { name: 'Cancel lesson' });
    expect(confirm).toBeDisabled();
    expect(screen.getByLabelText(/later lessons in this weekly series/)).toBeInTheDocument();
    await user.type(screen.getByLabelText('Reason'), 'Exam week');
    expect(confirm).toBeEnabled();
  });

  it('shows the saved note on a completed lesson', () => {
    renderWithProviders(<LessonPanel onDone={() => {}}
      lesson={lesson({ status: 'Completed', progress: { note: 'Great session', understandingRating: 5 } })} />);
    expect(screen.getByText('Great session')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: '5 out of 5' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel lesson' })).not.toBeInTheDocument();
  });
});
