import * as d3 from 'd3';
import type { CompanySummary, GeoPoint } from '../api/types';
import { bubbleRadius, money, trendClass } from './format';
import { GEO } from './baseMap';

type P = [number, number];

interface Node extends d3.SimulationNodeDatum {
  c: CompanySummary; r: number; tx?: number; ty?: number;
}

export interface MapCallbacks {
  onHover: (ticker: string | null, el?: HTMLElement) => void;
  onSelect: (ticker: string) => void;
  onBackground: () => void;
  onUserZoom: () => void;
}

const RINGS = [1, 2, 5, 10, 25, 50];
const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

/**
 * Imperative D3 renderer for the Map view (zoom/pan + collision layout run every frame, so it
 * lives outside React's render cycle). React owns the overlays and calls the methods below.
 */
export class MapController {
  private W = 800; private H = 600; private cx = 400; private cy = 300; private ppm = 10;
  private transform = d3.zoomIdentity;
  private origin: GeoPoint = { latitude: 0, longitude: 0 };
  private radius = 10;
  private showMap = true;
  private nodes: Node[] = [];
  private readonly nodeBy = new Map<string, Node>();
  private hovered: Node | null = null;
  private selected: string | null = null;
  private highlighted: string | null = null;

  private readonly svg: d3.Selection<SVGSVGElement, unknown, null, undefined>;
  private readonly gMap; private readonly gRings; private readonly gLink; private readonly gYou;
  private readonly stage: d3.Selection<HTMLDivElement, unknown, null, undefined>;
  private readonly bubbles: d3.Selection<HTMLDivElement, unknown, null, undefined>;
  private readonly zoom: d3.ZoomBehavior<HTMLDivElement, unknown>;
  private readonly sim: d3.Simulation<Node, undefined>;
  private readonly lineOpen = d3.line().curve(d3.curveCatmullRom);
  private readonly lineClosed = d3.line().curve(d3.curveCatmullRomClosed);

  constructor(stageEl: HTMLDivElement, svgEl: SVGSVGElement, bubblesEl: HTMLDivElement, private readonly cb: MapCallbacks) {
    this.stage = d3.select(stageEl);
    this.svg = d3.select(svgEl);
    this.bubbles = d3.select(bubblesEl);
    this.svg.selectAll('*').remove();       // never draw on top of a previous instance
    this.bubbles.selectAll('*').remove();

    this.svg.append('defs').append('filter').attr('id', 'soft').attr('x', '-20%').attr('y', '-20%').attr('width', '140%').attr('height', '140%')
      .append('feGaussianBlur').attr('stdDeviation', 12);
    this.gMap = this.svg.append('g');
    this.gRings = this.svg.append('g');
    this.gLink = this.svg.append('g');
    this.gYou = this.svg.append('g');
    this.gYou.append('circle').attr('class', 'you-glow').attr('r', 22);
    this.gYou.append('circle').attr('class', 'you-core').attr('r', 7);
    this.gYou.append('text').attr('class', 'you-label').attr('y', 26).attr('text-anchor', 'middle').text('YOU');

    this.sim = d3.forceSimulation<Node>().stop()
      .force('x', d3.forceX<Node>(d => d.tx ?? 0).strength(0.28))
      .force('y', d3.forceY<Node>(d => d.ty ?? 0).strength(0.28))
      .force('collide', d3.forceCollide<Node>(d => d.r + 4).strength(0.9));

    this.zoom = d3.zoom<HTMLDivElement, unknown>().scaleExtent([0.15, 10]).on('zoom', e => {
      this.transform = e.transform;
      this.render();
      if (e.sourceEvent) cb.onUserZoom();
    });
    this.stage.call(this.zoom).on('dblclick.zoom', null);
    this.stage.on('click', () => cb.onBackground());
    this.resize();
  }

  resize() {
    const el = this.stage.node()!;
    this.W = el.clientWidth || innerWidth;
    this.H = el.clientHeight || innerHeight;
    this.cx = this.W / 2; this.cy = this.H / 2;
    this.ppm = (Math.min(this.W, this.H) * 0.42) / 12;   // 12 miles fills the view at zoom 1
    this.svg.attr('viewBox', `0 0 ${this.W} ${this.H}`);
    this.render();
  }

  setData(items: CompanySummary[], origin: GeoPoint, radius: number) {
    const moved = origin.latitude !== this.origin.latitude || origin.longitude !== this.origin.longitude;
    this.origin = origin;
    const radiusChanged = radius !== this.radius;
    this.radius = radius;
    if (moved) this.nodeBy.forEach(n => { n.tx = undefined; });

    this.nodes = items.map(c => {
      let n = this.nodeBy.get(c.ticker);
      if (!n) { n = { c, r: bubbleRadius(c.indicators.ttmRevenue) }; this.nodeBy.set(c.ticker, n); }
      n.c = c;
      return n;
    });
    this.sim.nodes(this.nodes);

    const cb = this.cb;
    this.bubbles.selectAll<HTMLButtonElement, Node>('button.bub')
      .data(this.nodes, d => d.c.ticker)
      .join(enter => {
        const el = enter.append('button')
          .attr('data-ticker', d => d.c.ticker)
          .style('width', d => `${d.r * 2}px`).style('height', d => `${d.r * 2}px`);
        el.append('span').attr('class', 'glass');
        el.append('span').attr('class', 'tk').style('font-size', d => `${Math.max(9, Math.min(16, d.r * 0.38))}px`).text(d => d.c.ticker);
        el.append('span').attr('class', 'nm').text(d => d.c.name);
        el.on('mouseenter focus', (e: Event, d) => { this.hovered = d; this.drawLink(); cb.onHover(d.c.ticker, e.currentTarget as HTMLElement); })
          .on('mouseleave blur', () => { this.hovered = null; this.drawLink(); cb.onHover(null); })
          .on('click', (e: Event, d) => { e.stopPropagation(); cb.onSelect(d.c.ticker); });
        return el;
      })
      .attr('class', d => `bub t-${trendClass(d.c.indicators.trend)}`)
      .attr('aria-label', d => `${d.c.name}, revenue ${money(d.c.indicators.ttmRevenue)}`);

    if (moved || radiusChanged) this.fit(!moved); else this.render();
  }

  setShowMap(show: boolean) { this.showMap = show; this.render(); }

  setHighlight(selected: string | null, hovered: string | null) {
    this.selected = selected; this.highlighted = hovered;
    this.bubbles.selectAll<HTMLButtonElement, Node>('button.bub')
      .classed('sel', d => d.c.ticker === selected)
      .classed('hl', d => d.c.ticker === hovered);
  }

  zoomBy(factor: number) { this.stage.transition().duration(450).call(this.zoom.scaleBy, factor, [this.cx, this.cy]); }

  /** Zoom so the chosen search radius fills the view. */
  fit(animate = true) {
    const k = 12 / this.radius;
    const t = d3.zoomIdentity.translate(this.cx, this.cy).scale(k).translate(-this.cx, -this.cy);
    if (animate && !reducedMotion()) this.stage.transition().duration(650).call(this.zoom.transform, t);
    else this.stage.call(this.zoom.transform, t);
  }

  /** Undo everything the constructor did, so a remount (React StrictMode, view switches) starts clean. */
  destroy() {
    this.stage.interrupt().on('.zoom', null).on('click', null);
    this.sim.stop();
    this.svg.selectAll('*').remove();
    this.bubbles.selectAll('*').remove();
    this.nodeBy.clear();
  }

  // ---------------- rendering ----------------

  private project(lat: number, lng: number): P {
    const dx = (lng - this.origin.longitude) * 69.17 * Math.cos((this.origin.latitude * Math.PI) / 180);
    const dy = (lat - this.origin.latitude) * 69.0;
    return this.transform.apply([this.cx + dx * this.ppm, this.cy - dy * this.ppm]) as P;
  }

  private render() {
    const [ux, uy] = this.transform.apply([this.cx, this.cy]);
    const k = this.transform.k, maxR = Math.hypot(this.W, this.H) * 1.6;
    this.renderBaseMap(k);

    const rings = [...new Set([...RINGS, this.radius])].sort((a, b) => a - b)
      .map(m => ({ m, r: m * this.ppm * k, limit: m === this.radius }))
      .filter(d => d.r > 30 && d.r < maxR);
    this.gRings.selectAll('circle.ring-shade').data(rings, (d: any) => d.m).join('circle').attr('class', 'ring-shade').attr('cx', ux).attr('cy', uy).attr('r', d => d.r);
    this.gRings.selectAll('circle.ring').data(rings, (d: any) => d.m).join('circle').attr('class', d => `ring${d.limit ? ' limit' : ''}`).attr('cx', ux).attr('cy', uy).attr('r', d => d.r);
    this.gRings.selectAll('text').data(rings, (d: any) => d.m).join('text').attr('class', d => `ring-label${d.limit ? ' limit' : ''}`)
      .attr('text-anchor', 'middle').attr('x', ux).attr('y', d => uy - d.r - 7).text(d => `${d.m} mi${d.limit ? ' · your radius' : ''}`);
    this.gYou.attr('transform', `translate(${ux},${uy})`);

    for (const n of this.nodes) {
      const [tx, ty] = this.project(n.c.nearestLocation.point.latitude, n.c.nearestLocation.point.longitude);
      if (n.tx == null || !Number.isFinite(n.x) || !Number.isFinite(n.vx)) { n.x = tx; n.y = ty; n.vx = 0; n.vy = 0; }
      else { n.x! += tx - n.tx; n.y! += ty - n.ty!; }
      n.tx = tx; n.ty = ty;
    }
    (this.sim.force('x') as d3.ForceX<Node>).x(d => d.tx ?? 0);
    (this.sim.force('y') as d3.ForceY<Node>).y(d => d.ty ?? 0);
    this.sim.alpha(0.6);
    for (let i = 0; i < 36; i++) this.sim.tick();

    this.bubbles.selectAll<HTMLButtonElement, Node>('button.bub')
      .style('transform', d => `translate3d(${d.x! - d.r}px,${d.y! - d.r}px,0)`)
      .classed('named', d => d.r >= 30 || k >= 1.9)
      .classed('sel', d => d.c.ticker === this.selected)
      .classed('hl', d => d.c.ticker === this.highlighted);
    this.drawLink();
  }

  private drawLink() {
    const [ux, uy] = this.transform.apply([this.cx, this.cy]);
    const d = this.hovered ? [this.hovered] : [];
    this.gLink.selectAll('line').data(d).join('line').attr('class', 'link').attr('x1', ux).attr('y1', uy).attr('x2', n => n.x!).attr('y2', n => n.y!);
    this.gLink.selectAll('text').data(d).join('text').attr('class', 'link-label').attr('text-anchor', 'middle')
      .attr('x', n => (ux + n.x!) / 2).attr('y', n => (uy + n.y!) / 2 - 7).text(n => `${n.c.distanceMiles.toFixed(1)} mi`);
  }

  private renderBaseMap(k: number) {
    this.gMap.attr('display', this.showMap ? null : 'none');
    if (!this.showMap) return;
    const P = (p: P) => this.project(p[0], p[1]);
    this.gMap.selectAll('path.ridge').data(GEO.ridges).join('path').attr('class', 'ridge')
      .attr('d', d => this.lineOpen(d.pts.map(P))).attr('stroke-width', Math.max(26, Math.min(90, 34 * k)));
    this.gMap.selectAll('path.lake').data(GEO.lakes).join('path').attr('class', 'lake').attr('d', d => this.lineClosed(d.pts.map(P)));
    this.gMap.selectAll('path.river').data(GEO.rivers).join('path').attr('class', 'river').attr('d', d => this.lineOpen(d.map(P)));
    this.gMap.selectAll('path.road-under').data(GEO.roads).join('path').attr('class', 'road-under').attr('d', d => this.lineOpen(d.pts.map(P)));
    this.gMap.selectAll('path.road').data(GEO.roads).join('path').attr('class', 'road').attr('d', d => this.lineOpen(d.pts.map(P)));
    const shields = this.gMap.selectAll<SVGGElement, (typeof GEO.roads)[number]>('g.shield').data(GEO.roads).join(e => {
      const g = e.append('g').attr('class', 'shield');
      g.append('rect').attr('rx', 5).attr('height', 16).attr('y', -8);
      g.append('text').attr('dy', '.35em').attr('text-anchor', 'middle');
      return g;
    });
    shields.attr('transform', d => `translate(${P(d.pts[d.at])})`);
    shields.select('text').text(d => d.name);
    shields.select('rect').attr('width', d => d.name.length * 6.6 + 10).attr('x', d => -(d.name.length * 6.6 + 10) / 2);
    this.gMap.selectAll('text.geo').data([...GEO.lakes, ...GEO.ridges]).join('text').attr('class', 'geo').attr('text-anchor', 'middle')
      .attr('transform', d => `translate(${P(d.at)})`).text(d => d.name);
    const cities = GEO.cities.filter(c => c[3] || k >= 0.55);
    this.gMap.selectAll('text.cityname').data(cities, (d: any) => d[0]).join('text').attr('class', d => `cityname${d[3] ? ' major' : ''}`)
      .attr('text-anchor', 'middle').attr('transform', d => `translate(${P([d[1], d[2]])})`).text(d => (d[3] ? d[0].toUpperCase() : d[0]));
  }
}
