import type { ReactNode } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import type { Role } from './api/types';
import { useAuth } from './auth/AuthContext';
import { Layout } from './components/Layout';
import { Loading } from './components/ui';
import { AccountPage } from './pages/AccountPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { StudentDetailPage } from './pages/StudentDetailPage';
import { StudentsPage } from './pages/StudentsPage';
import { WeekPage } from './pages/WeekPage';
import { ReportPage } from './pages/admin/ReportPage';
import { TutorsPage } from './pages/admin/TutorsPage';

function RequireAuth({ role, children }: { role?: Role; children: ReactNode }) {
  const { user, loading } = useAuth();
  const location = useLocation();
  if (loading) return <Loading label="Opening TutorDesk" />;
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (role && user.role !== role) return <Navigate to={user.role === 'Admin' ? '/admin/report' : '/'} replace />;
  return <>{children}</>;
}

function Home() {
  const { user } = useAuth();
  return user?.role === 'Admin' ? <Navigate to="/admin/report" replace /> : <DashboardPage />;
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth><Layout /></RequireAuth>}>
        <Route index element={<Home />} />
        <Route path="week" element={<RequireAuth role="Tutor"><WeekPage /></RequireAuth>} />
        <Route path="students" element={<RequireAuth role="Tutor"><StudentsPage /></RequireAuth>} />
        <Route path="students/:id" element={<StudentDetailPage />} />
        <Route path="account" element={<AccountPage />} />
        <Route path="admin/report" element={<RequireAuth role="Admin"><ReportPage /></RequireAuth>} />
        <Route path="admin/tutors" element={<RequireAuth role="Admin"><TutorsPage /></RequireAuth>} />
        <Route path="admin/students" element={<RequireAuth role="Admin"><StudentsPage /></RequireAuth>} />
        <Route path="*" element={<div className="sheet"><h1>Page not found</h1><p>Use the menu to get back on track.</p></div>} />
      </Route>
    </Routes>
  );
}
