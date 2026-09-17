import { useEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import { api, ApiError } from '../api/client';
import type { ExecutiveDetail } from '../api/types';
import { money, pct, tone } from '../lib/format';
import { companyPath, Link } from '../lib/router';
import { initials } from './ExecutivesView';
import { Filing } from './Filing';
import { PageBack, type Nearby } from './PageShell';

interface Props {
  personId: string;
  from: Nearby | null;
  onLoaded: (about: string) => void;
}

// One colour per company in a person's career (the most recent company gets the accent).
const COMPANY_COLORS = ['var(--s1)', 'var(--s3)', 'var(--flat)', 'var(--s2)', 'var(--up)', 'var(--down)'];

/** One person's page (/executive/dylan-field-2073586): pay history across every company they were a named executive at. */
export function ExecutivePage({ personId, from, onLoaded }: Props) {
  const [data, setData] = useState<ExecutiveDetail | null>(null);
  const [error, setError] = useState<{ notFound: boolean; message: string } | null>(null);
  const loadedFor = useRef(onLoaded);
  loadedFor.current = onLoaded;

  useEffect(() => {
    let cancelled = false;
    setError(null);
    api.executive(personId)
      .then(p => {
        if (cancelled) return;
        setData(p);
        document.title = `${p.name}, ${p.currentTitle} at ${p.currentCompany.name} — pay history · CompanyPaisa`;
        loadedFor.current(`${p.name} (executive)`);
      })
      .catch(e => {
        if (cancelled) return;
        const notFound = e instanceof ApiError && e.status === 404;
        setError({ notFound, message: notFound ? "We don't have this executive. The link may be out of date." : 'Could not load this executive. Please try again.' });
        document.title = 'CompanyPaisa';
      });
    return () => { cancelled = true; };
  }, [personId]);

  const d = data?.personId === personId ? data : null;

  const colorOf = useMemo(() => {
    const order = d ? [...new Set(d.roles.map(r => r.company.ticker))] : [];
    return (ticker: string) => COMPANY_COLORS[Math.max(0, order.indexOf(ticker)) % COMPANY_COLORS.length];
  }, [d]);

  if (error) return (
    <main className="wrap page">
      <PageBack from={from} />
      <section className="pane page-card page-missing">
        <h1>{error.notFound ? 'Executive not found' : 'Something went wrong'}</h1>
        <p>{error.message}</p>
        <Link className="primary" to="/">Find public companies near you</Link>
      </section>
    </main>
  );
  const latest = d?.history[0];
  if (!d || !latest) return <main className="wrap page"><PageBack from={from} /><p className="fine page-loading">Loading…</p></main>;

  const previous = d.history.find(h => h.year === latest.year - 1);
  const latestYearTotal = d.history.filter(h => h.year === latest.year).reduce((s, h) => s + h.total, 0);
  const prevYearTotal = previous ? d.history.filter(h => h.year === previous.year).reduce((s, h) => s + h.total, 0) : 0;
  const change = prevYearTotal > 0 ? latestYearTotal / prevYearTotal - 1 : null;
  const companies = new Set(d.roles.map(r => r.company.ticker)).size;
  const cur = d.currentCompany.currency ?? 'USD';
  const uk = d.roles.every(r => r.company.ticker.endsWith('.L'));

  return (
    <main className="wrap page">
      <PageBack from={from} />

      <section className="pane page-card page-head">
        <div className="p-chips">
          <span className="chip tk">{d.currentCompany.ticker}</span>
          <span className="chip">{d.currentCompany.sector}</span>
          {d.secCik && <span className="chip">SEC CIK {d.secCik}</span>}
        </div>
        <div className="person-head">
          <span className="avatar big" aria-hidden="true">{initials(d.name)}</span>
          <div>
            <h1>{d.name}</h1>
            <p className="p-loc">{d.currentTitle} · <Link className="linkbtn" to={companyPath(d.currentCompany.ticker)}>{d.currentCompany.name}</Link></p>
          </div>
        </div>

        <div className="kpis page-kpis">
          <Kpi k="Latest pay" s={`${latest.year} total`} v={money(latestYearTotal, cur)} />
          <Kpi k="Change" s={`vs ${latest.year - 1}`} v={pct(change)} cls={tone(change)} />
          <Kpi k="Total earned" s={`${d.firstYear}–${d.latestYear}`} v={money(d.totalPay, cur)} />
          <Kpi k="Career" s={uk ? 'as an executive director' : 'as a named executive'} v={`${companies} ${companies === 1 ? 'company' : 'companies'}`} />
        </div>
        <p className="note page-note">{uk
          ? "Each year's \"single total figure\" from the company's directors' remuneration report (salary, bonus, long-term share awards as they vest, benefits and pension)."
          : "Reported compensation (salary, bonus, stock awards at grant value, other) from each company's proxy filings."}</p>
      </section>

      <div className="page-grid">
        <section className="pane page-card" aria-labelledby="pay-h">
          <h2 className="subh" id="pay-h">Pay by year</h2>
          <div className="keys">
            {[...new Map(d.roles.map(r => [r.company.ticker, r.company])).values()].map(c => (
              <span key={c.ticker}><i style={{ background: colorOf(c.ticker) }} />{c.name}</span>
            ))}
          </div>
          <PayByYearChart detail={d} colorOf={colorOf} currency={cur} />
        </section>

        <section className="pane page-card" aria-labelledby="career-h">
          <h2 className="subh" id="career-h">Career</h2>
          <ol className="career">
            {d.roles.map(r => (
              <li key={`${r.company.ticker}-${r.fromYear}`}>
                <i style={{ background: colorOf(r.company.ticker) }} />
                <div>
                  <b>{r.title}</b>
                  <Link className="linkbtn" to={companyPath(r.company.ticker)}>{r.company.name} ({r.company.ticker})</Link>
                  <small>{r.fromYear === r.toYear ? r.fromYear : `${r.fromYear}–${r.toYear}`}</small>
                </div>
                <span className="num">{money(r.totalPay, r.company.currency)}</span>
              </li>
            ))}
          </ol>
        </section>
      </div>

      <section className="pane page-card" aria-labelledby="years-h">
        <h2 className="subh" id="years-h">Year by year</h2>
        <div className="tablewrap">
          <table>
            <thead><tr><th>Year</th><th>Company</th><th>Salary</th><th>Bonus</th><th>Stock</th><th>Other</th><th>Total</th></tr></thead>
            <tbody>
              {d.history.map(h => (
                <tr key={`${h.year}-${h.company.ticker}`}>
                  <td><Filing href={h.sourceFiling}>{h.year}</Filing></td>
                  <td className="co"><i style={{ background: colorOf(h.company.ticker) }} /><Link className="linkbtn" to={companyPath(h.company.ticker)}>{h.company.ticker}</Link></td>
                  <td>{money(h.salary, h.company.currency)}</td><td>{money(h.bonus, h.company.currency)}</td><td>{money(h.stockAwards, h.company.currency)}</td>
                  <td>{money(h.other, h.company.currency)}</td><td><b>{money(h.total, h.company.currency)}</b></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <p className="disclaimer page-source">
        {uk
          ? 'Only years this person was an executive director of a FTSE 350 company in our data appear here; UK careers are not yet linked across companies.'
          : "Only companies where this person was a named executive officer of a public company appear here. Earlier or private-company roles aren't reported to the SEC."}
      </p>
    </main>
  );
}

function Kpi({ k, s, v, cls = '' }: { k: string; s: string; v: string; cls?: string }) {
  return <div className="kpi"><span className="k">{k}</span><span className="s">{s}</span><span className={`v ${cls}`}>{v}</span></div>;
}

/** Stacked bars: total pay per year, one segment per company paid that year. */
function PayByYearChart({ detail, colorOf, currency }: { detail: ExecutiveDetail; colorOf: (t: string) => string; currency: string }) {
  const host = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(400);
  useEffect(() => {
    const ro = new ResizeObserver(([e]) => setW(Math.max(260, e.contentRect.width)));
    if (host.current) ro.observe(host.current);
    return () => ro.disconnect();
  }, []);

  const years = d3.range(detail.firstYear, detail.latestYear + 1);
  const byYear = years.map(y => ({ year: y, parts: detail.history.filter(h => h.year === y) }));
  const h = 190, m = { t: 10, r: 8, b: 24, l: 54 };
  const x = d3.scaleBand<number>().domain(years).range([m.l, w - m.r]).padding(0.28);
  const max = d3.max(byYear, b => d3.sum(b.parts, p => p.total)) ?? 1;
  const y = d3.scaleLinear().domain([0, max]).nice(4).range([h - m.b, m.t]);
  const every = years.length > 8 ? 2 : 1;

  return (
    <div className="chart" ref={host}>
      <svg viewBox={`0 0 ${w} ${h}`} width="100%" height={h} role="img" aria-label="Total pay by year, coloured by company">
        {y.ticks(4).map(t => (
          <g key={t}>
            <line className={t === 0 ? 'zero' : 'grid'} x1={m.l} x2={w - m.r} y1={y(t)} y2={y(t)} />
            <text className="axis" x={m.l - 8} y={y(t)} dy=".32em" textAnchor="end">{t === 0 ? '0' : money(t, currency)}</text>
          </g>
        ))}
        {byYear.map(b => {
          let base = 0;
          return b.parts.map(p => {
            const y0 = base; base += p.total;
            return (
              <rect key={`${b.year}-${p.company.ticker}`} x={x(b.year)} width={x.bandwidth()} rx={2}
                y={y(base)} height={Math.max(0, y(y0) - y(base))} style={{ fill: colorOf(p.company.ticker) }}>
                <title>{`${b.year} · ${p.company.name}: ${money(p.total, p.company.currency)} (${p.title})`}</title>
              </rect>
            );
          });
        })}
        {years.filter((_, i) => (years.length - 1 - i) % every === 0).map(yr => (
          <text key={yr} className="axis" x={x(yr)! + x.bandwidth() / 2} y={h - 7} textAnchor="middle">{`'${String(yr).slice(2)}`}</text>
        ))}
      </svg>
    </div>
  );
}
