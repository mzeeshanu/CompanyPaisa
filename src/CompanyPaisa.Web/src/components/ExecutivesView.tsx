import { useEffect, useState } from 'react';
import * as d3 from 'd3';
import type { ExecutiveSort, ExecutiveSummary, ExecutivesNearResponse, PayPoint } from '../api/types';
import { money, pct, tone, total } from '../lib/format';
import { SortHeader, useSortFlip } from './SortHeader';

interface Props {
  data: ExecutivesNearResponse;
  placeName: string;
  sort: ExecutiveSort; onSort: (s: ExecutiveSort) => void;
  search: string; onSearch: (s: string) => void;
  includeFormer: boolean; onIncludeFormer: (b: boolean) => void;
  selected: string | null;
  loading: boolean;
  onSelect: (personId: string) => void;
}

const SORTS: { key: ExecutiveSort; label: string }[] = [
  { key: 'Pay', label: 'Latest pay' },
  { key: 'TotalPay', label: '10-year total' },
  { key: 'PayGrowth', label: 'Pay growth' },
  { key: 'Distance', label: 'Distance' },
  { key: 'Name', label: 'Name' },
];

export const initials = (name: string) =>
  name.split(/\s+/).filter(w => /^[A-Za-z]/.test(w)).slice(0, 2).map(w => w[0].toUpperCase()).join('') || '?';

/** "Executives near me": named executive officers of nearby public companies, ranked by pay. */
export function ExecutivesView({ data, placeName, sort, onSort, search, onSearch, includeFormer, onIncludeFormer, selected, loading, onSelect }: Props) {
  const s = data.summary;
  const years = data.items[0]?.windowYears ?? 10;
  const { flipped, choose, order } = useSortFlip(sort, onSort, (e: ExecutiveSummary, k) =>
    k === 'PayGrowth' ? e.payGrowthYoY : k === 'Pay' ? e.latestTotalPay : k === 'TotalPay' ? e.windowTotalPay : k === 'Name' ? e.name : e.distanceMiles);
  const head = { sort, flipped, onSort: choose };

  // Debounce typing so every keystroke doesn't hit the API.
  const [text, setText] = useState(search);
  useEffect(() => { const t = setTimeout(() => { if (text !== search) onSearch(text); }, 300); return () => clearTimeout(t); }, [text, search, onSearch]);

  return (
    <main className={`wrap${loading ? ' loading' : ''}`}>
      <section className="summary" aria-live="polite">
        {s.executiveCount > 0 ? (
          <>
            <h1><em>{s.executiveCount} {s.executiveCount === 1 ? 'executive' : 'executives'}</em> at {s.companyCount} public {s.companyCount === 1 ? 'company' : 'companies'} within {data.radiusMiles} miles of {placeName}</h1>
            <span className="stat" title={s.approximate ? 'Some pay is in another currency; converted at approximate rates' : undefined}>
              <b>{total(s.combinedLatestPay, s.currency, s.approximate)}</b> combined pay{s.latestYear ? ` in ${s.latestYear}` : ''}</span>
            <span className="stat"><b>{total(s.medianLatestPay, s.currency, s.approximate)}</b> median</span>
            <span className="stat">{data.items.some(e => e.company.ticker.endsWith('.L'))
              ? "Pay as reported in company filings · US named executive officers and UK executive directors"
              : 'Pay as reported in proxy filings · named executive officers only'}</span>
          </>
        ) : (
          <>
            <h1>No executives found within {data.radiusMiles} miles</h1>
            <span className="stat">Try a wider radius, another sector, or clear the search.</span>
          </>
        )}
      </section>

      <section className="listcard pane" aria-label="Ranked executives">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {SORTS.map(k => <button key={k.key} aria-pressed={sort === k.key} onClick={() => onSort(k.key)}>{k.label}</button>)}
          </div>
          <input className="searchbox" type="search" placeholder="Search name or title…" aria-label="Search executives"
            value={text} onChange={e => setText(e.target.value)} />
          <label className="switch"><input type="checkbox" checked={includeFormer} onChange={e => onIncludeFormer(e.target.checked)} /> Include people who moved away</label>
        </div>

        {data.items.length === 0 ? <div className="empty">Nothing matches.</div> : (
          <div className="rows">
            <div className="row xrow head">
              <span>#</span><span className="c-mini" />
              <SortHeader k="Name" {...head}>Executive</SortHeader>
              <SortHeader k="Distance" {...head} className="c-at">Company · distance</SortHeader>
              <SortHeader k="Pay" {...head} className="r">Latest pay</SortHeader>
              <SortHeader k="PayGrowth" {...head} className="r">Change</SortHeader>
              <span className="r c-spark">Pay history</span>
              <SortHeader k="TotalPay" {...head} className="r c-net">{`${years}-yr total`}</SortHeader>
            </div>
            {order(data.items).map((e, i) => (
              <button key={e.personId} className={`row xrow${selected === e.personId ? ' sel' : ''}${e.isCurrent ? '' : ' former'}`} onClick={() => onSelect(e.personId)}>
                <span className="rank">{i + 1}</span>
                <span className="avatar c-mini" aria-hidden="true">{initials(e.name)}</span>
                <span className="who"><b>{e.name}</b><small>{e.title}{!e.isCurrent && ' · former'}</small></span>
                <span className="at c-at">{e.company.name}<small>{e.company.ticker} · {e.nearestLocation.city} · {e.distanceMiles.toFixed(1)} mi</small></span>
                <span className="val">{money(e.latestTotalPay, e.company.currency)}<small>{e.latestYear}</small></span>
                <span className="r"><span className={`pill ${tone(e.payGrowthYoY)}`}>{pct(e.payGrowthYoY)}</span></span>
                <span className="r c-spark spark"><PaySparkline points={e.payHistory} /></span>
                <span className="val c-net">{money(e.windowTotalPay, e.company.currency)}<small>{e.companyCount > 1 ? `${e.companyCount} companies` : `${e.windowYears} yrs`}</small></span>
              </button>
            ))}
          </div>
        )}
      </section>
    </main>
  );
}

/** Pay per year; a dot marks each year the person changed company. */
function PaySparkline({ points, w = 92, h = 28 }: { points: PayPoint[]; w?: number; h?: number }) {
  if (points.length < 2) return null;
  const x = d3.scaleLinear().domain([points[0].year, points[points.length - 1].year]).range([3, w - 4]);
  const y = d3.scaleLinear().domain([0, d3.max(points, p => p.total)!]).range([h - 3, 3]);
  const line = d3.line<PayPoint>().x(p => x(p.year)).y(p => y(p.total)).curve(d3.curveMonotoneX)(points)!;
  const area = d3.area<PayPoint>().x(p => x(p.year)).y0(h).y1(p => y(p.total)).curve(d3.curveMonotoneX)(points)!;
  const moves = points.filter((p, i) => i > 0 && p.ticker !== points[i - 1].ticker);
  return (
    <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} aria-hidden="true">
      <path d={area} style={{ fill: 'var(--accent)', fillOpacity: 0.1 }} />
      <path d={line} style={{ fill: 'none', stroke: 'var(--accent)', strokeWidth: 1.5 }} />
      {moves.map(p => <circle key={p.year} cx={x(p.year)} cy={y(p.total)} r={3} style={{ fill: 'var(--flat)', stroke: 'var(--ground)', strokeWidth: 1 }} />)}
      <circle cx={x(points[points.length - 1].year)} cy={y(points[points.length - 1].total)} r={2.6} style={{ fill: 'var(--accent)' }} />
    </svg>
  );
}
