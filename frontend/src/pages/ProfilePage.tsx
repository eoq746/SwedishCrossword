import { Link } from 'react-router-dom';
import { useEffect, useRef, useState } from 'react';
import { useAuth, type AuthUser } from '../hooks/useAuth';
import { usePageTitle } from '../hooks/usePageTitle';
import {
  fetchMyStats, saveAlias, fetchFriends, fetchFriendRequests,
  sendFriendRequest, acceptFriendRequest, declineFriendRequest,
  removeFriend, fetchChallenges, fetchExpiredChallenges, sendChallenge, respondChallenge,
  exportMyData, deleteMyAccount,
  formatTime, todayIso, SIZE_LABELS,
  type UserStatsResponse, type FriendInfo,
  type FriendRequestInfo, type FriendChallengeInfo,
} from '../api/profile';
import '../styles/static-pages.css';

// ── Login prompt ──────────────────────────────────────────────────────────────

function LoginPrompt() {
  const returnUrl = encodeURIComponent('/app/profile');
  return (
    <div className="profile-login-prompt">
      <div style={{ fontSize: '3.5rem', lineHeight: 1, marginBottom: '16px' }} aria-hidden="true">
        👤
      </div>
      <h1>Min Profil</h1>
      <p>Logga in för att se din statistik, synkad mellan alla dina enheter.</p>
      <div className="profile-login-buttons">
        <a href={`/api/auth/login/google?returnUrl=${returnUrl}`} className="profile-login-btn">
          🔵 Logga in med Google
        </a>
        <a href={`/api/auth/login/microsoft?returnUrl=${returnUrl}`} className="profile-login-btn">
          🟦 Logga in med Microsoft
        </a>
      </div>
      <Link to="/" className="back-link" style={{ marginTop: 24 }}>← Tillbaka till startsidan</Link>
    </div>
  );
}

// ── Alias section ─────────────────────────────────────────────────────────────

function AliasSection({ initial }: { initial: string }) {
  const [alias, setAlias] = useState(initial);
  const [status, setStatus] = useState<{ msg: string; ok: boolean } | null>(null);
  const [saving, setSaving] = useState(false);

  async function handleSave() {
    const trimmed = alias.trim();
    if (trimmed.length < 2 || trimmed.length > 20) {
      setStatus({ msg: 'Alias måste vara 2–20 tecken.', ok: false });
      return;
    }
    setSaving(true);
    setStatus(null);
    try {
      const res = await saveAlias(trimmed);
      setStatus({ msg: `✓ Alias sparat: ${res.alias}`, ok: true });
    } catch (e) {
      setStatus({ msg: e instanceof Error ? e.message : 'Kunde inte spara alias.', ok: false });
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="profile-section">
      <h2>🏷️ Alias</h2>
      <p style={{ marginBottom: '14px', color: 'var(--text-body)', fontSize: '0.9rem', lineHeight: 1.5 }}
       >Ditt alias visas på topplistan. Det måste vara unikt och 2–20 tecken.</p>
      <div className="profile-alias-row">
        <input
          type="text"
          className="profile-input"
          value={alias}
          onChange={e => setAlias(e.target.value)}
          maxLength={20}
          placeholder="Välj ett alias"
          aria-label="Alias"
          onKeyDown={e => e.key === 'Enter' && void handleSave()}
        />
        <button
          className="profile-login-btn"
          onClick={() => void handleSave()}
          disabled={saving}
          style={{ padding: '9px 18px', fontSize: '0.9rem' }}
        >
          {saving ? '⏳ Sparar…' : 'Spara'}
        </button>
      </div>
      {status && (
        <p
          className={status.ok ? 'profile-status-ok' : 'profile-status-err'}
          role="status"
          aria-live="polite"
        >
          {status.msg}
        </p>
      )}
    </section>
  );
}

// ── Stats section ─────────────────────────────────────────────────────────────

function StatsSection() {
  const [stats, setStats] = useState<UserStatsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchMyStats()
      .then(setStats)
      .catch(() => setError('Kunde inte ladda statistik.'));
  }, []);

  if (error) return <p className="leaderboard-error" role="alert">{error}</p>;
  if (!stats) return <p className="leaderboard-loading">Laddar statistik…</p>;
  if (stats.totalSolved === 0) {
    return (
      <section className="profile-section">
        <h2>📊 Statistik</h2>
        <p className="profile-empty">
          Du har inga sparade resultat ännu.<br />
          Lös ett korsord medan du är inloggad!
        </p>
      </section>
    );
  }

  return (
    <section className="profile-section">
      <h2>📊 Statistik</h2>

      {stats.perSize && Object.keys(stats.perSize).length > 0 && (
        <div className="profile-size-groups">
          {Object.entries(stats.perSize).sort(([a], [b]) => a.localeCompare(b)).map(([size, s]) => (
            <div key={size} className="profile-size-group">
              <h3>{SIZE_LABELS[size] ?? size}</h3>
              <div className="profile-stats-grid">
                <div className="stat-card"><span className="value">{s.count}</span><span className="label">Lösta</span></div>
                <div className="stat-card"><span className="value">{formatTime(s.bestTime)}</span><span className="label">Bästa tid</span></div>
                <div className="stat-card"><span className="value">{formatTime(s.averageTime)}</span><span className="label">Snittid</span></div>
                <div className="stat-card"><span className="value">{s.currentStreak}</span><span className="label">Streak</span></div>
                <div className="stat-card"><span className="value">{s.bestStreak}</span><span className="label">Bästa streak</span></div>
              </div>
            </div>
          ))}
        </div>
      )}

      {stats.recentSolves.length > 0 && (
        <>
          <h2 style={{ borderTop: '1px solid var(--border)', paddingTop: '16px', marginTop: '8px' }}>
            🕐 Senaste resultat
          </h2>
          <ul className="recent-list" aria-label="Senaste lösningar">
            {stats.recentSolves.map((s, i) => {
              const hints = (s.hintsUsed ?? 0) + (s.wordHintsUsed ?? 0);
              return (
                <li key={i}>
                  <span>
                    <span className="recent-date">{s.date}</span>
                    {s.puzzleSize && <span className="recent-meta"> · {SIZE_LABELS[s.puzzleSize] ?? s.puzzleSize}</span>}
                    {hints > 0 && <span className="recent-meta"> · 💡 {hints} ledtrådar</span>}
                  </span>
                  <span className="recent-time">{formatTime(s.time)}</span>
                </li>
              );
            })}
          </ul>
        </>
      )}

      {stats.badges && stats.badges.length > 0 && (
        <>
          <h2 style={{ borderTop: '1px solid var(--border)', paddingTop: '16px', marginTop: '8px' }}>
            🏅 Prestationer
          </h2>
          <div className="profile-achievements">
            {stats.badges.map(b => (
              <div key={b.id} className={`achievement-card${b.unlocked ? '' : ' locked'}`}>
                <span className="achievement-icon" aria-hidden="true">{b.icon}</span>
                <div>
                  <div className="achievement-title">{b.name}</div>
                  <div className="achievement-desc">{b.description}</div>
                  <span className="achievement-state">{b.unlocked ? '✓ Upplåst' : '🔒 Låst'}</span>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  );
}

// ── Friends section ───────────────────────────────────────────────────────────

function FriendsSection() {
  const [friends, setFriends] = useState<FriendInfo[]>([]);
  const [requests, setRequests] = useState<FriendRequestInfo[]>([]);
  const [challenges, setChallenges] = useState<FriendChallengeInfo[]>([]);
  const [expiredChallenges, setExpiredChallenges] = useState<FriendChallengeInfo[]>([]);
  const [expiredChallengesLoaded, setExpiredChallengesLoaded] = useState(false);
  const [searchAlias, setSearchAlias] = useState('');
  const [friendStatus, setFriendStatus] = useState<{ msg: string; ok: boolean } | null>(null);
  const [challengeStatus, setChallengeStatus] = useState<{ msg: string; ok: boolean } | null>(null);
  const [loading, setLoading] = useState(true);
  const [showAllExpiredChallenges, setShowAllExpiredChallenges] = useState(false);

  async function reload() {
    try {
      const [f, r, c] = await Promise.all([fetchFriends(), fetchFriendRequests(), fetchChallenges()]);
      setFriends(f);
      setRequests(r);
      setChallenges(c);
      setExpiredChallenges([]);
      setExpiredChallengesLoaded(false);
      setShowAllExpiredChallenges(false);
    } catch {
      // non-fatal
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { void reload(); }, []);

  async function handleSendRequest() {
    const trimmed = searchAlias.trim();
    if (!trimmed) { setFriendStatus({ msg: 'Ange ett alias.', ok: false }); return; }
    try {
      await sendFriendRequest(trimmed);
      setFriendStatus({ msg: '✓ Vänförfrågan skickad!', ok: true });
      setSearchAlias('');
      void reload();
    } catch (e) {
      setFriendStatus({ msg: e instanceof Error ? e.message : 'Kunde inte skicka förfrågan.', ok: false });
    }
  }

  async function handleAccept(id: string) {
    await acceptFriendRequest(id).catch(() => null);
    void reload();
  }

  async function handleDecline(id: string) {
    await declineFriendRequest(id).catch(() => null);
    void reload();
  }

  async function handleRemove(friendshipId: string, name: string) {
    if (!confirm(`Ta bort ${name} som vän?`)) return;
    await removeFriend(friendshipId).catch(() => null);
    void reload();
  }

  async function handleChallenge(friendId: string, name: string) {
    setChallengeStatus({ msg: `Skickar utmaning till ${name}…`, ok: true });
    try {
      await sendChallenge(friendId, todayIso(), '17x17');
      setChallengeStatus({ msg: `✓ Utmaning skickad till ${name}!`, ok: true });
      void reload();
    } catch (e) {
      setChallengeStatus({ msg: e instanceof Error ? e.message : 'Kunde inte skicka utmaning.', ok: false });
    }
  }

  async function handleRespondChallenge(challengeId: string, accepted: boolean) {
    setChallengeStatus({ msg: accepted ? 'Accepterar utmaning…' : 'Avböjer utmaning…', ok: true });
    try {
      await respondChallenge(challengeId, accepted);
      setChallengeStatus({ msg: accepted ? '✓ Utmaning accepterad!' : '✓ Utmaning avböjd.', ok: true });
      void reload();
    } catch (e) {
      setChallengeStatus({ msg: e instanceof Error ? e.message : 'Nätverksfel.', ok: false });
    }
  }

  async function handleToggleExpiredChallenges() {
    if (showAllExpiredChallenges) {
      setShowAllExpiredChallenges(false);
      return;
    }

    if (!expiredChallengesLoaded) {
      setChallengeStatus({ msg: 'Laddar utgångna utmaningar…', ok: true });
      try {
        const expired = await fetchExpiredChallenges();
        setExpiredChallenges(expired);
        setExpiredChallengesLoaded(true);
        setChallengeStatus(null);
      } catch (e) {
        setChallengeStatus({ msg: e instanceof Error ? e.message : 'Kunde inte ladda utgångna utmaningar.', ok: false });
        return;
      }
    }

    setShowAllExpiredChallenges(true);
  }

  if (loading) return <div className="leaderboard-loading">Laddar vänner…</div>;

  const incoming = requests.filter(r => r.direction === 'incoming');
  const outgoing = requests.filter(r => r.direction === 'outgoing');

  const visibleExpiredChallenges = showAllExpiredChallenges ? expiredChallenges : [];
  const hiddenExpiredCount = expiredChallenges.length;
  const visibleChallenges = [...challenges, ...visibleExpiredChallenges];

  return (
    <section className="profile-section">
      <h2>👥 Vänner</h2>
      <div className="profile-alias-row">
        <input
          type="text"
          className="profile-input"
          value={searchAlias}
          onChange={e => setSearchAlias(e.target.value)}
          maxLength={20}
          placeholder="Sök alias…"
          aria-label="Sök användare via alias"
          onKeyDown={e => e.key === 'Enter' && void handleSendRequest()}
        />
        <button
          className="profile-login-btn"
          onClick={() => void handleSendRequest()}
          style={{ padding: '8px 16px', fontSize: '0.9rem' }}
        >
          Lägg till vän
        </button>
      </div>
      {friendStatus && (
        <p className={friendStatus.ok ? 'profile-status-ok' : 'profile-status-err'}>{friendStatus.msg}</p>
      )}

      {incoming.length > 0 && (
        <>
          <h3 className="profile-subheading">Väntande förfrågningar</h3>
          <ul className="friend-list" role="list">
            {incoming.map(r => (
              <li key={r.id}>
                <span>Från: <strong>{r.fromAlias}</strong></span>
                <div className="friend-actions">
                  <button className="friend-btn friend-btn-accept" onClick={() => void handleAccept(r.id)}>Acceptera</button>
                  <button className="friend-btn friend-btn-danger" onClick={() => void handleDecline(r.id)}>Avböj</button>
                </div>
              </li>
            ))}
          </ul>
        </>
      )}

      {friends.length > 0 ? (
        <>
          <h3 className="profile-subheading">Vänlista</h3>
          <ul className="friend-list" role="list">
            {friends.map(f => (
              <li key={f.friendId}>
                <span>{f.alias}</span>
                <div className="friend-actions">
                  <button className="friend-btn" onClick={() => void handleChallenge(f.friendId, f.alias)}>Utmana</button>
                  <button className="friend-btn friend-btn-danger" onClick={() => void handleRemove(f.friendId, f.alias)}>Ta bort</button>
                </div>
              </li>
            ))}
          </ul>
        </>
      ) : (
        <p className="profile-empty">Inga vänner ännu. Sök på alias ovan!</p>
      )}

      {outgoing.length > 0 && (
        <p className="profile-empty" style={{ marginTop: 8 }}>
          {outgoing.map(r => `Väntande förfrågan till: ${r.toAlias}`).join(' · ')}
        </p>
      )}

      <h3 className="profile-subheading" style={{ marginTop: 20 }}>⚔️ Vänutmaningar</h3>
      {challengeStatus && (
        <p className={challengeStatus.ok ? 'profile-status-ok' : 'profile-status-err'}>{challengeStatus.msg}</p>
      )}
      {challenges.length === 0 ? (
        <p className="profile-empty">Inga vänutmaningar ännu.</p>
      ) : (
        <>
          <ul className="friend-list" role="list">
            {visibleChallenges.map(c => {
              const label = c.resultStatus === 'completed'
                ? 'Avgjord'
                : c.resultStatus === 'expired'
                  ? 'Utgången'
                  : c.status === 'pending'
                    ? 'Väntar'
                    : c.status === 'accepted'
                      ? 'Accepterad'
                      : 'Avböjd';
              return (
                <li key={c.id}>
                  <div>
                    <span>
                      {c.direction === 'incoming' ? 'Från' : 'Till'}: <strong>{c.friendAlias}</strong> · {c.date}
                      {c.puzzleSize && <span className="recent-meta"> · {SIZE_LABELS[c.puzzleSize] ?? c.puzzleSize}</span>} · {label}
                    </span>
                    {c.resultStatus === 'completed' && (
                      <div className="challenge-result-summary">
                        <strong>{c.winnerAlias ? `${c.winnerAlias} vann` : 'Oavgjort'}</strong>
                        {c.resultReason ? <span className="recent-meta"> · {c.resultReason}</span> : null}
                        <div className="challenge-result-rows">
                          {c.currentUserSolve && (
                            <div className="challenge-result-row">
                              <span>{c.currentUserSolve.playerAlias}</span>
                              <span>{formatTime(c.currentUserSolve.time)} · 💡{c.currentUserSolve.hintsUsed} · 🧩{c.currentUserSolve.wordHintsUsed}</span>
                            </div>
                          )}
                          {c.friendSolve && (
                            <div className="challenge-result-row">
                              <span>{c.friendSolve.playerAlias}</span>
                              <span>{formatTime(c.friendSolve.time)} · 💡{c.friendSolve.hintsUsed} · 🧩{c.friendSolve.wordHintsUsed}</span>
                            </div>
                          )}
                        </div>
                      </div>
                    )}
                    {c.resultStatus === 'expired' && (
                      <div className="challenge-result-summary">
                        <strong>Utmaningen gick ut utan vinnare.</strong>
                      </div>
                    )}
                  </div>
                  {c.direction === 'incoming' && c.status === 'pending' && (
                    <div className="friend-actions">
                      <button className="friend-btn friend-btn-accept" onClick={() => void handleRespondChallenge(c.id, true)}>Acceptera</button>
                      <button className="friend-btn friend-btn-danger" onClick={() => void handleRespondChallenge(c.id, false)}>Avböj</button>
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
          {(expiredChallengesLoaded ? expiredChallenges.length > 0 : true) && (
            <button
              type="button"
              className="friend-btn challenge-toggle-btn"
              onClick={() => void handleToggleExpiredChallenges()}
            >
              {showAllExpiredChallenges
                ? 'Dölj utgångna'
                : expiredChallengesLoaded
                  ? `Visa utgångna (${hiddenExpiredCount})`
                  : 'Visa utgångna'}
            </button>
          )}
        </>
      )}
    </section>
  );
}

// ── GDPR section ──────────────────────────────────────────────────────────────

function GdprSection() {
  const [status, setStatus] = useState<{ msg: string; ok: boolean } | null>(null);

  async function handleExport() {
    setStatus({ msg: 'Exporterar…', ok: true });
    try {
      await exportMyData();
      setStatus({ msg: '✓ Export klar — filen har laddats ner.', ok: true });
    } catch {
      setStatus({ msg: 'Kunde inte exportera data.', ok: false });
    }
  }

  async function handleDelete() {
    if (!confirm(
      'ÄR DU SÄKER PÅ ATT DU VILL RADERA DITT KONTO?\n\n' +
      'Detta anonymiserar alla dina poäng och historik, tar bort ditt alias och alla vänrelationer, och loggar ut dig.\n\n' +
      'Åtgärden kan INTE ångras.'
    )) return;
    try {
      await deleteMyAccount();
      window.location.href = '/app/';
    } catch {
      setStatus({ msg: 'Kunde inte radera kontot. Försök igen.', ok: false });
    }
  }

  return (
    <section className="profile-section">
      <h2>🔒 Dina uppgifter (GDPR)</h2>
      <p style={{ fontSize: '0.9rem', color: 'var(--text-body)', lineHeight: 1.55, marginBottom: '16px' }}>
        Du har rätt att exportera eller radera alla dina serverlagrade uppgifter.
        Läs mer i vår <Link to="/privacy-policy" style={{ color: 'var(--accent)' }}>integritetspolicy</Link>.
      </p>
      <div className="profile-gdpr-buttons">
        <button className="profile-login-btn" onClick={() => void handleExport()}>
          📥 Exportera mina uppgifter
        </button>
        <button
          className="friend-btn friend-btn-danger"
          onClick={() => void handleDelete()}
          style={{ padding: '10px 18px', fontSize: '0.9rem' }}
        >
          🗑️ Radera mitt konto
        </button>
      </div>
      {status && (
        <p
          className={status.ok ? 'profile-status-ok' : 'profile-status-err'}
          role="status"
          aria-live="polite"
        >
          {status.msg}
        </p>
      )}
    </section>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

function ProfileContent({ user }: { user: AuthUser }) {
  const logoutRef = useRef<HTMLButtonElement>(null);

  async function handleLogout() {
    try {
      await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });
    } catch { /* ignore */ }
    window.location.href = '/app/';
  }

  return (
    <div className="page-content">
      <h1>Min Profil</h1>

      <div className="profile-header">
        <div className="profile-avatar" aria-hidden="true">
          {user.avatarUrl
            ? <img src={user.avatarUrl} alt={`${user.name}s avatar`} />
            : '👤'}
        </div>
        <div className="profile-info">
          <h2>{user.name}</h2>
          <span className="profile-provider">
            Inloggad via {user.provider ?? 'okänd provider'}
          </span>
        </div>
      </div>

      <div className="profile-verified-box" role="status">
        ✓ <strong>Verifierat konto</strong> — Dina resultat på topplistan markeras med ✓ så att andra vet att det är du.
      </div>

      <AliasSection initial={user.alias ?? ''} />
      <StatsSection />

      <div className="profile-two-col">
        <FriendsSection />
        <GdprSection />
      </div>

      {user.isAdmin && (
        <section className="profile-section" style={{ textAlign: 'center' }}>
          <h2>🔧 Administration</h2>
          <Link to="/admin" className="profile-login-btn" style={{ padding: '11px 28px' }}>
            Öppna adminpanel
          </Link>
        </section>
      )}

      <div className="profile-section" style={{ textAlign: 'center' }}>
        <button ref={logoutRef} className="profile-logout-btn" onClick={() => void handleLogout()}>
          Logga ut
        </button>
      </div>

      <Link to="/" className="back-link">← Tillbaka till startsidan</Link>
    </div>
  );
}

export default function ProfilePage() {
  usePageTitle('Min Profil – Svenskt Korsord');
  const { user, loading } = useAuth();

  if (loading) {
    return (
      <div className="page-content">
        <p className="leaderboard-loading">Laddar profil…</p>
      </div>
    );
  }

  if (!user) return <div className="page-content"><LoginPrompt /></div>;

  return <ProfileContent user={user} />;
}
