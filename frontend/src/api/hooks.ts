import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  AttendanceOutcome,
  BookingResult,
  BookLessonInput,
  Lesson,
  LessonMode,
  MonthlyReport,
  Paged,
  StudentDetail,
  StudentInput,
  StudentListItem,
  StudentProgress,
  Subject,
  TutorDashboard,
  TutorSummary,
} from './types';

export const keys = {
  dashboard: ['dashboard'] as const,
  subjects: ['subjects'] as const,
  students: (q: object) => ['students', q] as const,
  student: (id: string) => ['student', id] as const,
  progress: (id: string) => ['student', id, 'progress'] as const,
  lessons: (from: string, to: string, studentId?: string) => ['lessons', from, to, studentId ?? null] as const,
  tutors: ['admin', 'tutors'] as const,
  report: (y: number, m: number) => ['admin', 'report', y, m] as const,
};

/** Anything that changes lessons can affect the calendar, dashboard and student stats. */
function useInvalidateLessons() {
  const qc = useQueryClient();
  return () =>
    Promise.all([
      qc.invalidateQueries({ queryKey: ['lessons'] }),
      qc.invalidateQueries({ queryKey: keys.dashboard }),
      qc.invalidateQueries({ queryKey: ['student'] }),
      qc.invalidateQueries({ queryKey: ['students'] }),
    ]);
}

// ---------- reads ----------
export const useSubjects = () =>
  useQuery({ queryKey: keys.subjects, queryFn: () => api.get<Subject[]>('/api/subjects'), staleTime: 60 * 60_000 });

export const useDashboard = () =>
  useQuery({ queryKey: keys.dashboard, queryFn: () => api.get<TutorDashboard>('/api/dashboard') });

export function useStudents(q: { search?: string; includeArchived?: boolean; page?: number; pageSize?: number }) {
  return useQuery({
    queryKey: keys.students(q),
    queryFn: () => api.get<Paged<StudentListItem>>('/api/students', q),
    placeholderData: keepPreviousData,
  });
}

export const useStudent = (id: string) =>
  useQuery({ queryKey: keys.student(id), queryFn: () => api.get<StudentDetail>(`/api/students/${id}`) });

export const useStudentProgress = (id: string) =>
  useQuery({ queryKey: keys.progress(id), queryFn: () => api.get<StudentProgress>(`/api/students/${id}/progress`) });

export function useLessons(from: string, to: string, studentId?: string) {
  return useQuery({
    queryKey: keys.lessons(from, to, studentId),
    queryFn: () => api.get<Lesson[]>('/api/lessons', { from, to, studentId }),
    placeholderData: keepPreviousData,
  });
}

export const useTutors = () =>
  useQuery({ queryKey: keys.tutors, queryFn: () => api.get<TutorSummary[]>('/api/admin/tutors') });

export const useMonthlyReport = (year: number, month: number) =>
  useQuery({
    queryKey: keys.report(year, month),
    queryFn: () => api.get<MonthlyReport>('/api/admin/reports/monthly', { year, month }),
    placeholderData: keepPreviousData,
  });

// ---------- writes ----------
export function useCreateStudent() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: StudentInput) => api.post<StudentDetail>('/api/students', input),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['students'] }),
  });
}

export function useUpdateStudent(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: StudentInput & { version: number }) => api.put<StudentDetail>(`/api/students/${id}`, input),
    onSuccess: (data) => {
      qc.setQueryData(keys.student(id), data);
      return qc.invalidateQueries({ queryKey: ['students'] });
    },
  });
}

export function useArchiveStudent() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: (id: string) => api.delete<{ cancelledFutureLessons: number }>(`/api/students/${id}`),
    onSuccess: invalidate,
  });
}

export function useRestoreStudent() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/api/students/${id}/restore`),
    onSuccess: invalidate,
  });
}

export function useBookLesson() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: (input: BookLessonInput) => api.post<BookingResult>('/api/lessons', input),
    onSuccess: invalidate,
  });
}

export function useRescheduleLesson() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string; subjectId: number; startsAt: string; durationMinutes: number;
      mode: LessonMode; location?: string | null; version: number;
    }) => api.put<Lesson>(`/api/lessons/${id}`, body),
    onSuccess: invalidate,
  });
}

export function useCancelLesson() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; reason: string; version: number; includeFollowingInSeries: boolean }) =>
      api.post<{ cancelledCount: number }>(`/api/lessons/${id}/cancel`, body),
    onSuccess: invalidate,
  });
}

export function useMarkAttendance() {
  const invalidate = useInvalidateLessons();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string; outcome: AttendanceOutcome; progressNote?: string; understandingRating?: number | null; version: number;
    }) => api.post<Lesson>(`/api/lessons/${id}/attendance`, body),
    onSuccess: invalidate,
  });
}

export function useCreateTutor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (input: { fullName: string; email: string; temporaryPassword: string }) =>
      api.post<TutorSummary>('/api/admin/tutors', input),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.tutors }),
  });
}

export function useSetTutorStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      api.put<void>(`/api/admin/tutors/${id}/status`, { isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin'] }),
  });
}

export const useChangePassword = () =>
  useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      api.post<void>('/api/auth/change-password', input),
  });
