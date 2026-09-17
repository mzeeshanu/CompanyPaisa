import { useEffect, useState, type ReactNode } from 'react';
import type { NearbySummary } from '../api/types';
import { currencySymbol, money } from '../lib/format';

interface Props {
  summary: NearbySummary;
  showExecutives: boolean;
  onOpenPerson: (personId: string) => void;
}

interface Fact { icon: string; text: ReactNode; about: ReactNode }

const SECONDS_PER_YEAR = 365.25 * 24 * 3600;

/** One line of perspective between the bubbles and the ranked list; ↻ cycles through the facts that apply to this search. */
export function QuickFact({ summary, showExecutives, onOpenPerson }: Props) {
  const facts = [revenueFact(summary), showExecutives ? ceoFact(summary, onOpenPerson) : null].filter((f): f is Fact => f !== null);
  // The same search always opens on the same fact; ↻ moves on from there.
  const [step, setStep] = useState(0);
  const [info, setInfo] = useState(false);
  useEffect(() => setInfo(false), [step]);
  if (facts.length === 0) return null;
  const fact = facts[(summary.companyCount + step) % facts.length];

  return (
    <section className="fact pane" aria-label="Quick fact">
      <div className="fact-line">
        <span className="fact-ico" aria-hidden="true">{fact.icon}</span>
        <p aria-live="polite">{fact.text}</p>
        <span className="fact-btns">
          <button className="fact-btn" aria-expanded={info} aria-label="Where this number comes from" title="Where this number comes from"
            onClick={() => setInfo(i => !i)}>i</button>
          {facts.length > 1 && (
            <button className="fact-btn" aria-label="Another fact" title="Another fact" onClick={() => setStep(s => s + 1)}>↻</button>
          )}
        </span>
      </div>
      {info && <p className="fact-about">{fact.about}</p>}
    </section>
  );
}

function revenueFact(s: NearbySummary): Fact | null {
  if (s.companyCount < 2 || s.combinedTtmRevenue <= 0) return null;
  const rate = perUnitOfTime(s.combinedTtmRevenue);
  const approx = s.approximate ? '≈' : '';
  return {
    icon: '⚡',
    text: <>Together, these {s.companyCount} companies bring in <b>{approx}{exact(rate.amount, s.currency)} every {rate.unit}</b>.</>,
    about: <>Their combined revenue over the latest 12 months ({approx}{money(s.combinedTtmRevenue, s.currency)}) spread evenly over a year.
      Revenue is sales worldwide, not only in this area{s.approximate ? '; amounts in other currencies were converted at approximate rates' : ''}.</>,
  };
}

function ceoFact(s: NearbySummary, onOpenPerson: (personId: string) => void): Fact | null {
  const ceo = s.topPaidCeo;
  const median = ceo?.medianWorker;
  if (!ceo || !median) return null;
  return {
    icon: '⏱',
    text: <>
      <button className="linkbtn fact-name" onClick={() => onOpenPerson(ceo.personId)}>{ceo.name}</button> ({ceo.companyName}), the top-paid {ceo.role}{' '}
      based here, made {money(ceo.totalPay, ceo.currency)} in {ceo.year}: a typical {median.description}'s yearly pay{' '}
      <b>every {duration(median.hoursToEarn)}</b>.
    </>,
    about: <>
      Total pay for {ceo.year} as reported in {ceo.companyName}'s filing, including stock awards valued when they were granted. Typical pay
      is the median for a {median.description}, {exact(median.annualPay, median.currency)} a year, measured {median.period}, from
      the <a className="linkbtn" href={median.sourceUrl} target="_blank" rel="noreferrer">{median.source}</a>. Time counts every hour of the
      year, nights and weekends included{median.approximate ? '; the CEO’s pay was converted at an approximate rate' : ''}.
    </>,
  };
}

/** A yearly amount per second — or per minute / hour when a second's share is under one unit of currency. */
function perUnitOfTime(yearly: number): { amount: number; unit: string } {
  const perSecond = yearly / SECONDS_PER_YEAR;
  if (perSecond >= 1) return { amount: perSecond, unit: 'second' };
  if (perSecond * 60 >= 1) return { amount: perSecond * 60, unit: 'minute' };
  return { amount: perSecond * 3600, unit: 'hour' };
}

/** $76,120 · C$9.51 — full figures read better than "$76K" in a sentence. */
function exact(amount: number, currency: string): string {
  const digits = amount >= 100 ? 0 : 2;
  return currencySymbol(currency) + amount.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });
}

/** 0.63 → "38 minutes" · 5.2 → "5.2 hours" · 75 → "3.1 days" */
function duration(hours: number): string {
  const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;
  if (hours < 1 / 60) return plural(Math.max(1, Math.round(hours * 3600)), 'second');
  if (hours < 59.5 / 60) return plural(Math.round(hours * 60), 'minute');
  if (hours < 10) return plural(Math.round(hours * 10) / 10, 'hour');
  if (hours < 48) return plural(Math.round(hours), 'hour');
  return plural(Math.round(hours / 24 * 10) / 10, 'day');
}
