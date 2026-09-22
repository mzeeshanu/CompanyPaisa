// Mirrors src/CompanyPaisa.Contracts (C#). Keep in sync until types are generated from /openapi/v1.json.
// Money values are whole dollars.

export type TrendStatus = 'Up' | 'Flat' | 'Down';
export type CompanySort = 'Revenue' | 'Growth' | 'Profit' | 'Distance';
export type PeriodType = 'Quarterly' | 'Annual';
/** What an executive does, read from their title (Other = none of the named roles). */
export type RoleFilter = 'Ceo' | 'Cfo' | 'Coo' | 'Technology' | 'Legal' | 'Other';
export type LocationType = 'Headquarters' | 'Campus' | 'Office' | 'Plant';

export interface GeoPoint { latitude: number; longitude: number }

export interface GeoLookup { query: string; city: string; state: string; postalCode: string | null; point: GeoPoint }

export interface Location {
  locationId: string; type: LocationType; label: string; street: string;
  city: string; state: string; postalCode: string; point: GeoPoint;
}

export interface AnnualPoint { fiscalYear: number; revenue: number; netIncome: number }

export interface Indicators {
  ttmRevenue: number; ttmNetIncome: number;
  revenueGrowthYoY: number | null; revenueCagr: number | null; cagrYears: number;
  netMargin: number | null; trend: TrendStatus;
  latestQuarterLabel: string | null; latestQuarterRevenue: number | null; latestQuarterNetIncome: number | null;
  revenueHistory: AnnualPoint[];
}

export interface CompanySummary {
  ticker: string; name: string; exchange: string; sector: string;
  isHeadquarteredNearby: boolean; nearestLocation: Location; distanceMiles: number; indicators: Indicators;
  currency: string;
}

/** Totals are in `currency`; `approximate` = some results were converted from another currency. */
export interface NearbySummary {
  companyCount: number; combinedTtmRevenue: number; growingCount: number; headquarteredCount: number;
  currency: string; approximate: boolean;
  /** The best-paid CEO of a company based in the results (latest year), or null. */
  topPaidCeo?: TopPaidCeo | null;
}

export interface TopPaidCeo {
  personId: string; name: string; title: string; ticker: string; companyName: string;
  year: number; totalPay: number; currency: string;
  /** "CEO", or "executive director" where the filing doesn't say who the chief executive is (UK annual reports). */
  role: string;
  medianWorker: MedianPayComparison | null;
}

/** A country's median full-time pay and how many hours (of a 24/7 year) the CEO takes to earn it. */
export interface MedianPayComparison {
  country: string; description: string; annualPay: number; currency: string; period: string;
  source: string; sourceUrl: string; hoursToEarn: number; approximate: boolean;
}

export interface NearbyResponse {
  origin: GeoPoint; originLabel: string | null; radiusMiles: number; sort: CompanySort;
  page: number; pageSize: number; totalCount: number; summary: NearbySummary; items: CompanySummary[];
}

export interface CompanyDetail {
  ticker: string; name: string; exchange: string; sector: string; industry: string | null; website: string | null;
  employees: number | null; marketCap: number | null; description: string | null; currency: string;
  fiscalYearEnd: string | null; asOfDate: string | null; locations: Location[]; indicators: Indicators;
  /** Currency of executive pay (can differ from the accounts' currency). */
  payCurrency?: string | null;
  /** The company's careers or jobs page, when one was found on its website. */
  careersUrl?: string | null;
  /** Median employee vs CEO pay by year, as disclosed in the proxy statement (US companies). */
  workerPay?: WorkerPay[] | null;
  /** How many job titles have salaries (from H-1B wage filings); 0 = none. */
  salaryTitles?: number;
}

/** The CEO was paid `ratio` times what the median employee was. */
export interface WorkerPay { year: number; medianEmployeePay: number; ceoPay: number; ratio: number; currency: string; sourceFiling: string | null }

export interface JobSalaryPlace { city: string; state: string; filings: number; low: number; median: number; high: number; latitude: number | null; longitude: number | null }
/**
 * Yearly salary for one job title. Visa filings: low = 25th percentile, high = 75th of the salaries offered. Job ads: the
 * typical bottom and top of the advertised range; `url` is a current ad. `filings` counts filings or ads.
 */
export interface JobSalary { title: string; occupation: string | null; filings: number; low: number; median: number; high: number; min: number; max: number; places: JobSalaryPlace[]; url?: string | null }
/** One source: 'postings' (pay ranges in the company's job ads) or 'h1b' (its work-visa wage filings). */
export interface JobSalarySet { kind: 'postings' | 'h1b'; from: string; to: string; source: string; titles: JobSalary[] }
export interface JobSalariesResponse { ticker: string; currency: string; sources: JobSalarySet[] }

export interface FinancialPeriod {
  label: string; fiscalYear: number; fiscalQuarter: number | null; periodType: PeriodType;
  revenue: number; netIncome: number; revenueGrowthYoY: number | null; sourceFiling: string | null;
}
export interface FinancialsResponse { ticker: string; periodType: PeriodType; periods: FinancialPeriod[] }

export interface ExecutiveYear { year: number; salary: number; bonus: number; stockAwards: number; other: number; total: number; sourceFiling: string | null }
export interface Executive { executiveId: string; name: string; title: string; history: ExecutiveYear[] }
export interface ExecutivesResponse { ticker: string; executives: Executive[] }

// ---------- executives (people) ----------

export type ExecutiveSort = 'Pay' | 'TotalPay' | 'PayGrowth' | 'Distance' | 'Name';

export interface CompanyRef { ticker: string; name: string; sector: string; currency: string }
export interface PayPoint { year: number; total: number; ticker: string }

export interface ExecutiveSummary {
  personId: string; name: string; title: string; company: CompanyRef;
  nearestLocation: Location; distanceMiles: number; isCurrent: boolean;
  latestYear: number; latestTotalPay: number; payGrowthYoY: number | null;
  windowTotalPay: number; windowYears: number; companyCount: number; payHistory: PayPoint[];
  /** Appointed recently, with the package the company announced. */
  newHire?: NewExecutive | null;
  /** False when known only from the appointment announcement (no pay reported yet, so no person page). */
  hasProfile?: boolean;
}

export type PackageItemKind = 'Salary' | 'SignOnCash' | 'Bonus' | 'Stock' | 'PerformanceStock' | 'Options' | 'OtherCash';

/** An officer appointment the company announced (8-K Item 5.02) with the package it stated — not pay received. */
export interface NewExecutive {
  personId: string | null; name: string; title: string; company: CompanyRef;
  announcedOn: string; startsOn: string | null; total: number; currency: string;
  package: { kind: PackageItemKind; label: string; amount: number }[];
  sourceFiling: string | null;
}

export interface ExecutivesNearResponse {
  origin: GeoPoint; originLabel: string | null; radiusMiles: number; sort: ExecutiveSort;
  page: number; pageSize: number; totalCount: number;
  summary: {
    executiveCount: number; companyCount: number; combinedLatestPay: number; medianLatestPay: number | null; latestYear: number | null;
    currency: string; approximate: boolean;
  };
  items: ExecutiveSummary[];
}

export interface ExecutiveRole { company: CompanyRef; title: string; fromYear: number; toYear: number; totalPay: number }
export interface ExecutivePayYear {
  year: number; company: CompanyRef; title: string;
  salary: number; bonus: number; stockAwards: number; other: number; total: number; sourceFiling: string | null;
}
export interface ExecutiveDetail {
  personId: string; name: string; secCik: string | null; currentTitle: string; currentCompany: CompanyRef;
  firstYear: number; latestYear: number; totalPay: number; yearsReported: number;
  roles: ExecutiveRole[]; history: ExecutivePayYear[];
}

export interface DataMeta { dataVersion: string; asOfDate: string | null; isSampleData: boolean; companyCount: number; locationCount: number; loadedAt: string }

export interface ClientConfig {
  defaultView: 'List' | 'Map'; defaultTheme: 'Auto' | 'Light' | 'Dark';
  defaultRadiusMiles: number; allowedRadiiMiles: number[]; defaultSort: CompanySort;
  showBaseMapByDefault: boolean; mapTilesUrl: string | null;
  consentCookieName: string; consentCookieDays: number; features: Record<string, boolean>;
  coverage: CoverageArea[];
  /** Email address or URL for corrections and data-protection requests; null until the site owner sets one. */
  privacyContact?: string | null;
}

export type Country = 'US' | 'CA' | 'UK' | 'FR' | 'NL' | 'IT' | 'ES' | 'AU' | 'NZ' | 'PK';
export interface CoverageArea { name: string; exampleZip: string; country: Country }

/** Facts worked out from a company's figures; each is null when the data doesn't support it. */
export interface CompanyInsights {
  ticker: string; currency: string;
  payVsResults: { personId: string; name: string; title: string; year: number; totalPay: number; currency: string; payChange: number; revenueChange: number } | null;
  sectorRank: Rank | null;
  cityRank: Rank | null;
  revenueStreak: { direction: TrendStatus; count: number; unit: PeriodType } | null;
  records: { bestYear: number; bestRevenue: number; bestIsLatest: boolean; profitableYears: number; yearsCounted: number } | null;
  ceoVsWorker: TopPaidCeo | null;
  marginVsSector: { netMargin: number; sectorMedian: number; sectorCount: number; sector: string } | null;
  revenuePerEmployee: number | null; employees: number | null; revenuePerSecond: number;
  similarSameSector: boolean;
  similar: SimilarCompany[];
  newExecutives?: NewExecutive[] | null;
  payVsPeers?: PayVsPeers | null;
}

/** How a company's executive pay compares with similar companies (same sector and size). Percentiles = share of peers paid less. */
export interface PayVsPeers {
  sector: string; minRevenue: number; maxRevenue: number; peerCount: number; currency: string; approximate: boolean;
  topRole: string; topPay: number; topPayPeerMedian: number; topPayPercentile: number;
  otherExecutivesPay: number | null; otherExecutivesPeerMedian: number | null; otherExecutivesPercentile: number | null;
  nearby: { ticker: string; name: string; role: string; topPay: number; year: number; isThisCompany: boolean }[];
}
export interface Rank { rank: number; count: number; within: string }
export interface SimilarCompany {
  ticker: string; name: string; sector: string; city: string; state: string; distanceMiles: number;
  ttmRevenue: number; revenueGrowthYoY: number | null; trend: TrendStatus; currency: string;
}

export interface NameSearchCompany {
  ticker: string; name: string; exchange: string; sector: string; city: string | null; state: string | null;
  ttmRevenue: number; currency: string;
}
export interface NameSearchExecutive { personId: string; name: string; title: string; company: CompanyRef; latestYear: number; latestTotalPay: number }
export interface NameSearchResponse { query: string; companies: NameSearchCompany[]; executives: NameSearchExecutive[] }

export interface ProblemDetails { title?: string; detail?: string; status?: number; errors?: Record<string, string[]> }
