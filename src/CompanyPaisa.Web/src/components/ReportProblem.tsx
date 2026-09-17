import { useEffect, useRef, useState } from 'react';
import { track } from '../lib/analytics';

interface Props {
  /** Where reports go (Ui:PrivacyContact); the button hides when there's none. */
  contact: string | null | undefined;
  /** What the visitor is looking at, e.g. "Apple Inc. (AAPL)" or "companies near Lehi, UT". */
  about: string;
}

const KINDS = ['Wrong number', 'Missing company', 'Wrong location', 'Something else'] as const;

/**
 * A small flag in the bottom-left corner that opens a compact "report a problem" card. Sending opens the visitor's
 * email app with the details filled in — nothing is stored by the site.
 */
export function ReportProblem({ contact, about }: Props) {
  const [open, setOpen] = useState(false);
  const [kind, setKind] = useState<(typeof KINDS)[number]>('Wrong number');
  const [details, setDetails] = useState('');
  const card = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    const onDown = (e: PointerEvent) => { if (!card.current?.parentElement?.contains(e.target as Node)) setOpen(false); };
    window.addEventListener('keydown', onKey);
    window.addEventListener('pointerdown', onDown);
    return () => { window.removeEventListener('keydown', onKey); window.removeEventListener('pointerdown', onDown); };
  }, [open]);

  if (!contact) return null;

  const subject = `CompanyPaisa: ${kind} — ${about}`;
  const body = `What's wrong: ${kind}\nAbout: ${about}\nPage: ${location.href}\n\n${details}`;
  const href = `mailto:${contact}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;

  return (
    <div className="report">
      {open && (
        <div ref={card} className="report-card pane" role="dialog" aria-label="Report a problem">
          <b>Report a problem</b>
          <div className="report-kinds" role="group" aria-label="What's wrong?">
            {KINDS.map(k => <button key={k} aria-pressed={kind === k} onClick={() => setKind(k)}>{k}</button>)}
          </div>
          <textarea rows={3} placeholder="Details (optional) — e.g. the figure and what the filing says"
            value={details} onChange={e => setDetails(e.target.value)} />
          <span className="fine">About: {about}</span>
          <a className="report-send" href={href} onClick={() => setOpen(false)}>Email it</a>
          <span className="fine">Opens your email app. We only use it to check and fix the data.</span>
        </div>
      )}
      <button className="report-btn pane" aria-expanded={open} aria-label="Report a problem with the data" title="Report a problem"
        onClick={() => { if (!open) track('report_open'); setOpen(o => !o); }}>
        <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.5 14.5V2.2M3.5 2.5h8.2l-1.9 3 1.9 3H3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" /></svg>
      </button>
    </div>
  );
}
