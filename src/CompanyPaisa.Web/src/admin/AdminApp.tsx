import { Children, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { companyPath, personPath } from '../lib/router';

// ---------- API shapes (mirror src/CompanyPaisa.Analytics/AnalyticsModels.cs) ----------

interface Status { provider: string; enabled: boolean; message: string }
interface Totals { visitors: number; pageViews: number; searches: number; companyViews: number; executiveViews: number; events: number }
interface Day { day: string; visitors: number; pageViews: number; searches: number; companyViews: number; executiveViews: number }
interface Count { key: string; label: string | null; count: number; visitors: number }
interface Report {
  from: string; to: string; totals: Totals; days: Day[];
  places: Count[]; companies: Count[]; executives: Count[]; countries: Count[]; cities: Count[];
  devices: Count[]; browsers: Count[]; operatingSystems: Count[]; referrers: Count[]; actions: Count[]; sources: Count[];
}
interface Dashboard { status: Status; report: Report | null; areas: Count[] }
interface Step { at: string; name: string; subject: string | null; label: string | null; detail: string | null; path: string | null }
interface Visit {
  start: string; end: string; country: string | null; region: string | null; city: string | null; device: string; browser: string; os: string;
  referrer: string | null; source: string; events: number; searches: number; companyViews: number; executiveViews: number; steps: Step[];
}
interface Visits { total: number; visits: Visit[] }

const BASE = '/api/admin/analytics';
const KEY_STORE = 'cp_admin_key';
const RANGES = [7, 30, 90, 365] as const;

class HttpError extends Error { constructor(public status: number) { super(`HTTP ${status}`); } }

async function call(path: string, key: string, init?: RequestInit): Promise<Response> {
  const res = await fetch(`${BASE}${path}`, { ...init, headers: { ...init?.headers, 'X-Admin-Key': key } });
  if (!res.ok) throw new HttpError(res.status);
  return res;
}

function readKey(): string {
  try { return sessionStorage.getItem(KEY_STORE) ?? ''; } catch { return ''; }
}
function saveKey(key: string | null) {
  try { if (key) sessionStorage.setItem(KEY_STORE, key); else sessionStorage.removeItem(KEY_STORE); } catch { /* private mode */ }
}

/** Private analytics dashboard at /admin: the key stays in this browser tab only. */
export default function AdminApp() {
  const [key, setKey] = useState(readKey);
  const [days, setDays] = useState<number>(30);
  const [data, setData] = useState<Dashboard | null>(null);
  const [visits, setVisits] = useState<Visits | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [excluded, setExcluded] = useState<boolean | null>(null);

  useEffect(() => { document.title = 'Analytics · CompanyPaisa'; }, []);

  const load = useCallback(async (k: string, d: number) => {
    setLoading(true); setError('');
    try {
      const [res, v] = await Promise.all([call(`/report?days=${d}`, k), call(`/visits?days=${d}`, k)]);
      setData(await res.json());
      setVisits(await v.json());
      const owner = await call('/owner', k);
      setExcluded((await owner.json()).excluded);
      saveKey(k);
    } catch (e) {
      const status = e instanceof HttpError ? e.status : 0;
      setError(status === 401 ? 'That key is wrong.' : status === 404 ? 'The dashboard is turned off (no Analytics:DashboardKey is set).'
        : status === 429 ? 'Too many tries. Wait a minute.' : 'Could not load the numbers. Try again.');
      if (status === 401 || status === 404) { saveKey(null); setData(null); }
    } finally { setLoading(false); }
  }, []);

  useEffect(() => { if (key) void load(key, days); }, [key, days, load]);

  if (!key || (!data && error)) return <SignIn error={error} busy={loading} onSubmit={k => { setError(''); setKey(k); }} />;

  const r = data?.report;
  return (
    <>
      <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>
      <main className="wrap admin">
        <header className="admin-head">
          <div>
            <p className="eyebrow">CompanyPaisa</p>
            <h1>Visitors &amp; searches</h1>
            {r && <p className="admin-sub">{r.from} to {r.to} (UTC){data?.status && <> · {data.status.provider}</>}</p>}
          </div>
          <div className="admin-tools">
            <div className="chips" role="group" aria-label="Date range">
              {RANGES.map(d => <button key={d} aria-pressed={days === d} onClick={() => setDays(d)}>{d === 365 ? '1 year' : `${d} days`}</button>)}
            </div>
            <button className="ghost small" disabled={!data?.status.enabled} onClick={() => downloadEvents(key, days).catch(() => setError('Download failed.'))}>
              Download events (CSV)
            </button>
            <label className="admin-owner" title="Sets a cookie in this browser so your own visits aren't counted">
              <input type="checkbox" className="switch-box" checked={excluded ?? false} disabled={excluded === null}
                onChange={async e => {
                  const on = e.target.checked;
                  try { await call('/owner', key, { method: on ? 'POST' : 'DELETE' }); setExcluded(on); } catch { setError('Could not change that.'); }
                }} />
              Don't count this browser
            </label>
            <button className="linkbtn" onClick={() => { saveKey(null); setKey(''); setData(null); }}>Sign out</button>
          </div>
        </header>

        {error && <p className="banner-error pane">{error}</p>}
        {data && !data.status.enabled && <p className="banner-error pane">Analytics aren't recording: {data.status.message}</p>}

        {r && (
          <div className={loading ? 'loading' : undefined}>
            <section className="admin-tiles">
              <Tile label="Visits" value={r.totals.visitors} hint="Distinct visitors per day, added up: someone who came on two days counts twice." />
              <Tile label="Page views" value={r.totals.pageViews} />
              <Tile label="Searches" value={r.totals.searches} />
              <Tile label="Companies opened" value={r.totals.companyViews} />
              <Tile label="Executives opened" value={r.totals.executiveViews} />
            </section>

            <section className="pane admin-card">
              <h2>Visits per day</h2>
              <DailyChart days={r.days} from={r.from} to={r.to} />
            </section>

            <Group title="What people looked for">
              <Table title="Areas searched" rows={data!.areas} note="Searches, named after the nearest town" />
              <Table title="Postcodes & cities typed" rows={r.places} />
              <Table title="Companies opened" rows={r.companies} labelFirst />
              <Table title="Executives opened" rows={r.executives} labelFirst />
              <Table title="Clicks & events" rows={r.actions.map(a => ({ ...a, key: ACTIONS[a.key] ?? a.key }))} />
            </Group>
            <Group title="Where visitors are and how they got here">
              <Table title="Countries" rows={r.countries.map(c => ({ ...c, key: countryName(c.key) }))} />
              <Table title="Cities" rows={r.cities} note="From Cloudflare's estimate of each visitor's location" />
              <Table title="Came from" rows={r.referrers} note="Other websites that linked here" />
              <Table title="Where requests came from" rows={r.sources.map(s => ({ ...s, key: SOURCES[s.key] ?? s.key }))} />
            </Group>
            <Group title="Devices">
              <Table title="Devices" rows={r.devices} />
              <Table title="Browsers" rows={r.browsers} />
              <Table title="Operating systems" rows={r.operatingSystems} />
            </Group>
            {visits && <VisitList data={visits} />}
          </div>
        )}
        {!r && loading && <p className="admin-sub">Loading…</p>}
      </main>
    </>
  );
}

function SignIn({ error, busy, onSubmit }: { error: string; busy: boolean; onSubmit: (key: string) => void }) {
  const [value, setValue] = useState('');
  const submit = (e: FormEvent) => { e.preventDefault(); if (value.trim()) onSubmit(value.trim()); };
  return (
    <>
      <div className="aurora" aria-hidden="true"><i /><i /><i /><i /></div>
      <main className="gate admin-gate">
        <form className="card pane" onSubmit={submit}>
          <p className="eyebrow">CompanyPaisa</p>
          <h1>Analytics</h1>
          <p>Enter the dashboard key.</p>
          <div className="zip">
            <input type="password" autoComplete="current-password" aria-label="Dashboard key" placeholder="Dashboard key" value={value}
              onChange={e => setValue(e.target.value)} autoFocus />
            <button className="ghost" type="submit" disabled={busy || !value.trim()}>{busy ? '…' : 'Open'}</button>
          </div>
          {error && <p className="err" role="alert">{error}</p>}
        </form>
      </main>
    </>
  );
}

function Tile({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <div className="pane admin-tile" title={hint}>
      <span>{label}</span>
      <b>{value.toLocaleString('en-US')}</b>
    </div>
  );
}

/** One bar per day of the range, including days with no visits. */
function DailyChart({ days, from, to }: { days: Day[]; from: string; to: string }) {
  const series = useMemo(() => {
    const byDay = new Map(days.map(d => [d.day, d]));
    const out: Day[] = [];
    for (let t = Date.parse(`${from}T00:00:00Z`), end = Date.parse(`${to}T00:00:00Z`); t <= end; t += 86_400_000) {
      const day = new Date(t).toISOString().slice(0, 10);
      out.push(byDay.get(day) ?? { day, visitors: 0, pageViews: 0, searches: 0, companyViews: 0, executiveViews: 0 });
    }
    return out;
  }, [days, from, to]);
  const max = Math.max(1, ...series.map(d => d.visitors));
  const w = 1000, h = 180, gap = series.length > 120 ? 0.5 : 2, bar = w / series.length;
  return (
    <div className="admin-chart">
      <svg viewBox={`0 0 ${w} ${h + 22}`} preserveAspectRatio="none" role="img" aria-label="Visits per day">
        {series.map((d, i) => {
          const bh = (d.visitors / max) * h;
          return (
            <rect key={d.day} x={i * bar + gap / 2} y={h - bh} width={Math.max(0.5, bar - gap)} height={Math.max(bh, d.visitors ? 1 : 0)} rx={bar > 8 ? 3 : 0}>
              <title>{`${d.day}: ${d.visitors} visits, ${d.searches} searches, ${d.companyViews} companies opened`}</title>
            </rect>
          );
        })}
      </svg>
      <div className="admin-axis"><span>{from}</span><span>peak {max.toLocaleString('en-US')} a day</span><span>{to}</span></div>
    </div>
  );
}

const COL_MIN = 340, COL_GAP = 14;

/**
 * Related cards under one heading, packed into columns with no holes: each card goes, in order,
 * into whichever column is shortest so far (measured, and redone whenever a card or the page resizes).
 */
function Group({ title, children }: { title: string; children: ReactNode }) {
  const items = Children.toArray(children);
  const ref = useRef<HTMLDivElement>(null);
  const [cols, setCols] = useState(1);
  const [heights, setHeights] = useState<number[]>([]);

  const columns = useMemo(() => {
    const out: number[][] = Array.from({ length: cols }, () => []);
    const filled: number[] = new Array(cols).fill(0);
    for (let i = 0; i < items.length; i++) {
      const c = filled.indexOf(Math.min(...filled));
      out[c].push(i);
      filled[c] += (heights[i] ?? 0) + COL_GAP;
    }
    return out;
  }, [cols, heights, items.length]);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const measure = () => {
      setCols(Math.max(1, Math.floor((el.clientWidth + COL_GAP) / (COL_MIN + COL_GAP))));
      const hs: number[] = [];
      el.querySelectorAll<HTMLElement>('[data-card]').forEach(n => { hs[Number(n.dataset.card)] = n.offsetHeight; });
      setHeights(prev => prev.length === hs.length && prev.every((h, i) => h === hs[i]) ? prev : hs);
    };
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    el.querySelectorAll('[data-card]').forEach(n => ro.observe(n));
    return () => ro.disconnect();
  }, [columns]);

  return (
    <section className="admin-group">
      <h2>{title}</h2>
      <div className="admin-masonry" ref={ref}>
        {columns.map((col, c) => (
          <div key={c}>{col.map(i => <div key={i} data-card={i}>{items[i]}</div>)}</div>
        ))}
      </div>
    </section>
  );
}

function Table({ title, rows, note, labelFirst }: { title: string; rows: Count[]; note?: string; labelFirst?: boolean }) {
  return (
    <section className="pane admin-card">
      <div className="admin-card-h">
        <h2>{title}</h2>
        {rows.length > 0 && <button className="linkbtn" onClick={() => downloadCsv(title, rows)}>CSV</button>}
      </div>
      {note && <p className="admin-note">{note}</p>}
      {rows.length === 0 ? <p className="admin-note">Nothing yet.</p> : (
        <table>
          <thead><tr><th>{labelFirst ? 'Name' : 'What'}</th><th className="r">Visitors</th><th className="r">Times</th></tr></thead>
          <tbody>
            {rows.map(row => (
              <tr key={row.key}>
                <td>{labelFirst && row.label ? <>{row.label} <small>{row.key}</small></> : <>{row.key}{row.label && <small> {row.label}</small>}</>}</td>
                <td className="r">{row.visitors.toLocaleString('en-US')}</td>
                <td className="r">{row.count.toLocaleString('en-US')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

const VISITS_PAGE = 25;

/** Each visit on one line (newest first); open one to see everything the visitor did, in order. */
function VisitList({ data }: { data: Visits }) {
  const [websiteOnly, setWebsiteOnly] = useState(true);
  const [shown, setShown] = useState(VISITS_PAGE);
  const list = websiteOnly ? data.visits.filter(v => v.source === 'website') : data.visits;
  return (
    <section className="admin-group">
      <div className="admin-visits-h">
        <h2>Visits</h2>
        <label className="admin-owner">
          <input type="checkbox" className="switch-box" checked={websiteOnly} onChange={e => setWebsiteOnly(e.target.checked)} />
          Website only (hide public API use)
        </label>
      </div>
      <div className="pane admin-card">
        <p className="admin-note">
          One visitor's actions with no break longer than 30 minutes. A visitor can only be followed within one day (UTC).
          {data.total > data.visits.length && ` Showing the newest ${data.visits.length.toLocaleString('en-US')} of ${data.total.toLocaleString('en-US')}.`}
        </p>
        {list.length === 0 ? <p className="admin-note">Nothing yet.</p> : (
          <ul className="admin-visits">
            {list.slice(0, shown).map(v => <VisitRow key={`${v.start}|${v.end}|${v.device}|${v.browser}|${v.os}|${v.city}`} v={v} />)}
          </ul>
        )}
        {list.length > shown && <button className="linkbtn admin-more" onClick={() => setShown(s => s + VISITS_PAGE)}>Show {Math.min(VISITS_PAGE, list.length - shown)} more</button>}
      </div>
    </section>
  );
}

function VisitRow({ v }: { v: Visit }) {
  const place = [v.city, v.region, v.country && countryName(v.country)].filter(Boolean).join(', ') || 'Unknown place';
  const minutes = Math.round((Date.parse(v.end) - Date.parse(v.start)) / 60_000);
  const did = [
    v.searches && plural(v.searches, 'search', 'searches'),
    v.companyViews && plural(v.companyViews, 'company', 'companies'),
    v.executiveViews && plural(v.executiveViews, 'executive', 'executives'),
  ].filter(Boolean).join(' · ') || plural(v.events, 'event', 'events');
  return (
    <li>
      <details>
        <summary>
          <span className="admin-visit-when">{when(v.start)}</span>
          <span className="admin-visit-who">
            <b>{place}</b>
            <small>{v.device} · {v.browser} · {v.os}{v.referrer && <> · from {v.referrer}</>}{v.source !== 'website' && <> · {SOURCES[v.source] ?? v.source}</>}</small>
          </span>
          <span className="admin-visit-did">{did}<small>{minutes < 1 ? 'under a minute' : `${minutes} min`}</small></span>
        </summary>
        <ol className="admin-timeline">
          {v.steps.map((s, i) => (
            <li key={i}>
              <time>{new Date(s.at).toISOString().slice(11, 19)}</time>
              <span>{describe(s)}</span>
            </li>
          ))}
          {v.events > v.steps.length && <li><time /><span className="admin-note">…and {v.events - v.steps.length} more</span></li>}
        </ol>
      </details>
    </li>
  );
}

/** A step in plain words, with the company or executive linked to its page on the site. */
function describe(s: Step): ReactNode {
  const d = parseDetail(s.detail);
  const near = s.label ?? d.near ?? 'a spot on the map';
  const within = [d.results && `${d.results} results`, d.radiusMiles && `within ${d.radiusMiles} mi`, d.sector].filter(Boolean).join(', ');
  switch (s.name) {
    case 'page_view': return <>Opened <code>{s.path ?? '/'}</code></>;
    case 'search': return <>Searched companies near <b>{near}</b>{within && <small> {within}</small>}</>;
    case 'executive_search': return <>Searched executives near <b>{near}</b>{within && <small> {within}</small>}</>;
    case 'place_lookup': return <>Typed <b>{s.subject}</b>{s.label && <small> {s.label}</small>}</>;
    case 'company_view': return s.subject
      ? <>Opened company <a href={companyPath(s.subject)} target="_blank" rel="noreferrer">{s.label ?? s.subject}</a> <small>{s.subject}</small></>
      : <>Opened a company</>;
    case 'executive_view': return s.subject
      ? <>Opened executive <a href={personPath(s.subject)} target="_blank" rel="noreferrer">{s.label ?? s.subject}</a></>
      : <>Opened an executive</>;
    case 'name_search': return <>Searched by name{s.subject && <> for <b>{s.subject}</b></>}</>;
    case 'location_area': return <>Picked the area <b>{s.subject}</b></>;
    case 'location_link': return <>Arrived on a link for <b>{s.subject}</b></>;
    default: return <>{ACTIONS[s.name] ?? s.name}{s.subject && <small> {s.subject}</small>}</>;
  }
}

function parseDetail(detail: string | null): Record<string, string | undefined> {
  if (!detail) return {};
  try { return JSON.parse(detail); } catch { return {}; }
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** "Sep 22, 14:05 UTC": the dashboard works in UTC days throughout. */
function when(iso: string): string {
  const d = new Date(iso);
  return `${d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })}, ${d.toISOString().slice(11, 16)}`;
}

const ACTIONS: Record<string, string> = {
  page_view: 'Page views', search: 'Company searches', executive_search: 'Executive searches', company_view: 'Company opened',
  executive_view: 'Executive opened', place_lookup: 'Postcode / city typed', location_gps: 'Used "my location"',
  location_zip: 'Typed a ZIP / postcode', location_area: 'Picked an area button', view_map: 'Switched to Map', view_list: 'Switched to List',
  mode_companies: 'Switched to Companies', mode_executives: 'Switched to Executives', fact_next: 'Next quick fact (↻)',
  fact_info: 'Quick fact source (i)', executives_more: 'Show more executives', companies_more: 'Show more companies', about_open: 'Opened "About the data"',
  privacy_open: 'Opened privacy notice', report_open: 'Opened "Report a problem"',
};
const SOURCES: Record<string, string> = { website: 'The website', api: 'Public API (no key)', 'api-key': 'Public API (with a key)' };

function countryName(code: string): string {
  try { return `${new Intl.DisplayNames(['en'], { type: 'region' }).of(code) ?? code} (${code})`; } catch { return code; }
}

function downloadCsv(title: string, rows: Count[]) {
  const cell = (v: string | number | null) => {
    const s = v == null ? '' : String(v);
    return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const csv = ['key,label,visitors,count', ...rows.map(r => [r.key, r.label, r.visitors, r.count].map(cell).join(','))].join('\r\n');
  save(new Blob([csv], { type: 'text/csv;charset=utf-8' }), `${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}.csv`);
}

async function downloadEvents(key: string, days: number) {
  const res = await call(`/events.csv?days=${days}`, key);
  save(await res.blob(), `companypaisa-events-last-${days}-days.csv`);
}

function save(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = Object.assign(document.createElement('a'), { href: url, download: name });
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
