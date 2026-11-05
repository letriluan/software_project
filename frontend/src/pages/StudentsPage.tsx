import { useDeferredValue, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useCreateStudent, useStudents } from '../api/hooks';
import { StudentForm } from '../components/StudentForm';
import { Dialog, ErrorAlert } from '../components/ui';
import { fmt } from '../lib/time';

export function StudentsPage() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'Admin';
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [page, setPage] = useState(1);
  const [adding, setAdding] = useState(false);
  const deferred = useDeferredValue(search);
  const students = useStudents({ search: deferred || undefined, includeArchived, page, pageSize: 20 });
  const create = useCreateStudent();
  const data = students.data;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{isAdmin ? 'All students' : 'Students'}</h1>
          <p>{data ? `${data.totalCount} ${includeArchived ? 'in total' : 'active'}` : ' '}</p>
        </div>
        {!isAdmin && <button type="button" className="btn" onClick={() => setAdding(true)}>Add student</button>}
      </div>

      <div className="sheet">
        <div className="actions" style={{ marginBottom: 16 }}>
          <div className="field" style={{ flex: '1 1 280px' }}>
            <label htmlFor="search" className="visually-hidden">Search students</label>
            <input id="search" type="search" placeholder="Search by name, school or parent"
              value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} />
          </div>
          <label className="check">
            <input type="checkbox" checked={includeArchived} onChange={(e) => { setIncludeArchived(e.target.checked); setPage(1); }} />
            Show archived
          </label>
        </div>

        {students.isError ? <ErrorAlert error={students.error} /> : data && data.items.length === 0 ? (
          <p className="empty">
            {deferred ? <><strong>No students match "{deferred}".</strong> Try a different spelling.</>
              : isAdmin ? <strong>No students yet.</strong>
                : <><strong>No students yet.</strong> Add your first student to start booking lessons.</>}
          </p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Name</th><th>Year</th><th>School</th>
                  {isAdmin && <th>Tutor</th>}
                  <th>Next lesson</th><th className="num">Lessons done</th>
                </tr>
              </thead>
              <tbody>
                {data?.items.map((s) => (
                  <tr key={s.id} className={s.isArchived ? 'dim' : undefined}>
                    <td>
                      <Link className="row-link" to={`/students/${s.id}`}>{s.firstName} {s.lastName}</Link>
                      {s.isArchived && <span className="tag"> archived</span>}
                    </td>
                    <td>{s.yearLevel}</td>
                    <td>{s.school ?? '-'}</td>
                    {isAdmin && <td>{s.tutorName}</td>}
                    <td>{s.nextLessonUtc ? fmt.dateTime(s.nextLessonUtc) : <span className="tag">none booked</span>}</td>
                    <td className="num">{s.completedLessons}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {data && data.totalPages > 1 && (
          <div className="actions" style={{ marginTop: 16, justifyContent: 'flex-end' }}>
            <button type="button" className="btn secondary small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
            <span>Page {data.page} of {data.totalPages}</span>
            <button type="button" className="btn secondary small" disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}>Next</button>
          </div>
        )}
      </div>

      <Dialog open={adding} onClose={() => setAdding(false)} title="Add a student">
        {adding && (
          <StudentForm submitLabel="Add student" onCancel={() => setAdding(false)}
            onSubmit={async (input) => {
              const created = await create.mutateAsync(input);
              setAdding(false);
              navigate(`/students/${created.id}`);
            }} />
        )}
      </Dialog>
    </>
  );
}
