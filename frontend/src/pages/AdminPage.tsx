import { Link } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { useAuth } from '../hooks/useAuth';
import { usePageTitle } from '../hooks/usePageTitle';
import {
  fetchAnalyticsSummary, fetchDailyAnalytics, fetchTopPlayers,
  AccessDeniedError,
  type AnalyticsSummary, type DailyAnalytics, type TopPlayer,
} from '../api/admin';
import '../styles/static-pages.css';

const SIZE_LABELS: Record<string, string> = {
  '10x10': 'Liten (10×10)',
  '15x15': 'Mellan (15×15)',
  '17x17': 'Stor (17×17)',
};

function formatTime(seconds: number | null | undefined): string {
  if (seconds == null || seconds === 0) return '–';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s < 10 ? '0' : ''}${s}`;
}

// ── Sub-components ────────────────────────────────────────────────────────────

function SummaryCards({ summary }: { summary: AnalyticsSummary }) {
  const cards = [
    { label: 'Lösningar idag', value: summary.completionsToday },
    { label: 'Aktiva idag', value: summary.activeToday },
    { label: 'Totalt lösningar', value: summary.totalCompletions },
    { label: 'Unika spelare', value: summary.uniquePlayers },
    { label: 'Registrerade', value: summary.registeredUsers },
    { label: 'Snittid', value: formatTime(summary.averageTime) },
    { label: 'Bästa tid', value: formatTime(summary.bestTime) },
    { label: 'Ledtrådsandel', value: summary.hintUsageRate != null ? `${Math.round(summary.hintUsageRate * 100)}%` : '–' },
  ];

  return (
    <div className="admin-stats-grid">
      {cards.map(c => (
        <div key={c.label} className="admin-stat-card">
          <span className="value">{c.value}</span>
          <span className="label">{c.label}</span>
        </div>
      ))}
    </div>
  );
}

function PerSizeSection({ perSize }: { perSize: Record<string, { completions: number; averageTime: number }> }) {
  const entries = Object.entries(perSize).sort(([a], [b]) => a.localeCompare(b));
  return (
    <div className="admin-section">
      <h2>📐 Per pusselstorlek</h2>
      <div className="admin-stats-grid">
        {entries.map(([key, s]) => (
          <div key={key} className="admin-stat-card">
            <span className="value">{s.completions}</span>
            <span className="label">{SIZE_LABELS[key] ?? key} · snitt {formatTime(s.averageTime)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function DailySection({ daily }: { daily: DailyAnalytics[] }) {
  const maxCount = Math.max(...daily.map(d => d.completions), 1);
  return (
    <div className="admin-section">
      <h2>📊 Daglig aktivitet (senaste 30 dagar)</h2>
      {daily.length === 0 ? (
        <p className="profile-empty">Ingen data.</p>
      ) : (
        <div className="admin-table-wrap">
          <table className="leaderboard-table">
            <thead>
              <tr>
                <th>Datum</th>
                <th>Lösningar</th>
                <th>Spelare</th>
                <th>Snittid</th>
                <th className="admin-bar-col"></th>
              </tr>
            </thead>
            <tbody>
              {daily.map(d => {
                const pct = Math.round((d.completions / maxCount) * 100);
                return (
                  <tr key={d.date}>
                    <td>{d.date}</td>
                    <td>{d.completions}</td>
                    <td>{d.uniquePlayers}</td>
                    <td>{formatTime(d.averageTime)}</td>
                    <td>
                      <div className="admin-bar-row">
                        <div className="admin-bar" style={{ width: `${pct}%` }} />
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function PlayersSection({ players }: { players: TopPlayer[] }) {
  return (
    <div className="admin-section">
      <h2>🏆 Topspelare</h2>
      {players.length === 0 ? (
        <p className="profile-empty">Inga spelare.</p>
      ) : (
        <div className="admin-table-wrap">
          <table className="leaderboard-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Spelare</th>
                <th>Lösningar</th>
                <th>Snittid</th>
                <th>Bästa tid</th>
              </tr>
            </thead>
            <tbody>
              {players.map((p, i) => (
                <tr key={i}>
                  <td className="rank-cell">{i + 1}</td>
                  <td>
                    {p.displayName || p.rawName || '–'}
                    {p.verified
                      ? <span className="badge badge-verified" title="Verifierat konto">✓</span>
                      : <span className="badge badge-guest" title="Gäst">👤</span>
                    }
                  </td>
                  <td>{p.gamesPlayed}</td>
                  <td>{formatTime(p.averageTime)}</td>
                  <td>{formatTime(p.bestTime)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

interface DashboardData {
  summary: AnalyticsSummary;
  daily: DailyAnalytics[];
  players: TopPlayer[];
}

export default function AdminPage() {
  usePageTitle('Admin – Svenskt Korsord');
  const { user, loading: authLoading } = useAuth();
  const [data, setData] = useState<DashboardData | null>(null);
  const [error, setError] = useState<'denied' | 'error' | null>(null);
  const [loading, setLoading] = useState(true);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const [summary, daily, players] = await Promise.all([
        fetchAnalyticsSummary(),
        fetchDailyAnalytics(30),
        fetchTopPlayers(20),
      ]);
      setData({ summary, daily, players });
    } catch (e) {
      setError(e instanceof AccessDeniedError ? 'denied' : 'error');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    if (authLoading) return;
    if (!user || !user.isAdmin) {
      setLoading(false);
      return;
    }

    void load();
  }, [authLoading, user]);

  if (authLoading || loading) {
    return (
      <div className="page-content">
        <p className="leaderboard-loading">Laddar adminpanel…</p>
      </div>
    );
  }

  if (error === 'denied' || (user && !user.isAdmin) || !user) {
    return (
      <div className="page-content">
        <div className="leaderboard-error">⛔ Åtkomst nekad. Du måste vara inloggad som admin.</div>
        <Link to="/profile" className="back-link" style={{ marginTop: 16 }}>Gå till profil</Link>
      </div>
    );
  }

  if (error === 'error' || !data) {
    return (
      <div className="page-content">
        <div className="leaderboard-error">Kunde inte ladda data. Kontrollera att API:et är igång.</div>
        <button className="admin-refresh-btn" onClick={() => void load()} style={{ marginTop: 12 }}>🔄 Försök igen</button>
      </div>
    );
  }

  return (
    <div className="page-content">
      <div className="admin-page-header">
        <h1>🔧 Adminpanel</h1>
        <button className="admin-refresh-btn" onClick={() => void load()}>🔄 Uppdatera</button>
      </div>

      <SummaryCards summary={data.summary} />

      {data.summary.perSize && Object.keys(data.summary.perSize).length > 0 && (
        <PerSizeSection perSize={data.summary.perSize} />
      )}

      <DailySection daily={data.daily} />
      <PlayersSection players={data.players} />

      <Link to="/profile" className="back-link">← Tillbaka till profil</Link>
    </div>
  );
}
