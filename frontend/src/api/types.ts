// Mirrors the API DTOs (backend/src/TutorDesk.Api/Features/**). Dates are ISO strings in UTC.

export type Role = 'Admin' | 'Tutor';
export type LessonStatus = 'Scheduled' | 'Completed' | 'NoShow' | 'Cancelled';
export type LessonMode = 'InPerson' | 'Online';
export type AttendanceOutcome = 'Completed' | 'NoShow';

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: Role;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Subject {
  id: number;
  name: string;
}

export interface StudentListItem {
  id: string;
  firstName: string;
  lastName: string;
  yearLevel: number;
  school: string | null;
  isArchived: boolean;
  tutorId: string;
  tutorName: string;
  nextLessonUtc: string | null;
  completedLessons: number;
}

export interface StudentStats {
  completedLessons: number;
  noShows: number;
  cancelledLessons: number;
  upcomingLessons: number;
  hoursTaught: number;
  attendanceRate: number | null;
}

export interface StudentInput {
  firstName: string;
  lastName: string;
  yearLevel: number;
  school?: string | null;
  parentName?: string | null;
  parentEmail?: string | null;
  parentPhone?: string | null;
  notes?: string | null;
}

export interface StudentDetail extends Required<StudentInput> {
  id: string;
  isArchived: boolean;
  tutorId: string;
  tutorName: string;
  createdAtUtc: string;
  version: number;
  stats: StudentStats;
}

export interface ProgressNote {
  id: string;
  lessonId: string | null;
  subjectId: number;
  subjectName: string;
  note: string;
  understandingRating: number | null;
  createdAtUtc: string;
  lessonStartsAtUtc: string | null;
}

export interface SubjectProgress {
  subjectId: number;
  subjectName: string;
  notes: number;
  averageRating: number | null;
  latestRating: number | null;
  trend: 'improving' | 'declining' | 'steady' | 'not enough data';
}

export interface StudentProgress {
  subjects: SubjectProgress[];
  notes: ProgressNote[];
}

export interface Lesson {
  id: string;
  tutorId: string;
  tutorName: string;
  studentId: string;
  studentName: string;
  subjectId: number;
  subjectName: string;
  startsAtUtc: string;
  endsAtUtc: string;
  durationMinutes: number;
  mode: LessonMode;
  location: string | null;
  status: LessonStatus;
  cancellationReason: string | null;
  seriesId: string | null;
  version: number;
  progress: { note: string; understandingRating: number | null } | null;
}

export interface BookLessonInput {
  studentId: string;
  subjectId: number;
  startsAt: string; // ISO with offset
  durationMinutes: number;
  mode: LessonMode;
  location?: string | null;
  repeatWeekly: number;
}

export interface LessonConflict {
  requestedStartUtc: string;
  conflictingLessonId: string;
  conflictingStartUtc: string;
  conflictsWith: 'tutor' | 'student';
}

export interface BookingResult {
  seriesId: string | null;
  lessons: Lesson[];
}

export interface TutorDashboard {
  today: string;
  todaysLessons: Lesson[];
  needsAttendance: Lesson[];
  upcomingThisWeek: number;
  hoursThisMonth: number;
  completedThisMonth: number;
  activeStudents: number;
  attendanceRateThisMonth: number | null;
}

export interface TutorSummary {
  id: string;
  fullName: string;
  email: string;
  isActive: boolean;
  activeStudents: number;
  upcomingLessons: number;
  hoursThisMonth: number;
  lastLessonUtc: string | null;
  createdAtUtc: string;
}

export interface TutorMonthRow {
  tutorId: string;
  tutorName: string;
  isActive: boolean;
  hoursCompleted: number;
  lessonsCompleted: number;
  noShows: number;
  cancelled: number;
  stillScheduled: number;
  studentsTaught: number;
  attendanceRate: number | null;
}

export interface MonthlyReport {
  year: number;
  month: number;
  totals: {
    hoursCompleted: number;
    lessonsCompleted: number;
    noShows: number;
    cancelled: number;
    stillScheduled: number;
    studentsTaught: number;
    attendanceRate: number | null;
  };
  tutors: TutorMonthRow[];
  subjects: { subjectId: number; subjectName: string; hoursCompleted: number; lessonsCompleted: number }[];
  daily: { date: string; hoursCompleted: number }[];
}
