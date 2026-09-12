import { useMemo } from 'react';
import * as d3 from 'd3';
import type { CompanySort, CompanySummary, NearbyResponse } from '../api/types';
import { bubbleRadius, money, pct, tone, trendClass } from '../lib/format';

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
}

const SORTS: CompanySort[] = ['Revenue', 'Growth', 'Profit', 'Distance'];

export function ListView({ data, placeName, sort, onSort, highlight, loading, onHover, onSelect }: Props) {
  const s = data.summary;
  return (
    <main className={`wrap${loading ? ' loading' : ''}`}>
      <section className="summary" aria-live="polite">
        {s.companyCount > 0 ? (
          <>
            <h1><em>{s.companyCount} public {s.companyCount === 1 ? 'company' : 'companies'}</em> within {data.radiusMiles} miles of {placeName}</h1>
            <span className="stat"><b>{money(s.combinedTtmRevenue)}</b> combined revenue, last 12 months</span>
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
            <h2>By city · nearest first</h2>
            <div className="legend-row">
              <span>Bubble size = annual revenue</span>
              <span><i className="dot up" />Growing</span>
              <span><i className="dot flat" />Flat</span>
              <span><i className="dot down" />Shrinking or losing money</span>
            </div>
          </div>
          <CityClusters items={data.items} highlight={highlight} onHover={onHover} onSelect={onSelect} />
        </>
      )}

      <section className="listcard pane" aria-label="Ranked companies">
        <div className="toolbar">
          <span className="lbl">Rank by</span>
          <div className="chips" role="group" aria-label="Rank by">
            {SORTS.map(k => <button key={k} aria-pressed={sort === k} onClick={() => onSort(k)}>{k}</button>)}
          </div>
        </div>
        <RankedRows items={data.items} sort={sort} highlight={highlight} onHover={onHover} onSelect={onSelect} />
      </section>
    </main>
  );
}

function CityClusters({ items, highlight, onHover, onSelect }: { items: CompanySummary[]; highlight: Highlight } & BubbleEvents) {
  const groups = useMemo(() => {
    return d3.groups(items, c => c.nearestLocation.city)
      .map(([city, list]) => {
        const nodes = list.map(c => ({ c, r: bubbleRadius(c.indicators.ttmRevenue), x: 0, y: 0 })).sort((a, b) => b.r - a.r);
        d3.packSiblings(nodes);
        const enc = d3.packEnclose(nodes)!;
        return { city, nodes, enc, size: Math.ceil(enc.r * 2 + 12), nearest: d3.min(list, c => c.distanceMiles)! };
      })
      .sort((a, b) => a.nearest - b.nearest);
  }, [items]);

  return (
    <div className="clusters">
      {groups.map(g => (
        <div className="city pane" key={g.city}>
          <div className="city-h"><b>{g.city}</b><span>{g.nodes.length} · {g.nearest.toFixed(1)} mi</span></div>
          <div className="pack" style={{ width: g.size, height: g.size }}>
            {g.nodes.map(n => (
              <button key={n.c.ticker}
                className={`bub t-${trendClass(n.c.indicators.trend)}${highlight.hovered === n.c.ticker ? ' hl' : ''}${highlight.selected === n.c.ticker ? ' sel' : ''}`}
                style={{ left: n.x - g.enc.x + g.size / 2 - n.r, top: n.y - g.enc.y + g.size / 2 - n.r, width: n.r * 2, height: n.r * 2 }}
                aria-label={`${n.c.name}, ${money(n.c.indicators.ttmRevenue)} revenue`}
                onMouseEnter={e => onHover(n.c.ticker, e.currentTarget)} onMouseLeave={() => onHover(null)}
                onFocus={e => onHover(n.c.ticker, e.currentTarget)} onBlur={() => onHover(null)}
                onClick={() => onSelect(n.c.ticker)}>
                <span className="glass" />
                <span className="tk" style={{ fontSize: Math.max(8.5, Math.min(16, n.r * 0.38)) }}>{n.c.ticker}</span>
              </button>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

function RankedRows({ items, sort, highlight, onHover, onSelect }: { items: CompanySummary[]; sort: CompanySort; highlight: Highlight } & BubbleEvents) {
  if (items.length === 0) return <div className="empty">Nothing matches. Widen the radius or clear the filters.</div>;
  const sorted = (k: CompanySort) => (sort === k ? ' sorted' : '');
  return (
    <div className="rows">
      <div className="row head" aria-hidden="true">
        <span>#</span><span className="c-mini" /><span>Company</span><span className={`c-at${sorted('Distance')}`}>Nearest location</span>
        <span className={`r${sorted('Revenue')}`}>Revenue</span><span className={`r${sorted('Growth')}`}>Growth</span>
        <span className="r c-spark">History</span><span className={`r c-net${sorted('Profit')}`}>Net income</span>
      </div>
      {items.map((c, i) => (
        <button key={c.ticker}
          className={`row${highlight.hovered === c.ticker ? ' hl' : ''}${highlight.selected === c.ticker ? ' sel' : ''}`}
          onMouseEnter={() => onHover(c.ticker)} onMouseLeave={() => onHover(null)} onClick={() => onSelect(c.ticker)}>
          <span className="rank">{i + 1}</span>
          <span className={`mini c-mini t-${trendClass(c.indicators.trend)}`} />
          <span className="who"><b>{c.name}</b>{c.isHeadquarteredNearby && <span className="hq">HQ</span>}<small>{c.ticker} · {c.sector}</small></span>
          <span className="at c-at">{c.nearestLocation.type === 'Headquarters' ? 'Headquarters' : c.nearestLocation.label}<small>{c.nearestLocation.city} · {c.distanceMiles.toFixed(1)} mi</small></span>
          <span className="val">{money(c.indicators.ttmRevenue)}<small>12 mo</small></span>
          <span className="r"><span className={`pill ${tone(c.indicators.revenueGrowthYoY)}`}>{pct(c.indicators.revenueGrowthYoY)}</span></span>
          <span className="r c-spark spark"><Sparkline points={c.indicators.revenueHistory.map(p => p.revenue)} /></span>
          <span className={`val c-net${c.indicators.ttmNetIncome < 0 ? ' neg' : ''}`}>{money(c.indicators.ttmNetIncome)}<small>12 mo</small></span>
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
