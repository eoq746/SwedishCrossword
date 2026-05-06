import { useRef, useState, useEffect, type ReactNode } from 'react';
import { NavLink, Link } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { useTheme } from '../hooks/useTheme';
import { useDbStatus } from '../hooks/useDbStatus';
import DbUnavailableBanner from './DbUnavailableBanner';
import CookieBanner from './CookieBanner';
import '../styles/static-pages.css';

interface LayoutProps {
  children: ReactNode;
}

export default function Layout({ children }: LayoutProps) {
  const { user } = useAuth();
  const [theme, toggleTheme] = useTheme();
  const dbUnavailable = useDbStatus();
  const [loginOpen, setLoginOpen] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const loginRef = useRef<HTMLDivElement>(null);
  const rawReturnUrl = typeof window !== 'undefined'
    ? `${window.location.pathname}${window.location.search}${window.location.hash}`
    : '/app/';
  const returnUrl = encodeURIComponent(rawReturnUrl.startsWith('/app') ? rawReturnUrl : '/app/');

  async function handleLogout() {
    try {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });
    } catch {
      // ignore and still return to the app shell
    }
    window.location.href = '/app/';
  }

  // Close login dropdown on outside click
  useEffect(() => {
    if (!loginOpen) return;
    function handleClick(e: MouseEvent) {
      if (loginRef.current && !loginRef.current.contains(e.target as Node)) {
        setLoginOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [loginOpen]);

  // Close mobile nav on route change / resize
  useEffect(() => {
    function handleResize() {
      if (window.innerWidth > 600) setMobileOpen(false);
    }
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  function closeMobile() { setMobileOpen(false); }

  return (
    <>
      {dbUnavailable && <DbUnavailableBanner />}
      <CookieBanner />
      <div className="container">
        <nav className="page-nav" aria-label="Huvudnavigation">
          {/* Mobile hamburger */}
          <button
            className={`nav-hamburger${mobileOpen ? ' open' : ''}`}
            aria-label={mobileOpen ? 'Stäng meny' : 'Öppna meny'}
            aria-expanded={mobileOpen}
            aria-controls="nav-links"
            onClick={() => setMobileOpen(o => !o)}
          >
            <span />
            <span />
            <span />
          </button>

          {/* Nav links */}
          <div
            id="nav-links"
            className={`nav-links${mobileOpen ? ' open' : ''}`}
          >
            <NavLink to="/" end onClick={closeMobile}>Hem</NavLink>
            <NavLink to="/play" onClick={closeMobile}>Spela</NavLink>
            <NavLink to="/leaderboard" onClick={closeMobile}>Topplista</NavLink>
            <NavLink to="/calendar" onClick={closeMobile}>Kalender</NavLink>
            {user && <NavLink to="/profile" onClick={closeMobile}>Profil</NavLink>}
            {user?.isAdmin && <NavLink to="/admin" onClick={closeMobile}>Admin</NavLink>}
          </div>

          {/* Right actions */}
          <div className="nav-actions">
            {user ? (
              <button type="button" className="auth-btn" onClick={() => void handleLogout()}>Logga ut</button>
            ) : (
              <div className="login-dropdown-wrap" ref={loginRef}>
                <button
                  className="auth-btn"
                  onClick={() => setLoginOpen(o => !o)}
                  aria-haspopup="true"
                  aria-expanded={loginOpen}
                >
                  Logga in
                </button>
                {loginOpen && (
                  <div className="auth-login-menu auth-menu-open">
                    <a
                      href={`/api/auth/login/google?returnUrl=${returnUrl}`}
                      onClick={() => setLoginOpen(false)}
                    >
                      🔵 Logga in med Google
                    </a>
                    <a
                      href={`/api/auth/login/microsoft?returnUrl=${returnUrl}`}
                      onClick={() => setLoginOpen(false)}
                    >
                      🟦 Logga in med Microsoft
                    </a>
                  </div>
                )}
              </div>
            )}
            <button
              className="theme-toggle"
              onClick={toggleTheme}
              aria-label={theme === 'dark' ? 'Byt till ljust tema' : 'Byt till mörkt tema'}
              title={theme === 'dark' ? 'Ljust tema' : 'Mörkt tema'}
            >
              {theme === 'dark' ? '☀️' : '🌙'}
            </button>
          </div>
        </nav>

        <main>{children}</main>

        <footer className="site-footer" role="contentinfo">
          <nav aria-label="Sidfot">
            <Link to="/about">Om oss</Link>
            <span className="footer-sep" aria-hidden="true">·</span>
            <Link to="/contact">Kontakt</Link>
            <span className="footer-sep" aria-hidden="true">·</span>
            <Link to="/privacy-policy">Integritetspolicy</Link>
          </nav>
          <p>© 2025–2026 Svenskt Korsord. Alla rättigheter förbehållna.</p>
        </footer>
      </div>
    </>
  );
}
