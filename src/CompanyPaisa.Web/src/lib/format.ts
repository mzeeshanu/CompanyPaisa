import type { TrendStatus } from '../api/types';
import * as d3 from 'd3';

const SYMBOLS: Record<string, string> = { USD: '$', GBP: '£', EUR: '€', CAD: 'C$' };

/** $21.5B · £540M · $4.6M · €820K — input is whole currency units; ISO currency code, default USD. */
export function money(amount: number | null | undefined, currency = 'USD'): string {
  if (amount == null) return '—';
  const sym = SYMBOLS[currency] ?? `${currency} `;
  const a = Math.abs(amount);
  let s: string;
  if (a >= 1e12) s = `${sym}${(a / 1e12).toFixed(2)}T`;
  else if (a >= 1e9) s = `${sym}${(a / 1e9).toFixed(a >= 1e10 ? 1 : 2)}B`;
  else if (a >= 1e8) s = `${sym}${(a / 1e6).toFixed(0)}M`;
  else if (a >= 1e6) s = `${sym}${(a / 1e6).toFixed(1)}M`;
  else s = `${sym}${(a / 1e3).toFixed(0)}K`;
  return (amount < 0 ? '−' : '') + s;
}

/** A total that may have been converted between currencies: "≈£1.2T" when approximate. */
export const total = (amount: number | null | undefined, currency: string, approximate: boolean) =>
  (approximate ? '≈' : '') + money(amount, currency);

/** 0.093 → "+9.3%" */
export function pct(ratio: number | null | undefined): string {
  if (ratio == null) return '—';
  return `${ratio >= 0 ? '+' : '−'}${Math.abs(ratio * 100).toFixed(1)}%`;
}

/** Colour class for a growth ratio (display only; the API decides the company's trend status). */
export function tone(ratio: number | null | undefined): 'up' | 'flat' | 'down' {
  if (ratio == null) return 'flat';
  return ratio > 0.04 ? 'up' : ratio < -0.01 ? 'down' : 'flat';
}

export const trendClass = (t: TrendStatus) => t.toLowerCase() as 'up' | 'flat' | 'down';

/** Bubble radius: area proportional to revenue, with a minimum so small companies stay clickable. */
const rScale = d3.scaleSqrt().domain([0, 55e9]).range([0, 62]);
export const bubbleRadius = (revenue: number) => Math.max(14, rScale(Math.max(0, revenue)));
