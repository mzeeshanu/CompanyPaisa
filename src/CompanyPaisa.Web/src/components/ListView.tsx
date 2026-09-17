import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import * as d3 from 'd3';
import type { CompanySort, CompanySummary, NearbyResponse } from '../api/types';
import { bubbleRadius, money, pct, tone, total, trendClass } from '../lib/format';
import { QuickFact } from './QuickFact';
import { SortHeader, useSortFlip } from './SortHeader';

export interface Highlight { selected: string | null; hovered: string | null }
export interface BubbleEvents {
  onHover: (ticker: string | null, el?: HTMLElement) => void;
  onSelect: (ticker: string) => void;
}

interface Props extends BubbleEvents {
  data: NearbyResponse;
  placeName: string;
  sort: CompanySort; onSort: (s: CompanySort) => void;
  highlight: Highlight;
  loading: boolean;
  showExecutives: boolean;
  onOpenPerson: (personId: string) => void;
}

const SORTS: CompanySort[] = ['Revenue', 'Growth', 'Profit', 'Distance'];

/** Phones start with one merged box (city boxes take a lot of scrolling there); wider screens start split by city. */
const PHONE = '(max-width: 720px)';

export function ListView({ data, placeName, sort, onSort, highlight, loading, showExecutives, onOpenPerson, onHover, onSelect }: Props) {
  const s = data.summary;
  const [merged, setMerged] = useState(() => typeof window !== 'undefined' && window.matchMedia(PHONE).matches);
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
          <div className="sec-h">
            <div className="sec-title">
              <h2>{merged ? 'All nearby companies' : 'By city · nearest first'}</h2>
              <div className="chips group-toggle" role="group" aria-label="Group bubbles">
                <button aria-pressed={!merged} onClick={() => setMerged(false)}>By city</button>
                <button aria-pressed={merged} onClick={() => setMerged(true)}>All in one</button>
              </div>
            </div>
            <div className="legend-row">
              <span>Bubble size = annual revenue</span>
              <span><i className="dot up" />Growing</span>
              <span><i className="dot flat" />Flat</span>
              <span><i className="dot down" />Shrinking or losing money</span>
            </div>
          </div>
          <CityClusters items={data.items} merged={merged} allLabel={placeName} highlight={highlight} onHover={onHover} onSelect={onSelect} />
          <QuickFact summary={s} showExecutives={showExecutives} onOpenPerson={onOpenPerson} />
        </>
      )}

      <section className="listcard pane" aria-label="Ranked companies">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {SORTS.map(k => <button key={k} aria-pressed={sort === k} onClick={() => onSort(k)}>{k}</button>)}
          </div>
        </div>
        <RankedRows items={data.items} sort={sort} onSort={onSort} highlight={highlight} onHover={onHover} onSelect={onSelect} />
      </section>
    </main>
  );
}

/** Bubble packs per city, or (merged) one pack of every company labelled with the search place. */
function CityClusters({ items, merged, allLabel, highlight, onHover, onSelect }:
  { items: CompanySummary[]; merged: boolean; allLabel: string; highlight: Highlight } & BubbleEvents) {
  const groups = useMemo(() => {
    const byCity = merged
      ? [[`Near ${allLabel}`, items] as const]
      : d3.groups(items, c => cityKey(c.nearestLocation.city)).map(([, list]) => [displayName(list.map(c => c.nearestLocation.city)), list] as const);
    return byCity
      .map(([city, list]) => {
        const nodes = list.map(c => ({ c, r: bubbleRadius(c.indicators.ttmRevenue), x: 0, y: 0 })).sort((a, b) => b.r - a.r);
        d3.packSiblings(nodes);
        const enc = d3.packEnclose(nodes)!;
        return { city, nodes, enc, size: Math.ceil(enc.r * 2 + 12), nearest: d3.min(list, c => c.distanceMiles)! };
      })
      .sort((a, b) => a.nearest - b.nearest);
  }, [items, merged, allLabel]);

  // Masonry: cards snap to whole columns (so every row ends flush at both edges) and to 1px rows sized from each card's
  // measured height; "dense" placement drops small cities into the holes beside big ones. A big pack shrinks to fit.
  const box = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const cards = useRef(new Map<string, HTMLDivElement>());
  const [heights, setHeights] = useState<Record<string, number>>({});
  useEffect(() => {
    const el = box.current;
    if (!el) return;
    const measure = () => setWidth(el.clientWidth);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    window.addEventListener('resize', measure);   // rotation / window resizes, in case the observer misses them
    return () => { ro.disconnect(); window.removeEventListener('resize', measure); };
  }, []);

  const cols = width < 600 ? 1 : Math.max(1, Math.floor((width + GAP) / (COL_MIN + GAP)));
  const colWidth = cols > 0 && width > 0 ? (width - GAP * (cols - 1)) / cols : 0;
  const layout = groups.map(g => {
    const span = width === 0 ? cols : Math.min(cols, Math.ceil((Math.max(CARD_MIN, g.size + CITY_PADDING) + GAP) / (colWidth + GAP)));
    const cardWidth = span * colWidth + (span - 1) * GAP;
    const scale = width === 0 ? 1 : Math.min(1, (cardWidth - CITY_PADDING) / g.size);
    return { g, span, scale };
  });

  useLayoutEffect(() => {
    const next: Record<string, number> = {};
    cards.current.forEach((el, city) => { next[city] = Math.ceil(el.getBoundingClientRect().height); });
    setHeights(prev => Object.keys(next).length === Object.keys(prev).length && Object.entries(next).every(([k, v]) => prev[k] === v) ? prev : next);
  });

  return (
    <div className="clusters" ref={box} style={{ gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))` }}>
      {layout.map(({ g, span, scale }) => {
        const shown = g.size * scale;
        return (
          <div className="city pane" key={g.city}
            ref={el => { if (el) cards.current.set(g.city, el); else cards.current.delete(g.city); }}
            style={{ gridColumn: `span ${span}`, gridRowEnd: `span ${(heights[g.city] ?? shown + 70) + GAP}` }}>
            <div className="city-h"><b>{g.city}</b><span>{g.nodes.length} · {g.nearest.toFixed(1)} mi</span></div>
            <div className="pack-fit" style={{ width: shown, height: shown }}>
              <div className="pack" style={{ width: g.size, height: g.size, transform: scale < 1 ? `scale(${scale})` : undefined }}>
                {g.nodes.map(n => (
                  <button key={n.c.ticker}
                    className={`bub t-${trendClass(n.c.indicators.trend)}${highlight.hovered === n.c.ticker ? ' hl' : ''}${highlight.selected === n.c.ticker ? ' sel' : ''}`}
                    style={{ left: n.x - g.enc.x + g.size / 2 - n.r, top: n.y - g.enc.y + g.size / 2 - n.r, width: n.r * 2, height: n.r * 2 }}
                    aria-label={`${n.c.name}, ${money(n.c.indicators.ttmRevenue, n.c.currency)} revenue`}
                    onMouseEnter={e => onHover(n.c.ticker, e.currentTarget)} onMouseLeave={() => onHover(null)}
                    onFocus={e => onHover(n.c.ticker, e.currentTarget)} onBlur={() => onHover(null)}
                    onClick={() => onSelect(n.c.ticker)}>
                    <span className="glass" />
                    <span className="tk" style={{ fontSize: Math.max(8.5, Math.min(16, n.r * 0.38)) }}>{n.c.ticker}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

/** Left + right padding and border of a city card (.city in styles.css). */
const CITY_PADDING = 34;
/** Grid column minimum, gap between cards (matches .clusters in styles.css) and the narrowest card (fits a city name and count). */
const COL_MIN = 100;
const GAP = 14;
const CARD_MIN = 200;

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

function RankedRows({ items, sort, onSort, highlight, onHover, onSelect }: { items: CompanySummary[]; sort: CompanySort; onSort: (s: CompanySort) => void; highlight: Highlight } & BubbleEvents) {
  const { flipped, choose, order } = useSortFlip(sort, onSort, (c: CompanySummary, k) =>
    k === 'Growth' ? c.indicators.revenueGrowthYoY : k === 'Profit' ? c.indicators.ttmNetIncome : k === 'Revenue' ? c.indicators.ttmRevenue : c.distanceMiles);
  if (items.length === 0) return <div className="empty">Nothing matches. Widen the radius or clear the filters.</div>;
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
        <button key={c.ticker}
          className={`row${highlight.hovered === c.ticker ? ' hl' : ''}${highlight.selected === c.ticker ? ' sel' : ''}`}
          onMouseEnter={() => onHover(c.ticker)} onMouseLeave={() => onHover(null)} onClick={() => onSelect(c.ticker)}>
          <span className="rank">{i + 1}</span>
          <span className={`mini c-mini t-${trendClass(c.indicators.trend)}`} />
          <span className="who"><b>{c.name}</b>{c.isHeadquarteredNearby && <span className="hq">HQ</span>}<small>{c.ticker} · {c.sector}</small></span>
          <span className="at c-at">{c.nearestLocation.type === 'Headquarters' ? 'Headquarters' : c.nearestLocation.label}<small>{c.nearestLocation.city} · {c.distanceMiles.toFixed(1)} mi</small></span>
          <span className="val">{money(c.indicators.ttmRevenue, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
          <span className="r"><span className={`pill ${tone(c.indicators.revenueGrowthYoY)}`}>{pct(c.indicators.revenueGrowthYoY)}</span></span>
          <span className="r c-spark spark"><Sparkline points={c.indicators.revenueHistory.map(p => p.revenue)} /></span>
          <span className={`val c-net${c.indicators.ttmNetIncome < 0 ? ' neg' : ''}`}>{money(c.indicators.ttmNetIncome, c.currency)}<small>{c.indicators.latestQuarterLabel ? '12 mo' : 'year'}</small></span>
        </button>
      ))}
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
