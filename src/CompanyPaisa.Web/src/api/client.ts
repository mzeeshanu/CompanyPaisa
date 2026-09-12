import type {
  ClientConfig, CompanyDetail, CompanySort, DataMeta, ExecutivesResponse, FinancialsResponse,
  GeoLookup, NearbyResponse, PeriodType, ProblemDetails,
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
  const res = await fetch(url, { signal, headers: { Accept: 'application/json' } });
  if (!res.ok) {
    let problem: ProblemDetails | null = null;
    try { problem = await res.json(); } catch { /* not JSON */ }
    throw new ApiError(res.status, problem);
  }
  return res.json() as Promise<T>;
}

export interface NearbyQuery {
  latitude: number; longitude: number; radiusMiles: number;
  sector?: string; headquarteredOnly?: boolean; sort?: CompanySort; pageSize?: number;
}

export const api = {
  clientConfig: () => get<ClientConfig>('/client-config'),
  meta: () => get<DataMeta>('/meta'),
  sectors: () => get<string[]>('/sectors'),
  lookup: (q: string) => get<GeoLookup>('/geo/lookup', { q }),
  near: (q: NearbyQuery, signal?: AbortSignal) =>
    get<NearbyResponse>('/companies/near', {
      latitude: q.latitude, longitude: q.longitude, radiusMiles: q.radiusMiles,
      sector: q.sector, headquarteredOnly: q.headquarteredOnly || undefined, sort: q.sort, pageSize: q.pageSize ?? 200,
    }, signal),
  company: (ticker: string) => get<CompanyDetail>(`/companies/${encodeURIComponent(ticker)}`),
  financials: (ticker: string, period: PeriodType, years?: number) =>
    get<FinancialsResponse>(`/companies/${encodeURIComponent(ticker)}/financials`, { period, years }),
  executives: (ticker: string, years = 5) =>
    get<ExecutivesResponse>(`/companies/${encodeURIComponent(ticker)}/executives`, { years }),
};
