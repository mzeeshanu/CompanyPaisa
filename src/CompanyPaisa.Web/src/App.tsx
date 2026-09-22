import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { api, ApiError } from './api/client';
import type { ClientConfig, CompanySort, CompanySummary, DataMeta, ExecutiveSort, ExecutivesNearResponse, NearbyResponse, RoleFilter } from './api/types';
import { AboutData } from './components/AboutData';
import { CompanyPage } from './components/CompanyPage';
import { SalariesPage } from './components/WorkforcePay';
import { ConsentBanner } from './components/ConsentBanner';
import { ExecutivePage } from './components/ExecutivePage';
import { ExecutivesView } from './components/ExecutivesView';
import { ListView } from './components/ListView';
import { LocationGate, type Origin } from './components/LocationGate';
import { MapView } from './components/MapView';
import type { Nearby } from './components/PageShell';
import { PrivacyNotice } from './components/PrivacyNotice';
import { ReportProblem } from './components/ReportProblem';
import { PageBar, TopBar } from './components/TopBar';
import { track } from './lib/analytics';
import { DISCLAIMER } from './lib/disclaimer';
import { money, pct } from './lib/format';
import {
  applyTheme, clearPrefs, configurePrefs, readPrefs, readSessionConsent, writePrefs, writeSessionConsent,
  type Consent, type Mode, type Theme, type View,
} from './lib/prefs';
import { companyPath, navigate, replaceAddress, savedScroll, searchPath, useRoute } from './lib/router';

const FALLBACK_CONFIG: ClientConfig = {
  defaultView: 'List', defaultTheme: 'Auto', defaultRadiusMiles: 10, allowedRadiiMiles: [5, 10, 25, 50],
  defaultSort: 'Revenue', showBaseMapByDefault: true, mapTilesUrl: null,
  consentCookieName: 'cp_prefs', consentCookieDays: 365, features: { MapView: true, ListView: true, Executives: true },
  coverage: [],
};
const COVERAGE_MILES = 60;
const COMPANY_SORTS: CompanySort[] = ['Revenue', 'Growth', 'Profit', 'Distance'];
const EXEC_SORTS: ExecutiveSort[] = ['Pay', 'TotalPay', 'PayGrowth', 'Distance', 'Name'];
const EXEC_ROLES: RoleFilter[] = ['Ceo', 'Cfo', 'Coo', 'Technology', 'Legal', 'Other'];

interface Tip { company: CompanySummary; x: number; y: number }

export default function App() {
  const route = useRoute();
  const onHome = route.kind === 'home';
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
  const [execRole, setExecRole] = useState<RoleFilter | ''>('');
  // Narrows the ranked company list only (the bubbles and summary keep the whole area).
  const [companyFind, setCompanyFind] = useState('');
  const [includeFormer, setIncludeFormer] = useState(false);

  const [view, setView] = useState<View>('list');
  const [theme, setTheme] = useState<Theme>('auto');
  const [showBaseMap, setShowBaseMap] = useState(true);
  const [consent, setConsent] = useState<Consent>(null);
  const [showConsent, setShowConsent] = useState(false);
  const [showPrivacy, setShowPrivacy] = useState(false);
  const [showAbout, setShowAbout] = useState(false);

  const [data, setData] = useState<NearbyResponse | null>(null);
  const [execData, setExecData] = useState<ExecutivesNearResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  // The company or person the visitor last opened: highlighted in the list when they come back to it.
  const [lastOpened, setLastOpened] = useState<string | null>(null);
  const [pageAbout, setPageAbout] = useState('');
  const [hovered, setHovered] = useState<string | null>(null);
  const [tip, setTip] = useState<Tip | null>(null);
  const header = useRef<HTMLElement>(null);
  // A search address (/near/84043?radius=25) is being turned into a search: the location screen waits until it's known.
  const [resolving, setResolving] = useState(() => route.kind === 'home' && route.search !== null);
  const [gateNotice, setGateNotice] = useState<string | undefined>();
  const originNow = useRef(origin);
  originNow.current = origin;
  const consentNow = useRef(consent);
  consentNow.current = consent;

  // ---- boot: settings come from the API (appsettings.json), preferences from the consent cookie ----
  useEffect(() => {
    track('page_view');
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

  const mapActive = onHome && mode === 'companies' && view === 'map';
  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => { if (consent === 'yes') writePrefs({ view, theme, map: showBaseMap, mode }); }, [consent, view, theme, showBaseMap, mode]);
  useEffect(() => {
    document.body.classList.toggle('locked', gateOpen && onHome);
    document.body.classList.toggle('view-map', mapActive);
  }, [gateOpen, onHome, mapActive]);

  // A page starts at the top; coming back to the search returns to where the visitor was in the list.
  useLayoutEffect(() => {
    const y = savedScroll();
    const root = document.documentElement;
    // The bubble grid sizes itself just after this and the browser's scroll anchoring would shift the page while it does:
    // turn anchoring off for a moment and place the page again, unless the visitor scrolls somewhere else first.
    root.style.overflowAnchor = 'none';
    scrollTo(0, y);
    let landed = scrollY;
    const timers = [50, 150, 400, 900].map(ms => setTimeout(() => {
      if (scrollY !== landed || scrollY === y) return;
      scrollTo(0, y);
      landed = scrollY;
    }, ms));
    const done = setTimeout(() => { root.style.overflowAnchor = ''; }, 1000);
    if (onHome) document.title = 'CompanyPaisa';
    else { setTip(null); setPageAbout(''); }
    return () => { timers.forEach(clearTimeout); clearTimeout(done); root.style.overflowAnchor = ''; };
  }, [route, onHome]);

  // The Map view sits under the sticky header; keep its offset in sync with the header's height.
  useLayoutEffect(() => {
    const el = header.current;
    if (!el) return;
    const ro = new ResizeObserver(() => document.documentElement.style.setProperty('--hdr', `${el.getBoundingClientRect().height}px`));
    ro.observe(el);
    return () => ro.disconnect();
  }, [config, onHome]);

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
      includeFormer, search: execSearch.trim() || undefined, role: execRole || undefined, sort: execSort,
    }, ctrl.signal)
      .then(setExecData)
      .catch(e => { if (e.name !== 'AbortError') setError(e instanceof ApiError ? e.message : 'Could not load executives. Please try again.'); })
      .finally(() => { if (!ctrl.signal.aborted) setLoading(false); });
    return () => ctrl.abort();
  }, [origin, radius, sector, includeFormer, execSearch, execRole, execSort, mode]);

  // The next page of executives, added below the ones already shown (same search, next page number).
  const [loadingMore, setLoadingMore] = useState(false);
  const loadMoreExecutives = useCallback(() => {
    if (!origin || !execData || loadingMore) return;
    setLoadingMore(true);
    track('executives_more');
    api.executivesNear({
      latitude: origin.latitude, longitude: origin.longitude, radiusMiles: radius, sector: sector || undefined,
      includeFormer, search: execSearch.trim() || undefined, role: execRole || undefined, sort: execSort, page: execData.page + 1,
    })
      .then(next => setExecData(prev => prev && prev.page + 1 === next.page ? { ...next, items: [...prev.items, ...next.items] } : prev))
      .catch(e => setError(e instanceof ApiError ? e.message : 'Could not load more executives. Please try again.'))
      .finally(() => setLoadingMore(false));
  }, [origin, execData, loadingMore, radius, sector, includeFormer, execSearch, execRole, execSort]);

  const onLocated = useCallback((o: Origin) => {
    setOrigin(o); setGateOpen(false); setLastOpened(null); setGateNotice(undefined);
    if (consentNow.current === null) setTimeout(() => setShowConsent(true), 900);
  }, []);

  // ---- a search address opened directly (shared link, bookmark, reload) ----
  useEffect(() => {
    if (!config) return;
    if (route.kind !== 'home' || !route.search) { setResolving(false); return; }
    const { place, executives } = route.search;
    if (originNow.current?.place === place) { setResolving(false); return; }   // already showing it (came back from a page)

    const p = new URLSearchParams(location.search);
    const r = Number(p.get('radius'));
    setRadius(config.allowedRadiiMiles.includes(r) ? r : config.defaultRadiusMiles);
    setSector(p.get('sector') ?? '');
    setHqOnly(p.get('hq') === '1');
    const s = p.get('sort')?.toLowerCase();
    const peopleMode = executives && config.features.Executives !== false;
    setMode(peopleMode ? 'executives' : 'companies');
    if (peopleMode) {
      setExecSort(EXEC_SORTS.find(k => k.toLowerCase() === s) ?? 'Pay');
      setExecSearch(p.get('q') ?? '');
      setExecRole(EXEC_ROLES.find(k => k.toLowerCase() === p.get('role')?.toLowerCase()) ?? '');
      setIncludeFormer(p.get('former') === '1');
    } else {
      setSort(COMPANY_SORTS.find(k => k.toLowerCase() === s) ?? config.defaultSort);
      setCompanyFind(p.get('q') ?? '');
    }
    track('location_link', place);

    let cancelled = false;
    const fail = (notice?: string) => { if (cancelled) return; setGateNotice(notice); setGateOpen(true); setResolving(false); };
    const found = (o: Origin) => { if (cancelled) return; onLocated(o); setResolving(false); };
    setResolving(true);
    if (place.toLowerCase() === 'me') {
      // "Near me": only when the browser already allows location (no surprise prompt); otherwise the visitor chooses.
      const permission = navigator.permissions?.query({ name: 'geolocation' }) ?? Promise.reject(new Error('no permissions API'));
      permission.then(status => {
        if (status.state !== 'granted') return fail();
        navigator.geolocation.getCurrentPosition(
          pos => found({ latitude: pos.coords.latitude, longitude: pos.coords.longitude, label: 'your location', place: 'me' }),
          () => fail(), { timeout: 9000, maximumAge: 600000 });
      }).catch(() => fail());
    } else {
      api.lookup(place)
        .then(hit => found({ latitude: hit.point.latitude, longitude: hit.point.longitude, label: `${hit.city}, ${hit.state} ${hit.postalCode ?? place}`, place }))
        .catch(e => fail(e instanceof ApiError && e.status === 404
          ? `We couldn't find "${place}". Enter a ZIP code or postcode, or pick an area below.`
          : 'Something went wrong opening that search. Please try again.'));
    }
    return () => { cancelled = true; };
  }, [route, config, onLocated]);

  // ---- keep the address in step with the search on screen, so it can be shared, bookmarked or reloaded ----
  useEffect(() => {
    if (!config || !onHome || !origin || resolving) return;
    const q = new URLSearchParams();
    if (radius !== config.defaultRadiusMiles) q.set('radius', String(radius));
    if (sector) q.set('sector', sector);
    if (mode === 'companies') {
      if (hqOnly) q.set('hq', '1');
      if (sort !== config.defaultSort) q.set('sort', sort);
      if (companyFind.trim()) q.set('q', companyFind.trim());
    } else {
      if (execSort !== 'Pay') q.set('sort', execSort);
      if (execSearch.trim()) q.set('q', execSearch.trim());
      if (execRole) q.set('role', execRole.toLowerCase());
      if (includeFormer) q.set('former', '1');
    }
    const qs = q.toString();
    replaceAddress(searchPath(origin.place, mode === 'executives') + (qs ? `?${qs}` : ''));
  }, [route, config, onHome, origin, resolving, radius, sector, hqOnly, sort, mode, execSort, execSearch, execRole, includeFormer, companyFind]);

  const onHover = useCallback((ticker: string | null, el?: HTMLElement) => {
    setHovered(ticker);
    if (!ticker || !el) { setTip(null); return; }
    const c = data?.items.find(i => i.ticker === ticker);
    if (!c) return;
    const r = el.getBoundingClientRect();
    setTip({ company: c, x: Math.min(innerWidth - 260, Math.max(8, r.left + r.width / 2 - 110)), y: Math.max(8, r.top - 58) });
  }, [data]);

  // Companies and executives open as their own pages (/company/AAPL, /executive/…); the links do the navigating.
  const noteOpened = useCallback((id: string) => { setTip(null); setHovered(null); setLastOpened(id); }, []);
  const openCompany = useCallback((ticker: string) => { noteOpened(ticker); navigate(companyPath(ticker)); }, [noteOpened]);

  useEffect(() => {
    const onScroll = () => setTip(null);
    addEventListener('scroll', onScroll, { passive: true });
    return () => removeEventListener('scroll', onScroll);
  }, []);

  /** "Companies near here" on a company page: a new search around that location. */
  const explore = useCallback((point: { latitude: number; longitude: number }, label: string, place: string) => {
    setMode('companies'); setGateOpen(false); setLastOpened(null);
    setOrigin({ latitude: point.latitude, longitude: point.longitude, label, place });
    navigate(searchPath(place, false));
  }, []);

  const nearby: Nearby | null = useMemo(() => origin
    ? { point: { latitude: origin.latitude, longitude: origin.longitude }, label: origin.label, place: origin.place, mode }
    : null, [origin, mode]);
  const highlight = { selected: lastOpened, hovered };
  const features = config?.features ?? FALLBACK_CONFIG.features;
  const placeName = origin?.label.split(',')[0] ?? '';

  if (bootError) return <div className="wrap"><p className="banner-error pane">{bootError}</p></div>;
  if (!config) return <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>;

  const openAbout = () => { track('about_open'); setShowAbout(true); };

  const footer = (
    <div className="wrap foot">
      <p className="disclaimer" role="note">
        <b>Please note:</b> {DISCLAIMER} This is not financial advice — check the company's original filing before relying on any
        number. <button className="linkbtn" onClick={openAbout}>About the data</button>
      </p>
      <span className="sources-line">{meta?.isSampleData
        ? 'Sample data — company names and approximate locations are real; financial figures and executive names are synthetic.'
        : <>Data as of {meta?.asOfDate ?? '—'}. Sources:{' '}
            <a className="linkbtn" href="https://www.sec.gov/search-filings/edgar-application-programming-interfaces" target="_blank" rel="noreferrer">SEC EDGAR</a>,{' '}
            <a className="linkbtn" href="https://filings.xbrl.org/" target="_blank" rel="noreferrer">filings.xbrl.org</a> (UK &amp; European annual reports),{' '}
            <a className="linkbtn" href="https://www.gleif.org/en" target="_blank" rel="noreferrer">GLEIF</a>,{' '}
            <a className="linkbtn" href="https://www.openfigi.com/" target="_blank" rel="noreferrer">OpenFIGI</a>,{' '}
            <a className="linkbtn" href="https://www.census.gov/geographies/reference-files/time-series/geo/gazetteer-files.html" target="_blank" rel="noreferrer">US Census</a>;
            postcode names © <a className="linkbtn" href="https://www.geonames.org/" target="_blank" rel="noreferrer">GeoNames</a> (CC BY 4.0).</>}</span>
      <span>
        <button className="linkbtn" onClick={openAbout}>About the data</button>
        {' · '}
        <a className="linkbtn" href="/swagger" target="_blank" rel="noreferrer">Public API</a>
        {' · '}
        <button className="linkbtn" onClick={() => { track('privacy_open'); setShowPrivacy(true); }}>Privacy</button>
        {' · '}
        <button className="linkbtn" onClick={() => setShowConsent(true)}>Cookie settings</button>
      </span>
    </div>
  );

  const gateShown = gateOpen && onHome && !resolving;
  const showExecutives = features.Executives !== false;

  return (
    <>
      <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>
      <div className="app">
        {onHome ? (
          <TopBar ref={header}
            placeLabel={origin?.label ?? 'Lehi, UT 84043'} onChangeLocation={() => setGateOpen(true)}
            mode={mode} onMode={m => { track(m === 'executives' ? 'mode_executives' : 'mode_companies'); setMode(m); }} showExecutives={showExecutives}
            view={view} onView={v => { track(v === 'map' ? 'view_map' : 'view_list'); setView(v); }} showMapView={features.MapView !== false}
            theme={theme} onTheme={setTheme}
            radii={config.allowedRadiiMiles} radius={radius} onRadius={setRadius}
            sectors={sectors} sector={sector} onSector={setSector}
            hqOnly={hqOnly} onHqOnly={setHqOnly}
            isSample={meta?.isSampleData ?? false} />
        ) : (
          <PageBar ref={header} theme={theme} onTheme={setTheme} isSample={meta?.isSampleData ?? false} showExecutives={showExecutives} />
        )}

        {route.kind === 'company' && (
          <>
            {route.salaries
              ? <SalariesPage ticker={route.ticker} from={nearby} onLoaded={setPageAbout} />
              : <CompanyPage ticker={route.ticker} from={nearby} showExecutives={showExecutives} onExplore={explore} onLoaded={setPageAbout} />}
            {footer}
          </>
        )}

        {route.kind === 'executive' && (
          <>
            <ExecutivePage personId={route.personId} from={nearby} onLoaded={setPageAbout} />
            {footer}
          </>
        )}

        {onHome && error && <div className="wrap"><p className="banner-error pane">{error}</p></div>}

        {onHome && mode === 'companies' && data && view === 'list' && (
          <>
            <ListView data={data} placeName={placeName} sort={sort} onSort={setSort} highlight={highlight} loading={loading}
              showExecutives={showExecutives} onOpened={noteOpened} onHover={onHover}
              find={companyFind} onFind={setCompanyFind} />
            {footer}
          </>
        )}

        {mapActive && data && (
          <MapView data={data} highlight={highlight} showBaseMap={showBaseMap} onToggleBaseMap={() => setShowBaseMap(s => !s)}
            onBackground={() => setTip(null)} onHover={onHover} onSelect={openCompany} />
        )}

        {onHome && mode === 'executives' && execData && (
          <>
            <ExecutivesView data={execData} placeName={placeName} sort={execSort} onSort={setExecSort}
              search={execSearch} onSearch={setExecSearch} role={execRole} onRole={setExecRole} includeFormer={includeFormer} onIncludeFormer={setIncludeFormer}
              selected={lastOpened} loading={loading} onOpened={noteOpened} onMore={loadMoreExecutives} loadingMore={loadingMore} />
            {footer}
          </>
        )}
      </div>

      {tip && onHome && (
        <div className="tip pane" style={{ left: tip.x, top: tip.y }}>
          <b>{tip.company.name}</b>
          <span>{money(tip.company.indicators.ttmRevenue, tip.company.currency)} revenue · {pct(tip.company.indicators.revenueGrowthYoY)} · {tip.company.distanceMiles.toFixed(1)} mi</span>
        </div>
      )}

      {gateShown && <LocationGate coverageMiles={COVERAGE_MILES} coverage={(config ?? FALLBACK_CONFIG).coverage ?? []} onLocated={o => { setCompanyFind(''); onLocated(o); }}
        showExecutives={showExecutives} notice={gateNotice} />}

      <PrivacyNotice open={showPrivacy} contact={config.privacyContact} onClose={() => setShowPrivacy(false)} />
      <AboutData open={showAbout} contact={config.privacyContact} onClose={() => setShowAbout(false)} />

      {!gateShown && !showConsent && (
        <ReportProblem contact={config.privacyContact}
          about={!onHome ? pageAbout || location.pathname : origin ? `companies near ${origin.label}` : 'CompanyPaisa'} />
      )}
      {showConsent && !gateShown && (
        <ConsentBanner
          onAccept={() => { setConsent('yes'); writeSessionConsent('yes'); setShowConsent(false); }}
          onDecline={() => { setConsent('no'); writeSessionConsent('no'); clearPrefs(); setShowConsent(false); }} />
      )}
    </>
  );
}
