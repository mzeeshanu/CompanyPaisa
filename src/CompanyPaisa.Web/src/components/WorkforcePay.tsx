import { useEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import { api } from '../api/client';
import type { CompanyDetail, JobSalariesResponse, JobSalary, JobSalarySet, WorkerPay } from '../api/types';
import { money } from '../lib/format';
import { Filing } from './Filing';
import { companyPath, Link } from '../lib/router';
import { milesBetween, PageBack, type Nearby } from './PageShell';

/** How long the CEO takes to earn the median employee's year: "9 days", "14 hours". */
function catchUp(ratio: number): string {
  const days = 365 / ratio;
  if (days >= 1.5) return `${Math.round(days)} days`;
  const hours = (24 * 365) / ratio;
  return hours >= 1.5 ? `${Math.round(hours)} hours` : `${Math.max(1, Math.round(hours * 60))} minutes`;
}

const ratioText = (r: number) => (r >= 10 ? Math.round(r) : Math.round(r * 10) / 10).toLocaleString();

/** The CEO next to the company's median employee, as the company disclosed it (US proxy statements). */
export function PayRatioCard({ pay }: { pay: WorkerPay[] }) {
  const latest = pay[pay.length - 1];
  const earlier = pay.slice(0, -1).slice(-3);
  return (
    <section className="pane page-card ratio-card" aria-labelledby="ratio-h">
      <h2 className="subh" id="ratio-h">CEO vs the typical employee</h2>
      <div className="ratio-top">
        <div className="ratio-big num">{ratioText(latest.ratio)}<small>×</small></div>
        <p>
          In {latest.year} the CEO was paid <b className="num">{money(latest.ceoPay, latest.currency)}</b>, {ratioText(latest.ratio)} times the{' '}
          <b className="num">{money(latest.medianEmployeePay, latest.currency)}</b> the company's median employee earned. The CEO made the median
          employee's yearly pay in about <b>{catchUp(latest.ratio)}</b>.
        </p>
      </div>
      <div className="ratio-bars" aria-hidden="true">
        <div><span>Median employee</span><i style={{ width: `${Math.max(0.6, 100 / latest.ratio)}%` }} /></div>
        <div><span>CEO</span><i className="ceo" style={{ width: '100%' }} /></div>
      </div>
      {earlier.length > 0 && (
        <p className="fine">
          Earlier: {earlier.map(p => `${p.year} ${ratioText(p.ratio)}×`).join(' · ')}
        </p>
      )}
      <p className="fine">
        The company's own figures (its <Filing href={latest.sourceFiling}>proxy statement</Filing>). The median employee is the middle
        of all its employees worldwide, part-time included.
      </p>
    </section>
  );
}

/** Titles listed before "Show all". */
const SHOWN_TITLES = 12;

/** How each source is described: its switch label, what a count is, the intro, and its caveat. */
const SOURCES: Record<JobSalarySet['kind'], { tab: string; unit: string; intro: (name: string) => string; how: string; note: string }> = {
  postings: {
    tab: 'Job ads', unit: 'ad',
    intro: name => `The pay ranges ${name} advertised in its US job ads`,
    how: 'the typical middle of the advertised range, and the bar spans its typical bottom to top',
    note: "Source: the company's own job ads (pay-transparency laws in states such as California, Colorado, New York and Washington require a pay range). Advertised base pay, before bonuses and stock.",
  },
  h1b: {
    tab: 'Visa filings', unit: 'filing',
    intro: name => `What ${name} offered in its US work-visa (H-1B) wage filings`,
    how: 'the median yearly salary, and the bar spans the middle half of the offers',
    note: 'Source: US Department of Labor, H-1B labor condition applications. They cover jobs the company hired from abroad for, mostly professional roles, and state the salary it committed to pay; a job shows only with at least three filings.',
  },
};

/**
 * What a company pays, on its own page (/company/AAPL/salaries): every job title, from every source. The page people
 * reach from a search like "Apple software engineer salary".
 */
export function SalariesPage({ ticker, from, onLoaded }: { ticker: string; from: Nearby | null; onLoaded: (about: string) => void }) {
  const [company, setCompany] = useState<CompanyDetail | null>(null);
  const [missing, setMissing] = useState(false);
  const loaded = useRef(onLoaded);
  loaded.current = onLoaded;

  useEffect(() => {
    let cancelled = false;
    setCompany(null); setMissing(false);
    api.company(ticker).then(c => {
      if (cancelled) return;
      setCompany(c);
      document.title = `${c.name} (${c.ticker}) salaries by job title · CompanyPaisa`;
      loaded.current(`salaries at ${c.name}`);
    }, () => { if (!cancelled) setMissing(true); });
    return () => { cancelled = true; };
  }, [ticker]);

  if (missing) return (
    <main className="wrap page">
      <PageBack from={from} />
      <section className="pane page-card page-missing"><h1>Company not found</h1><Link className="primary" to="/">Find public companies near you</Link></section>
    </main>
  );
  if (!company) return <main className="wrap page"><PageBack from={from} /><p className="fine page-loading">Loading…</p></main>;

  return (
    <main className="wrap page">
      <PageBack from={from} />
      <section className="pane page-card page-head">
        <div className="p-chips"><span className="chip tk">{company.ticker}</span><span className="chip">{company.sector}</span></div>
        <h1>Salaries at {company.name}</h1>
        <p className="p-loc">What this company pays, by job title — from its own US job ads and its work-visa wage filings.</p>
        <p className="page-facts">
          <Link className="linkbtn" to={companyPath(company.ticker)}>Revenue, profit and executive pay →</Link>
          {company.careersUrl && <a className="linkbtn" href={company.careersUrl} target="_blank" rel="noreferrer">Careers ↗</a>}
        </p>
      </section>
      <JobSalaries ticker={company.ticker} name={company.name} from={from} startAll showEmpty />
    </main>
  );
}

/** Yearly salaries by job title: from the company's job ads, and from its US work-visa (H-1B) wage filings. */
export function JobSalaries({ ticker, name, from, startAll = false, showEmpty = false }:
  { ticker: string; name: string; from: Nearby | null; startAll?: boolean; showEmpty?: boolean }) {
  const [data, setData] = useState<JobSalariesResponse | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState('');
  const [all, setAll] = useState(startAll);
  const [open, setOpen] = useState<string | null>(null);
  const [kind, setKind] = useState<JobSalarySet['kind'] | null>(null);

  useEffect(() => {
    let cancelled = false;
    setData(null); setFailed(false); setFilter(''); setAll(startAll); setOpen(null); setKind(null);
    api.salaries(ticker).then(r => { if (!cancelled) setData(r); }, () => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, [ticker, startAll]);

  // Job ads first (the API orders them): they cover every kind of job the company is hiring for.
  const set = data?.sources.find(s => s.kind === kind) ?? data?.sources[0] ?? null;
  const titles = useMemo(() => {
    if (!set) return [];
    const words = filter.toLowerCase().split(/\s+/).filter(Boolean);
    return words.length === 0 ? set.titles
      : set.titles.filter(t => words.every(w => `${t.title} ${t.occupation ?? ''}`.toLowerCase().includes(w)));
  }, [set, filter]);

  // Someone can arrive at a company we have no salaries for (searching "<company> salaries"): say so rather than nothing.
  const nothing = showEmpty ? (
    <section className="pane page-card">
      <h2 className="subh">Salaries by job title</h2>
      <p className="fine">
        We don't have salaries for {name} yet. They come from a company's own US job ads and its work-visa wage filings, which not every
        company has. <Link className="linkbtn" to={companyPath(ticker)}>See its revenue, profit and executive pay →</Link>
      </p>
    </section>
  ) : null;

  if (!data) return failed ? nothing : <section className="pane page-card"><h2 className="subh">Salaries by job title</h2><p className="fine">Loading…</p></section>;
  if (!set || set.titles.length === 0) return nothing;

  const about = SOURCES[set.kind];
  const shown = all || filter ? titles : titles.slice(0, SHOWN_TITLES);
  const lo = d3.min(shown, t => t.low) ?? 0, hi = d3.max(shown, t => t.high) ?? 1;
  const x = d3.scaleLinear().domain([lo, hi === lo ? lo + 1 : hi]).range([0, 100]);
  const choose = (k: JobSalarySet['kind']) => { setKind(k); setOpen(null); setAll(false); setFilter(''); };

  return (
    <section className="pane page-card jobs-card" aria-labelledby="jobs-h">
      <div className="seg">
        <h2 className="subh" id="jobs-h">Salaries by job title</h2>
        {set.titles.length > 8 && (
          <input className="searchbox" type="search" placeholder={`Search ${set.titles.length.toLocaleString()} jobs`} value={filter}
            onChange={e => setFilter(e.target.value)} aria-label="Search job titles" />
        )}
      </div>
      {data.sources.length > 1 && (
        <div className="opts job-sources" role="group" aria-label="Where the salaries come from">
          {data.sources.map(s => (
            <button key={s.kind} aria-pressed={s.kind === set.kind} onClick={() => choose(s.kind)}>
              {SOURCES[s.kind].tab}<small className="num">{s.titles.length.toLocaleString()}</small>
            </button>
          ))}
        </div>
      )}
      <p className="fine">
        {about.intro(name)}, {monthYear(set.from) === monthYear(set.to) ? `in ${monthYear(set.to)}` : `${monthYear(set.from)} – ${monthYear(set.to)}`}: {about.how}. Tap a job to see it by city.
      </p>
      <ul className="jobs">
        {shown.map(t => (
          <li key={t.title} className={open === t.title ? 'open' : ''}>
            <button className="job" onClick={() => setOpen(o => (o === t.title ? null : t.title))} aria-expanded={open === t.title}>
              <span className="job-name">
                <b>{t.title}</b>
                <small>{t.filings.toLocaleString()} {about.unit}{t.filings === 1 ? '' : 's'}{t.occupation && t.occupation.toLowerCase() !== t.title.toLowerCase() ? ` · ${t.occupation}` : ''}</small>
              </span>
              <span className="job-pay num">{money(t.median, data.currency)}</span>
              <span className="job-range" title={`${money(t.low, data.currency)} – ${money(t.high, data.currency)}`}>
                <i style={{ left: `${x(t.low)}%`, width: `${Math.max(1.5, x(t.high) - x(t.low))}%` }} />
                <b style={{ left: `${x(t.median)}%` }} />
              </span>
            </button>
            {open === t.title && <Places job={t} currency={data.currency} from={from} unit={about.unit} />}
            {open === t.title && t.url && <a className="linkbtn job-ad" href={t.url} target="_blank" rel="noreferrer">See a current ad ↗</a>}
          </li>
        ))}
      </ul>
      {filter && titles.length === 0 && <p className="fine">No job title matches “{filter}”.</p>}
      {!filter && titles.length > SHOWN_TITLES && (
        <button className="linkbtn page-more" onClick={() => setAll(a => !a)}>{all ? 'Show fewer' : `Show all ${titles.length.toLocaleString()} jobs`}</button>
      )}
      <p className="fine">{about.note}</p>
    </section>
  );
}

function Places({ job, currency, from, unit }: { job: JobSalary; currency: string; from: Nearby | null; unit: string }) {
  if (job.places.length === 0) return (
    <p className="fine job-places-none">
      {unit === 'ad' ? 'The ads name no single city (remote, or many places).' : 'Filed in several places, none with three filings of its own.'}{' '}
      Range across all: {money(job.min, currency)} – {money(job.max, currency)}.
    </p>
  );
  const distance = (p: JobSalary['places'][number]) =>
    from && p.latitude != null && p.longitude != null ? milesBetween(from.point, { latitude: p.latitude, longitude: p.longitude }) : null;
  const places = [...job.places].sort((a, b) => (from ? (distance(a) ?? 1e9) - (distance(b) ?? 1e9) : b.filings - a.filings));
  return (
    <ul className="job-places">
      {places.map(p => {
        const miles = distance(p);
        return (
          <li key={`${p.city}|${p.state}`}>
            <span>{p.city}, {p.state}{miles != null && <small className="num"> · {miles.toFixed(0)} mi</small>}</span>
            <span className="num">{money(p.median, currency)}</span>
            <small className="num">{money(p.low, currency)} – {money(p.high, currency)} · {p.filings} {unit}{p.filings === 1 ? '' : 's'}</small>
          </li>
        );
      })}
    </ul>
  );
}

const monthYear = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
