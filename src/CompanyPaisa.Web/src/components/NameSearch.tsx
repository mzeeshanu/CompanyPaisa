import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react';
import { api } from '../api/client';
import type { NameSearchResponse } from '../api/types';
import { track } from '../lib/analytics';
import { money } from '../lib/format';
import { companyPath, Link, navigate, personPath, salariesPath, searchPath } from '../lib/router';

interface Props {
  showExecutives: boolean;
  /** "inline": results push the content below down (inside the location card) instead of floating over the page. */
  variant?: 'bar' | 'inline';
}

interface Option { key: string; path: string }

/** Words that mean "show me what they pay", not part of a company's name ("apple salaries", "nvidia pay"). */
const PAY_WORDS = /\b(salary|salaries|pay|wage|wages|compensation)\b/i;
const ALL_PAY_WORDS = new RegExp(PAY_WORDS.source, 'gi');

/**
 * Find any company (name or ticker) or executive (name) and open their page, wherever they are — or a place ("Utah",
 * "USA", "Dallas"), which opens the search there (every company in a country or state; around a city).
 */
export function NameSearch({ showExecutives, variant = 'bar' }: Props) {
  const [text, setText] = useState('');
  const [result, setResult] = useState<NameSearchResponse | null>(null);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const box = useRef<HTMLDivElement>(null);
  const listId = useId();

  // "apple salaries", "nvidia pay": the words say what they want, the rest is the company to look for.
  const wantsPay = PAY_WORDS.test(text);
  const query = wantsPay ? text.replace(ALL_PAY_WORDS, ' ').trim() : text.trim();

  // Ask as the visitor types (after a short pause), cancelling the previous question.
  useEffect(() => {
    const q = query;
    if (q.length < 2) { setResult(null); return; }
    const ctrl = new AbortController();
    const t = setTimeout(() => {
      api.searchByName(q, ctrl.signal).then(r => { setResult(r); setActive(0); }).catch(() => {});
    }, 180);
    return () => { clearTimeout(t); ctrl.abort(); };
  }, [query]);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: PointerEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false); };
    addEventListener('pointerdown', onDown);
    return () => removeEventListener('pointerdown', onDown);
  }, [open]);

  const places = wantsPay ? [] : result?.places ?? [];
  const companies = result?.companies ?? [];
  const executives = showExecutives ? result?.executives ?? [] : [];
  const options: Option[] = [
    ...places.map(p => ({ key: `l:${p.place}`, path: searchPath(p.place, false) })),
    ...companies.map(c => ({ key: `c:${c.ticker}`, path: wantsPay ? salariesPath(c.ticker) : companyPath(c.ticker) })),
    ...executives.map(e => ({ key: `p:${e.personId}`, path: personPath(e.personId) })),
  ];
  const shown = open && query.length >= 2 && result !== null;

  const picked = () => { track('name_search', text.trim()); setOpen(false); setText(''); setResult(null); };

  const onKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') { setOpen(false); return; }
    if (!options.length) return;
    if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); setActive(a => (a + 1) % options.length); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => (a - 1 + options.length) % options.length); }
    else if (e.key === 'Enter' && shown) { e.preventDefault(); const to = options[Math.min(active, options.length - 1)].path; picked(); navigate(to); }
  };

  const optionProps = (i: number) => ({
    id: `${listId}-${i}`, role: 'option', 'aria-selected': i === active,
    className: `ns-opt${i === active ? ' on' : ''}`,
    onMouseEnter: () => setActive(i),
    onClick: picked,
  });

  return (
    <div className={`namesearch ${variant}`} ref={box}>
      <svg className="ns-ico" viewBox="0 0 16 16" aria-hidden="true"><circle cx="7" cy="7" r="4.6" fill="none" stroke="currentColor" strokeWidth="1.6" /><path d="m10.5 10.5 3.5 3.5" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" /></svg>
      <input type="search" value={text} placeholder={showExecutives ? 'Search a company, executive or place' : 'Search a company or place'}
        aria-label={showExecutives ? 'Search companies and executives by name, or a place' : 'Search companies by name or ticker, or a place'}
        role="combobox" aria-expanded={shown} aria-controls={listId} aria-autocomplete="list"
        aria-activedescendant={shown && options.length ? `${listId}-${active}` : undefined}
        autoComplete="off" spellCheck={false}
        onChange={e => { setText(e.target.value); setOpen(true); }} onFocus={() => setOpen(true)} onKeyDown={onKey} />
      {shown && (
        <div className="ns-list pane" id={listId} role="listbox" aria-label="Matches">
          {options.length === 0 && <p className="ns-empty">No company{showExecutives ? ', executive' : ''} or place matches “{text.trim()}”.</p>}
          {places.length > 0 && <p className="ns-h">Places</p>}
          {places.map((p, i) => (
            <Link to={searchPath(p.place, false)} key={p.place} {...optionProps(i)}>
              <span className="ns-main"><b>{p.label}</b><small>{!p.region ? 'Public companies nearby'
                : p.region.kind === 'Country' ? 'Every public company in the country' : 'Every public company in the state'}</small></span>
            </Link>
          ))}
          {companies.length > 0 && <p className="ns-h">Companies</p>}
          {companies.map((c, i) => (
            <Link to={wantsPay ? salariesPath(c.ticker) : companyPath(c.ticker)} key={c.ticker} {...optionProps(places.length + i)}>
              <span className="ns-main"><b>{c.name}</b><small>{wantsPay ? 'salaries · ' : ''}{c.ticker}{c.city ? ` · ${c.city}, ${c.state}` : ''}</small></span>
              {c.ttmRevenue > 0 && <span className="ns-side num">{money(c.ttmRevenue, c.currency)}</span>}
            </Link>
          ))}
          {executives.length > 0 && <p className="ns-h">Executives</p>}
          {executives.map((e, j) => (
            <Link to={personPath(e.personId)} key={e.personId} {...optionProps(places.length + companies.length + j)}>
              <span className="ns-main"><b>{e.name}</b><small>{e.title} · {e.company.name}</small></span>
              <span className="ns-side num">{money(e.latestTotalPay, e.company.currency)}</span>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
