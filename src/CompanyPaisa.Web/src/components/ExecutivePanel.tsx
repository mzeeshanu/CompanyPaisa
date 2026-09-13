import { useEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import { api } from '../api/client';
import type { ExecutiveDetail } from '../api/types';
import { money, pct, tone } from '../lib/format';
import { initials } from './ExecutivesView';

interface Props {
  personId: string | null;
  onClose: () => void;
  onOpenCompany: (ticker: string) => void;
}

// One colour per company in a person's career (the most recent company gets the accent).
const COMPANY_COLORS = ['var(--s1)', 'var(--s3)', 'var(--flat)', 'var(--s2)', 'var(--up)', 'var(--down)'];

/** One person's pay history across every company they were a named executive at. */
export function ExecutivePanel({ personId, onClose, onOpenCompany }: Props) {
  const [data, setData] = useState<ExecutiveDetail | null>(null);
  const [error, setError] = useState('');
  const panel = useRef<HTMLElement>(null);

  useEffect(() => {
    if (!personId) return;
    let cancelled = false;
    setError('');
    api.executive(personId)
      .then(d => { if (!cancelled) { setData(d); panel.current?.scrollTo({ top: 0 }); } })
      .catch(e => { if (!cancelled) setError(e.message ?? 'Could not load this executive.'); });
    return () => { cancelled = true; };
  }, [personId]);

  const open = personId !== null;
  const d = data?.personId === personId ? data : null;

  const colorOf = useMemo(() => {
    const order = d ? [...new Set(d.roles.map(r => r.company.ticker))] : [];
    return (ticker: string) => COMPANY_COLORS[Math.max(0, order.indexOf(ticker)) % COMPANY_COLORS.length];
  }, [d]);

  const latest = d?.history[0];
  const previous = d?.history.find(h => h.year === (latest?.year ?? 0) - 1);
  const latestYearTotal = d ? d.history.filter(h => h.year === latest!.year).reduce((s, h) => s + h.total, 0) : 0;
  const prevYearTotal = d && previous ? d.history.filter(h => h.year === previous.year).reduce((s, h) => s + h.total, 0) : 0;
  const change = prevYearTotal > 0 ? latestYearTotal / prevYearTotal - 1 : null;
  const companies = d ? new Set(d.roles.map(r => r.company.ticker)).size : 0;

  return (
    <aside className={`panel pane${open ? ' open' : ''}`} ref={panel} aria-label="Executive details" aria-hidden={!open}>
      <button className="iconbtn close" onClick={onClose} aria-label="Close details">✕</button>
      <div className="p-inner">
        {error && <p className="err">{error}</p>}
        {!d && !error && open && <p className="fine">Loading…</p>}
        {d && latest && (
          <>
            <header className="p-head">
              <div className="p-chips">
                <span className="chip tk">{d.currentCompany.ticker}</span>
                <span className="chip">{d.currentCompany.sector}</span>
                {d.secCik && <span className="chip">SEC CIK {d.secCik}</span>}
              </div>
              <div className="person-head">
                <span className="avatar big" aria-hidden="true">{initials(d.name)}</span>
                <div>
                  <h2>{d.name}</h2>
                  <p className="p-loc">{d.currentTitle} · <button className="linkbtn" onClick={() => onOpenCompany(d.currentCompany.ticker)}>{d.currentCompany.name}</button></p>
                </div>
              </div>
            </header>

            <div className="kpis">
              <Kpi k="Latest pay" s={`${latest.year} total`} v={money(latestYearTotal)} />
              <Kpi k="Change" s={`vs ${latest.year - 1}`} v={pct(change)} cls={tone(change)} />
              <Kpi k="Total earned" s={`${d.firstYear}–${d.latestYear}`} v={money(d.totalPay)} />
              <Kpi k="Career" s="as a named executive" v={`${companies} ${companies === 1 ? 'company' : 'companies'}`} />
            </div>
            <p className="note">Reported compensation (salary, bonus, stock awards at grant value, other) from each company's proxy filings.</p>

            <section>
              <div className="seg">
                <div className="keys">
                  {[...new Map(d.roles.map(r => [r.company.ticker, r.company])).values()].map(c => (
                    <span key={c.ticker}><i style={{ background: colorOf(c.ticker) }} />{c.name}</span>
                  ))}
                </div>
              </div>
              <PayByYearChart detail={d} colorOf={colorOf} />
            </section>

            <section>
              <h3 className="subh">Career</h3>
              <ol className="career">
                {d.roles.map(r => (
                  <li key={`${r.company.ticker}-${r.fromYear}`}>
                    <i style={{ background: colorOf(r.company.ticker) }} />
                    <div>
                      <b>{r.title}</b>
                      <button className="linkbtn" onClick={() => onOpenCompany(r.company.ticker)}>{r.company.name} ({r.company.ticker})</button>
                      <small>{r.fromYear === r.toYear ? r.fromYear : `${r.fromYear}–${r.toYear}`}</small>
                    </div>
                    <span className="num">{money(r.totalPay)}</span>
                  </li>
                ))}
              </ol>
            </section>

            <section>
              <h3 className="subh">Year by year</h3>
              <div className="tablewrap">
                <table>
                  <thead><tr><th>Year</th><th>Company</th><th>Salary</th><th>Bonus</th><th>Stock</th><th>Total</th></tr></thead>
                  <tbody>
                    {d.history.map(h => (
                      <tr key={`${h.year}-${h.company.ticker}`}>
                        <td>{h.year}</td>
                        <td className="co"><i style={{ background: colorOf(h.company.ticker) }} />{h.company.ticker}</td>
                        <td>{money(h.salary)}</td><td>{money(h.bonus)}</td><td>{money(h.stockAwards)}</td><td><b>{money(h.total)}</b></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            <p className="disclaimer">
              Only companies where this person was a named executive officer of a public company appear here.
              Earlier or private-company roles aren't reported to the SEC.
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

/** Stacked bars: total pay per year, one segment per company paid that year. */
function PayByYearChart({ detail, colorOf }: { detail: ExecutiveDetail; colorOf: (t: string) => string }) {
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
            <text className="axis" x={m.l - 8} y={y(t)} dy=".32em" textAnchor="end">{t === 0 ? '0' : money(t)}</text>
          </g>
        ))}
        {byYear.map(b => {
          let base = 0;
          return b.parts.map(p => {
            const y0 = base; base += p.total;
            return (
              <rect key={`${b.year}-${p.company.ticker}`} x={x(b.year)} width={x.bandwidth()} rx={2}
                y={y(base)} height={Math.max(0, y(y0) - y(base))} style={{ fill: colorOf(p.company.ticker) }}>
                <title>{`${b.year} · ${p.company.name}: ${money(p.total)} (${p.title})`}</title>
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
