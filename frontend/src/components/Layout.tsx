import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useDashboard } from '../api/hooks';
import { Logo } from './ui';

function TutorNav() {
  const { data } = useDashboard();
  const toMark = data?.needsAttendance.length ?? 0;
  return (
    <>
      <NavLink to="/" end>Today</NavLink>
      <NavLink to="/week">
        Week
        {toMark > 0 && <span className="count" aria-label={`${toMark} lessons to mark`}>{toMark}</span>}
      </NavLink>
      <NavLink to="/students">Students</NavLink>
      <NavLink to="/account">Account</NavLink>
    </>
  );
}

function AdminNav() {
  return (
    <>
      <NavLink to="/admin/report">Monthly report</NavLink>
      <NavLink to="/admin/tutors">Tutors</NavLink>
      <NavLink to="/admin/students">All students</NavLink>
      <NavLink to="/account">Account</NavLink>
    </>
  );
}

export function Layout() {
  const { user, logout } = useAuth();
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand"><Logo /> TutorDesk</div>
        <nav className="nav" aria-label="Main">{user?.role === 'Admin' ? <AdminNav /> : <TutorNav />}</nav>
        <div className="sidebar-foot">
          <strong>{user?.fullName}</strong>
          {user?.role === 'Admin' ? 'Administrator' : 'Tutor'}
          <button type="button" className="btn ghost small" onClick={() => void logout()}>Sign out</button>
        </div>
      </aside>
      <main className="main" id="main">
        <Outlet />
      </main>
    </div>
  );
}
