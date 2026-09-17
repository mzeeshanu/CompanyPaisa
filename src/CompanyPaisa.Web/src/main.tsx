import { lazy, StrictMode, Suspense } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import './styles.css';

// The private analytics dashboard loads separately, so visitors never download it.
const AdminApp = lazy(() => import('./admin/AdminApp'));
const isAdmin = /^\/admin\/?$/.test(location.pathname);
// Going back to the search puts the visitor where they were in the list (App restores it once the list has drawn).
if ('scrollRestoration' in history) history.scrollRestoration = 'manual';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {isAdmin ? <Suspense fallback={null}><AdminApp /></Suspense> : <App />}
  </StrictMode>,
);
