import { useEffect, useRef } from 'react';

interface Props {
  open: boolean;
  /** Email address or URL for corrections and data-protection requests (Ui:PrivacyContact); null until set. */
  contact: string | null | undefined;
  onClose: () => void;
}

/**
 * Plain-English privacy notice, mainly for the named executives and directors whose published pay the site shows
 * (UK GDPR applies to UK directors even though the figures are public). Also covers what the site does with visitors.
 */
export function PrivacyNotice({ open, contact, onClose }: Props) {
  const close = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    close.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);
  if (!open) return null;

  const contactLink = contact
    ? contact.includes('@') ? <a className="linkbtn" href={`mailto:${contact}`}>{contact}</a> : <a className="linkbtn" href={contact} target="_blank" rel="noreferrer">{contact}</a>
    : null;

  return (
    <div className="modal" role="dialog" aria-modal="true" aria-labelledby="privacyTitle" onClick={onClose}>
      <div className="modal-card pane" onClick={e => e.stopPropagation()}>
        <button ref={close} className="iconbtn close" onClick={onClose} aria-label="Close privacy notice">✕</button>
        <h2 id="privacyTitle">Privacy notice</h2>

        <h3>What we show</h3>
        <p>
          Financial figures of public companies, and the names, roles and pay of their executives and directors — exactly as the
          companies publish them in their legally required filings: SEC filings (10-K, 10-Q, 40-F, proxy statements) for US and Canadian companies and UK annual
          reports (the directors' remuneration report). We don't collect anything else about these people: no contact details,
          no addresses, nothing that isn't in those public reports.
        </p>

        <h3>Why</h3>
        <p>
          To help people understand the public companies around them. Companies are required by law to publish this pay
          information so that it is open to scrutiny; we rely on our legitimate interest in making that published information easier
          to find and compare.
        </p>

        <h3>Accuracy</h3>
        <p>
          The figures are collected and read automatically by software built with AI tools, so some may be missing or wrong. Every
          figure comes from a named filing, which is the authoritative source.
        </p>

        <h3>If this is about you</h3>
        <p>
          You can ask us to correct a figure, to explain what we hold about you, or object to it being shown. Under UK and EU data
          protection law (UK GDPR / GDPR) you also have the right to complain to your data-protection authority — in the UK that is
          the <a className="linkbtn" href="https://ico.org.uk/make-a-complaint/" target="_blank" rel="noreferrer">Information Commissioner's Office</a>.
        </p>
        <p>{contactLink ? <>Contact: {contactLink}</> : <>A contact address for these requests is being set up.</>}</p>

        <h3>Visitors</h3>
        <p>
          No ads, no tracking cookies, and nothing is shared with other companies. With your consent we keep one small cookie that
          remembers your view and theme; "Just this visit" stores nothing. Every page also sets a short-lived security cookie that
          lets it load data from our servers (so other sites and bots can't); it holds only its expiry time, nothing about you.
        </p>
        <p>
          To see how the site is used, we count visits on our own servers: which areas are searched (the search point rounded to
          about 1 km, or the postcode typed), which companies and executives are opened, a few clicks, the type of device and browser,
          the website that linked here, and the approximate city and country our network provider estimates from your connection.
          We don't store your IP address or anything that identifies you: each visit gets a code that changes every day and can't be
          traced back to you or linked to another day.
        </p>
      </div>
    </div>
  );
}
