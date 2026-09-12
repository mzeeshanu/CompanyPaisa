import type { TrendStatus } from '../api/types';
import * as d3 from 'd3';

/** $21.5B · $540M · $4.6M · $820K — input is whole dollars. */
export function money(dollars: number | null | undefined): string {
  if (dollars == null) return '—';
  const a = Math.abs(dollars);
  let s: string;
  if (a >= 1e9) s = `$${(a / 1e9).toFixed(a >= 1e10 ? 1 : 2)}B`;
  else if (a >= 1e8) s = `$${(a / 1e6).toFixed(0)}M`;
  else if (a >= 1e6) s = `$${(a / 1e6).toFixed(1)}M`;
  else s = `$${(a / 1e3).toFixed(0)}K`;
  return (dollars < 0 ? '−' : '') + s;
}

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
