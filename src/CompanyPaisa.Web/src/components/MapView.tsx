import { useEffect, useRef, useState } from 'react';
import type { NearbyResponse } from '../api/types';
import { bubbleRadius, money } from '../lib/format';
import { MapController } from '../lib/mapController';
import type { BubbleEvents, Highlight } from './ListView';

interface Props extends BubbleEvents {
  data: NearbyResponse;
  highlight: Highlight;
  showBaseMap: boolean; onToggleBaseMap: () => void;
  onBackground: () => void;
}

export function MapView({ data, highlight, showBaseMap, onToggleBaseMap, onBackground, onHover, onSelect }: Props) {
  const stageRef = useRef<HTMLDivElement>(null);
  const svgRef = useRef<SVGSVGElement>(null);
  const bubblesRef = useRef<HTMLDivElement>(null);
  const ctl = useRef<MapController | null>(null);
  const handlers = useRef({ onHover, onSelect, onBackground });
  handlers.current = { onHover, onSelect, onBackground };
  const [hint, setHint] = useState(true);

  // Create the D3 controller once; route its events to the latest React props.
  useEffect(() => {
    const c = new MapController(stageRef.current!, svgRef.current!, bubblesRef.current!, {
      onHover: (t, el) => handlers.current.onHover(t, el),
      onSelect: t => handlers.current.onSelect(t),
      onBackground: () => handlers.current.onBackground(),
      onUserZoom: () => setHint(false),
    });
    ctl.current = c;
    const ro = new ResizeObserver(() => c.resize());
    ro.observe(stageRef.current!);
    const timer = setTimeout(() => setHint(false), 12000);
    return () => { ro.disconnect(); clearTimeout(timer); c.destroy(); ctl.current = null; };
  }, []);

  useEffect(() => { ctl.current?.setData(data.items, data.origin, data.radiusMiles); }, [data]);
  useEffect(() => { ctl.current?.setShowMap(showBaseMap); }, [showBaseMap]);
  useEffect(() => { ctl.current?.setHighlight(highlight.selected, highlight.hovered); }, [highlight.selected, highlight.hovered]);

  const s = data.summary;
  return (
    <section className="mapview" aria-label="Map of nearby companies">
      <div className="stage" ref={stageRef}>
        <svg ref={svgRef} aria-hidden="true" />
        <div className="bubbles" ref={bubblesRef} />
      </div>

      <div className="ov m-sum pane">
        {s.companyCount > 0 ? (
          <>
            <b>{s.companyCount} {s.companyCount === 1 ? 'company' : 'companies'} within {data.radiusMiles} mi</b>
            <span><b>{money(s.combinedTtmRevenue)}</b> revenue</span>
            <span><i className="dot up" /> <b>{s.growingCount}</b> growing</span>
            <span><b>{s.headquarteredCount}</b> HQ here</span>
          </>
        ) : <><b>No companies within {data.radiusMiles} mi</b><span>Try a wider radius.</span></>}
      </div>

      <div className="ov m-ctl pane">
        <button className="iconbtn" onClick={() => ctl.current?.zoomBy(1 / 1.7)} aria-label="Zoom out">−</button>
        <button className="iconbtn" onClick={() => ctl.current?.zoomBy(1.7)} aria-label="Zoom in">+</button>
        <button className="iconbtn" onClick={() => ctl.current?.fit()} aria-label="Fit to search radius">◎</button>
        <button className="iconbtn" aria-pressed={showBaseMap} onClick={onToggleBaseMap} aria-label="Show roads, lakes and cities">Map</button>
      </div>

      <div className="ov m-leg pane">
        <div>
          <h4>Size · annual revenue</h4>
          <div className="sizes">
            {([[1e8, '$100M'], [1e9, '$1B'], [1e10, '$10B'], [5e10, '$50B']] as const).map(([v, l]) => {
              const d = bubbleRadius(v) * 1.1;
              return <div key={l}><i style={{ width: d, height: d }} />{l}</div>;
            })}
          </div>
        </div>
        <div>
          <h4>Tint · trend</h4>
          <div className="glows">
            <span><i className="dot up" />Growing &amp; profitable</span>
            <span><i className="dot flat" />Flat</span>
            <span><i className="dot down" />Shrinking or losing money</span>
          </div>
        </div>
      </div>

      <div className={`ov m-hint pane${hint ? '' : ' gone'}`}>Scroll or pinch to zoom · drag to move · tap a bubble</div>
    </section>
  );
}
