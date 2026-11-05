import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ApiError } from '../api/client';
import type { LessonStatus } from '../api/types';

export function Logo({ size = 28 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true">
      <rect width="32" height="32" rx="6" fill="#FCFCFA" />
      <g stroke="#C9D6EA" strokeWidth="1.5"><path d="M4 11h24M4 17h24M4 23h24" /></g>
      <path d="M9 4v24" stroke="#C93C3E" strokeWidth="2" />
      <path d="M14 13l3 3 7-7" fill="none" stroke="#1C2A4A" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/** Native <dialog>: focus trapping, Esc to close and the backdrop come for free. */
export function Dialog({ open, onClose, title, subtitle, children }: {
  open: boolean; onClose: () => void; title: string; subtitle?: ReactNode; children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    if (open && !el.open) el.showModal?.();
    if (!open && el.open) el.close?.();
  }, [open]);

  return (
    <dialog ref={ref} onClose={onClose} aria-labelledby="dialog-title" open={typeof HTMLDialogElement === 'undefined' ? open : undefined}>
      {open && (
        <>
          <div className="dialog-head">
            <div>
              <h2 id="dialog-title">{title}</h2>
              {subtitle && <p>{subtitle}</p>}
            </div>
            <button type="button" className="btn ghost" onClick={onClose} aria-label="Close">✕</button>
          </div>
          <div className="dialog-body">{children}</div>
        </>
      )}
    </dialog>
  );
}

export function Field({ label, error, hint, htmlFor, children, wide }: {
  label: string; error?: string; hint?: string; htmlFor: string; children: ReactNode; wide?: boolean;
}) {
  return (
    <div className={`field${wide ? ' wide' : ''}`}>
      <label htmlFor={htmlFor}>{label}</label>
      {children}
      {error ? <span className="error" id={`${htmlFor}-error`} role="alert">{error}</span>
        : hint ? <span className="hint">{hint}</span> : null}
    </div>
  );
}

export function ErrorAlert({ error }: { error: unknown }) {
  if (!error) return null;
  if (error instanceof ApiError) {
    const fields = Object.values(error.fieldErrors).flat();
    return (
      <div className="alert" role="alert">
        <strong>{error.message}</strong>
        {fields.length > 0 && <ul>{fields.map((f) => <li key={f}>{f}</li>)}</ul>}
      </div>
    );
  }
  return <div className="alert" role="alert">Can't reach TutorDesk. Check your connection and try again.</div>;
}

const STATUS_LABEL: Record<LessonStatus, string> = {
  Scheduled: 'Scheduled', Completed: 'Completed', NoShow: 'No-show', Cancelled: 'Cancelled',
};

export function Status({ status }: { status: LessonStatus }) {
  return <span className={`status ${status}`}>{STATUS_LABEL[status]}</span>;
}

export function Loading({ label = 'Loading' }: { label?: string }) {
  return <div className="center-fill" role="status">{label}…</div>;
}

export function Dots({ value, of = 5 }: { value: number | null; of?: number }) {
  if (value === null) return <span className="tag">no rating</span>;
  return (
    <span className="dots" role="img" aria-label={`${value} out of ${of}`}>
      {Array.from({ length: of }, (_, i) => <i key={i} className={i < value ? 'on' : ''} />)}
    </span>
  );
}

/** Stable colour per subject id, used on the lesson's left edge. */
export const subjectColor = (subjectId: number) => `var(--subj-${((subjectId - 1) % 8) + 1})`;

export function useToast() {
  const [message, setMessage] = useState<string | null>(null);
  useEffect(() => {
    if (!message) return;
    const t = setTimeout(() => setMessage(null), 3500);
    return () => clearTimeout(t);
  }, [message]);
  const node = message ? <div className="toast" role="status">{message}</div> : null;
  return { show: setMessage, node };
}
