import { useEffect, useRef, useState } from 'react';
import * as d3 from 'd3';
import { api } from '../api/client';
import type { CompanyDetail, Executive, FinancialPeriod, PeriodType } from '../api/types';
import { money, pct, tone, trendClass } from '../lib/format';

interface Props {
  ticker: string | null;
  distanceMiles: number | null;
  nearestLabel: string | null;
  showExecutives: boolean;
  onClose: () => void;
  onOpenPerson: (personId: string) => void;
}

interface Loaded {
  detail: CompanyDetail;
  quarterly: FinancialPeriod[];
  annual: FinancialPeriod[];
  executives: Executive[];
}

export function CompanyPanel({ ticker, distanceMiles, nearestLabel, showExecutives, onClose, onOpenPerson }: Props) {
  const [data, setData] = useState<Loaded | null>(null);
  const [error, setError] = useState('');
  const [tab, setTab] = useState<'earn' | 'exec'>('earn');
  const [period, setPeriod] = useState<PeriodType>('Quarterly');
  const panel = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!ticker) return;
    let cancelled = false;
    setError(''); setTab('earn');
    Promise.all([
      api.company(ticker),
      api.financials(ticker, 'Quarterly', 4),
      api.financials(ticker, 'Annual'),
      showExecutives ? api.executives(ticker, 5) : Promise.resolve({ ticker, executives: [] }),
    ]).then(([detail, q, a, ex]) => {
      if (cancelled) return;
      setData({ detail, quarterly: q.periods.slice(-12), annual: a.periods, executives: ex.executives });
      panel.current?.scrollTo({ top: 0 });
    }).catch(e => { if (!cancelled) setError(e.message ?? 'Could not load this company.'); });
    return () => { cancelled = true; };
  }, [ticker, showExecutives]);

  const open = ticker !== null;
  const d = data?.detail.ticker === ticker ? data : null;
  const ind = d?.detail.indicators;
  const hq = d?.detail.locations.find(l => l.type === 'Headquarters');
  const nearestIsHq = !!hq && nearestLabel === hq.label;

  return (
    <aside className={`panel pane${open ? ' open' : ''}`} ref={panel} aria-label="Company details" aria-hidden={!open}>
      <button className="iconbtn close" onClick={onClose} aria-label="Close details">✕</button>
      <div className="p-inner">
        {error && <p className="err">{error}</p>}
        {!d && !error && open && <p className="fine">Loading…</p>}
        {d && ind && (
          <>
            <header className="p-head">
              <div className="p-chips">
                <span className="chip tk">{d.detail.ticker}</span><span className="chip">{d.detail.exchange}</span><span className="chip">{d.detail.sector}</span>
                {nearestIsHq && <span className="chip">Headquartered here</span>}
              </div>
              <h2>{d.detail.name}</h2>
              <p className="p-loc">
                <i className={`dot ${trendClass(ind.trend)}`} />
                {nearestLabel ?? d.detail.locations[0]?.label} · {d.detail.locations[0]?.city}, {d.detail.locations[0]?.state}
                {distanceMiles != null && <> · <b className="num">{distanceMiles.toFixed(1)} mi</b> from you</>}
              </p>
            </header>

            <div className="kpis">
              <Kpi k="Revenue" s={ind.latestQuarterLabel ?? 'Latest quarter'} v={money(ind.latestQuarterRevenue)} />
              <Kpi k="Net income" s={ind.latestQuarterLabel ?? 'Latest quarter'} v={money(ind.latestQuarterNetIncome)} cls={(ind.latestQuarterNetIncome ?? 0) < 0 ? 'down' : ''} />
              <Kpi k="Revenue growth" s="Last 12 mo vs prior" v={pct(ind.revenueGrowthYoY)} cls={tone(ind.revenueGrowthYoY)} />
              <Kpi k={`${ind.cagrYears}-year CAGR`} s="Annual revenue" v={pct(ind.revenueCagr)} cls={tone(ind.revenueCagr)} />
            </div>
            <p className="note">{nearestIsHq ? 'Company-wide figures.' : 'Company-wide figures, not just this location.'}</p>

            <div className="tabs" role="tablist">
              <button role="tab" aria-selected={tab === 'earn'} onClick={() => setTab('earn')}>Earnings</button>
              {showExecutives && <button role="tab" aria-selected={tab === 'exec'} onClick={() => setTab('exec')}>Executives</button>}
            </div>

            {tab === 'earn' && (
              <section>
                <div className="seg">
                  <div className="keys"><span><i style={{ background: 'var(--bar-b)' }} />Revenue</span><span><i style={{ background: 'var(--ink)', height: 2 }} />Net income</span></div>
                  <div className="opts">
                    <button aria-pressed={period === 'Quarterly'} onClick={() => setPeriod('Quarterly')}>Quarterly</button>
                    <button aria-pressed={period === 'Annual'} onClick={() => setPeriod('Annual')}>Annual</button>
                  </div>
                </div>
                <EarningsChart rows={period === 'Quarterly' ? d.quarterly : d.annual} quarterly={period === 'Quarterly'} />
                <table>
                  <thead><tr><th>Period</th><th>Revenue</th><th>Net income</th><th>YoY</th></tr></thead>
                  <tbody>
                    {(period === 'Quarterly' ? d.quarterly : d.annual).slice(-4).reverse().map(p => (
                      <tr key={p.label}>
                        <td>{p.label}</td><td>{money(p.revenue)}</td>
                        <td className={`chg ${p.netIncome < 0 ? 'down' : ''}`}>{money(p.netIncome)}</td>
                        <td className={`chg ${tone(p.revenueGrowthYoY)}`}>{pct(p.revenueGrowthYoY)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </section>
            )}

            {tab === 'exec' && <Executives execs={d.executives} onOpenPerson={onOpenPerson} />}

            <p className="disclaimer">
              Source: company 10-K, 10-Q and proxy (DEF 14A) filings{d.detail.asOfDate ? `, as of ${d.detail.asOfDate}` : ''}.
              {d.detail.description?.toLowerCase().includes('synthetic') && ' This is sample data — figures are synthetic.'}
            </p>
          </>
        )}
      </div>
    </aside>
  );
}

function Kpi({ k, s, v, cls = '' }: { k: string; s: string; v: string; cls?: string }) {
  return <div className="kpi"><span className="k">{k}</span><span className="s">{s}</span><span className={`v ${cls}`}>{v}</span></div>;
}

function EarningsChart({ rows, quarterly }: { rows: FinancialPeriod[]; quarterly: boolean }) {
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
            <text className="axis" x={m.l - 8} y={y(t)} dy=".32em" textAnchor="end">{t === 0 ? '0' : money(t)}</text>
          </g>
        ))}
        {rows.map((p, i) => (
          <rect key={p.label} x={x(label(p))} width={x.bandwidth()} rx={Math.min(5, x.bandwidth() / 3)}
            y={y(Math.max(0, p.revenue))} height={Math.abs(y(p.revenue) - y(0))} fill="url(#barg)" fillOpacity={i === rows.length - 1 ? 1 : 0.35}>
            <title>{`${p.label}: revenue ${money(p.revenue)}, net income ${money(p.netIncome)}`}</title>
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

function Executives({ execs, onOpenPerson }: { execs: Executive[]; onOpenPerson: (personId: string) => void }) {
  if (execs.length === 0) return <p className="fine">No executive compensation on file.</p>;
  const max = d3.max(execs, e => e.history[e.history.length - 1].total) ?? 1;
  return (
    <section>
      <div className="seg" style={{ marginBottom: 12 }}>
        <div className="keys">{STACK.map(s => <span key={s.key}><i style={{ background: s.color }} />{s.label}</span>)}</div>
      </div>
      <div className="execs">
        {execs.map(e => {
          const a = e.history[e.history.length - 1], p = e.history[e.history.length - 2];
          const ch = p ? a.total / p.total - 1 : null;
          return (
            <div className="exec" key={e.executiveId}>
              <div className="ex-top">
                <div>
                  <div className="ex-title">{e.title}</div>
                  <div className="ex-name"><button className="linkbtn" onClick={() => onOpenPerson(e.executiveId)} title="See this person's pay across all companies">{e.name} →</button></div>
                </div>
                <div className="ex-total num">{money(a.total)}<small>total {a.year}{ch != null && <> · <span className={`chg ${tone(ch)}`}>{pct(ch)}</span></>}</small></div>
              </div>
              <div className="stack" style={{ width: `${(a.total / max) * 100}%` }}>
                {STACK.map(s => <i key={s.key} style={{ width: `${(a[s.key] / a.total) * 100}%`, background: s.color }} title={`${s.label}: ${money(a[s.key])}`} />)}
              </div>
              <PaySpark history={e.history} />
            </div>
          );
        })}
      </div>
    </section>
  );
}

function PaySpark({ history }: { history: Executive['history'] }) {
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
      {history.map(d => <circle key={d.year} cx={x(d.year)} cy={y(d.total)} r={8} fill="transparent"><title>{`${d.year}: ${money(d.total)}`}</title></circle>)}
    </svg>
  );
}
