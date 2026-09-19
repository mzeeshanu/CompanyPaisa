import { useState, type ReactNode } from 'react';
import type { CompanyInsights, NewExecutive, PackageItemKind, PayVsPeers, Rank } from '../api/types';
import { currencySymbol, money, pct, tone, trendClass } from '../lib/format';
import { companyPath, Link, personPath } from '../lib/router';
import { duration, exact, perUnitOfTime } from './QuickFact';

interface Fact { key: string; icon: string; text: ReactNode }

/** Ranks worth quoting: the top 10, or the top 10% of a big group. */
const notable = (r: Rank | null, top: number) => !!r && (r.rank <= top || r.rank <= r.count * 0.1);

/** One unit of the currency: "$1", "£1", "Rs 1". */
const oneUnit = (currency: string) => currency === 'PKR' ? 'Rs 1' : `${currencySymbol(currency).trim()}1`;

/** 0.2255 → "23%" · 0.034 → "3.4%" */
const size = (ratio: number) => `${Math.abs(ratio * 100).toFixed(Math.abs(ratio) < 0.1 ? 1 : 0)}%`;

/** 0.034 of a dollar → "3¢"; pence for pounds, paisa for rupees; "less than 1¢" rather than "0¢". */
function perUnit(ratio: number, currency: string) {
  const n = Math.round(Math.abs(ratio) * 100);
  const unit = currency === 'GBP' ? 'p' : currency === 'PKR' ? ' paisa' : '¢';
  return n === 0 ? `less than 1${unit}` : `${n}${unit}`;
}

/** "At a glance": short facts worked out from the company's own figures; only the ones the data supports appear. */
export function AtAGlance({ insights: i, name }: { insights: CompanyInsights; name: string }) {
  const cur = i.currency;
  const facts: Fact[] = [];

  // The newest appointment of the last year, with its announced package.
  const latest = i.newExecutives?.[0];
  if (latest && new Date(`${latest.startsOn ?? latest.announcedOn}T00:00:00`) > new Date(Date.now() - 365 * 864e5)) {
    const when = latest.startsOn ?? latest.announcedOn;
    const future = new Date(`${when}T00:00:00`) > new Date();
    facts.push({
      key: 'new', icon: '🆕',
      text: <>
        New {latest.title}{' '}
        {latest.personId ? <Link className="linkbtn" to={personPath(latest.personId)}>{latest.name}</Link> : <b>{latest.name}</b>}{' '}
        {future ? 'joins' : 'joined'} on {shortDate(when)} with an announced package of <b>{money(latest.total, latest.currency)}</b>.
      </>,
    });
  }

  if (i.payVsResults) {
    const p = i.payVsResults;
    facts.push({
      key: 'pay', icon: '⚖️',
      text: <>
        CEO <Link className="linkbtn" to={personPath(p.personId)}>{p.name}</Link>'s pay {p.payChange >= 0 ? 'rose' : 'fell'}{' '}
        <b className={tone(p.payChange)}>{size(p.payChange)}</b> to {money(p.totalPay, p.currency)} in {p.year}, while revenue for fiscal {p.year}{' '}
        {p.revenueChange >= 0 ? 'rose' : 'fell'} <b className={tone(p.revenueChange)}>{size(p.revenueChange)}</b>.
      </>,
    });
  }
  if (notable(i.sectorRank, 10)) {
    const r = i.sectorRank!;
    facts.push({
      key: 'sector', icon: '🏆',
      text: r.rank === 1
        ? <>The <b>biggest</b> of the {r.count.toLocaleString()} {r.within} companies we track, by revenue.</>
        : <><b>#{r.rank}</b> by revenue among the {r.count.toLocaleString()} {r.within} companies we track.</>,
    });
  }
  if (notable(i.cityRank, 5)) {
    const r = i.cityRank!;
    facts.push({
      key: 'city', icon: '🏙️',
      text: r.rank === 1
        ? <>The <b>biggest</b> of the {r.count} public companies headquartered in {r.within}.</>
        : <><b>#{r.rank}</b> biggest of the {r.count} public companies headquartered in {r.within}.</>,
    });
  }
  if (i.revenueStreak) {
    const s = i.revenueStreak, up = s.direction === 'Up';
    facts.push({
      key: 'streak', icon: up ? '📈' : '📉',
      text: s.unit === 'Quarterly'
        ? <>Revenue has {up ? 'grown' : 'shrunk'} <b className={up ? 'up' : 'down'}>{s.count} quarters in a row</b>, each against the same quarter a year before.</>
        : <>Revenue has {up ? 'grown' : 'shrunk'} <b className={up ? 'up' : 'down'}>{s.count} years in a row</b>.</>,
    });
  }
  if (i.records) {
    const r = i.records;
    facts.push({
      key: 'best', icon: '🥇',
      text: r.bestIsLatest
        ? <><b>{r.bestYear} was its best year yet</b>, with {money(r.bestRevenue, cur)} in revenue.</>
        : <>Best year for revenue: <b>{r.bestYear}</b>, with {money(r.bestRevenue, cur)}.</>,
    });
    facts.push({
      key: 'profit', icon: r.profitableYears * 2 >= r.yearsCounted ? '✅' : '⚠️',
      text: r.profitableYears === 0
        ? <>Hasn't made a profit in <b>any of the last {r.yearsCounted} years</b>.</>
        : r.profitableYears === r.yearsCounted
          ? <>Made a profit in <b>every one of the last {r.yearsCounted} years</b>.</>
          : <>Made a profit in <b>{r.profitableYears} of the last {r.yearsCounted} years</b>.</>,
    });
  }
  if (i.ceoVsWorker?.medianWorker) {
    const c = i.ceoVsWorker, m = i.ceoVsWorker.medianWorker;
    facts.push({
      key: 'worker', icon: '⏱',
      text: <>
        <Link className="linkbtn" to={personPath(c.personId)}>{c.name}</Link> made {money(c.totalPay, c.currency)} in {c.year}: a typical{' '}
        {m.description}'s yearly pay <b>every {duration(m.hoursToEarn)}</b>.
      </>,
    });
  }
  if (i.marginVsSector) {
    const m = i.marginVsSector;
    const keeps = (r: number) => r >= 0 ? 'keeps' : 'loses';
    facts.push({
      key: 'margin', icon: '🧮',
      text: <>
        {m.netMargin >= 0
          ? <>Keeps <b>{perUnit(m.netMargin, cur)}</b> of every {oneUnit(cur)} of revenue as profit</>
          : <>Loses <b className="down">{perUnit(m.netMargin, cur)}</b> on every {oneUnit(cur)} of revenue</>}; the median {m.sector}{' '}
        company {keeps(m.sectorMedian)} {perUnit(m.sectorMedian, cur)}.
      </>,
    });
  }
  if (i.revenuePerEmployee && i.employees) {
    facts.push({
      key: 'employee', icon: '👥',
      text: <><b>{money(i.revenuePerEmployee, cur)}</b> of revenue per employee, across {i.employees.toLocaleString()} people.</>,
    });
  } else if (i.revenuePerSecond > 0) {
    const t = perUnitOfTime(i.revenuePerSecond * 365.25 * 24 * 3600);
    facts.push({
      key: 'second', icon: '💵',
      text: <>Takes in <b>{exact(t.amount, cur)} every {t.unit}</b> in sales, day and night.</>,
    });
  }

  if (facts.length === 0) return null;
  return (
    <section className="pane page-card glance" aria-labelledby="glance-h">
      <h2 className="subh" id="glance-h">{name} at a glance</h2>
      <ul className="glance-list">
        {facts.map(f => <li key={f.key}><span className="glance-ico" aria-hidden="true">{f.icon}</span><p>{f.text}</p></li>)}
      </ul>
      <p className="fine">Worked out from the figures on this page and the other companies we track. Revenue is the latest 12 months unless a year is named.</p>
    </section>
  );
}

/** Companies closest to this one's headquarters: the same sector when there are enough, otherwise any. */
export function SimilarCompanies({ insights: i }: { insights: CompanyInsights }) {
  if (i.similar.length === 0) return null;
  return (
    <section className="pane page-card" aria-labelledby="similar-h">
      <h2 className="subh" id="similar-h">{i.similarSameSector ? 'Similar companies nearby' : 'Companies nearby'}</h2>
      <ul className="similar-list">
        {i.similar.map(s => (
          <li key={s.ticker}>
            <Link className="similar-row" to={companyPath(s.ticker)}>
              <span className={`mini t-${trendClass(s.trend)}`} aria-hidden="true" />
              <span className="similar-who"><b>{s.name}</b><small>{s.ticker} · {s.city}, {s.state} · {s.distanceMiles < 1 ? 'same area' : `${s.distanceMiles.toFixed(0)} mi`}</small></span>
              <span className="similar-num"><span className="num">{money(s.ttmRevenue, s.currency)}</span><span className={`pill ${tone(s.revenueGrowthYoY)}`}>{pct(s.revenueGrowthYoY)}</span></span>
            </Link>
          </li>
        ))}
      </ul>
      <p className="fine">{i.similarSameSector ? 'Same sector, closest to its headquarters' : 'Closest to its headquarters'} · revenue for the latest 12 months and growth on the year before.</p>
    </section>
  );
}

// ---------- New leadership: appointments and the packages the company announced ----------

const PACKAGE_COLORS: Record<PackageItemKind, string> = {
  Salary: 'var(--s1)', Bonus: 'var(--s2)', SignOnCash: 'var(--flat)', OtherCash: 'var(--s4)',
  Stock: 'var(--s3)', PerformanceStock: 'var(--up)', Options: 'var(--accent)',
};

/** "16 Apr 2026" */
export const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

/** Same-label items added up ("Stock awards $2.0M + $800K" → one line), biggest first. */
function packageLines(p: NewExecutive) {
  const lines = new Map<string, { label: string; kind: PackageItemKind; amount: number; count: number }>();
  for (const item of p.package) {
    const line = lines.get(item.label) ?? { label: item.label, kind: item.kind, amount: 0, count: 0 };
    line.amount += item.amount; line.count++;
    lines.set(item.label, line);
  }
  return [...lines.values()].sort((a, b) => b.amount - a.amount);
}

/** Officers appointed recently with the package the company announced in its filing (not pay received). */
export function NewLeadership({ people }: { people: NewExecutive[] }) {
  const [all, setAll] = useState(false);
  const shown = all ? people : people.slice(0, 3);
  return (
    <section className="pane page-card new-leadership" aria-labelledby="new-h">
      <h2 className="subh" id="new-h"><span className="news-pill">News</span>New leadership</h2>
      <ul className="new-list">
        {shown.map(p => {
          const lines = packageLines(p);
          const upcoming = p.startsOn && new Date(`${p.startsOn}T00:00:00`) > new Date();
          return (
            <li key={`${p.name}-${p.announcedOn}`}>
              <div className="new-top">
                <div>
                  <b className="new-name">{p.personId ? <Link className="linkbtn" to={personPath(p.personId)}>{p.name} →</Link> : p.name}</b>
                  <small>{p.title}</small>
                  <small className="new-dates">Announced {shortDate(p.announcedOn)}{p.startsOn && <> · {upcoming ? 'starts' : 'started'} {shortDate(p.startsOn)}</>}</small>
                </div>
                <div className="new-total num">{money(p.total, p.currency)}<small>announced package</small></div>
              </div>
              <div className="stack">
                {lines.map(l => <i key={l.label} style={{ width: `${(l.amount / p.total) * 100}%`, background: PACKAGE_COLORS[l.kind] }} title={`${l.label}: ${money(l.amount, p.currency)}`} />)}
              </div>
              <ul className="new-parts">
                {lines.map(l => (
                  <li key={l.label}><i style={{ background: PACKAGE_COLORS[l.kind] }} />{l.label}{l.count > 1 ? ` (${l.count} grants)` : ''}<span className="num">{money(l.amount, p.currency)}</span></li>
                ))}
              </ul>
              {p.sourceFiling && <a className="linkbtn filing" href={p.sourceFiling} target="_blank" rel="noreferrer">Read the announcement (8-K) ↗</a>}
            </li>
          );
        })}
      </ul>
      {people.length > 3 && <button className="linkbtn page-more" onClick={() => setAll(a => !a)}>{all ? 'Show fewer' : `Show all ${people.length}`}</button>}
      <p className="fine">What the company said it would pay when it announced the appointment, not pay received. Stock is at the value the
        company stated; bonuses count only where a dollar amount was given, not a percentage target. Read automatically from the
        filing, so check it before relying on it.</p>
    </section>
  );
}

// ---------- Pay vs similar companies ----------

/** "more than 64%" / "less than 70%" / "about the middle" of similar companies. */
function standing(percentile: number) {
  if (percentile >= 55) return <>more than <b className="up">{percentile}%</b> of similar companies</>;
  if (percentile <= 45) return <>less than <b className="down">{100 - percentile}%</b> of similar companies</>;
  return <>about the <b>middle</b> of similar companies</>;
}

const vsMedian = (value: number, median: number) => median > 0 ? value / median - 1 : null;

/** Is this company paying its executives more or less than companies like it (same sector, similar size)? */
export function PayVsPeersCard({ pay: p, name, revenueCurrency }: { pay: PayVsPeers; name: string; revenueCurrency: string }) {
  const cur = p.currency;
  const top = p.topRole === 'CEO' ? 'CEO' : 'top-paid executive director';
  const max = Math.max(...p.nearby.map(n => n.topPay), 1);
  const topDiff = vsMedian(p.topPay, p.topPayPeerMedian);
  const otherDiff = p.otherExecutivesPay != null && p.otherExecutivesPeerMedian ? vsMedian(p.otherExecutivesPay, p.otherExecutivesPeerMedian) : null;
  return (
    <section className="pane page-card peer-pay" aria-labelledby="peer-pay-h">
      <h2 className="subh" id="peer-pay-h">Pay vs similar companies</h2>
      <p className="peer-lead">Pays its {top} {standing(p.topPayPercentile)}.</p>
      <div className="peer-tiles">
        <div className="kpi">
          <span className="k">{top === 'CEO' ? 'CEO pay' : 'Top executive'}</span>
          <span className="s">median of similar: {money(p.topPayPeerMedian, cur)}</span>
          <span className="v">{money(p.topPay, cur)}</span>
          {topDiff != null && <span className={`chg ${tone(topDiff)}`}>{pct(topDiff)} vs median</span>}
        </div>
        {p.otherExecutivesPay != null && p.otherExecutivesPeerMedian != null && (
          <div className="kpi">
            <span className="k">Other executives</span>
            <span className="s">median of similar: {money(p.otherExecutivesPeerMedian, cur)}</span>
            <span className="v">{money(p.otherExecutivesPay, cur)}</span>
            {otherDiff != null && <span className={`chg ${tone(otherDiff)}`}>{pct(otherDiff)} vs median</span>}
            <span className="s peer-standing">{standingShort(p.otherExecutivesPercentile!)}</span>
          </div>
        )}
      </div>
      <p className="fine peer-list-h">The {p.nearby.length - 1} companies closest in size, by {top === 'CEO' ? 'CEO' : 'top executive'} pay:</p>
      <ol className="peer-list" aria-label={`${top} pay at ${name} and the similar companies either side`}>
        {p.nearby.map(n => (
          <li key={n.ticker} className={n.isThisCompany ? 'me' : undefined}>
            {n.isThisCompany
              ? <span className="peer-name"><b>{n.name}</b><small>this company · {n.year}</small></span>
              : <Link className="peer-name" to={companyPath(n.ticker)}><b>{n.name}</b><small>{n.ticker} · {n.year}</small></Link>}
            <span className="peer-bar"><i style={{ width: `${(n.topPay / max) * 100}%` }} /></span>
            <span className="num">{money(n.topPay, cur)}</span>
          </li>
        ))}
      </ol>
      <p className="fine">
        Similar = {p.peerCount} {p.sector} companies
        {p.maxRevenue > 0 ? <> with revenue between {money(p.minRevenue, revenueCurrency)} and {money(p.maxRevenue, revenueCurrency)}</> : ' of every size'},
        comparing each one's latest reported year: the {top === 'CEO' ? 'CEO' : 'top-paid executive director'}'s total pay, and the median of the other
        named executives. Pay as reported, with stock at its grant value{p.approximate ? '; other currencies converted at approximate rates' : ''}.
      </p>
    </section>
  );
}

const standingShort = (percentile: number) =>
  percentile >= 55 ? `paid more than ${percentile}% of similar` : percentile <= 45 ? `paid less than ${100 - percentile}% of similar` : 'about the middle of similar';
