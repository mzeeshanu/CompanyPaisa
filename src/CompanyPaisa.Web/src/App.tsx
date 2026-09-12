import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from './api/client';
import type { ClientConfig, CompanySort, CompanySummary, DataMeta, NearbyResponse } from './api/types';
import { CompanyPanel } from './components/CompanyPanel';
import { ConsentBanner } from './components/ConsentBanner';
import { ListView } from './components/ListView';
import { LocationGate, type Origin } from './components/LocationGate';
import { MapView } from './components/MapView';
import { TopBar } from './components/TopBar';
import { money, pct } from './lib/format';
import {
  applyTheme, clearPrefs, configurePrefs, readPrefs, readSessionConsent, writePrefs, writeSessionConsent,
  type Consent, type Theme, type View,
} from './lib/prefs';

const FALLBACK_CONFIG: ClientConfig = {
  defaultView: 'List', defaultTheme: 'Auto', defaultRadiusMiles: 10, allowedRadiiMiles: [5, 10, 25, 50],
  defaultSort: 'Revenue', showBaseMapByDefault: true, mapTilesUrl: null,
  consentCookieName: 'cp_prefs', consentCookieDays: 365, features: { MapView: true, ListView: true, Executives: true },
};
const COVERAGE_MILES = 60;

interface Tip { company: CompanySummary; x: number; y: number }

export default function App() {
  const [config, setConfig] = useState<ClientConfig | null>(null);
  const [meta, setMeta] = useState<DataMeta | null>(null);
  const [sectors, setSectors] = useState<string[]>([]);
  const [bootError, setBootError] = useState('');

  const [origin, setOrigin] = useState<Origin | null>(null);
  const [gateOpen, setGateOpen] = useState(true);
  const [radius, setRadius] = useState(10);
  const [sector, setSector] = useState('');
  const [hqOnly, setHqOnly] = useState(false);
  const [sort, setSort] = useState<CompanySort>('Revenue');

  const [view, setView] = useState<View>('list');
  const [theme, setTheme] = useState<Theme>('auto');
  const [showBaseMap, setShowBaseMap] = useState(true);
  const [consent, setConsent] = useState<Consent>(null);
  const [showConsent, setShowConsent] = useState(false);

  const [data, setData] = useState<NearbyResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState<string | null>(null);
  const [hovered, setHovered] = useState<string | null>(null);
  const [tip, setTip] = useState<Tip | null>(null);
  const header = useRef<HTMLElement>(null);

  // ---- boot: settings come from the API (appsettings.json), preferences from the consent cookie ----
  useEffect(() => {
    Promise.all([api.clientConfig().catch(() => FALLBACK_CONFIG), api.meta().catch(() => null), api.sectors().catch(() => [])])
      .then(([cfg, m, secs]) => {
        configurePrefs(cfg.consentCookieName, cfg.consentCookieDays);
        const saved = readPrefs();
        setConfig(cfg);
        setMeta(m);
        setSectors(secs);
        setRadius(cfg.defaultRadiusMiles);
        setSort(cfg.defaultSort);
        setView(saved?.view ?? (cfg.defaultView === 'Map' && cfg.features.MapView !== false ? 'map' : 'list'));
        setTheme(saved?.theme ?? (cfg.defaultTheme.toLowerCase() as Theme));
        setShowBaseMap(saved?.map ?? cfg.showBaseMapByDefault);
        setConsent(saved ? 'yes' : readSessionConsent());
      })
      .catch(() => setBootError("We couldn't reach the CompanyPaisa service. Is the API running?"));
  }, []);

  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => { if (consent === 'yes') writePrefs({ view, theme, map: showBaseMap }); }, [consent, view, theme, showBaseMap]);
  useEffect(() => {
    document.body.classList.toggle('locked', gateOpen);
    document.body.classList.toggle('view-map', view === 'map');
  }, [gateOpen, view]);

  // The Map view sits under the sticky header; keep its offset in sync with the header's height.
  useLayoutEffect(() => {
    const el = header.current;
    if (!el) return;
    const ro = new ResizeObserver(() => document.documentElement.style.setProperty('--hdr', `${el.getBoundingClientRect().height}px`));
    ro.observe(el);
    return () => ro.disconnect();
  }, [config]);

  // ---- search whenever location or filters change ----
  useEffect(() => {
    if (!origin) return;
    const ctrl = new AbortController();
    setLoading(true); setError('');
    api.near({ latitude: origin.latitude, longitude: origin.longitude, radiusMiles: radius, sector: sector || undefined, headquarteredOnly: hqOnly, sort }, ctrl.signal)
      .then(setData)
      .catch(e => { if (e.name !== 'AbortError') setError(e instanceof ApiError ? e.message : 'Could not load companies. Please try again.'); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [origin, radius, sector, hqOnly, sort]);

  const onLocated = (o: Origin) => {
    setOrigin(o); setGateOpen(false); setSelected(null);
    if (consent === null) setTimeout(() => setShowConsent(true), 900);
  };

  const onHover = useCallback((ticker: string | null, el?: HTMLElement) => {
    setHovered(ticker);
    if (!ticker || !el) { setTip(null); return; }
    const c = data?.items.find(i => i.ticker === ticker);
    if (!c) return;
    const r = el.getBoundingClientRect();
    setTip({ company: c, x: Math.min(innerWidth - 260, Math.max(8, r.left + r.width / 2 - 110)), y: Math.max(8, r.top - 58) });
  }, [data]);

  const onSelect = useCallback((ticker: string) => { setTip(null); setSelected(ticker); }, []);
  const closePanel = useCallback(() => setSelected(null), []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setSelected(null); };
    const onScroll = () => setTip(null);
    addEventListener('keydown', onKey); addEventListener('scroll', onScroll, { passive: true });
    return () => { removeEventListener('keydown', onKey); removeEventListener('scroll', onScroll); };
  }, []);

  const selectedSummary = useMemo(() => data?.items.find(i => i.ticker === selected) ?? null, [data, selected]);
  const highlight = { selected, hovered };
  const features = config?.features ?? FALLBACK_CONFIG.features;
  const placeName = origin?.label.split(',')[0] ?? '';

  if (bootError) return <div className="wrap"><p className="banner-error pane">{bootError}</p></div>;
  if (!config) return <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>;

  return (
    <>
      <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>
      <div className="app" onClick={e => {
        if (selected && !(e.target as HTMLElement).closest('.bub,.row,.top,.panel')) setSelected(null);
      }}>
        <TopBar ref={header}
          placeLabel={origin?.label ?? 'Lehi, UT 84043'} onChangeLocation={() => { setSelected(null); setGateOpen(true); }}
          view={view} onView={setView} showMapView={features.MapView !== false}
          theme={theme} onTheme={setTheme}
          radii={config.allowedRadiiMiles} radius={radius} onRadius={setRadius}
          sectors={sectors} sector={sector} onSector={setSector}
          hqOnly={hqOnly} onHqOnly={setHqOnly}
          isSample={meta?.isSampleData ?? false} />

        {error && <div className="wrap"><p className="banner-error pane">{error}</p></div>}

        {data && view === 'list' && (
          <>
            <ListView data={data} placeName={placeName} sort={sort} onSort={setSort} highlight={highlight} loading={loading} onHover={onHover} onSelect={onSelect} />
            <div className="wrap foot">
              <span>{meta?.isSampleData ? 'Sample data — company names and approximate locations are real; financial figures are synthetic.' : `Data as of ${meta?.asOfDate ?? '—'}.`}</span>
              <span>
                <a className="linkbtn" href="/swagger" target="_blank" rel="noreferrer">Public API</a>
                {' · '}
                <button className="linkbtn" onClick={() => setShowConsent(true)}>Cookie settings</button>
              </span>
            </div>
          </>
        )}

        {data && view === 'map' && (
          <MapView data={data} highlight={highlight} showBaseMap={showBaseMap} onToggleBaseMap={() => setShowBaseMap(s => !s)}
            onBackground={closePanel} onHover={onHover} onSelect={onSelect} />
        )}
      </div>

      {tip && (
        <div className="tip pane" style={{ left: tip.x, top: tip.y }}>
          <b>{tip.company.name}</b>
          <span>{money(tip.company.indicators.ttmRevenue)} revenue · {pct(tip.company.indicators.revenueGrowthYoY)} · {tip.company.distanceMiles.toFixed(1)} mi</span>
        </div>
      )}

      <CompanyPanel ticker={selected} distanceMiles={selectedSummary?.distanceMiles ?? null}
        nearestLabel={selectedSummary?.nearestLocation.label ?? null}
        showExecutives={features.Executives !== false} onClose={closePanel} />

      {gateOpen && <LocationGate coverageMiles={COVERAGE_MILES} onLocated={onLocated} />}

      {showConsent && !gateOpen && (
        <ConsentBanner
          onAccept={() => { setConsent('yes'); writeSessionConsent('yes'); setShowConsent(false); }}
          onDecline={() => { setConsent('no'); writeSessionConsent('no'); clearPrefs(); setShowConsent(false); }} />
      )}
    </>
  );
}
