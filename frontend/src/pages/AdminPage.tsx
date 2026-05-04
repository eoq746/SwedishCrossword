import { Link } from 'react-router-dom';
import { useEffect, useState } from 'react';
import { useAuth } from '../hooks/useAuth';
import { usePageTitle } from '../hooks/usePageTitle';
import {
  fetchAnalyticsSummary, fetchDailyAnalytics, fetchTopPlayers,
  triggerRegenerateFuturePuzzles,
  searchUserByAlias, fetchAdminGrants, grantAdmin, revokeAdmin,
  fetchPendingClueFlags, resolveClueFlag, createCustomClue, syncWordListsDevToProd,
  AccessDeniedError,
  type AnalyticsSummary, type DailyAnalytics, type TopPlayer, type AdminGrant, type ClueFlag, type BlobSyncResult,
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

// ── Admin user management ─────────────────────────────────────────────────────

function AdminUsersSection() {
  const [searchAlias, setSearchAlias] = useState('');
  const [searchResult, setSearchResult] = useState<{ userId: string; alias: string } | null>(null);
  const [searchError, setSearchError] = useState<string | null>(null);
  const [searching, setSearching] = useState(false);

  const [grants, setGrants] = useState<AdminGrant[]>([]);
  const [grantsLoading, setGrantsLoading] = useState(true);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    fetchAdminGrants()
      .then(setGrants)
      .catch(() => setGrants([]))
      .finally(() => setGrantsLoading(false));
  }, []);

  const handleSearch = async () => {
    const alias = searchAlias.trim();
    if (!alias) return;
    setSearching(true);
    setSearchResult(null);
    setSearchError(null);
    try {
      const result = await searchUserByAlias(alias);
      setSearchResult(result);
    } catch (e) {
      setSearchError(e instanceof Error && e.message.includes('404') ? 'Ingen användare hittades med det aliaseet.' : 'Sökning misslyckades.');
    } finally {
      setSearching(false);
    }
  };

  const handleGrant = async (userId: string) => {
    setActionError(null);
    try {
      await grantAdmin(userId);
      const updated = await fetchAdminGrants();
      setGrants(updated);
      setSearchResult(null);
      setSearchAlias('');
    } catch {
      setActionError('Kunde inte ge adminrättigheter. Försök igen.');
    }
  };

  const handleRevoke = async (userId: string) => {
    setActionError(null);
    try {
      await revokeAdmin(userId);
      setGrants(prev => prev.filter(g => g.userId !== userId));
    } catch {
      setActionError('Kunde inte ta bort adminrättigheter. Försök igen.');
    }
  };

  const isAlreadyGranted = (userId: string) => grants.some(g => g.userId === userId);

  return (
    <div className="admin-section">
      <h2>👤 Adminrättigheter</h2>

      {/* Search */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <input
          className="alias-input"
          type="text"
          placeholder="Sök på alias…"
          value={searchAlias}
          onChange={e => setSearchAlias(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter') void handleSearch(); }}
          style={{ flex: '1 1 180px', minWidth: 0 }}
        />
        <button className="admin-refresh-btn" onClick={() => void handleSearch()} disabled={searching}>
          {searching ? '⏳' : '🔍 Sök'}
        </button>
      </div>

      {searchError && <p className="leaderboard-error" style={{ marginBottom: 8 }}>{searchError}</p>}

      {searchResult && (
        <div className="admin-table-wrap" style={{ marginBottom: 12 }}>
          <table className="leaderboard-table">
            <thead>
              <tr><th>Alias</th><th>User-ID</th><th></th></tr>
            </thead>
            <tbody>
              <tr>
                <td>{searchResult.alias}</td>
                <td><code style={{ fontSize: '0.75em' }}>{searchResult.userId.slice(0, 12)}…</code></td>
                <td>
                  {isAlreadyGranted(searchResult.userId)
                    ? <span className="badge badge-verified">✓ Admin</span>
                    : (
                      <button className="admin-refresh-btn" onClick={() => void handleGrant(searchResult.userId)}>
                        ＋ Ge admin
                      </button>
                    )
                  }
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      )}

      {/* Granted admins list */}
      <h3 style={{ marginBottom: 8, fontSize: '0.95rem' }}>Tilldelade admins</h3>
      {grantsLoading && <p className="leaderboard-loading">Laddar…</p>}
      {!grantsLoading && grants.length === 0 && (
        <p className="profile-empty">Inga adminrättigheter tilldelade ännu.</p>
      )}
      {!grantsLoading && grants.length > 0 && (
        <div className="admin-table-wrap">
          <table className="leaderboard-table">
            <thead>
              <tr><th>Alias</th><th>Tilldelad av</th><th>Datum</th><th></th></tr>
            </thead>
            <tbody>
              {grants.map(g => (
                <tr key={g.userId}>
                  <td>{g.alias ?? <em>Okänt alias</em>}</td>
                  <td>{g.grantedByAlias ?? '–'}</td>
                  <td>{new Date(g.grantedAt * 1000).toLocaleDateString('sv-SE')}</td>
                  <td>
                    <button className="admin-refresh-btn" style={{ color: 'var(--color-error, #c00)' }} onClick={() => void handleRevoke(g.userId)}>
                      Ta bort
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {actionError && <p className="leaderboard-error" style={{ marginTop: 8 }}>{actionError}</p>}
    </div>
  );
}

// ── Puzzle regeneration ───────────────────────────────────────────────────────

function RegeneratePuzzlesSection() {
  const [status, setStatus] = useState<'idle' | 'running' | 'done' | 'error'>('idle');

  const handleRegenerate = async () => {
    if (status === 'running') return;
    setStatus('running');
    try {
      await triggerRegenerateFuturePuzzles();
      setStatus('done');
    } catch {
      setStatus('error');
    }
  };

  return (
    <div className="admin-section">
      <h2>🔄 Framtida pussel</h2>
      <p className="profile-empty" style={{ marginBottom: 12 }}>
        Tar bort och regenererar alla förberäknade pussel från och med imorgon.
        Dagens pussel berörs inte.
      </p>
      <button
        className="admin-refresh-btn"
        onClick={() => void handleRegenerate()}
        disabled={status === 'running'}
      >
        {status === 'running' ? '⏳ Regenererar…' : '🔄 Regenerera framtida pussel'}
      </button>
      {status === 'done' && (
        <p className="badge badge-verified" style={{ marginTop: 8 }}>✅ Klart! Framtida pussel har genererats om.</p>
      )}
      {status === 'error' && (
        <p className="leaderboard-error" style={{ marginTop: 8 }}>⚠️ Något gick fel. Försök igen.</p>
      )}
    </div>
  );
}

// ── Flagged clues review ───────────────────────────────────────────────────────

function ClueFlagsSection() {
  const [flags, setFlags] = useState<ClueFlag[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [editedClues, setEditedClues] = useState<Record<string, string>>({});

  const loadFlags = async () => {
    setLoading(true);
    setError(null);
    try {
      const items = await fetchPendingClueFlags(100);
      setFlags(items);
      setEditedClues(prev => {
        const next = { ...prev };
        for (const item of items) {
          if (!next[item.id]) {
            next[item.id] = item.suggestedClue ?? item.currentClue;
          }
        }
        return next;
      });
    } catch {
      setError('Kunde inte ladda flaggade ledtrådar.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadFlags();
  }, []);

  const handleApprove = async (flag: ClueFlag) => {
    const clue = editedClues[flag.id]?.trim() ?? '';
    if (!clue) {
      setError('En ny ledtråd krävs för att godkänna.');
      return;
    }

    setSavingId(flag.id);
    setError(null);
    try {
      await resolveClueFlag(flag.id, 'approved', clue);
      setFlags(prev => prev.filter(f => f.id !== flag.id));
    } catch {
      setError('Kunde inte godkänna ändringen. Försök igen.');
    } finally {
      setSavingId(null);
    }
  };

  const handleReject = async (flag: ClueFlag) => {
    setSavingId(flag.id);
    setError(null);
    try {
      await resolveClueFlag(flag.id, 'rejected');
      setFlags(prev => prev.filter(f => f.id !== flag.id));
    } catch {
      setError('Kunde inte avvisa ändringen. Försök igen.');
    } finally {
      setSavingId(null);
    }
  };

  const handleRemoveClue = async (flag: ClueFlag) => {
    setSavingId(flag.id);
    setError(null);
    try {
      await resolveClueFlag(flag.id, 'approved', undefined, undefined, undefined, true);
      setFlags(prev => prev.filter(f => f.id !== flag.id));
    } catch {
      setError('Kunde inte ta bort ledtråden. Försök igen.');
    } finally {
      setSavingId(null);
    }
  };

  return (
    <div className="admin-section">
      <h2>📝 Flaggar för ledtrådar</h2>
      <p className="profile-empty" style={{ marginBottom: 12 }}>
        Granska rapporterade ledtrådar. Vid godkännande uppdateras ordlistan, analyscachen räknas om,
        och framtida pussel regenereras.
      </p>
      <button className="admin-refresh-btn" onClick={() => void loadFlags()} disabled={loading} style={{ marginBottom: 12 }}>
        {loading ? '⏳ Laddar…' : '🔄 Uppdatera lista'}
      </button>

      {error && <p className="leaderboard-error" style={{ marginBottom: 8 }}>{error}</p>}
      {!loading && flags.length === 0 && <p className="profile-empty">Inga väntande flaggar.</p>}

      {flags.length > 0 && (
        <div className="admin-table-wrap">
          <table className="leaderboard-table">
            <thead>
              <tr>
                <th>Ord</th>
                <th>Nuvarande</th>
                <th>Förslag</th>
                <th>Anledning</th>
                <th>Ny ledtråd</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {flags.map(flag => {
                const busy = savingId === flag.id;
                return (
                  <tr key={flag.id}>
                    <td>{flag.word}</td>
                    <td>{flag.currentClue}</td>
                    <td>{flag.suggestedClue ?? '–'}</td>
                    <td>{flag.reason ?? '–'}</td>
                    <td>
                      <input
                        className="alias-input"
                        value={editedClues[flag.id] ?? ''}
                        onChange={e => setEditedClues(prev => ({ ...prev, [flag.id]: e.target.value }))}
                        disabled={busy}
                      />
                    </td>
                    <td style={{ display: 'flex', gap: 8 }}>
                      <button className="admin-refresh-btn" onClick={() => void handleApprove(flag)} disabled={busy}>
                        ✅ Godkänn
                      </button>
                      <button className="admin-refresh-btn" onClick={() => void handleReject(flag)} disabled={busy}>
                        ❌ Avvisa
                      </button>
                      <button className="admin-refresh-btn" onClick={() => void handleRemoveClue(flag)} disabled={busy}>
                        🗑️ Ta bort
                      </button>
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

// ── Custom clue creation ───────────────────────────────────────────────────────

function CustomClueCreateSection() {
  const [word, setWord] = useState('');
  const [clue, setClue] = useState('');
  const [status, setStatus] = useState<'idle' | 'saving' | 'done' | 'error'>('idle');

  const handleCreate = async () => {
    const w = word.trim().toUpperCase();
    const c = clue.trim();
    if (!w || !c) {
      setStatus('error');
      return;
    }

    setStatus('saving');
    try {
      await createCustomClue(w, c, 'Custom', 'Medium');
      setWord('');
      setClue('');
      setStatus('done');
    } catch {
      setStatus('error');
    }
  };

  return (
    <div className="admin-section">
      <h2>➕ Ny egen ledtråd</h2>
      <p className="profile-empty" style={{ marginBottom: 12 }}>
        Skapar en ny post i <code>custom-words.json</code>. Denna funktion skriver aldrig till Lexin, Synonymer, Kelly eller DSSO.
      </p>
      <div style={{ display: 'grid', gap: 8 }}>
        <input
          className="alias-input"
          value={word}
          onChange={e => setWord(e.target.value)}
          placeholder="Ord (t.ex. KATT)"
          maxLength={64}
        />
        <input
          className="alias-input"
          value={clue}
          onChange={e => setClue(e.target.value)}
          placeholder="Ledtråd"
          maxLength={500}
        />
      </div>
      <button className="admin-refresh-btn" style={{ marginTop: 10 }} onClick={() => void handleCreate()} disabled={status === 'saving'}>
        {status === 'saving' ? '⏳ Sparar…' : '💾 Lägg till i custom-words'}
      </button>
      {status === 'done' && <p className="badge badge-verified" style={{ marginTop: 8 }}>✅ Sparat i custom-words.json</p>}
      {status === 'error' && <p className="leaderboard-error" style={{ marginTop: 8 }}>⚠️ Kunde inte spara. Kontrollera ord och ledtråd.</p>}
    </div>
  );
}

// ── Dev → Prod ordlistesynk (Blob) ─────────────────────────────────────────────

function BlobSyncSection() {
  const [running, setRunning] = useState<'none' | 'dryRun' | 'apply'>('none');
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BlobSyncResult | null>(null);

  const runSync = async (dryRun: boolean) => {
    setRunning(dryRun ? 'dryRun' : 'apply');
    setError(null);
    try {
      const syncResult = await syncWordListsDevToProd(dryRun);
      setResult(syncResult);
    } catch {
      setError('Kunde inte köra blob-synk. Kontrollera konfiguration och försök igen.');
    } finally {
      setRunning('none');
    }
  };

  return (
    <div className="admin-section">
      <h2>☁️ Dev → Prod ordlistesynk (Blob)</h2>
      <p className="profile-empty" style={{ marginBottom: 12 }}>
        Kör konfliktmedveten 3-vägsmerge mellan blob-containrarna för dev och prod.
      </p>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <button className="admin-refresh-btn" onClick={() => void runSync(true)} disabled={running !== 'none'}>
          {running === 'dryRun' ? '⏳ Kör torrkörning…' : '🧪 Torrkörning'}
        </button>
        <button className="admin-refresh-btn" onClick={() => void runSync(false)} disabled={running !== 'none'}>
          {running === 'apply' ? '⏳ Synkar…' : '🚀 Synka till prod'}
        </button>
      </div>

      {error && <p className="leaderboard-error" style={{ marginTop: 8 }}>{error}</p>}

      {result && (
        <div style={{ marginTop: 12 }}>
          <p className="profile-empty">
            {result.dryRun ? 'Torrkörning' : 'Synk'}: {result.filesChanged}/{result.filesProcessed} filer ändras ·
            +{result.totalAdded} / ~{result.totalUpdated} / -{result.totalRemoved} · konflikter: {result.totalConflicts}
          </p>

          {result.files.some(f => (f.conflicts ?? 0) > 0 || !!f.error) && (
            <div className="admin-table-wrap" style={{ marginTop: 8 }}>
              <table className="leaderboard-table">
                <thead>
                  <tr>
                    <th>Fil</th>
                    <th>+ / ~ / -</th>
                    <th>Konflikter</th>
                    <th>Detaljer</th>
                  </tr>
                </thead>
                <tbody>
                  {result.files
                    .filter(f => (f.conflicts ?? 0) > 0 || !!f.error)
                    .map(f => (
                      <tr key={f.fileName}>
                        <td>{f.fileName}</td>
                        <td>{f.added} / {f.updated} / {f.removed}</td>
                        <td>{f.conflicts}</td>
                        <td>
                          {f.error && <div className="leaderboard-error">{f.error}</div>}
                          {!!f.conflictDetails?.length && (
                            <ul style={{ margin: 0, paddingLeft: 18 }}>
                              {f.conflictDetails.slice(0, 10).map((c, i) => (
                                <li key={`${f.fileName}-${c.word}-${i}`}>
                                  <code>{c.word}</code>: {c.reason} ({c.resolution})
                                </li>
                              ))}
                            </ul>
                          )}
                        </td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </div>
          )}
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
      <AdminUsersSection />
      <BlobSyncSection />
      <CustomClueCreateSection />
      <ClueFlagsSection />
      <RegeneratePuzzlesSection />

      <Link to="/profile" className="back-link">← Tillbaka till profil</Link>
    </div>
  );
}
