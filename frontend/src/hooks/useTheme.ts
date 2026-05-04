import { useEffect, useState } from 'react';

type Theme = 'light' | 'dark';

function resolveInitialTheme(): Theme {
  const stored = localStorage.getItem('theme');
  if (stored === 'light' || stored === 'dark') return stored;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

/** Returns the active theme and a toggle function. Persists choice to localStorage
 *  and sets data-theme on <html> so the existing CSS custom properties apply. */
export function useTheme(): [Theme, () => void] {
  const [theme, setTheme] = useState<Theme>(resolveInitialTheme);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
  }, [theme]);

  const toggle = () => setTheme(t => (t === 'light' ? 'dark' : 'light'));
  return [theme, toggle];
}
