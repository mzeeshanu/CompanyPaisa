import type {
  ClientConfig, CompanyDetail, CompanyInsights, RoleFilter, CompanySort, DataMeta, ExecutiveDetail, ExecutiveSort, ExecutivesNearResponse,
  ExecutivesResponse, FinancialsResponse, GeoLookup, JobSalariesResponse, NameSearchResponse, NearbyResponse, PeriodType, ProblemDetails,
} from './types';

/** Error carrying the API's problem details; `message` is safe to show to users. */
export class ApiError extends Error {
  constructor(public readonly status: number, public readonly problem: ProblemDetails | null) {
    super(problem?.detail ?? firstFieldError(problem) ?? problem?.title ?? `Request failed (${status}).`);
  }
}

function firstFieldError(p: ProblemDetails | null): string | undefined {
  return p?.errors ? Object.values(p.errors).flat()[0] : undefined;
}

// The website only talks to the public API — the same one other apps use.
const BASE = '/api/v1';

async function get<T>(path: string, params?: Record<string, string | number | boolean | null | undefined>, signal?: AbortSignal): Promise<T> {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(params ?? {})) if (v !== undefined && v !== null && v !== '') qs.set(k, String(v));
  const url = `${BASE}${path}${qs.size ? `?${qs}` : ''}`;
  const init: RequestInit = { signal, headers: { Accept: 'application/json' } };
  let res = await fetch(url, init);
  // The page's pass to the API ran out (it was left open for hours, or the server restarted): get a new one, try again once.
  if (res.status === 401 && (await renewSession())) res = await fetch(url, init);
  if (!res.ok) {
    let problem: ProblemDetails | null = null;
    try { problem = await res.json(); } catch { /* not JSON */ }
    throw new ApiError(res.status, problem);
  }
  return res.json() as Promise<T>;
}

/** One renewal at a time, however many calls failed together. */
let renewing: Promise<boolean> | null = null;
function renewSession(): Promise<boolean> {
  renewing ??= fetch('/api/session', { credentials: 'same-origin' })
    .then(r => r.ok, () => false)
    .finally(() => setTimeout(() => { renewing = null; }, 10_000));
  return renewing;
}

/** Every company in the radius (the bubbles and the ranked list need them all; the API allows up to 2,000). */
export const ALL_COMPANIES = 2000;
/** Executives load a page at a time — a big city has thousands. */
export const EXECUTIVES_PAGE = 200;

export interface NearbyQuery {
  latitude: number; longitude: number; radiusMiles: number;
  sector?: string; headquarteredOnly?: boolean; sort?: CompanySort; pageSize?: number;
}

export const api = {
  clientConfig: () => get<ClientConfig>('/client-config'),
  meta: () => get<DataMeta>('/meta'),
  sectors: () => get<string[]>('/sectors'),
  lookup: (q: string) => get<GeoLookup>('/geo/lookup', { q }),
  searchByName: (q: string, signal?: AbortSignal) => get<NameSearchResponse>('/search', { q, limit: 5 }, signal),
  near: (q: NearbyQuery, signal?: AbortSignal) =>
    get<NearbyResponse>('/companies/near', {
      latitude: q.latitude, longitude: q.longitude, radiusMiles: q.radiusMiles,
      sector: q.sector, headquarteredOnly: q.headquarteredOnly || undefined, sort: q.sort, pageSize: q.pageSize ?? ALL_COMPANIES,
    }, signal),
  company: (ticker: string) => get<CompanyDetail>(`/companies/${encodeURIComponent(ticker)}`),
  insights: (ticker: string) => get<CompanyInsights>(`/companies/${encodeURIComponent(ticker)}/insights`),
  salaries: (ticker: string) => get<JobSalariesResponse>(`/companies/${encodeURIComponent(ticker)}/salaries`),
  financials: (ticker: string, period: PeriodType, years?: number) =>
    get<FinancialsResponse>(`/companies/${encodeURIComponent(ticker)}/financials`, { period, years }),
  executives: (ticker: string, years = 5) =>
    get<ExecutivesResponse>(`/companies/${encodeURIComponent(ticker)}/executives`, { years }),
  executivesNear: (q: ExecutivesNearQuery, signal?: AbortSignal) =>
    get<ExecutivesNearResponse>('/executives/near', {
      latitude: q.latitude, longitude: q.longitude, radiusMiles: q.radiusMiles, sector: q.sector,
      includeFormer: q.includeFormer || undefined, search: q.search, role: q.role, sort: q.sort, years: q.years, page: q.page, pageSize: q.pageSize ?? EXECUTIVES_PAGE,
    }, signal),
  executive: (personId: string) => get<ExecutiveDetail>(`/executives/${encodeURIComponent(personId)}`),
};

export interface ExecutivesNearQuery {
  latitude: number; longitude: number; radiusMiles: number;
  sector?: string; includeFormer?: boolean; search?: string; role?: RoleFilter; sort?: ExecutiveSort; years?: number; page?: number; pageSize?: number;
}
