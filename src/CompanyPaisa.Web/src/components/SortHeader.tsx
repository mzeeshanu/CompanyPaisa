import { useEffect, useState } from 'react';

/** A tappable column header: first tap sorts by the column, tapping it again flips the order. */
export function SortHeader<K extends string>({ k, sort, flipped, onSort, className = '', children }: {
  k: K; sort: K; flipped: boolean; onSort: (k: K) => void; className?: string; children: React.ReactNode;
}) {
  const active = sort === k;
  return (
    <button type="button" className={`sorthead ${className}${active ? ' sorted' : ''}`} aria-pressed={active}
      aria-label={`Sort by ${typeof children === 'string' ? children : k}${active ? (flipped ? ', reversed' : '') : ''}`}
      onClick={() => onSort(k)}>
      {children}<i aria-hidden="true">{active ? (flipped ? '▲' : '▼') : ''}</i>
    </button>
  );
}

/**
 * The API returns each sort in its natural order (biggest first; nearest / A→Z for distance and name).
 * Tapping the active column again reverses that order on the client; rows with no value ("—") stay at the bottom.
 */
export function useSortFlip<K extends string, T>(sort: K, onSort: (k: K) => void, valueOf: (item: T, k: K) => unknown) {
  const [flipped, setFlipped] = useState(false);
  useEffect(() => setFlipped(false), [sort]);
  const choose = (k: K) => (k === sort ? setFlipped(f => !f) : onSort(k));
  const order = (items: T[]) => {
    if (!flipped) return items;
    const blank = (i: T) => valueOf(i, sort) == null;
    return [...items.filter(i => !blank(i)).reverse(), ...items.filter(blank)];
  };
  return { flipped, choose, order };
}
