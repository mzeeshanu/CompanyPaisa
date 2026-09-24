import { useEffect, useRef, useState } from 'react';

const DARK_QUERY = '(prefers-color-scheme: dark)';
/** Height of the widget in pixels; the card keeps this space so nothing jumps when it loads. */
const WIDGET_HEIGHT = 300;
/** Full names for the exchanges we store short; the rest ("NYSE American", "Euronext Paris"…) read fine as they are. */
const EXCHANGE_NAMES: Record<string, string> = { Nasdaq: 'Nasdaq Stock Market', NYSE: 'New York Stock Exchange' };

/** True when the page is drawn dark: the visitor's theme choice (data-mode on <html>), else the system setting. */
function useDarkMode(): boolean {
  const read = () => {
    const mode = document.documentElement.getAttribute('data-mode');
    return mode ? mode === 'dark' : window.matchMedia(DARK_QUERY).matches;
  };
  const [dark, setDark] = useState(read);
  useEffect(() => {
    const update = () => setDark(read());
    const media = window.matchMedia(DARK_QUERY);
    media.addEventListener('change', update);
    const attr = new MutationObserver(update);
    attr.observe(document.documentElement, { attributes: true, attributeFilter: ['data-mode'] });
    return () => { media.removeEventListener('change', update); attr.disconnect(); };
  }, []);
  return dark;
}

/** One TradingView widget: their script reads its settings from its own body and draws an iframe beside it. */
function widget(kind: string, settings: object): HTMLElement {
  const box = document.createElement('div');
  box.className = 'tradingview-widget-container';
  const inner = document.createElement('div');
  inner.className = 'tradingview-widget-container__widget';
  const script = document.createElement('script');
  script.src = `https://s3.tradingview.com/external-embedding/embed-widget-${kind}.js`;
  script.async = true;
  script.textContent = JSON.stringify(settings);
  box.append(inner, script);
  return box;
}

/**
 * Share price and a long-term chart from TradingView's free widget (delayed prices, loaded in the visitor's browser; we
 * store none). Only for companies whose exchange allows its prices in the widgets — the server leaves `symbol` out otherwise.
 * The widgets load when the card is about to scroll into view, so they don't slow the page.
 */
export function SharePrice({ symbol, name, exchange }: { symbol: string; name: string; exchange: string }) {
  const card = useRef<HTMLElement>(null);
  const box = useRef<HTMLDivElement>(null);
  const [near, setNear] = useState(false);
  const dark = useDarkMode();

  useEffect(() => {
    const el = card.current;
    if (!el) return;
    const seen = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting)) { setNear(true); seen.disconnect(); }
    }, { rootMargin: '300px' });
    seen.observe(el);
    return () => seen.disconnect();
  }, []);

  useEffect(() => {
    const el = box.current;
    if (!near || !el) return;
    const colorTheme = dark ? 'dark' : 'light';
    // One compact widget: price and change on the day above the chart. (It shows its own company name; a label we pass is ignored.)
    el.replaceChildren(widget('symbol-overview', {
      symbols: [[name, `${symbol}|1D`]], chartOnly: false, width: '100%', height: WIDGET_HEIGHT, locale: 'en', colorTheme, isTransparent: true,
      autosize: false, chartType: 'area', showVolume: false, hideDateRanges: false, scalePosition: 'right', scaleMode: 'Normal',
      changeMode: 'price-and-percent', dateRanges: ['12m|1D', '60m|1W', 'all|1M'],
    }));
    return () => el.replaceChildren();
  }, [near, dark, symbol, name]);

  return (
    <section ref={card} className="pane page-card price-card" aria-labelledby="price-h">
      <div className="seg">
        <h2 className="subh" id="price-h">Share price</h2>
        <span className="price-exchange"><b className="num">{symbol.slice(symbol.indexOf(':') + 1)}</b> · {EXCHANGE_NAMES[exchange] ?? exchange}</span>
      </div>
      <div ref={box} className="price-widgets" style={{ minHeight: WIDGET_HEIGHT }} />
      <p className="fine">
        Delayed prices and chart by{' '}
        <a className="linkbtn" href={`https://www.tradingview.com/symbols/${symbol.replace(':', '-')}/`} target="_blank" rel="noopener nofollow">TradingView</a>.
        Past performance isn't a guide to future returns.
      </p>
    </section>
  );
}
