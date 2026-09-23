import { useEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import type { CompanySort, CompanySummary, NearbyResponse } from '../api/types';
import { bubbleRadius, money, pct, tone, total, trendClass } from '../lib/format';
import { companyPath, Link } from '../lib/router';
import { QuickFact } from './QuickFact';
import { SortHeader, useSortFlip } from './SortHeader';

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
  highlight: Highlight;
  loading: boolean;
  showExecutives: boolean;
  /** Narrows the ranked list only, by name or ticker. */
  find: string; onFind: (s: string) => void;
}

const SORTS: CompanySort[] = ['Revenue', 'Growth', 'Profit', 'Distance'];

export function ListView({ data, placeName, sort, onSort, highlight, loading, showExecutives, onHover, onOpened, find, onFind }: Props) {
  const s = data.summary;
  return (
    <main className={`wrap${loading ? ' loading' : ''}`}>
      <section className="summary" aria-live="polite">
        {s.companyCount > 0 ? (
          <>
            <h1><em>{s.companyCount} public {s.companyCount === 1 ? 'company' : 'companies'}</em> within {data.radiusMiles} miles of {placeName}</h1>
            <span className="stat" title={s.approximate ? 'Some companies report in another currency; converted at approximate rates' : undefined}>
              <b>{total(s.combinedTtmRevenue, s.currency, s.approximate)}</b> combined annual revenue</span>
            <span className="stat"><i className="dot up" /><b>{s.growingCount}</b> growing</span>
            <span className="stat"><b>{s.headquarteredCount}</b> headquartered here</span>
          </>
        ) : (
          <>
            <h1>No public companies within {data.radiusMiles} miles</h1>
            <span className="stat">Try a wider radius or a different sector.</span>
          </>
        )}
      </section>

      {data.items.length > 0 && (
        <>
          <BubbleField items={data.items} highlight={highlight} onHover={onHover} onOpened={onOpened} />
          <QuickFact summary={s} showExecutives={showExecutives} onOpened={onOpened} />
        </>
      )}

      <section className="listcard pane" aria-label="Ranked companies">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {SORTS.map(k => <button key={k} aria-pressed={sort === k} onClick={() => onSort(k)}>{k}</button>)}
          </div>
          <input className="searchbox" type="search" placeholder="Search name or ticker…" aria-label="Search the list by company name or ticker"
            value={find} onChange={e => onFind(e.target.value)} />
        </div>
        <RankedRows items={data.items} sort={sort} onSort={onSort} highlight={highlight} onHover={onHover} onOpened={onOpened} find={find} onFind={onFind} />
      </section>
    </main>
  );
}

/**
 * Every company's bubble in one wide box, spread left to right so it takes the page's width rather than its height.
 * The dropdown narrows the box to one city (nearest first) without splitting it into separate cards.
 */
function BubbleField({ items, highlight, onHover, onOpened }: { items: CompanySummary[]; highlight: Highlight } & ListEvents) {
  const cities = useMemo(() => d3.groups(items, c => cityKey(c.nearestLocation.city))
    .map(([key, list]) => ({ key, name: displayName(list.map(c => c.nearestLocation.city)), count: list.length, nearest: d3.min(list, c => c.distanceMiles)! }))
    .sort((a, b) => a.nearest - b.nearest), [items]);
  const [city, setCity] = useState('');
  const picked = cities.find(c => c.key === city);
  const shown = useMemo(() => picked ? items.filter(c => cityKey(c.nearestLocation.city) === picked.key) : items, [items, picked]);

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

  return (
    <section className="field pane" aria-label="Companies by revenue">
      <div className="field-h">
        <div className="sel-wrap field-city">
          <select aria-label="Show companies in" value={picked ? picked.key : ''} onChange={e => setCity(e.target.value)}>
            <option value="">All cities · {items.length}</option>
            {cities.map(c => <option key={c.key} value={c.key}>{c.name} · {c.count} · {c.nearest.toFixed(1)} mi</option>)}
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
      <div className="field-box" ref={box} style={{ height: field?.height ?? 160 }}>
        {field?.nodes.map(n => (
          <Link key={n.c.ticker} to={companyPath(n.c.ticker)}
            className={`bub t-${trendClass(n.c.indicators.trend)}${n.r < 8 ? ' dot-sm' : ''}${highlight.hovered === n.c.ticker ? ' hl' : ''}${highlight.selected === n.c.ticker ? ' sel' : ''}`}
            style={{ left: n.x - n.r, top: n.y - n.r, width: n.r * 2, height: n.r * 2 }}
            aria-label={`${n.c.name}, ${money(n.c.indicators.ttmRevenue, n.c.currency)} revenue`}
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

interface Placed { c: CompanySummary; r: number; x: number; y: number }

/** Space between bubbles, and how much of a box circles fill when they're packed loosely. */
const BUBBLE_GAP = 3;
const FILL = 0.7;
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
 * Lays the bubbles out in a band as wide as the box (or narrower when there are only a few), biggest first near the
 * middle, none overlapping. A short force simulation, run to the end before anything is drawn, so it doesn't wobble.
 */
function spread(items: CompanySummary[], width: number, maxH: number): { nodes: Placed[]; height: number; shrunk: boolean } {
  const nodes: Placed[] = items
    .map(c => ({ c, r: bubbleRadius(c.indicators.ttmRevenue), x: 0, y: 0 }))
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
  let k = 1;
  for (let pass = 0; pass < 4 && area / width > maxH; pass++) {
    k *= Math.sqrt((maxH * width) / area);
    shrunk = true;
    gap = Math.max(1, BUBBLE_GAP * k);
    nodes.forEach((n, i) => { n.r = Math.max(MIN_DOT, base[i] * k); });
    area = areaOf();
  }
  const tallest = nodes[0].r * 2 + BUBBLE_GAP * 2;
  const bandW = Math.min(width, Math.max(tallest, Math.sqrt(area * ASPECT)));
  const bandH = Math.max(tallest, area / bandW);
  const cx = width / 2, cy = bandH / 2;

  // Start on a sunflower spiral stretched to the band, biggest in the middle.
  const golden = Math.PI * (3 - Math.sqrt(5));
  nodes.forEach((n, i) => {
    const t = Math.sqrt((i + 0.5) / nodes.length);
    n.x = cx + Math.cos(i * golden) * t * (bandW / 2 - n.r);
    n.y = cy + Math.sin(i * golden) * t * (bandH / 2 - n.r);
  });
  const left = cx - bandW / 2;
  const sim = d3.forceSimulation(nodes)
    .force('x', d3.forceX<Placed>(cx).strength(0.006))
    .force('y', d3.forceY<Placed>(cy).strength(0.05))
    .force('collide', d3.forceCollide<Placed>(n => n.r + gap).strength(1).iterations(3))
    .stop();
  const ticks = nodes.length > 600 ? 120 : 260;
  for (let i = 0; i < ticks; i++) {
    sim.tick();
    for (const n of nodes) {
      n.x = Math.max(left + n.r, Math.min(left + bandW - n.r, n.x));
      n.y = Math.max(n.r, Math.min(bandH - n.r, n.y));
    }
  }

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

/** "Société Générale" → "societe generale" — for matching what visitors type. */
const plain = (s: string) => s.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

function RankedRows({ items: all, sort, onSort, highlight, onHover, onOpened, find, onFind }:
  { items: CompanySummary[]; sort: CompanySort; onSort: (s: CompanySort) => void; highlight: Highlight; find: string; onFind: (s: string) => void } & ListEvents) {
  const q = plain(find.trim());
  const items = q ? all.filter(c => plain(c.name).includes(q) || c.ticker.toLowerCase().startsWith(q)) : all;
  const { flipped, choose, order } = useSortFlip(sort, onSort, (c: CompanySummary, k) =>
    k === 'Growth' ? c.indicators.revenueGrowthYoY : k === 'Profit' ? c.indicators.ttmNetIncome : k === 'Revenue' ? c.indicators.ttmRevenue : c.distanceMiles);
  if (all.length === 0) return <div className="empty">Nothing matches. Widen the radius or clear the filters.</div>;
  if (items.length === 0) return (
    <div className="empty">No company here matches “{find.trim()}”. <button className="linkbtn" onClick={() => onFind('')}>Clear</button></div>
  );
  const head = { sort, flipped, onSort: choose };
  return (
    <div className="rows">
      <div className="row head">
        <span>#</span><span className="c-mini" /><span>Company</span>
        <SortHeader k="Distance" {...head} className="c-at">Nearest location</SortHeader>
        <SortHeader k="Revenue" {...head} className="r">Revenue</SortHeader>
        <SortHeader k="Growth" {...head} className="r">Growth</SortHeader>
        <span className="r c-spark">History</span>
        <SortHeader k="Profit" {...head} className="r c-net">Net income</SortHeader>
      </div>
      {order(items).map((c, i) => (
        <Link key={c.ticker} to={companyPath(c.ticker)}
          className={`row${highlight.hovered === c.ticker ? ' hl' : ''}${highlight.selected === c.ticker ? ' sel' : ''}`}
          onMouseEnter={() => onHover(c.ticker)} onMouseLeave={() => onHover(null)} onClick={() => onOpened(c.ticker)}>
          <span className="rank">{i + 1}</span>
          <span className={`mini c-mini t-${trendClass(c.indicators.trend)}`} />
          <span className="who"><b>{c.name}</b>{c.isHeadquarteredNearby && <span className="hq">HQ</span>}<small>{c.ticker} · {c.sector}</small></span>
          <span className="at c-at">{c.nearestLocation.type === 'Headquarters' ? 'Headquarters' : c.nearestLocation.label}<small>{c.nearestLocation.city} · {c.distanceMiles.toFixed(1)} mi</small></span>
          <span className="val">{money(c.indicators.ttmRevenue, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
          <span className="r"><span className={`pill ${tone(c.indicators.revenueGrowthYoY)}`}>{pct(c.indicators.revenueGrowthYoY)}</span></span>
          <span className="r c-spark spark"><Sparkline points={c.indicators.revenueHistory.map(p => p.revenue)} /></span>
          <span className={`val c-net${c.indicators.ttmNetIncome < 0 ? ' neg' : ''}`}>{money(c.indicators.ttmNetIncome, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
        </Link>
      ))}
      {q && (
        <div className="more-row">
          <span>Showing {items.length} of {all.length}</span>
          <button className="linkbtn" onClick={() => onFind('')}>Clear</button>
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
