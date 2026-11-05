import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { BookLessonForm } from '../components/BookLessonForm';
import { renderWithProviders } from '../test/render';
import { lesson, server } from '../test/server';

describe('BookLessonForm', () => {
  it('sends the start time with the Adelaide offset and a weekly repeat', async () => {
    let body: Record<string, unknown> = {};
    server.use(http.post('/api/lessons', async ({ request }) => {
      body = (await request.json()) as Record<string, unknown>;
      return HttpResponse.json({ seriesId: 'x', lessons: [lesson(), lesson({ id: 'l2' })] }, { status: 201 });
    }));
    const onDone = vi.fn();
    const { user } = renderWithProviders(
      <BookLessonForm defaults={{ date: '2026-11-10', time: '16:00' }} onDone={onDone} onCancel={() => {}} />);

    await user.selectOptions(await screen.findByLabelText('Student'), await screen.findByRole('option', { name: 'Ava Nguyen' }));
    await user.selectOptions(screen.getByLabelText('Length'), '90');
    await user.selectOptions(screen.getByLabelText('Repeat'), '4');
    expect(screen.getByText(/If any week clashes, nothing is booked/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Book 4 lessons' }));

    await waitFor(() => expect(onDone).toHaveBeenCalledWith('2 weekly lessons booked', expect.anything()));
    expect(body).toMatchObject({
      studentId: 's1', subjectId: 1, startsAt: '2026-11-10T16:00:00.000+10:30',
      durationMinutes: 90, repeatWeekly: 4, mode: 'InPerson',
    });
  });

  it('lists each clash when the server returns 409', async () => {
    server.use(http.post('/api/lessons', () => HttpResponse.json({
      title: 'Conflict', detail: '1 of 1 requested time(s) overlap existing lessons.',
      conflicts: [{ requestedStartUtc: '2026-11-10T05:30:00Z', conflictingLessonId: 'l9',
        conflictingStartUtc: '2026-11-10T05:00:00Z', conflictsWith: 'tutor' }],
    }, { status: 409 })));
    const { user } = renderWithProviders(
      <BookLessonForm defaults={{ studentId: 's1', date: '2026-11-10', time: '16:00' }} onDone={() => {}} onCancel={() => {}} />);

    await screen.findByRole('option', { name: 'Ava Nguyen' });
    await user.click(screen.getByRole('button', { name: 'Book lesson' }));

    expect(await screen.findByText('1 of 1 requested time(s) overlap existing lessons.')).toBeInTheDocument();
    expect(screen.getByText('Tue 10 Nov, 4:00pm clashes with a lesson at 3:30pm')).toBeInTheDocument();
  });

  it('puts server timing errors next to the start time field', async () => {
    server.use(http.post('/api/lessons', () => HttpResponse.json({
      title: 'Validation failed', errors: { StartsAt: ['Lessons must be between 06:00 and 22:00 (Adelaide time).'] },
    }, { status: 400 })));
    const { user } = renderWithProviders(
      <BookLessonForm defaults={{ studentId: 's1', date: '2026-11-10', time: '21:30' }} onDone={() => {}} onCancel={() => {}} />);

    await screen.findByRole('option', { name: 'Ava Nguyen' });
    await user.click(screen.getByRole('button', { name: 'Book lesson' }));

    expect(await screen.findByText('Lessons must be between 06:00 and 22:00 (Adelaide time).')).toBeInTheDocument();
    expect(screen.getByLabelText('Start time')).toHaveAttribute('aria-invalid', 'true');
  });
});
