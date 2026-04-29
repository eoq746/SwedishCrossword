import { useEffect } from 'react';

const BASE_TITLE = 'Svenskt Korsord';

/**
 * Sets document.title for the current route.
 * Pass just the page name; the site name is appended automatically.
 * Pass an empty string to use only the base title (e.g. on the landing page).
 */
export function usePageTitle(pageTitle?: string) {
  useEffect(() => {
    document.title = pageTitle ? `${pageTitle} – ${BASE_TITLE}` : BASE_TITLE;
    return () => {
      document.title = BASE_TITLE;
    };
  }, [pageTitle]);
}
