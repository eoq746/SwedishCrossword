import { useRef, useState, useEffect, type ReactNode } from 'react';
import { NavLink, Link } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { useTheme } from '../hooks/useTheme';
import { useDbStatus } from '../hooks/useDbStatus';
import { useNotifications } from '../hooks/useNotifications';
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
  const [notificationsOpen, setNotificationsOpen] = useState(false);
  const loginRef = useRef<HTMLDivElement>(null);
  const notificationsRef = useRef<HTMLDivElement>(null);
  const {
    unreadNotifications,
    unreadCount,
    loading: notificationsLoading,
    markAsRead,
    markAllAsRead,
  } = useNotifications(user?.userId ?? null);
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

  // Close notifications dropdown on outside click
  useEffect(() => {
    if (!notificationsOpen) return;
    function handleClick(e: MouseEvent) {
      if (notificationsRef.current && !notificationsRef.current.contains(e.target as Node)) {
        setNotificationsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [notificationsOpen]);

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
            <NavLink to="/guides" onClick={closeMobile}>Guider</NavLink>
            <NavLink to="/lexicon" onClick={closeMobile}>Lexikon</NavLink>
            {user && <NavLink to="/profile" onClick={closeMobile}>Profil</NavLink>}
            {user?.isAdmin && <NavLink to="/admin" onClick={closeMobile}>Admin</NavLink>}
          </div>

          {/* Right actions */}
          <div className="nav-actions">
            {user ? (
              <>
                <div className="notifications-wrap" ref={notificationsRef}>
                  <button
                    type="button"
                    className="theme-toggle notifications-toggle"
                    onClick={() => setNotificationsOpen(open => !open)}
                    aria-label="Öppna notiser"
                    aria-haspopup="true"
                    aria-expanded={notificationsOpen}
                    title="Notiser"
                  >
                    🔔
                    {unreadCount > 0 && <span className="notifications-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>}
                  </button>
                  {notificationsOpen && (
                    <div className="notifications-menu auth-menu-open" role="menu" aria-label="Notiser">
                      <div className="notifications-menu-header">
                        <strong>Notiser</strong>
                        {unreadCount > 0 && (
                          <button type="button" className="notifications-mark-all" onClick={markAllAsRead}>
                            Markera alla som lästa
                          </button>
                        )}
                      </div>
                      {notificationsLoading ? (
                        <div className="notifications-empty">Laddar…</div>
                      ) : unreadNotifications.length === 0 ? (
                        <div className="notifications-empty">Inga olästa notiser.</div>
                      ) : (
                        <ul className="notifications-list">
                          {unreadNotifications.map(notification => (
                            <li key={notification.id} className="notifications-item">
                              <Link
                                to={notification.href}
                                onClick={() => {
                                  markAsRead(notification.id);
                                  setNotificationsOpen(false);
                                }}
                              >
                                <span className="notifications-item-title">{notification.title}</span>
                                <span className="notifications-item-text">{notification.description}</span>
                              </Link>
                            </li>
                          ))}
                        </ul>
                      )}
                    </div>
                  )}
                </div>
                <button type="button" className="auth-btn" onClick={() => void handleLogout()}>Logga ut</button>
              </>
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
                      <span aria-hidden="true">🔵</span> Logga in med Google
                    </a>
                    <a
                      href={`/api/auth/login/microsoft?returnUrl=${returnUrl}`}
                      onClick={() => setLoginOpen(false)}
                    >
                      <span aria-hidden="true">🟦</span> Logga in med Microsoft
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

        {/* Ad slot — insert AdSense <ins> tag here when configuring ads */}
        <div className="ad-slot ad-slot--below-content" aria-label="Annons" role="complementary" />

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
