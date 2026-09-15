import { useEffect, useRef } from 'react';

interface Props {
  open: boolean;
  /** Email address for corrections (Ui:PrivacyContact). */
  contact: string | null | undefined;
  onClose: () => void;
}

/** Where every number comes from, what it means, its limits, and where to check the original filing. */
export function AboutData({ open, contact, onClose }: Props) {
  const close = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    close.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);
  if (!open) return null;

  return (
    <div className="modal" role="dialog" aria-modal="true" aria-labelledby="aboutDataTitle" onClick={onClose}>
      <div className="modal-card pane" onClick={e => e.stopPropagation()}>
        <button ref={close} className="iconbtn close" onClick={onClose} aria-label="Close">✕</button>
        <h2 id="aboutDataTitle">About the data</h2>

        <h3>Where the numbers come from</h3>
        <ul className="sources">
          <li><b>US, Canada and Australia</b> — companies' filings with the US Securities and Exchange Commission:
            revenue and net income from the XBRL data in annual and quarterly reports (10-K, 10-Q, 40-F, 20-F), executive pay from
            the summary compensation table in proxy statements (DEF 14A). <Ext href="https://www.sec.gov/search-filings/edgar-application-programming-interfaces">SEC EDGAR data</Ext></li>
          <li><b>UK and Europe</b> — annual reports in the European Single Electronic Format (ESEF), indexed
            by <Ext href="https://filings.xbrl.org/">filings.xbrl.org</Ext>; UK directors' pay from the single total figure table in the directors' remuneration report.</li>
          <li><b>Headquarters and tickers</b> — <Ext href="https://www.gleif.org/en">GLEIF</Ext> legal entity records and <Ext href="https://www.openfigi.com/">OpenFIGI</Ext>.</li>
          <li><b>Places</b> — US ZIP code centres from the <Ext href="https://www.census.gov/geographies/reference-files/time-series/geo/gazetteer-files.html">US Census Bureau Gazetteer</Ext>;
            postcode and place names © <Ext href="https://www.geonames.org/">GeoNames</Ext> (CC BY 4.0).</li>
        </ul>

        <h3>What the figures mean</h3>
        <ul className="sources">
          <li><b>Revenue and net income</b> are as the company reported them, in its own currency, not adjusted for inflation. "Annual revenue" is the last
            twelve months where quarterly figures exist, otherwise the last financial year.</li>
          <li><b>Totals across currencies</b> (the "≈" figures) use approximate exchange rates and are only for adding up and ranking.</li>
          <li><b>Executive pay</b> is reported compensation: salary, bonus, stock and option awards at their grant-date value, and other pay. It isn't what
            anyone took home — awards can later be worth much more or less. <Ext href="https://www.investor.gov/introduction-investing/investing-basics/glossary/executive-compensation">What executive compensation disclosures include</Ext></li>
          <li><b>"Near you"</b> means a company has a headquarters or office within the distance you chose, measured in a straight line from the centre of your ZIP or postcode area.</li>
        </ul>

        <h3>Limits</h3>
        <p>
          Everything is collected and read automatically by software built with AI tools, and checked by automated tests — but automated reading
          can still get things wrong or miss them. Coverage is incomplete: listed companies that don't file in these formats are missing (most of
          Germany, most Australian and New Zealand companies), and executive pay isn't collected for Europe yet. Always check the original filing
          before relying on a number. This is not financial advice.
        </p>

        <h3>Check the original filing</h3>
        <ul className="sources">
          <li>US: <Ext href="https://www.sec.gov/edgar/search/">SEC EDGAR full-text search</Ext> · <Ext href="https://www.investor.gov/introduction-investing/investing-basics/glossary/form-10-k">What's in a 10-K</Ext> · <Ext href="https://www.investor.gov/introduction-investing/investing-basics/glossary/proxy-statements">What's in a proxy statement</Ext></li>
          <li>UK: <Ext href="https://data.fca.org.uk/#/nsm/nationalstoragemechanism">FCA National Storage Mechanism</Ext> · <Ext href="https://find-and-update.company-information.service.gov.uk/">Companies House</Ext></li>
          <li>Canada: <Ext href="https://www.sedarplus.ca/">SEDAR+</Ext> · Australia: <Ext href="https://www.asx.com.au/markets/trade-our-cash-market/announcements">ASX announcements</Ext> · Europe: <Ext href="https://filings.xbrl.org/">filings.xbrl.org</Ext></li>
          <li>New to company research? <Ext href="https://www.investor.gov/introduction-investing/getting-started/researching-investments">Researching investments (Investor.gov)</Ext></li>
        </ul>

        <h3>Corrections</h3>
        <p>
          Spotted a wrong number? {contact ? <>Email <a className="linkbtn" href={`mailto:${contact}`}>{contact}</a> with the company and the figure</> : 'Tell us'} —
          every figure on a company's page links to the filing it came from.
        </p>
        <p className="fine">
          CompanyPaisa is independent and isn't affiliated with, or endorsed by, any company, exchange or regulator named here. Company names and
          trademarks belong to their owners.
        </p>
      </div>
    </div>
  );
}

function Ext({ href, children }: { href: string; children: React.ReactNode }) {
  return <a className="linkbtn" href={href} target="_blank" rel="noreferrer">{children}</a>;
}
