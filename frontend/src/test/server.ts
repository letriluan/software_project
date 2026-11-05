import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type { AuthResponse, Lesson, StudentListItem, Subject, TutorDashboard } from '../api/types';

export const tutorUser = { id: 't1', email: 'tutor@tutordesk.demo', fullName: 'Luan Le', role: 'Tutor' as const };

export const subjects: Subject[] = [
  { id: 1, name: 'Mathematics' },
  { id: 3, name: 'English' },
];

export const students: StudentListItem[] = [
  { id: 's1', firstName: 'Ava', lastName: 'Nguyen', yearLevel: 11, school: 'Glenunga', isArchived: false,
    tutorId: 't1', tutorName: 'Luan Le', nextLessonUtc: null, completedLessons: 4 },
];

export function lesson(overrides: Partial<Lesson> = {}): Lesson {
  return {
    id: 'l1', tutorId: 't1', tutorName: 'Luan Le', studentId: 's1', studentName: 'Ava Nguyen',
    subjectId: 1, subjectName: 'Mathematics',
    startsAtUtc: '2026-11-10T05:30:00Z', endsAtUtc: '2026-11-10T06:30:00Z', durationMinutes: 60,
    mode: 'InPerson', location: null, status: 'Scheduled', cancellationReason: null, seriesId: null,
    version: 7, progress: null, ...overrides,
  };
}

export const dashboard: TutorDashboard = {
  today: '2026-11-10', todaysLessons: [lesson()], needsAttendance: [], upcomingThisWeek: 3,
  hoursThisMonth: 12.5, completedThisMonth: 12, activeStudents: 6, attendanceRateThisMonth: 0.92,
};

const auth: AuthResponse = { accessToken: 'token-1', expiresAt: '2026-11-10T06:00:00Z', user: tutorUser };

/** Default happy-path API; individual tests override with server.use(...). */
export const handlers = [
  http.post('/api/auth/refresh', () => HttpResponse.json({ title: 'Unauthorized' }, { status: 401 })),
  http.post('/api/auth/login', () => HttpResponse.json(auth)),
  http.post('/api/auth/logout', () => new HttpResponse(null, { status: 204 })),
  http.get('/api/subjects', () => HttpResponse.json(subjects)),
  http.get('/api/students', () =>
    HttpResponse.json({ items: students, page: 1, pageSize: 100, totalCount: students.length, totalPages: 1 })),
  http.get('/api/dashboard', () => HttpResponse.json(dashboard)),
  http.get('/api/lessons', () => HttpResponse.json([lesson()])),
];

export const server = setupServer(...handlers);
export { auth as authResponse };
