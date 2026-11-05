import { useState } from 'react';
import { download } from '../../api/client';
import { useMonthlyReport } from '../../api/hooks';
import { ErrorAlert, Loading, subjectColor } from '../../components/ui';
import { hours, nowInAdelaide, percent } from '../../lib/time';

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export function ReportPage() {
  const now = nowInAdelaide();
  const [ym, setYm] = useState({ year: now.getFullYear(), month: now.getMonth() + 1 });
  const report = useMonthlyReport(ym.year, ym.month);
  const [downloadError, setDownloadError] = useState<unknown>(null);

  const shift = (delta: number) => {
    const d = new Date(ym.year, ym.month - 1 + delta, 1);
    setYm({ year: d.getFullYear(), month: d.getMonth() + 1 });
  };
  const isCurrent = ym.year === now.getFullYear() && ym.month === now.getMonth() + 1;
  const r = report.data;
  const maxDaily = Math.max(1, ...(r?.daily.map((d) => d.hoursCompleted) ?? [0]));
  const maxSubject = Math.max(1, ...(r?.subjects.map((s) => s.hoursCompleted) ?? [0]));

  return (
    <>
      <div className="page-head">
        <div>
          <h1>{MONTHS[ym.month - 1]} {ym.year}</h1>
          <p>Completed lessons by tutor and subject, in Adelaide time{isCurrent ? '. Month in progress' : ''}</p>
        </div>
        <div className="actions">
          <button type="button" className="btn secondary small" onClick={() => shift(-1)} aria-label="Previous month">‹</button>
          <button type="button" className="btn secondary small" onClick={() => shift(1)} aria-label="Next month">›</button>
          <button type="button" className="btn" onClick={() => {
            setDownloadError(null);
            download(`/api/admin/reports/monthly.csv?year=${ym.year}&month=${ym.month}`,
              `tutordesk-${ym.year}-${String(ym.month).padStart(2, '0')}.csv`).catch(setDownloadError);
          }}>Download CSV</button>
        </div>
      </div>
      <ErrorAlert error={downloadError} />

      {report.isLoading ? <Loading /> : report.isError || !r ? <ErrorAlert error={report.error} /> : (
        <>
          <div className="figures">
            <div className="figure"><b>{hours(r.totals.hoursCompleted)}</b><span>taught</span></div>
            <div className="figure"><b>{r.totals.lessonsCompleted}</b><span>lessons completed</span></div>
            <div className="figure"><b>{r.totals.studentsTaught}</b><span>students taught</span></div>
            <div className="figure"><b>{percent(r.totals.attendanceRate)}</b><span>attendance</span></div>
            <div className="figure"><b>{r.totals.cancelled}</b><span>cancelled</span></div>
          </div>

          <section className="sheet" aria-labelledby="tutors-h">
            <h2 id="tutors-h">By tutor</h2>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Tutor</th><th className="num">Hours</th><th className="num">Completed</th><th className="num">No-shows</th>
                    <th className="num">Cancelled</th><th className="num">Still scheduled</th><th className="num">Students</th><th className="num">Attendance</th>
                  </tr>
                </thead>
                <tbody>
                  {r.tutors.map((t) => (
                    <tr key={t.tutorId} className={t.isActive ? undefined : 'dim'}>
                      <td><strong>{t.tutorName}</strong>{!t.isActive && <span className="tag"> deactivated</span>}</td>
                      <td className="num">{hours(t.hoursCompleted)}</td>
                      <td className="num">{t.lessonsCompleted}</td>
                      <td className="num">{t.noShows}</td>
                      <td className="num">{t.cancelled}</td>
                      <td className="num">{t.stillScheduled}</td>
                      <td className="num">{t.studentsTaught}</td>
                      <td className="num">{percent(t.attendanceRate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <div className="grid-2" style={{ marginTop: 24 }}>
            <section className="sheet" aria-labelledby="daily-h">
              <h2 id="daily-h">Hours taught each day</h2>
              <div className="bars" role="img" aria-label={`Daily hours, highest ${hours(maxDaily)}`}>
                {r.daily.map((d) => (
                  <div key={d.date} className={d.hoursCompleted === 0 ? 'zero' : undefined}
                    style={{ height: `${(d.hoursCompleted / maxDaily) * 100}%` }}
                    title={`${d.date}: ${hours(d.hoursCompleted)}`} />
                ))}
              </div>
              <div className="bars-axis"><span>1</span><span>{Math.ceil(r.daily.length / 2)}</span><span>{r.daily.length}</span></div>
            </section>
            <section className="sheet" aria-labelledby="subjects-h">
              <h2 id="subjects-h">By subject</h2>
              {r.subjects.length === 0 ? <p className="empty"><strong>No completed lessons this month.</strong></p> :
                r.subjects.map((s) => (
                  <div key={s.subjectId} className="hbar" style={{ ['--subj' as string]: subjectColor(s.subjectId) }}>
                    <span>{s.subjectName}</span>
                    <span className="track"><span style={{ width: `${(s.hoursCompleted / maxSubject) * 100}%` }} /></span>
                    <span className="num">{hours(s.hoursCompleted)}</span>
                  </div>
                ))}
            </section>
          </div>
        </>
      )}
    </>
  );
}
