import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import type { CompanyBubble, CompanySort, NearbyResponse, Region } from '../api/types';
import { bubbleRadius, money, pct, tone, total, trendClass } from '../lib/format';
import { hasStates, stateName } from '../lib/regions';
import { packWide } from '../lib/widePack';
import { companyPath, Link } from '../lib/router';
import { QuickFact } from './QuickFact';
import { SortHeader } from './SortHeader';

export interface Highlight { selected: string | null; hovered: string | null }
export interface BubbleEvents {
  onHover: (ticker: string | null, el?: HTMLElement) => void;
  onSelect: (ticker: string) => void;
}

/** The list's bubbles and rows are links to each company's page; `onOpened` just notes which one was opened. */
interface ListEvents {
  onHover: (ticker: string | null, el?: HTMLElement) => void;
  onOpened: (ticker: string) => void;
}

interface Props extends ListEvents {
  data: NearbyResponse;
  placeName: string;
  sort: CompanySort; onSort: (s: CompanySort) => void;
  /** The sort's opposite order (tapping the active column header again). */
  reverse: boolean; onReverse: () => void;
  /** The list arrives a page at a time: the next page, and whether it's on its way. */
  onMore: () => void; loadingMore: boolean;
  highlight: Highlight;
  loading: boolean;
  showExecutives: boolean;
  /** Narrows the ranked list only, by name or ticker. */
  find: string; onFind: (s: string) => void;
}

const SORTS: CompanySort[] = ['Revenue', 'Growth', 'Profit', 'Distance'];

export function ListView({ data, placeName, sort, onSort, reverse, onReverse, onMore, loadingMore, highlight, loading, showExecutives, onHover, onOpened, find, onFind }: Props) {
  const s = data.summary;
  // A whole country or state: no distances, so no "Distance" ranking and "in Texas" rather than "within 10 miles of".
  const region = data.region ?? null;
  const sorts = region ? SORTS.filter(k => k !== 'Distance') : SORTS;
  const shownSort: CompanySort = region && sort === 'Distance' ? 'Revenue' : sort;
  // Every company in the area, a few fields each (the list below is only the page loaded so far).
  const bubbles = data.bubbles ?? [];
  return (
    <main className={`wrap${loading ? ' loading' : ''}`}>
      <section className="summary" aria-live="polite">
        {s.companyCount > 0 ? (
          <>
            <h1><em>{s.companyCount.toLocaleString()} public {s.companyCount === 1 ? 'company' : 'companies'}</em> {region ? `in ${region.inSentence}` : `within ${data.radiusMiles} miles of ${placeName}`}</h1>
            <span className="stat" title={s.approximate ? 'Some companies report in another currency; converted at approximate rates' : undefined}>
              <b>{total(s.combinedTtmRevenue, s.currency, s.approximate)}</b> combined annual revenue</span>
            <span className="stat"><i className="dot up" /><b>{s.growingCount}</b> growing</span>
            <span className="stat"><b>{s.headquarteredCount}</b> headquartered here</span>
          </>
        ) : (
          <>
            <h1>No public companies {region ? `in ${region.inSentence}` : `within ${data.radiusMiles} miles`}</h1>
            <span className="stat">{region ? 'Try a different sector, or another place.' : 'Try a wider radius or a different sector.'}</span>
          </>
        )}
      </section>

      {bubbles.length > 0 && (
        <>
          <BubbleField items={bubbles} region={region} highlight={highlight} onHover={onHover} onOpened={onOpened} />
          <QuickFact summary={s} showExecutives={showExecutives} onOpened={onOpened} />
        </>
      )}

      <section className="listcard pane" aria-label="Ranked companies">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {sorts.map(k => <button key={k} aria-pressed={shownSort === k} onClick={() => onSort(k)}>{k}</button>)}
          </div>
          <input className="searchbox" type="search" placeholder="Search name or ticker…" aria-label="Search the list by company name or ticker"
            value={find} onChange={e => onFind(e.target.value)} />
        </div>
        <RankedRows data={data} region={!!region} sort={shownSort} onSort={onSort} reverse={reverse} onReverse={onReverse} onMore={onMore} loadingMore={loadingMore}
          highlight={highlight} onHover={onHover} onOpened={onOpened} find={find} onFind={onFind} />
      </section>
    </main>
  );
}

/**
 * Every company's bubble in one wide box, spread left to right so it takes the page's width rather than its height.
 * The dropdown narrows the box to one city (nearest first) without splitting it into separate cards.
 */
function BubbleField({ items, region, highlight, onHover, onOpened }: { items: CompanyBubble[]; region: Region | null; highlight: Highlight } & ListEvents) {
  // A radius search groups by city, nearest first; a whole US or Canadian search by state or province, biggest first;
  // other countries and states by city, biggest first.
  const byState = region?.kind === 'Country' && hasStates(region.country);
  const groupKey = useMemo(() => byState ? (c: CompanyBubble) => c.state.toUpperCase() : (c: CompanyBubble) => cityKey(c.city), [byState]);
  const cities = useMemo(() => d3.groups(items, groupKey)
    .map(([key, list]) => ({
      key, count: list.length, nearest: d3.min(list, c => c.distanceMiles)!,
      name: byState ? stateName(region!.country, key) : displayName(list.map(c => c.city)),
    }))
    .sort((a, b) => region ? b.count - a.count || a.name.localeCompare(b.name) : a.nearest - b.nearest), [items, groupKey, byState, region]);
  const [city, setCity] = useState('');
  const picked = cities.find(c => c.key === city);
  const shown = useMemo(() => picked ? items.filter(c => groupKey(c) === picked.key) : items, [items, picked, groupKey]);

  const box = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const el = box.current;
    if (!el) return;
    const measure = () => setWidth(el.clientWidth);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);
  // A big area (300+ companies) fits the box by making every bubble smaller, small companies becoming dots: the box
  // stays wide and short but still shows how many there are and how they compare. "Enlarge" lets it grow instead.
  const [enlarged, setEnlarged] = useState(false);
  const field = useMemo(() => width > 0 ? spread(shown, width, enlarged ? Infinity : maxHeight(width)) : null, [shown, width, enlarged]);

  // New results pop in, biggest first. Only once per search: not when coming back from a company page (same
  // results), switching city or resizing. Runs before the first paint so the bubbles never flash in first.
  const [pop, setPop] = useState(false);
  useLayoutEffect(() => {
    if (!field || poppedFor === items) return;
    poppedFor = items;
    setPop(true);
    setTimeout(() => setPop(false), popSpread(field.nodes.length) + POP_MS + 100);
  }, [items, field]);
  const popDelay = (i: number, n: number) => pop ? { animationDelay: `${Math.round(i / Math.max(1, n - 1) * popSpread(n))}ms` } : undefined;

  return (
    <section className="field pane" aria-label="Companies by revenue">
      <div className="field-h">
        <div className="sel-wrap field-city">
          <select aria-label="Show companies in" value={picked ? picked.key : ''} onChange={e => setCity(e.target.value)}>
            <option value="">{byState ? 'All states' : 'All cities'} · {items.length.toLocaleString()}</option>
            {cities.map(c => <option key={c.key} value={c.key}>{c.name} · {c.count.toLocaleString()}{region ? '' : ` · ${c.nearest.toFixed(1)} mi`}</option>)}
          </select>
        </div>
        <div className="legend-row">
          <span>Size = annual revenue</span>
          <span><i className="dot up" />Growing</span>
          <span><i className="dot flat" />Flat</span>
          <span><i className="dot down" />Shrinking or losing money</span>
        </div>
        {(field?.shrunk || enlarged) && (
          <button className="field-size" onClick={() => setEnlarged(e => !e)} aria-pressed={enlarged}>
            {enlarged ? 'Fit to box' : 'Enlarge'}
          </button>
        )}
      </div>
      <div className={`field-box${pop ? ' pop' : ''}`} ref={box} style={{ height: field?.height ?? 160 }}>
        {field?.nodes.map((n, i, all) => (
          <Link key={n.c.ticker} to={companyPath(n.c.ticker)}
            className={`bub t-${trendClass(n.c.trend)}${n.r < 8 ? ' dot-sm' : ''}${highlight.hovered === n.c.ticker ? ' hl' : ''}${highlight.selected === n.c.ticker ? ' sel' : ''}`}
            style={{ left: n.x - n.r, top: n.y - n.r, width: n.r * 2, height: n.r * 2, ...popDelay(i, all.length) }}
            aria-label={`${n.c.name}, ${money(n.c.ttmRevenue, n.c.currency)} revenue`}
            onMouseEnter={e => onHover(n.c.ticker, e.currentTarget)} onMouseLeave={() => onHover(null)}
            onFocus={e => onHover(n.c.ticker, e.currentTarget)} onBlur={() => onHover(null)}
            onClick={() => onOpened(n.c.ticker)}>
            <span className="glass" />
            {n.r >= 12 && <span className="tk" style={{ fontSize: Math.max(8, Math.min(16, n.r * 0.38)) }}>{n.c.ticker}</span>}
          </Link>
        ))}
      </div>
    </section>
  );
}

interface Placed { c: CompanyBubble; r: number; x: number; y: number }

/** The results the pop-in last played for (kept outside the component, which unmounts while a company page is open). */
let poppedFor: CompanyBubble[] | null = null;
/** One bubble's pop (matches .field-box.pop in styles.css), and how long the last one waits: longer for a crowd. */
const POP_MS = 520;
const popSpread = (n: number) => Math.min(650, 200 + n * 6);

/** Space between bubbles, and how much of a box circles fill when they're packed. */
const BUBBLE_GAP = 3;
const FILL = 0.8;
/** A wide box: aim for about this width to height, so a handful of companies stays together instead of scattering. */
const ASPECT = 5;
/** The box width bubbles are sized for, and the smallest a bubble gets when they shrink. */
const FULL_WIDTH = 1000;
const MIN_R = 10;
/** When the box is fitted to its height, the smallest companies are dots this big (still hoverable / tappable). */
const MIN_DOT = 3.5;

/** How tall the box may get before bubbles shrink to fit: wide and short on a desktop, about square on a phone. */
const maxHeight = (width: number) => width < 600 ? Math.max(300, width * 1.05) : Math.min(560, Math.max(340, width * 0.45));

/**
 * Lays the bubbles out in an oval as wide as the box (or narrower when there are only a few), biggest first in the
 * middle, touching but never overlapping (see packWide: fast enough for a whole country's 3,800 companies).
 */
function spread(items: CompanyBubble[], width: number, maxH: number): { nodes: Placed[]; height: number; shrunk: boolean } {
  const nodes: Placed[] = items
    .map(c => ({ c, r: bubbleRadius(c.ttmRevenue), x: 0, y: 0 }))
    .sort((a, b) => b.r - a.r || a.c.ticker.localeCompare(b.c.ticker));
  if (nodes.length === 0) return { nodes, height: 0, shrunk: false };
  // Sizes are set for a desktop page. A narrower box shrinks every bubble alike (small ones stay tappable), and the
  // very biggest companies (Apple, Walmart) never get wider than the box.
  const scale = Math.min(Math.max(0.5, width / FULL_WIDTH), 1, (width - BUBBLE_GAP * 2) / (nodes[0].r * 2));
  if (scale < 1) nodes.forEach(n => { n.r = Math.max(MIN_R, n.r * scale); });
  let gap = BUBBLE_GAP;
  const areaOf = () => d3.sum(nodes, n => Math.PI * (n.r + gap) ** 2) / FILL;
  let area = areaOf();

  // Too many to fit the box's height: shrink every bubble by the same factor (a few passes, since dots stop shrinking).
  let shrunk = false;
  const base = nodes.map(n => n.r);
  // A whole country (thousands of companies) needs finer dots to fit.
  const minDot = nodes.length > 1500 ? MIN_DOT / 2 : MIN_DOT;
  let k = 1;
  for (let pass = 0; pass < 4 && area / width > maxH; pass++) {
    k *= Math.sqrt((maxH * width) / area);
    shrunk = true;
    gap = Math.max(1, BUBBLE_GAP * k);
    nodes.forEach((n, i) => { n.r = Math.max(minDot, base[i] * k); });
    area = areaOf();
  }
  const tallest = nodes[0].r * 2 + BUBBLE_GAP * 2;
  const bandW = Math.min(width, Math.max(tallest, Math.sqrt(area * ASPECT)));
  const bandH = Math.max(tallest, area / bandW);

  // Pack into an oval as wide as the band, then shrink it all alike if it came out wider than the box.
  const circles = nodes.map(n => ({ r: n.r + gap / 2, x: 0, y: 0 }));
  packWide(circles, bandW / bandH);
  const packedW = d3.max(circles, c => c.x + c.r)! - d3.min(circles, c => c.x - c.r)!;
  const fit = Math.min(1, (width - 2) / packedW);
  nodes.forEach((n, i) => { n.r *= fit; n.x = width / 2 + circles[i].x * fit; n.y = circles[i].y * fit; });

  // Trim the band to what the bubbles use, with room for the hover ring.
  const pad = 10;
  const top = d3.min(nodes, n => n.y - n.r)!, bottom = d3.max(nodes, n => n.y + n.r)!;
  nodes.forEach(n => { n.y += pad - top; });
  return { nodes, height: Math.ceil(bottom - top + pad * 2), shrunk };
}

/** "Issy-Les-Moulineaux", "Issy les Moulineaux" and "Paris 12" / "Paris" are the same place. */
function cityKey(city: string): string {
  return city.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
    .replace(/[-‐'’]/g, ' ').replace(/\s+\d+(e|er|eme)?$/, '').replace(/\s+/g, ' ').trim();
}

/** The spelling most companies in the group use (ties: the one without an arrondissement number). */
function displayName(cities: string[]): string {
  const counts = d3.rollups(cities, v => v.length, c => c);
  counts.sort((a, b) => b[1] - a[1] || Number(/\d$/.test(a[0])) - Number(/\d$/.test(b[0])));
  return counts[0][0];
}

/**
 * The ranked list as the API sends it: sorted, reversed and searched there, a page at a time ("Show more" adds the next).
 * Tapping a column header sorts by it; tapping the active one again reverses the order.
 */
function RankedRows({ data, region, sort, onSort, reverse, onReverse, onMore, loadingMore, highlight, onHover, onOpened, find, onFind }:
  {
    data: NearbyResponse; region: boolean; sort: CompanySort; onSort: (s: CompanySort) => void; reverse: boolean; onReverse: () => void;
    onMore: () => void; loadingMore: boolean; highlight: Highlight; find: string; onFind: (s: string) => void;
  } & ListEvents) {
  const items = data.items;
  const searching = find.trim() !== '';
  if (data.summary.companyCount === 0) return <div className="empty">Nothing matches. Widen the radius or clear the filters.</div>;
  if (data.totalCount === 0) return (
    <div className="empty">No company here matches “{find.trim()}”. <button className="linkbtn" onClick={() => onFind('')}>Clear</button></div>
  );
  const head = { sort, flipped: reverse, onSort: (k: CompanySort) => (k === sort ? onReverse() : onSort(k)) };
  const left = data.totalCount - items.length;
  return (
    <div className="rows">
      <div className="row head">
        <span>#</span><span className="c-mini" /><span>Company</span>
        {region ? <span className="c-at">Location</span> : <SortHeader k="Distance" {...head} className="c-at">Nearest location</SortHeader>}
        <SortHeader k="Revenue" {...head} className="r">Revenue</SortHeader>
        <SortHeader k="Growth" {...head} className="r">Growth</SortHeader>
        <span className="r c-spark">History</span>
        <SortHeader k="Profit" {...head} className="r c-net">Net income</SortHeader>
      </div>
      {items.map((c, i) => (
        <Link key={c.ticker} to={companyPath(c.ticker)}
          className={`row${highlight.hovered === c.ticker ? ' hl' : ''}${highlight.selected === c.ticker ? ' sel' : ''}`}
          onMouseEnter={() => onHover(c.ticker)} onMouseLeave={() => onHover(null)} onClick={() => onOpened(c.ticker)}>
          <span className="rank">{i + 1}</span>
          <span className={`mini c-mini t-${trendClass(c.indicators.trend)}`} />
          <span className="who"><b>{c.name}</b>{c.isHeadquarteredNearby && <span className="hq">HQ</span>}<small>{c.ticker} · {c.sector}</small></span>
          <span className="at c-at">{c.nearestLocation.type === 'Headquarters' ? 'Headquarters' : c.nearestLocation.label}<small>{region ? `${c.nearestLocation.city}, ${c.nearestLocation.state}` : `${c.nearestLocation.city} · ${c.distanceMiles.toFixed(1)} mi`}</small></span>
          <span className="val">{money(c.indicators.ttmRevenue, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
          <span className="r"><span className={`pill ${tone(c.indicators.revenueGrowthYoY)}`}>{pct(c.indicators.revenueGrowthYoY)}</span></span>
          <span className="r c-spark spark"><Sparkline points={c.indicators.revenueHistory.map(p => p.revenue)} /></span>
          <span className={`val c-net${c.indicators.ttmNetIncome < 0 ? ' neg' : ''}`}>{money(c.indicators.ttmNetIncome, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
        </Link>
      ))}
      {(searching || left > 0) && (
        <div className="more-row">
          <span>Showing {items.length.toLocaleString()} of {data.totalCount.toLocaleString()}{searching ? ` matching (${data.summary.companyCount.toLocaleString()} in all)` : ''}</span>
          {left > 0 && (
            <button className="linkbtn" onClick={onMore} disabled={loadingMore}>
              {loadingMore ? 'Loading…' : `Show ${Math.min(left, data.pageSize).toLocaleString()} more`}
            </button>
          )}
          {searching && <button className="linkbtn" onClick={() => onFind('')}>Clear</button>}
        </div>
      )}
    </div>
  );
}

function Sparkline({ points, w = 92, h = 28 }: { points: number[]; w?: number; h?: number }) {
  if (points.length < 2) return null;
  const x = d3.scaleLinear().domain([0, points.length - 1]).range([2, w - 4]);
  const [lo, hi] = d3.extent(points) as [number, number];
  const y = d3.scaleLinear().domain(lo === hi ? [lo - 1, hi + 1] : [lo, hi]).range([h - 3, 3]);
  const col = points[points.length - 1] >= points[0] ? 'var(--up)' : 'var(--down)';
  const line = d3.line<number>().x((_, i) => x(i)).y(v => y(v)).curve(d3.curveMonotoneX)(points)!;
  const area = d3.area<number>().x((_, i) => x(i)).y0(h).y1(v => y(v)).curve(d3.curveMonotoneX)(points)!;
  return (
    <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} aria-hidden="true">
      <path d={area} style={{ fill: col, fillOpacity: 0.12 }} />
      <path d={line} style={{ fill: 'none', stroke: col, strokeWidth: 1.5 }} />
      <circle cx={x(points.length - 1)} cy={y(points[points.length - 1])} r={2.6} style={{ fill: col }} />
    </svg>
  );
}
