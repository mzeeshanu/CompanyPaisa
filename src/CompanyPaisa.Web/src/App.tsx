import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from './api/client';
import type { ClientConfig, CompanySort, CompanySummary, DataMeta, ExecutiveSort, ExecutivesNearResponse, NearbyResponse } from './api/types';
import { CompanyPanel } from './components/CompanyPanel';
import { ConsentBanner } from './components/ConsentBanner';
import { ExecutivePanel } from './components/ExecutivePanel';
import { ExecutivesView } from './components/ExecutivesView';
import { ListView } from './components/ListView';
import { LocationGate, type Origin } from './components/LocationGate';
import { MapView } from './components/MapView';
import { PrivacyNotice } from './components/PrivacyNotice';
import { TopBar } from './components/TopBar';
import { DISCLAIMER } from './lib/disclaimer';
import { money, pct } from './lib/format';
import {
  applyTheme, clearPrefs, configurePrefs, readPrefs, readSessionConsent, writePrefs, writeSessionConsent,
  type Consent, type Mode, type Theme, type View,
} from './lib/prefs';

const FALLBACK_CONFIG: ClientConfig = {
  defaultView: 'List', defaultTheme: 'Auto', defaultRadiusMiles: 10, allowedRadiiMiles: [5, 10, 25, 50],
  defaultSort: 'Revenue', showBaseMapByDefault: true, mapTilesUrl: null,
  consentCookieName: 'cp_prefs', consentCookieDays: 365, features: { MapView: true, ListView: true, Executives: true },
  coverage: [],
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

  // What we're looking up: companies (List/Map) or executives.
  const [mode, setMode] = useState<Mode>('companies');
  const [execSort, setExecSort] = useState<ExecutiveSort>('Pay');
  const [execSearch, setExecSearch] = useState('');
  const [includeFormer, setIncludeFormer] = useState(false);

  const [view, setView] = useState<View>('list');
  const [theme, setTheme] = useState<Theme>('auto');
  const [showBaseMap, setShowBaseMap] = useState(true);
  const [consent, setConsent] = useState<Consent>(null);
  const [showConsent, setShowConsent] = useState(false);
  const [showPrivacy, setShowPrivacy] = useState(false);

  const [data, setData] = useState<NearbyResponse | null>(null);
  const [execData, setExecData] = useState<ExecutivesNearResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState<string | null>(null);          // company ticker in the panel
  const [selectedPerson, setSelectedPerson] = useState<string | null>(null);
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
        setMode(saved?.mode === 'executives' && cfg.features.Executives !== false ? 'executives' : 'companies');
        setTheme(saved?.theme ?? (cfg.defaultTheme.toLowerCase() as Theme));
        setShowBaseMap(saved?.map ?? cfg.showBaseMapByDefault);
        setConsent(saved ? 'yes' : readSessionConsent());
      })
      .catch(() => setBootError("We couldn't reach the CompanyPaisa service. Is the API running?"));
  }, []);

  const mapActive = mode === 'companies' && view === 'map';
  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => { if (consent === 'yes') writePrefs({ view, theme, map: showBaseMap, mode }); }, [consent, view, theme, showBaseMap, mode]);
  useEffect(() => {
    document.body.classList.toggle('locked', gateOpen);
    document.body.classList.toggle('view-map', mapActive);
  }, [gateOpen, mapActive]);

  // The Map view sits under the sticky header; keep its offset in sync with the header's height.
  useLayoutEffect(() => {
    const el = header.current;
    if (!el) return;
    const ro = new ResizeObserver(() => document.documentElement.style.setProperty('--hdr', `${el.getBoundingClientRect().height}px`));
    ro.observe(el);
    return () => ro.disconnect();
  }, [config]);

  // ---- company search whenever location or filters change ----
  useEffect(() => {
    if (!origin || mode !== 'companies') return;
    const ctrl = new AbortController();
    setLoading(true); setError('');
    api.near({ latitude: origin.latitude, longitude: origin.longitude, radiusMiles: radius, sector: sector || undefined, headquarteredOnly: hqOnly, sort }, ctrl.signal)
      .then(setData)
      .catch(e => { if (e.name !== 'AbortError') setError(e instanceof ApiError ? e.message : 'Could not load companies. Please try again.'); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [origin, radius, sector, hqOnly, sort, mode]);

  // ---- executive search ----
  useEffect(() => {
    if (!origin || mode !== 'executives') return;
    const ctrl = new AbortController();
    setLoading(true); setError('');
    api.executivesNear({
      latitude: origin.latitude, longitude: origin.longitude, radiusMiles: radius, sector: sector || undefined,
      includeFormer, search: execSearch.trim() || undefined, sort: execSort,
    }, ctrl.signal)
      .then(setExecData)
      .catch(e => { if (e.name !== 'AbortError') setError(e instanceof ApiError ? e.message : 'Could not load executives. Please try again.'); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [origin, radius, sector, includeFormer, execSearch, execSort, mode]);

  const onLocated = (o: Origin) => {
    setOrigin(o); setGateOpen(false); setSelected(null); setSelectedPerson(null);
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

  // Only one details panel is open at a time; they link to each other.
  const openCompany = useCallback((ticker: string) => { setTip(null); setSelectedPerson(null); setSelected(ticker); }, []);
  const openPerson = useCallback((personId: string) => { setTip(null); setSelected(null); setSelectedPerson(personId); }, []);
  const closePanels = useCallback(() => { setSelected(null); setSelectedPerson(null); }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') closePanels(); };
    const onScroll = () => setTip(null);
    addEventListener('keydown', onKey); addEventListener('scroll', onScroll, { passive: true });
    return () => { removeEventListener('keydown', onKey); removeEventListener('scroll', onScroll); };
  }, [closePanels]);

  const selectedSummary = useMemo(() => data?.items.find(i => i.ticker === selected) ?? null, [data, selected]);
  const highlight = { selected, hovered };
  const features = config?.features ?? FALLBACK_CONFIG.features;
  const placeName = origin?.label.split(',')[0] ?? '';

  if (bootError) return <div className="wrap"><p className="banner-error pane">{bootError}</p></div>;
  if (!config) return <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>;

  const footer = (
    <div className="wrap foot">
      <p className="disclaimer" role="note">
        <b>Please note:</b> {DISCLAIMER} This is not financial advice — check the company's original filing before relying on any number.
      </p>
      <span>{meta?.isSampleData
        ? 'Sample data — company names and approximate locations are real; financial figures and executive names are synthetic.'
        : `Data as of ${meta?.asOfDate ?? '—'} from SEC EDGAR filings${meta?.dataVersion.includes('uk-') ? ' and UK annual reports (ESEF, via filings.xbrl.org; addresses from GLEIF)' : ''}.`}{' '}
        ZIP and postcode names © <a className="linkbtn" href="https://www.geonames.org/" target="_blank" rel="noreferrer">GeoNames</a> (CC BY 4.0).</span>
      <span>
        <a className="linkbtn" href="/swagger" target="_blank" rel="noreferrer">Public API</a>
        {' · '}
        <button className="linkbtn" onClick={() => setShowPrivacy(true)}>Privacy</button>
        {' · '}
        <button className="linkbtn" onClick={() => setShowConsent(true)}>Cookie settings</button>
      </span>
    </div>
  );

  return (
    <>
      <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>
      <div className="app" onClick={e => {
        if ((selected || selectedPerson) && !(e.target as HTMLElement).closest('.bub,.row,.top,.panel')) closePanels();
      }}>
        <TopBar ref={header}
          placeLabel={origin?.label ?? 'Lehi, UT 84043'} onChangeLocation={() => { closePanels(); setGateOpen(true); }}
          mode={mode} onMode={m => { closePanels(); setMode(m); }} showExecutives={features.Executives !== false}
          view={view} onView={setView} showMapView={features.MapView !== false}
          theme={theme} onTheme={setTheme}
          radii={config.allowedRadiiMiles} radius={radius} onRadius={setRadius}
          sectors={sectors} sector={sector} onSector={setSector}
          hqOnly={hqOnly} onHqOnly={setHqOnly}
          isSample={meta?.isSampleData ?? false} />

        {error && <div className="wrap"><p className="banner-error pane">{error}</p></div>}

        {mode === 'companies' && data && view === 'list' && (
          <>
            <ListView data={data} placeName={placeName} sort={sort} onSort={setSort} highlight={highlight} loading={loading} onHover={onHover} onSelect={openCompany} />
            {footer}
          </>
        )}

        {mapActive && data && (
          <MapView data={data} highlight={highlight} showBaseMap={showBaseMap} onToggleBaseMap={() => setShowBaseMap(s => !s)}
            onBackground={closePanels} onHover={onHover} onSelect={openCompany} />
        )}

        {mode === 'executives' && execData && (
          <>
            <ExecutivesView data={execData} placeName={placeName} sort={execSort} onSort={setExecSort}
              search={execSearch} onSearch={setExecSearch} includeFormer={includeFormer} onIncludeFormer={setIncludeFormer}
              selected={selectedPerson} loading={loading} onSelect={openPerson} />
            {footer}
          </>
        )}
      </div>

      {tip && (
        <div className="tip pane" style={{ left: tip.x, top: tip.y }}>
          <b>{tip.company.name}</b>
          <span>{money(tip.company.indicators.ttmRevenue, tip.company.currency)} revenue · {pct(tip.company.indicators.revenueGrowthYoY)} · {tip.company.distanceMiles.toFixed(1)} mi</span>
        </div>
      )}

      <CompanyPanel ticker={selected} distanceMiles={selectedSummary?.distanceMiles ?? null}
        nearestLabel={selectedSummary?.nearestLocation.label ?? null}
        showExecutives={features.Executives !== false} onClose={closePanels} onOpenPerson={openPerson} />

      <ExecutivePanel personId={selectedPerson} onClose={closePanels} onOpenCompany={openCompany} />

      {gateOpen && <LocationGate coverageMiles={COVERAGE_MILES} coverage={(config ?? FALLBACK_CONFIG).coverage ?? []} onLocated={onLocated} />}

      <PrivacyNotice open={showPrivacy} contact={config.privacyContact} onClose={() => setShowPrivacy(false)} />

      {showConsent && !gateOpen && (
        <ConsentBanner
          onAccept={() => { setConsent('yes'); writeSessionConsent('yes'); setShowConsent(false); }}
          onDecline={() => { setConsent('no'); writeSessionConsent('no'); clearPrefs(); setShowConsent(false); }} />
      )}
    </>
  );
}
