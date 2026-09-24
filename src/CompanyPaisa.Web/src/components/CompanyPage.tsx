import { useEffect, useRef, useState } from 'react';
import * as d3 from 'd3';
import { api, ApiError } from '../api/client';
import type { CompanyDetail, CompanyInsights, Executive, FinancialPeriod, GeoPoint, Location, PeriodType } from '../api/types';
import { money, pct, tone, trendClass } from '../lib/format';
import { Link, personPath, placeToken, salariesPath } from '../lib/router';
import { AtAGlance, NewLeadership, PayVsPeersCard, SimilarCompanies } from './CompanyInsights';
import { Filing } from './Filing';
import { JobSalaries, PayRatioCard } from './WorkforcePay';
import { milesBetween, PageBack, type Nearby } from './PageShell';

interface Props {
  ticker: string;
  /** The visitor's search point, when they have one: distances are measured from it. */
  from: Nearby | null;
  showExecutives: boolean;
  /** Starts a search around one of the company's locations (`place` is its postcode as it appears in the address). */
  onExplore: (point: GeoPoint, label: string, place: string) => void;
  onLoaded: (about: string) => void;
}

interface Loaded {
  detail: CompanyDetail;
  quarterly: FinancialPeriod[];
  annual: FinancialPeriod[];
  executives: Executive[];
  /** Facts and similar companies; the page still shows without them. */
  insights: CompanyInsights | null;
}

/** Locations listed before "Show all". */
const SHOWN_LOCATIONS = 6;

/** One company's page (/company/AAPL): profile, earnings, executives and locations. */
export function CompanyPage({ ticker, from, showExecutives, onExplore, onLoaded }: Props) {
  const [data, setData] = useState<Loaded | null>(null);
  const [error, setError] = useState<{ notFound: boolean; message: string } | null>(null);
  const [period, setPeriod] = useState<PeriodType>('Quarterly');
  const [allLocations, setAllLocations] = useState(false);
  const loadedFor = useRef(onLoaded);
  loadedFor.current = onLoaded;

  useEffect(() => {
    let cancelled = false;
    setError(null); setAllLocations(false);
    Promise.all([
      api.company(ticker),
      api.financials(ticker, 'Quarterly', 4),
      api.financials(ticker, 'Annual'),
      showExecutives ? api.executives(ticker, 5).catch(() => ({ ticker, executives: [] })) : Promise.resolve({ ticker, executives: [] }),
      api.insights(ticker).catch(() => null),
    ]).then(([detail, q, a, ex, insights]) => {
      if (cancelled) return;
      setData({ detail, quarterly: q.periods.slice(-12), annual: a.periods, executives: ex.executives, insights });
      document.title = `${detail.name} (${detail.ticker}) — revenue, profit and executive pay · CompanyPaisa`;
      loadedFor.current(`${detail.name} (${detail.ticker})`);
    }).catch(e => {
      if (cancelled) return;
      const notFound = e instanceof ApiError && e.status === 404;
      setError({ notFound, message: notFound ? `We don't have a company with the ticker ${ticker}.` : 'Could not load this company. Please try again.' });
      document.title = 'CompanyPaisa';
    });
    return () => { cancelled = true; };
  }, [ticker, showExecutives]);

  const d = data?.detail.ticker === ticker ? data : null;

  if (error) return (
    <main className="wrap page">
      <PageBack from={from} />
      <section className="pane page-card page-missing">
        <h1>{error.notFound ? 'Company not found' : 'Something went wrong'}</h1>
        <p>{error.message}</p>
        <Link className="primary" to="/">Find public companies near you</Link>
      </section>
    </main>
  );
  if (!d) return <main className="wrap page"><PageBack from={from} /><p className="fine page-loading">Loading…</p></main>;

  const ind = d.detail.indicators;
  const cur = d.detail.currency;
  const hq = d.detail.locations.find(l => l.type === 'Headquarters') ?? d.detail.locations[0];
  // Distances from the visitor's search point; a whole-country or state search has none.
  const near = from && !from.region ? from : null;
  const distance = (l: Location) => near ? milesBetween(near.point, l.point) : null;
  const nearest = near && d.detail.locations.length > 0 ? d3.least(d.detail.locations, l => distance(l)!)! : null;
  // UK companies report yearly only (no quarterly tagged data): show the latest year and the Annual view.
  const noQuarters = d.quarterly.length === 0;
  const lastYear = d.annual[d.annual.length - 1];
  const shownPeriod: PeriodType = noQuarters ? 'Annual' : period;
  const uk = d.detail.exchange === 'LSE';
  // European companies (ESEF reports, financials only): tickers end in .PA, .AS, .MI or .MC.
  const europe = /\.(PA|AS|MI|MC)$/.test(d.detail.ticker);
  // Pakistan Stock Exchange (".KA"): figures read from the company's annual report PDF.
  const pakistan = d.detail.ticker.endsWith('.KA');
  const locations = [...d.detail.locations].sort((a, b) => near ? distance(a)! - distance(b)! : 0);
  const shownLocations = allLocations ? locations : locations.slice(0, SHOWN_LOCATIONS);
  const website = d.detail.website ? d.detail.website.replace(/^https?:\/\//, '').replace(/\/$/, '') : null;

  return (
    <main className="wrap page">
      <PageBack from={from} />

      <section className="pane page-card page-head">
        <div className="p-chips">
          <span className="chip tk">{d.detail.ticker}</span><span className="chip">{d.detail.exchange}</span><span className="chip">{d.detail.sector}</span>
          {d.detail.industry && d.detail.industry !== d.detail.sector && <span className="chip">{d.detail.industry}</span>}
        </div>
        <h1>{d.detail.name}</h1>
        <p className="p-loc">
          <i className={`dot ${trendClass(ind.trend)}`} />
          {hq && <>Headquarters: {hq.city}, {hq.state}</>}
          {nearest && near && <> · nearest location <b className="num">{distance(nearest)!.toFixed(1)} mi</b> from {near.label}</>}
        </p>
        {(website || d.detail.employees || d.detail.careersUrl) && (
          <p className="page-facts">
            {d.detail.employees ? <span><b className="num">{d.detail.employees.toLocaleString()}</b> employees</span> : null}
            {website && <a className="linkbtn" href={d.detail.website!} target="_blank" rel="noreferrer">{website} ↗</a>}
            {d.detail.careersUrl && <a className="linkbtn" href={d.detail.careersUrl} target="_blank" rel="noreferrer">Careers ↗</a>}
            {/* Straight to what the company pays: the first thing a job seeker is after. */}
            {d.detail.salaryTitles ? (
              <Link className="linkbtn salaries-link" to={salariesPath(d.detail.ticker)}>
                Salaries <b className="num">{d.detail.salaryTitles.toLocaleString()}</b> jobs →
              </Link>
            ) : null}
          </p>
        )}
        {d.detail.description && !d.detail.description.toLowerCase().includes('synthetic') && <p className="page-about">{d.detail.description}</p>}

        <div className="kpis page-kpis">
          {noQuarters && lastYear ? (
            <>
              <Kpi k="Revenue" s={`Fiscal year ${lastYear.fiscalYear}`} v={money(lastYear.revenue, cur)} />
              <Kpi k="Net income" s={`Fiscal year ${lastYear.fiscalYear}`} v={money(lastYear.netIncome, cur)} cls={lastYear.netIncome < 0 ? 'down' : ''} />
            </>
          ) : (
            <>
              <Kpi k="Revenue" s={ind.latestQuarterLabel ?? 'Latest quarter'} v={money(ind.latestQuarterRevenue, cur)} />
              <Kpi k="Net income" s={ind.latestQuarterLabel ?? 'Latest quarter'} v={money(ind.latestQuarterNetIncome, cur)} cls={(ind.latestQuarterNetIncome ?? 0) < 0 ? 'down' : ''} />
            </>
          )}
          <Kpi k="Revenue growth" s={noQuarters ? 'Latest year vs prior' : 'Last 12 mo vs prior'} v={pct(ind.revenueGrowthYoY)} cls={tone(ind.revenueGrowthYoY)} />
          <Kpi k={`${ind.cagrYears}-year CAGR`} s="Annual revenue" v={pct(ind.revenueCagr)} cls={tone(ind.revenueCagr)} />
        </div>
        <p className="note page-note">Company-wide figures, not just one location.</p>
      </section>

      {d.insights?.newExecutives?.length ? <NewLeadership people={d.insights.newExecutives} /> : null}

      {/* Left: the numbers (earnings, then executive pay). Right: what they mean (facts, peers, neighbours, locations). */}
      <div className="page-grid">
        <div className="page-col">
          <section className="pane page-card page-earnings" aria-labelledby="earnings-h">
            <div className="seg">
              <h2 className="subh" id="earnings-h">Earnings</h2>
              {!noQuarters && (
                <div className="opts">
                  <button aria-pressed={period === 'Quarterly'} onClick={() => setPeriod('Quarterly')}>Quarterly</button>
                  <button aria-pressed={period === 'Annual'} onClick={() => setPeriod('Annual')}>Annual</button>
                </div>
              )}
            </div>
            <div className="keys"><span><i style={{ background: 'var(--bar-b)' }} />Revenue</span><span><i style={{ background: 'var(--ink)', height: 2 }} />Net income</span></div>
            <EarningsChart rows={shownPeriod === 'Quarterly' ? d.quarterly : d.annual} quarterly={shownPeriod === 'Quarterly'} currency={cur} />
            <table>
              <thead><tr><th>Period</th><th>Revenue</th><th>Net income</th><th>YoY</th></tr></thead>
              <tbody>
                {(shownPeriod === 'Quarterly' ? d.quarterly : d.annual).slice(-(shownPeriod === 'Quarterly' ? 4 : 10)).reverse().map(p => (
                  <tr key={p.label}>
                    <td><Filing href={p.sourceFiling}>{p.label}</Filing></td><td>{money(p.revenue, cur)}</td>
                    <td className={`chg ${p.netIncome < 0 ? 'down' : ''}`}>{money(p.netIncome, cur)}</td>
                    <td className={`chg ${tone(p.revenueGrowthYoY)}`}>{pct(p.revenueGrowthYoY)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="fine">Tap a period to open the filing it comes from.</p>
          </section>
          {showExecutives && (
            <section className="pane page-card page-execs" aria-labelledby="execs-h">
              <h2 className="subh" id="execs-h">Executive pay</h2>
              <Executives execs={d.executives} currency={d.detail.payCurrency ?? cur} />
            </section>
          )}
          {d.detail.workerPay?.length ? <PayRatioCard pay={d.detail.workerPay} /> : null}
          {d.detail.salaryTitles ? <JobSalaries ticker={d.detail.ticker} name={d.detail.name} from={from} /> : null}
        </div>
        <div className="page-col">
          {d.insights && <AtAGlance insights={d.insights} name={d.detail.name} />}
          {d.insights?.payVsPeers && <PayVsPeersCard pay={d.insights.payVsPeers} name={d.detail.name} revenueCurrency={cur} />}
          {d.insights && <SimilarCompanies insights={d.insights} />}
          {locations.length > 0 && (
            <section className="pane page-card page-locs" aria-labelledby="locations-h">
              <h2 className="subh" id="locations-h">{locations.length === 1 ? 'Location' : `${locations.length} locations`}</h2>
              <ul className="page-locations">
                {shownLocations.map(l => (
                  <li key={l.locationId}>
                    <div>
                      <b>{l.label}</b>{l.type === 'Headquarters' && l.label !== 'Headquarters' && <span className="hq">HQ</span>}
                      <small>{[l.street, l.city, l.state, l.postalCode].filter(Boolean).join(', ')}</small>
                    </div>
                    {near && <span className="num">{distance(l)!.toFixed(1)} mi</span>}
                    {l.postalCode && (
                      <button className="linkbtn" onClick={() => onExplore(l.point, `${l.city}, ${l.state} ${l.postalCode}`, placeToken(l.postalCode, countryOf(l, cur)))}>
                        Companies near here →
                      </button>
                    )}
                  </li>
                ))}
              </ul>
              {locations.length > SHOWN_LOCATIONS && (
                <button className="linkbtn page-more" onClick={() => setAllLocations(a => !a)}>
                  {allLocations ? 'Show fewer' : `Show all ${locations.length}`}
                </button>
              )}
            </section>
          )}
        </div>
      </div>

      <p className="disclaimer page-source">
        {uk
          ? `Source: the company's annual reports (ESEF) and their directors' remuneration reports${d.detail.asOfDate ? `, latest year ending ${d.detail.asOfDate}` : ''}. Pay is each executive director's "single total figure".`
          : europe
            ? `Source: the company's annual reports (ESEF)${d.detail.asOfDate ? `, latest year ending ${d.detail.asOfDate}` : ''}. Executive pay isn't collected for European companies yet.`
          : pakistan
            ? `Source: the company's annual reports filed with the Pakistan Stock Exchange${d.detail.asOfDate ? `, latest year ending ${d.detail.asOfDate}` : ''} — revenue and profit from the statement of profit or loss (the group's when it has subsidiaries), and the chief executive's pay from the note on remuneration of the chief executive, directors and executives. Company details from the exchange's company profile.`
            : `Source: the company's SEC filings (10-K, 20-F or 40-F annual reports, 10-Q quarterly reports and DEF 14A proxy statements)${d.detail.asOfDate ? `, as of ${d.detail.asOfDate}` : ''}.`}
        {d.detail.description?.toLowerCase().includes('synthetic') && ' This is sample data — figures are synthetic.'}
      </p>
    </main>
  );
}

/** European, Australian, New Zealand and Pakistani locations keep their country in the state field ("FR"); "NL" is also Newfoundland. */
function countryOf(l: Location, currency: string): string | null {
  if (l.state === 'NL') return currency === 'EUR' ? 'NL' : null;
  return ['FR', 'IT', 'ES', 'AU', 'NZ', 'PK'].includes(l.state) ? l.state : null;
}

function Kpi({ k, s, v, cls = '' }: { k: string; s: string; v: string; cls?: string }) {
  return <div className="kpi"><span className="k">{k}</span><span className="s">{s}</span><span className={`v ${cls}`}>{v}</span></div>;
}

function EarningsChart({ rows, quarterly, currency }: { rows: FinancialPeriod[]; quarterly: boolean; currency: string }) {
  const money$ = (v: number) => money(v, currency);
  const host = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(400);
  useEffect(() => {
    const ro = new ResizeObserver(([e]) => setW(Math.max(260, e.contentRect.width)));
    if (host.current) ro.observe(host.current);
    return () => ro.disconnect();
  }, []);
  if (rows.length === 0) return <p className="fine">No financial history yet.</p>;

  const h = 210, m = { t: 10, r: 8, b: 26, l: 54 };
  const label = (p: FinancialPeriod) => quarterly ? `Q${p.fiscalQuarter} '${String(p.fiscalYear).slice(2)}` : String(p.fiscalYear);
  const x = d3.scaleBand().domain(rows.map(label)).range([m.l, w - m.r]).padding(0.3);
  const lo = Math.min(0, d3.min(rows, p => p.netIncome)!), hi = Math.max(d3.max(rows, p => p.revenue)!, d3.max(rows, p => p.netIncome)!);
  const y = d3.scaleLinear().domain([lo, hi]).nice(4).range([h - m.b, m.t]);
  const cx = (p: FinancialPeriod) => x(label(p))! + x.bandwidth() / 2;
  const line = d3.line<FinancialPeriod>().x(cx).y(p => y(p.netIncome)).curve(d3.curveMonotoneX)(rows)!;
  const every = quarterly ? 2 : 1;

  return (
    <div className="chart" ref={host}>
      <svg viewBox={`0 0 ${w} ${h}`} width="100%" height={h} role="img" aria-label="Revenue and net income chart">
        <defs>
          <linearGradient id="barg" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0" style={{ stopColor: 'var(--bar-a)' }} /><stop offset="1" style={{ stopColor: 'var(--bar-b)' }} />
          </linearGradient>
        </defs>
        {y.ticks(4).map(t => (
          <g key={t}>
            <line className={t === 0 ? 'zero' : 'grid'} x1={m.l} x2={w - m.r} y1={y(t)} y2={y(t)} />
            <text className="axis" x={m.l - 8} y={y(t)} dy=".32em" textAnchor="end">{t === 0 ? '0' : money$(t)}</text>
          </g>
        ))}
        {rows.map((p, i) => (
          <rect key={p.label} x={x(label(p))} width={x.bandwidth()} rx={Math.min(5, x.bandwidth() / 3)}
            y={y(Math.max(0, p.revenue))} height={Math.abs(y(p.revenue) - y(0))} fill="url(#barg)" fillOpacity={i === rows.length - 1 ? 1 : 0.35}>
            <title>{`${p.label}: revenue ${money$(p.revenue)}, net income ${money$(p.netIncome)}`}</title>
          </rect>
        ))}
        <path className="netline" d={line} />
        {rows.map((p, i) => <circle key={p.label} className={p.netIncome < 0 ? 'pt-neg' : 'pt-pos'} cx={cx(p)} cy={y(p.netIncome)} r={i === rows.length - 1 ? 4.5 : 2.8} />)}
        {rows.filter((_, i) => (rows.length - 1 - i) % every === 0).map(p => (
          <text key={p.label} className="axis" x={cx(p)} y={h - 8} textAnchor="middle">{label(p)}</text>
        ))}
      </svg>
    </div>
  );
}

const STACK = [
  { key: 'salary', label: 'Salary', color: 'var(--s1)' },
  { key: 'bonus', label: 'Bonus', color: 'var(--s2)' },
  { key: 'stockAwards', label: 'Stock awards', color: 'var(--s3)' },
  { key: 'other', label: 'Other', color: 'var(--s4)' },
] as const;

function Executives({ execs, currency }: { execs: Executive[]; currency: string }) {
  const money$ = (v: number) => money(v, currency);
  if (execs.length === 0) return <p className="fine">No executive compensation on file.</p>;
  const max = d3.max(execs, e => e.history[e.history.length - 1].total) ?? 1;
  return (
    <>
      <div className="keys" style={{ marginBottom: 12 }}>{STACK.map(s => <span key={s.key}><i style={{ background: s.color }} />{s.label}</span>)}</div>
      <div className="execs">
        {execs.map(e => {
          const a = e.history[e.history.length - 1], p = e.history[e.history.length - 2];
          const ch = p ? a.total / p.total - 1 : null;
          return (
            <div className="exec" key={e.executiveId}>
              <div className="ex-top">
                <div>
                  <div className="ex-title">{e.title}</div>
                  <div className="ex-name"><Link className="linkbtn" to={personPath(e.executiveId)} title="See this person's pay across all companies">{e.name} →</Link></div>
                </div>
                <div className="ex-total num">{money$(a.total)}<small>total {a.year}{ch != null && <> · <span className={`chg ${tone(ch)}`}>{pct(ch)}</span></>}</small></div>
              </div>
              <div className="stack" style={{ width: `${(a.total / max) * 100}%` }}>
                {STACK.map(s => <i key={s.key} style={{ width: `${(a[s.key] / a.total) * 100}%`, background: s.color }} title={`${s.label}: ${money$(a[s.key])}`} />)}
              </div>
              <PaySpark history={e.history} currency={currency} />
            </div>
          );
        })}
      </div>
    </>
  );
}

function PaySpark({ history, currency }: { history: Executive['history']; currency: string }) {
  const w = 380, h = 40;
  if (history.length < 2) return null;
  const x = d3.scalePoint<number>().domain(history.map(d => d.year)).range([30, w - 40]);
  const y = d3.scaleLinear().domain([0, d3.max(history, d => d.total)!]).range([h - 6, 6]);
  const area = d3.area<(typeof history)[number]>().x(d => x(d.year)!).y0(h - 6).y1(d => y(d.total)).curve(d3.curveMonotoneX)(history)!;
  const line = d3.line<(typeof history)[number]>().x(d => x(d.year)!).y(d => y(d.total)).curve(d3.curveMonotoneX)(history)!;
  const last = history[history.length - 1];
  return (
    <svg className="espark" viewBox={`0 0 ${w} ${h}`} width="100%" height={h} aria-hidden="true">
      <path className="area" d={area} /><path className="line" d={line} />
      <circle className="end" cx={x(last.year)} cy={y(last.total)} r={3.5} />
      <text className="axis" x={0} y={h - 4}>{history[0].year}</text>
      <text className="axis" x={w} y={h - 4} textAnchor="end">{last.year}</text>
      {history.map(d => <circle key={d.year} cx={x(d.year)} cy={y(d.total)} r={8} fill="transparent"><title>{`${d.year}: ${money(d.total, currency)}`}</title></circle>)}
    </svg>
  );
}
