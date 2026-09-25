import { useEffect, useState } from 'react';
import * as d3 from '../lib/d3';
import type { ExecutiveSort, ExecutiveSummary, ExecutivesNearResponse, PayPoint, RoleFilter } from '../api/types';
import { money, pct, tone, total } from '../lib/format';
import { companyPath, Link, personPath } from '../lib/router';
import { SortHeader, useSortFlip } from './SortHeader';

interface Props {
  data: ExecutivesNearResponse;
  placeName: string;
  sort: ExecutiveSort; onSort: (s: ExecutiveSort) => void;
  search: string; onSearch: (s: string) => void;
  role: RoleFilter | ''; onRole: (r: RoleFilter | '') => void;
  includeFormer: boolean; onIncludeFormer: (b: boolean) => void;
  selected: string | null;
  loading: boolean;
  /** Rows are links to each person's page; this just notes which one was opened. */
  onOpened: (personId: string) => void;
  /** Loads the next page; the list shows how many of the total are loaded. */
  onMore: () => void;
  loadingMore: boolean;
}

const ROLES: { key: RoleFilter; label: string }[] = [
  { key: 'Ceo', label: 'CEOs' },
  { key: 'Cfo', label: 'CFOs' },
  { key: 'Coo', label: 'COOs' },
  { key: 'Technology', label: 'CTOs / CIOs' },
  { key: 'Legal', label: 'General counsel' },
  { key: 'Other', label: 'Other executives' },
];

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
export function ExecutivesView({ data, placeName, sort, onSort, search, onSearch, role, onRole, includeFormer, onIncludeFormer, selected, loading, onOpened, onMore, loadingMore }: Props) {
  const s = data.summary;
  const years = data.items[0]?.windowYears ?? 10;
  // A whole country or state: no distances to rank by or show.
  const region = data.region ?? null;
  // Only companies based in the area: say so ("headquartered within 25 miles of Lehi").
  const where = region ? `headquartered in ${region.inSentence}` : `headquartered within ${data.radiusMiles} miles of ${placeName}`;
  const sorts = region ? SORTS.filter(k => k.key !== 'Distance') : SORTS;
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
            <h1><em>{s.executiveCount} {s.executiveCount === 1 ? 'executive' : 'executives'}</em> at {s.companyCount} public {s.companyCount === 1 ? 'company' : 'companies'} {where}</h1>
            <span className="stat" title={s.approximate ? 'Some pay is in another currency; converted at approximate rates' : undefined}>
              <b>{total(s.combinedLatestPay, s.currency, s.approximate)}</b> combined pay{s.latestYear ? ` in ${s.latestYear}` : ''}</span>
            <span className="stat"><b>{total(s.medianLatestPay, s.currency, s.approximate)}</b> median</span>
            <span className="stat">{data.items.some(e => e.company.ticker.endsWith('.L') || e.company.ticker.endsWith('.KA'))
              ? "Pay as reported in company filings · US named executive officers, UK executive directors and Pakistani chief executives"
              : 'Pay as reported in proxy filings · named executive officers only'}</span>
          </>
        ) : (
          <>
            <h1>No executives found {region ? `in ${region.inSentence}` : `within ${data.radiusMiles} miles`}</h1>
            <span className="stat">{region ? 'Try another sector, or clear the search.' : 'Try a wider radius, another sector, or clear the search.'}</span>
          </>
        )}
      </section>

      <section className="listcard pane" aria-label="Ranked executives">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {sorts.map(k => <button key={k.key} aria-pressed={sort === k.key} onClick={() => onSort(k.key)}>{k.label}</button>)}
          </div>
          <input className="searchbox" type="search" placeholder="Search name or title…" aria-label="Search executives"
            value={text} onChange={e => setText(e.target.value)} />
          <div className="sel-wrap">
            <select aria-label="Role" value={role} onChange={e => onRole(e.target.value as RoleFilter | '')}>
              <option value="">All roles</option>
              {ROLES.map(r => <option key={r.key} value={r.key}>{r.label}</option>)}
            </select>
          </div>
          <label className="switch"><input type="checkbox" checked={includeFormer} onChange={e => onIncludeFormer(e.target.checked)} /> Include people who moved away</label>
        </div>

        {data.items.length === 0 ? <div className="empty">Nothing matches.</div> : (
          <div className="rows">
            <div className="row xrow head">
              <span>#</span><span className="c-mini" />
              <SortHeader k="Name" {...head}>Executive</SortHeader>
              {region ? <span className="c-at">Company</span> : <SortHeader k="Distance" {...head} className="c-at">Company · distance</SortHeader>}
              <SortHeader k="Pay" {...head} className="r">Latest pay</SortHeader>
              <SortHeader k="PayGrowth" {...head} className="r">Change</SortHeader>
              <span className="r c-spark">Pay history</span>
              <SortHeader k="TotalPay" {...head} className="r c-net">{`${years}-yr total`}</SortHeader>
            </div>
            {order(data.items).map((e, i) => (
              <Link key={e.personId} to={e.hasProfile === false ? companyPath(e.company.ticker) : personPath(e.personId)}
                className={`row xrow${selected === e.personId ? ' sel' : ''}${e.isCurrent ? '' : ' former'}`} onClick={() => onOpened(e.personId)}>
                <span className="rank">{i + 1}</span>
                <span className="avatar c-mini" aria-hidden="true">{initials(e.name)}</span>
                <span className="who">
                  <b>{e.name}</b>{e.newHire && <span className="new-tag" title={`Appointment announced ${e.newHire.announcedOn}`}>New</span>}
                  <small>{e.title}{!e.isCurrent && ' · former'}</small>
                </span>
                <span className="at c-at">{e.company.name}<small>{e.company.ticker} · {e.nearestLocation.city}{region ? `, ${e.nearestLocation.state}` : ` · ${e.distanceMiles.toFixed(1)} mi`}</small></span>
                {e.payHistory.length === 0 && e.newHire ? (
                  // Known only from the appointment: the announced package, not pay received.
                  <>
                    <span className="val">{money(e.newHire.total, e.newHire.currency)}<small>package</small></span>
                    <span className="r"><span className="pill flat">—</span></span>
                    <span className="r c-spark spark" />
                    <span className="val c-net">—<small>{e.hasProfile === false ? 'no pay yet' : 'new role'}</small></span>
                  </>
                ) : (
                  <>
                    <span className="val">{money(e.latestTotalPay, e.company.currency)}<small>{e.latestYear}</small></span>
                    <span className="r"><span className={`pill ${tone(e.payGrowthYoY)}`}>{pct(e.payGrowthYoY)}</span></span>
                    <span className="r c-spark spark"><PaySparkline points={e.payHistory} /></span>
                    <span className="val c-net">{money(e.windowTotalPay, e.company.currency)}<small>{e.companyCount > 1 ? `${e.companyCount} companies` : `${e.windowYears} yrs`}</small></span>
                  </>
                )}
              </Link>
            ))}
            {data.items.length < data.totalCount && (
              <div className="more-row">
                <span>Showing {data.items.length.toLocaleString()} of {data.totalCount.toLocaleString()}</span>
                <button className="linkbtn" onClick={onMore} disabled={loadingMore}>{loadingMore ? 'Loading…' : 'Show more'}</button>
              </div>
            )}
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
