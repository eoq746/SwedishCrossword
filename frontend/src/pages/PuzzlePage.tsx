import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { fetchFriends, respondChallenge, sendChallenges, sendChallengesToAll, type FriendInfo } from '../api/profile';
import { createClueFlag } from '../api/clues';
import { useAuth } from '../hooks/useAuth';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import { PuzzleClues } from './puzzle/PuzzleClues';
import { PuzzleGrid } from './puzzle/PuzzleGrid';
import { PuzzleHistorySection } from './puzzle/PuzzleHistorySection';
import { PuzzleLeaderboardSection } from './puzzle/PuzzleLeaderboardSection';
import type { ClueEntry, PuzzleSize } from './puzzle/types';
import { formatLeaderboardTime, usePuzzleGame } from './puzzle/usePuzzleGame';
import { usePuzzleLayout } from './puzzle/usePuzzleLayout';
import '../styles/static-pages.css';

const SIZES: Array<{ key: PuzzleSize; label: string; sub: string }> = [
  { key: '10x10', label: '🟢 Liten', sub: '10×10' },
  { key: '15x15', label: '🟡 Mellan', sub: '15×15' },
  { key: '17x17', label: '🔴 Stor', sub: '17×17' },
];

const CLUE_REPORTS_PREFIX = 'crossword-reported-clues:';

function getReportStorageKey(puzzleHash: string, puzzleDate: string): string {
  return `${CLUE_REPORTS_PREFIX}${puzzleHash || puzzleDate}`;
}

function getClueReportKey(entry: ClueEntry): string {
  return `${entry.id}:${entry.clue}`;
}

export default function PuzzlePage() {
  const [searchParams] = useSearchParams();
  const { user } = useAuth();

  const size = (searchParams.get('size') as PuzzleSize) || '17x17';
  const dateParam = searchParams.get('date') ?? '';

  const seoTitle = dateParam ? `Korsord ${dateParam}` : 'Spela Korsord';
  usePageTitle(dateParam ? `Korsord ${dateParam}` : 'Spela Korsord');
  
  useSEO({
    title: seoTitle,
    description: dateParam 
      ? `Spela korsord från ${dateParam}. Svenska korsord med ord, synonymer och ledtrådar. Testa ditt ordförråd och tävla på topplistan.`
      : `Spela dagens svenska korsord online. Lösa ord pussel med ledtrådar, träna hjärnan och förbättra ditt ordförråd.`,
    canonical: `https://www.svensktkorsord.se/puzzle${dateParam ? `?date=${dateParam}` : ''}`,
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Spela', url: 'https://www.svensktkorsord.se/play' },
      { name: seoTitle, url: `https://www.svensktkorsord.se/puzzle${dateParam ? `?date=${dateParam}` : ''}` }
    ])
  });

  const [messageModal, setMessageModal] = useState<{ title: string; body: string } | null>(null);
  const [confirmModal, setConfirmModal] = useState<{ title: string; body: string; onConfirm: () => void } | null>(null);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [showClues, setShowClues] = useState(true);
  const [showLeaderboardPanel, setShowLeaderboardPanel] = useState(false);
  const [showIntro, setShowIntro] = useState(false);
  const [showHistoryPanel, setShowHistoryPanel] = useState(false);
  const [reportingClue, setReportingClue] = useState<ClueEntry | null>(null);
  const [reportSuggestedClue, setReportSuggestedClue] = useState('');
  const [reportReason, setReportReason] = useState('');
  const [reportSubmitting, setReportSubmitting] = useState(false);
  const [reportedClueKeys, setReportedClueKeys] = useState<Record<string, boolean>>({});
  const [postSubmitChallengeOpen, setPostSubmitChallengeOpen] = useState(false);
  const [challengeableFriends, setChallengeableFriends] = useState<FriendInfo[]>([]);
  const [selectedChallengeFriendIds, setSelectedChallengeFriendIds] = useState<string[]>([]);
  const [challengeSubmissionStatus, setChallengeSubmissionStatus] = useState<{ msg: string; ok: boolean } | null>(null);
  const [challengeSubmitting, setChallengeSubmitting] = useState(false);
  const [challengeRespondingId, setChallengeRespondingId] = useState<string | null>(null);

  const {
    loading,
    puzzleUnavailable,
    puzzleNotFound,
    puzzle,
    puzzleDate,
    seconds,
    values,
    incorrectCells,
    emptyWarningCells,
    hintRevealedCells,
    leaderboard,
    friendsLeaderboard,
    activePuzzleChallenges,
    fetchPuzzleChallenges,
    hasFriends,
    activeLeaderboardTab,
    setActiveLeaderboardTab,
    historyRows,
    usernameModalOpen,
    setUsernameModalOpen,
    username,
    setUsername,
    nav,
    clueEntries,
    activeEntry,
    filledCount,
    totalFillableCount,
    progressPercent,
    currentHintSummary,
    activateCell,
    handleCellChange,
    handleCellKeyDown,
    checkAnswers,
    clearGrid,
    revealSolution,
    revealLetter,
    revealWord,
    submitScore,
    isClueFilled,
  } = usePuzzleGame({ size, dateParam, user });

  const gridSectionRef = useRef<HTMLDivElement | null>(null);
  const gridHeaderRef = useRef<HTMLDivElement | null>(null);
  const controlsRef = useRef<HTMLDivElement | null>(null);
  const mainLayoutRef = useRef<HTMLDivElement | null>(null);
  const autoScrollKeyRef = useRef<string | null>(null);

  const layout = usePuzzleLayout({
    enabled: Boolean(puzzle),
    gridSectionRef,
    gridHeaderRef,
    controlsRef,
    columns: puzzle?.width ?? 0,
    rows: puzzle?.height ?? 0,
    layoutKey: [
      showIntro ? 'intro' : '',
      showClues ? 'clues' : '',
      showLeaderboardPanel ? 'lb' : '',
      showHistoryPanel ? 'hist' : '',
    ].join('-'),
  });

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === '?' && !(e.target instanceof HTMLInputElement)) {
        e.preventDefault();
        setShortcutsOpen(v => !v);
      }
      if (e.key === 'Escape') {
        setShortcutsOpen(false);
        setUsernameModalOpen(false);
        setMessageModal(null);
        setConfirmModal(null);
        setReportingClue(null);
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [setUsernameModalOpen]);

  useEffect(() => {
    document.body.classList.toggle('hide-clues', !showClues);
    document.body.classList.toggle('show-leaderboard', showLeaderboardPanel);
    document.body.classList.toggle('show-intro', showIntro);
    document.body.classList.toggle('show-history', showHistoryPanel);

    return () => {
      document.body.classList.remove('hide-clues', 'show-leaderboard', 'show-intro', 'show-history');
    };
  }, [showClues, showHistoryPanel, showIntro, showLeaderboardPanel]);

  const handleCheck = async () => {
    const result = await checkAnswers();
    if (result.status === 'solved') return;

    if (result.status === 'incomplete') {
      setMessageModal({
        title: 'Inte klart ännu',
        body: `Du har ${result.emptyCount} tomma rutor kvar.`,
      });
      return;
    }

    if (result.status === 'error') {
      setMessageModal({
        title: 'Kontroll otillgänglig',
        body: 'Kunde inte kontrollera korsordet just nu. Försök igen om en stund.',
      });
      return;
    }

    setMessageModal({
      title: 'Felaktiga bokstäver',
      body: `${result.incorrectCount} bokstäver är felaktiga. Försök igen!` ,
    });
  };

  const handleRevealLetter = async () => {
    const result = await revealLetter();
    if (result === 'no-active-cell') {
      setMessageModal({ title: 'Tips', body: 'Välj en ruta först.' });
      return;
    }

    if (result === 'unavailable') {
      setMessageModal({
        title: 'Tips otillgängligt',
        body: 'Kunde inte hämta en bokstav just nu. Försök igen om en stund.',
      });
    }
  };

  const handleRevealWord = async () => {
    const result = await revealWord();
    if (result === 'no-active-cell') {
      setMessageModal({ title: 'Tips', body: 'Välj en ruta i ordet först.' });
      return;
    }

    if (result === 'unavailable') {
      setMessageModal({
        title: 'Tips otillgänglig',
        body: 'Kunde inte hämta ordet just nu. Försök igen om en stund.',
      });
    }
  };

  const handleClearGrid = () => {
    setConfirmModal({
      title: 'Rensa korsord',
      body: 'Vill du rensa alla svar?',
      onConfirm: () => {
        clearGrid();
        setConfirmModal(null);
      },
    });
  };

  const handleRevealSolution = () => {
    setConfirmModal({
      title: 'Visa lösning',
      body: 'Vill du visa lösningen? Du kommer inte kunna skicka in ditt resultat.',
      onConfirm: () => {
        void (async () => {
          const result = await revealSolution();
          setConfirmModal(null);
          if (result === 'unavailable') {
            setMessageModal({
              title: 'Lösning otillgänglig',
              body: 'Kunde inte hämta lösningen just nu. Försök igen om en stund.',
            });
          }
        })();
      },
    });
  };

  const finishScoreSubmissionUi = () => {
    if (window.matchMedia('(max-width:1100px)').matches) {
      setShowLeaderboardPanel(true);
      setShowClues(false);
      setShowIntro(false);
      setShowHistoryPanel(false);
    }
  };

  const handleSubmitScore = async () => {
    const completed = await submitScore();
    if (!completed) return;

    if (!user || !puzzle) {
      finishScoreSubmissionUi();
      return;
    }

    try {
      const friends = await fetchFriends();
      if (friends.length === 0) {
        finishScoreSubmissionUi();
        return;
      }

      setChallengeableFriends(friends);
      setSelectedChallengeFriendIds(friends.map(friend => friend.friendId));
      setChallengeSubmissionStatus(null);
      setPostSubmitChallengeOpen(true);
      return;
    } catch {
      finishScoreSubmissionUi();
      return;
    }
  };

  const solvedPuzzleSize = puzzle ? `${puzzle.width}x${puzzle.height}` : size;

  const handleToggleChallengeFriend = (friendId: string) => {
    setSelectedChallengeFriendIds(current => current.includes(friendId)
      ? current.filter(id => id !== friendId)
      : [...current, friendId]);
  };

  const closePostSubmitChallengeModal = () => {
    setPostSubmitChallengeOpen(false);
    setChallengeSubmissionStatus(null);
    finishScoreSubmissionUi();
  };

  const handleChallengeSelectedFriends = async () => {
    if (selectedChallengeFriendIds.length === 0) {
      setChallengeSubmissionStatus({ msg: 'Välj minst en vän att utmana.', ok: false });
      return;
    }

    setChallengeSubmitting(true);
    setChallengeSubmissionStatus(null);
    try {
      const result = await sendChallenges(puzzleDate, solvedPuzzleSize, selectedChallengeFriendIds);
      setChallengeSubmissionStatus({
        msg: result.skipped > 0
          ? `✓ Skickade ${result.sent} utmaningar. ${result.skipped} hoppades över eftersom de redan fanns.`
          : `✓ Skickade ${result.sent} utmaningar!`,
        ok: true,
      });
    } catch (error) {
      setChallengeSubmissionStatus({ msg: error instanceof Error ? error.message : 'Kunde inte skicka utmaningar.', ok: false });
    } finally {
      setChallengeSubmitting(false);
    }
  };

  const handleChallengeAllFriends = async () => {
    setChallengeSubmitting(true);
    setChallengeSubmissionStatus(null);
    try {
      const result = await sendChallengesToAll(puzzleDate, solvedPuzzleSize);
      setChallengeSubmissionStatus({
        msg: result.skipped > 0
          ? `✓ Skickade ${result.sent} utmaningar till alla vänner. ${result.skipped} hoppades över eftersom de redan fanns.`
          : `✓ Skickade ${result.sent} utmaningar till alla vänner!`,
        ok: true,
      });
    } catch (error) {
      setChallengeSubmissionStatus({ msg: error instanceof Error ? error.message : 'Kunde inte skicka utmaningar till alla vänner.', ok: false });
    } finally {
      setChallengeSubmitting(false);
    }
  };

  const handleRespondPuzzleChallenge = async (challengeId: string, accepted: boolean) => {
    setChallengeRespondingId(challengeId);
    try {
      await respondChallenge(challengeId, accepted);
      await fetchPuzzleChallenges();
    } catch (error) {
      setMessageModal({
        title: accepted ? 'Kunde inte acceptera utmaning' : 'Kunde inte avböja utmaning',
        body: error instanceof Error ? error.message : 'Försök igen om en stund.',
      });
    } finally {
      setChallengeRespondingId(null);
    }
  };

  useEffect(() => {
    const storageKey = getReportStorageKey(puzzle?.puzzleHash ?? '', puzzleDate);
    try {
      const raw = localStorage.getItem(storageKey);
      if (!raw) {
        setReportedClueKeys({});
        return;
      }
      const parsed = JSON.parse(raw) as Record<string, boolean>;
      setReportedClueKeys(parsed ?? {});
    } catch {
      setReportedClueKeys({});
    }
  }, [puzzle?.puzzleHash, puzzleDate]);

  const isClueAlreadyReported = (entry: ClueEntry) => Boolean(reportedClueKeys[getClueReportKey(entry)]);

  const handleOpenClueReport = (entry: ClueEntry) => {
    if (isClueAlreadyReported(entry)) {
      setMessageModal({ title: 'Redan rapporterad', body: 'Du har redan rapporterat denna ledtråd för det här pusslet.' });
      return;
    }

    setReportingClue(entry);
    setReportSuggestedClue('');
    setReportReason('');
  };

  const handleSubmitClueReport = async () => {
    if (!reportingClue || reportSubmitting) return;

    setReportSubmitting(true);
    try {
      await createClueFlag({
        currentClue: reportingClue.clue,
        clueCells: reportingClue.cells.map(cell => [cell.row, cell.col]),
        suggestedClue: reportSuggestedClue.trim() || undefined,
        reason: reportReason.trim() || undefined,
        puzzleDate,
        puzzleSize: size,
        puzzleHash: puzzle?.puzzleHash,
      });

      const clueKey = getClueReportKey(reportingClue);
      const storageKey = getReportStorageKey(puzzle?.puzzleHash ?? '', puzzleDate);
      setReportedClueKeys(prev => {
        const next = { ...prev, [clueKey]: true };
        try {
          localStorage.setItem(storageKey, JSON.stringify(next));
        } catch {
          // ignore storage quota/privacy failures
        }
        return next;
      });

      setReportingClue(null);
      setMessageModal({
        title: 'Tack!',
        body: 'Ledtråden har rapporterats till admin för granskning.',
      });
    } catch {
      setMessageModal({
        title: 'Rapportering misslyckades',
        body: 'Kunde inte skicka rapporten just nu. Försök igen om en stund.',
      });
    } finally {
      setReportSubmitting(false);
    }
  };

  useEffect(() => {
    autoScrollKeyRef.current = null;
  }, [puzzle?.puzzleHash, size, dateParam]);

  useEffect(() => {
    if (!puzzle || loading || seconds <= 0) return;
    const scrollKey = puzzle.puzzleHash ?? `${size}-${puzzleDate}`;
    if (autoScrollKeyRef.current === scrollKey) return;

    const layoutElement = mainLayoutRef.current;
    if (!layoutElement) return;

    const rect = layoutElement.getBoundingClientRect();
    const viewportHeight = window.innerHeight;
    const fullyVisible = rect.top >= 8 && rect.bottom <= viewportHeight - 8;
    if (!fullyVisible) {
      const targetTop = Math.max(0, window.scrollY + rect.top - 12);
      window.scrollTo({ top: targetTop, behavior: 'smooth' });
    }

    autoScrollKeyRef.current = scrollKey;
  }, [dateParam, loading, puzzle, puzzleDate, seconds, size]);

  return (
    <>
      <div id="announcements" className="sr-only" aria-live="polite" aria-atomic="true" />

      <div className="puzzle-size-selector">
        {SIZES.map(s => (
          <Link
            key={s.key}
            to={`/puzzle${dateParam ? `?size=${s.key}&date=${dateParam}` : `?size=${s.key}`}` }
            className={`size-tab${size === s.key ? ' active' : ''}`}
          >
            {s.label} <span className="size-sub">{s.sub}</span>
          </Link>
        )) }
      </div>

      {puzzle ? (
        <div className="puzzle-meta">
          <div className="puzzle-info" id="puzzle-info">
            <span id="info-size">{`${puzzle.width}x${puzzle.height}`}</span>
            <span id="info-words">{puzzle.wordCount ? `${puzzle.wordCount} ord` : ''}</span>
            <span id="info-fill">{typeof puzzle.fillPercentage === 'number' ? `${puzzle.fillPercentage}%` : ''}</span>
            <span id="info-difficulty" hidden />
          </div>
          <div className="generation-date" id="generation-date">{puzzleDate}</div>
        </div>
      ) : null}

      {puzzle && !loading && !puzzleUnavailable && !puzzleNotFound && (
        <div className="top-controls">
          <button
            id="clues-toggle"
            className="clues-toggle"
            aria-expanded={showClues}
            onClick={() => {
              const next = !showClues;
              setShowClues(next);
              if (next) {
                setShowLeaderboardPanel(false);
                setShowIntro(false);
                setShowHistoryPanel(false);
              }
            }}
          >
            Visa ledtrådar
          </button>
          <button
            id="leaderboard-toggle"
            className="clues-toggle"
            aria-expanded={showLeaderboardPanel}
            onClick={() => {
              const next = !showLeaderboardPanel;
              setShowLeaderboardPanel(next);
              setShowClues(!next);
              setShowIntro(false);
              setShowHistoryPanel(false);
            }}
          >
            Visa Topplista
          </button>
          <button
            id="history-toggle"
            className="clues-toggle"
            aria-expanded={showHistoryPanel}
            onClick={() => {
              const next = !showHistoryPanel;
              setShowHistoryPanel(next);
              setShowClues(!next);
              setShowIntro(false);
              setShowLeaderboardPanel(false);
            }}
          >
            Visa Historik
          </button>
          <button
            id="intro-toggle"
            className="clues-toggle"
            aria-expanded={showIntro}
            onClick={() => {
              const next = !showIntro;
              setShowIntro(next);
              if (next) {
                setShowClues(false);
                setShowLeaderboardPanel(false);
                setShowHistoryPanel(false);
              }
            }}
          >
            Visa info
          </button>
          <button
            id="help-toggle"
            className="clues-toggle"
            aria-label="Tangentbordsgenvägar"
            title="Tangentbordsgenvägar (?)"
            onClick={() => setShortcutsOpen(v => !v)}
          >
            ⌨️
          </button>
        </div>
      )}

      <section aria-label="Spelinstruktioner" className="intro-section" id="intro-section">
        <h2>Så här spelar du</h2>
        <div className="info-cards">
          <div className="info-card">
            <div className="info-card-icon">🎮</div>
            <h3>Kontroller</h3>
            <ul>
              <li><kbd>Klicka</kbd> på en ruta för att aktivera</li>
              <li><kbd>Mellanslag</kbd> byter riktning</li>
              <li><kbd>Piltangenter</kbd> navigerar</li>
              <li><kbd>Tab</kbd> nästa ledtråd, <kbd>Shift+Tab</kbd> föregående</li>
              <li><kbd>Backspace</kbd> raderar och flyttar</li>
            </ul>
          </div>
          <div className="info-card">
            <div className="info-card-icon">🏆</div>
            <h3>Topplista</h3>
            <p>Lös korsordet och registrera din tid! Topplistan visar de snabbaste tiderna.</p>
          </div>
        </div>
      </section>

      {loading && (
        <div id="loading" className="loading">
          <div className="loading-spinner" />
          <p>Laddar korsord...</p>
        </div>
      )}

      {puzzleUnavailable && !loading && (
        <div className="unavailable-card">
          <div className="unavailable-icon">🔧</div>
          <h2>Korsordet genereras...</h2>
          <p>Dagens korsord håller på att skapas. Försök igen om en stund.</p>
        </div>
      )}


      {puzzleNotFound && !loading && (
        <div className="unavailable-card">
          <div className="unavailable-icon">📅</div>
          <h2>Inget korsord tillgängligt</h2>
          <p>Det finns inget korsord för valt datum.</p>
        </div>
      )}

      {!loading && puzzle && !puzzleUnavailable && !puzzleNotFound && (
        <div className="site-container react-puzzle-page-shell">
          <div className="main-layout react-puzzle-layout" id="main-layout" role="main" ref={mainLayoutRef}>
            <div
              className="grid-section"
              ref={gridSectionRef}
              style={{
                height: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
                minHeight: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
                maxHeight: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
              }}
            >
              <div className="grid-header" ref={gridHeaderRef}>
                <div className="grid-heading">
                  <h2>{dateParam ? `Korsord ${puzzleDate}` : 'Dagens Korsord'}</h2>
                  <p className="grid-subtitle">Fokusera på rutnätet och ledtrådarna. Tid och framsteg följer med medan du spelar.</p>
                </div>
              </div>
              <div className="grid-inner">
                <div className="controls" aria-label="Spelkontroller" ref={controlsRef}>
                  <button className="btn btn-primary" onClick={handleCheck}>Kontrollera</button>
                  <button className="btn btn-secondary" onClick={handleClearGrid}>Rensa</button>
                  <button className="btn btn-hint" id="hint-letter-btn" title="Avslöja bokstav" onMouseDown={e => e.preventDefault()} onClick={handleRevealLetter}>💡 Bokstav</button>
                  <button className="btn btn-hint" id="hint-word-btn" title="Avslöja ord" onMouseDown={e => e.preventDefault()} onClick={handleRevealWord}>💡 Ord</button>
                  <button className="btn btn-success" onClick={handleRevealSolution}>Visa lösning</button>
                  <div className="grid-status-bar">
                    <div className="timer" id="timer">{formatLeaderboardTime(seconds)}</div>
                    <div className="grid-status-pill" id="stats">{filledCount}/{totalFillableCount} rutor · {progressPercent}%</div>
                    {currentHintSummary ? <div className="grid-status-pill grid-status-pill-muted">{currentHintSummary}</div> : null}
                  </div>
                </div>
                <div
                  className="grid-area"
                  style={{
                    height: layout.gridAreaHeight ? `${layout.gridAreaHeight}px` : undefined,
                    minHeight: layout.gridAreaHeight ? `${layout.gridAreaHeight}px` : undefined,
                  }}
                >
                  <PuzzleGrid
                    puzzle={puzzle}
                    values={values}
                    activeCell={nav.active}
                    activeEntry={activeEntry}
                    incorrectCells={incorrectCells}
                    emptyWarningCells={emptyWarningCells}
                    hintRevealedCells={hintRevealedCells}
                    cellSize={layout.gridCellSize}
                    gridWidth={layout.gridWidth}
                    gridHeight={layout.gridHeight}
                    onActivate={(row, col) => activateCell(row, col)}
                    onChange={handleCellChange}
                    onKeyDown={handleCellKeyDown}
                  />
                </div>
              </div>
            </div>

            <PuzzleClues
              across={clueEntries.across}
              down={clueEntries.down}
              activeEntryId={activeEntry?.id ?? null}
              isClueFilled={isClueFilled}
              onSelect={entry => {
                const first = entry.cells[0];
                if (first) activateCell(first.row, first.col, entry.direction);
              }}
              onReport={handleOpenClueReport}
              isReported={isClueAlreadyReported}
              height={layout.boardHeight}
            />

            <div className="puzzle-side-panels" aria-label="Topplistor"
              style={{
                height: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
                minHeight: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
                maxHeight: layout.boardHeight ? `${layout.boardHeight}px` : undefined,
              }}
            >
              {user && activePuzzleChallenges.length > 0 && (
                <section className="puzzle-challenges-panel" aria-label="Aktiva vänutmaningar">
                  <h2>⚔️ Aktiva vänutmaningar</h2>
                  <ul className="puzzle-challenges-list" role="list">
                    {activePuzzleChallenges.map(challenge => {
                      const statusLabel = challenge.resultStatus === 'completed'
                        ? 'Avgjord'
                        : challenge.resultStatus === 'expired'
                          ? 'Utgången'
                          : challenge.status === 'pending'
                            ? 'Väntar på svar'
                            : challenge.status === 'accepted'
                              ? 'Accepterad'
                              : 'Avböjd';
                      return (
                        <li key={challenge.id} className={`puzzle-challenge-item${challenge.status === 'accepted' ? ' accepted' : ''}`}>
                          <div>
                            <strong>{challenge.friendAlias}</strong>
                            <div className="puzzle-challenge-meta">
                              {challenge.direction === 'incoming' ? 'Utmanade dig' : 'Du utmanade'} · {statusLabel}
                            </div>
                            {challenge.resultStatus === 'completed' && (
                              <div className="puzzle-challenge-result">
                                <strong>{challenge.winnerAlias ? `${challenge.winnerAlias} vann` : 'Oavgjort'}</strong>
                                {challenge.resultReason ? <span className="recent-meta"> · {challenge.resultReason}</span> : null}
                              </div>
                            )}
                            {challenge.resultStatus === 'expired' && (
                              <div className="puzzle-challenge-result">
                                <strong>Utmaningen gick ut utan vinnare.</strong>
                              </div>
                            )}
                          </div>
                          {challenge.direction === 'incoming' && challenge.status === 'pending' && (
                            <div className="friend-actions puzzle-challenge-actions">
                              <button
                                className="friend-btn friend-btn-accept"
                                onClick={() => void handleRespondPuzzleChallenge(challenge.id, true)}
                                disabled={challengeRespondingId === challenge.id}
                              >
                                Acceptera
                              </button>
                              <button
                                className="friend-btn friend-btn-danger"
                                onClick={() => void handleRespondPuzzleChallenge(challenge.id, false)}
                                disabled={challengeRespondingId === challenge.id}
                              >
                                Avböj
                              </button>
                            </div>
                          )}
                        </li>
                      );
                    })}
                  </ul>
                </section>
              )}
              <PuzzleLeaderboardSection
                puzzleDate={puzzleDate}
                leaderboard={leaderboard}
                friendsLeaderboard={friendsLeaderboard}
                currentUserDisplayName={user?.alias ?? user?.name ?? null}
                activeTab={activeLeaderboardTab}
                onTabChange={setActiveLeaderboardTab}
                showFriendsTab={!!user}
                hasFriends={hasFriends}
                height={layout.supportPanelHeight}
              />
              <PuzzleHistorySection historyRows={historyRows} height={layout.supportPanelHeight} />
            </div>
          </div>

          <div className="player-stats-section" id="player-stats-section">
            <h2>Din Statistik</h2>
            <div id="player-stats">
              <div className="player-stats-grid">
                <div className="stat-item">
                  <span className="stat-value">{formatLeaderboardTime(seconds)}</span>
                  <span className="stat-label">Tid</span>
                </div>
                <div className="stat-item">
                  <span className="stat-value">{filledCount}</span>
                  <span className="stat-label">Ifyllda</span>
                </div>
                <div className="stat-item">
                  <span className="stat-value">{progressPercent}%</span>
                  <span className="stat-label">Progress</span>
                </div>
              </div>
            </div>
            <div id="personal-stats" style={{ display: 'none' }} />
          </div>
        </div>
      )}

      <div className={`modal-overlay${messageModal ? ' active' : ''}`} id="message-modal" role="dialog" aria-modal="true" aria-labelledby="message-modal-title">
        <div className="modal">
          <h3 id="message-modal-title">{messageModal?.title ?? ''}</h3>
          <p id="message-modal-body">{messageModal?.body ?? ''}</p>
          <div className="modal-buttons" id="message-modal-buttons">
            <button className="btn btn-primary" onClick={() => setMessageModal(null)}>OK</button>
          </div>
        </div>
      </div>

      <div className={`modal-overlay${confirmModal ? ' active' : ''}`} role="dialog" aria-modal="true" aria-labelledby="confirm-modal-title">
        <div className="modal">
          <h3 id="confirm-modal-title">{confirmModal?.title ?? ''}</h3>
          <p>{confirmModal?.body ?? ''}</p>
          <div className="modal-buttons">
            <button className="btn btn-primary" onClick={() => confirmModal?.onConfirm()}>Ja</button>
            <button className="btn btn-secondary" onClick={() => setConfirmModal(null)}>Avbryt</button>
          </div>
        </div>
      </div>

      <div className={`modal-overlay${reportingClue ? ' active' : ''}`} role="dialog" aria-modal="true" aria-labelledby="clue-report-title">
        <div className="modal">
          <h3 id="clue-report-title">Rapportera ledtråd</h3>
          <p>
            {reportingClue
              ? `Ledtråd ${reportingClue.number} (${reportingClue.direction === 'across' ? 'vågrätt' : 'lodrätt'}): ${reportingClue.clue}`
              : '' }
          </p>

          <label htmlFor="report-suggested-clue-input">Föreslagen ledtråd</label>
          <input
            id="report-suggested-clue-input"
            type="text"
            value={reportSuggestedClue}
            onChange={e => setReportSuggestedClue(e.target.value)}
            maxLength={256}
            autoComplete="off"
            placeholder="Ny ledtråd (valfritt)"
            disabled={reportSubmitting}
          />

          <label htmlFor="report-reason-input" style={{ marginTop: 8 }}>Orsak (valfritt)</label>
          <textarea
            id="report-reason-input"
            className="clue-report-reason"
            value={reportReason}
            onChange={e => setReportReason(e.target.value)}
            rows={3}
            maxLength={1000}
            placeholder="Varför är ledtråden dålig eller missvisande?"
            disabled={reportSubmitting}
          />

          <div className="modal-buttons">
            <button className="btn btn-primary" onClick={() => void handleSubmitClueReport()} disabled={reportSubmitting}>
              {reportSubmitting ? 'Skickar…' : 'Skicka rapport'}
            </button>
            <button className="btn btn-secondary" onClick={() => setReportingClue(null)} disabled={reportSubmitting}>Avbryt</button>
          </div>
        </div>
      </div>

      <div className={`modal-overlay${usernameModalOpen ? ' active' : ''}`} id="username-modal" role="dialog" aria-modal="true" aria-label="Spara resultat">
        <div className="modal">
          <h3>Grattis!</h3>
          <p>Du löste korsordet!</p>
          <div className="modal-time" id="modal-time">{formatLeaderboardTime(seconds)}</div>
          <p>Ange ditt namn för topplistan:</p>
          <input
            type="text"
            id="username-input"
            placeholder="Ditt namn"
            maxLength={20}
            autoComplete="off"
            autoCorrect="off"
            autoCapitalize="words"
            spellCheck={false}
            value={username}
            onChange={e => setUsername(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter') {
                e.preventDefault();
                void handleSubmitScore();
              }
            }}
          />
          <div className="modal-buttons">
            <button className="btn btn-primary" onClick={() => void handleSubmitScore()}>Spara</button>
            <button className="btn btn-secondary" onClick={() => setUsernameModalOpen(false)}>Hoppa över</button>
          </div>
        </div>
      </div>

      <div className={`modal-overlay${postSubmitChallengeOpen ? ' active' : ''}`} role="dialog" aria-modal="true" aria-label="Utmana vänner">
        <div className="modal">
          <h3>⚔️ Utmana vänner</h3>
          <p>Du löste korsordet för {puzzleDate} · {solvedPuzzleSize}. Vem vill du utmana?</p>
          {challengeSubmissionStatus && (
            <p className={challengeSubmissionStatus.ok ? 'profile-status-ok' : 'profile-status-err'}>{challengeSubmissionStatus.msg}</p>
          )}
          <div className="challenge-selection-list" role="list" aria-label="Välj vänner att utmana">
            {challengeableFriends.map(friend => (
              <label key={friend.friendId} className="challenge-selection-item">
                <input
                  type="checkbox"
                  checked={selectedChallengeFriendIds.includes(friend.friendId)}
                  onChange={() => handleToggleChallengeFriend(friend.friendId)}
                  disabled={challengeSubmitting}
                />
                <span>{friend.alias}</span>
              </label>
            ))}
          </div>
          <div className="modal-buttons challenge-modal-buttons">
            <button className="btn btn-primary" onClick={() => void handleChallengeSelectedFriends()} disabled={challengeSubmitting}>
              {challengeSubmitting ? 'Skickar…' : 'Utmana valda'}
            </button>
            <button className="btn btn-primary" onClick={() => void handleChallengeAllFriends()} disabled={challengeSubmitting}>
              Utmana alla
            </button>
            <button className="btn btn-secondary" onClick={closePostSubmitChallengeModal} disabled={challengeSubmitting}>Klar</button>
          </div>
        </div>
      </div>

      <div
        className="shortcuts-overlay"
        id="shortcuts-overlay"
        role="dialog"
        aria-label="Tangentbordsgenvägar"
        aria-modal="true"
        style={{ display: shortcutsOpen ? 'flex' : 'none' }}
        onClick={e => {
          if (e.target === e.currentTarget) setShortcutsOpen(false);
        }}
      >
        <div className="shortcuts-card">
          <button className="shortcuts-close" id="shortcuts-close" aria-label="Stäng" onClick={() => setShortcutsOpen(false)}>
            &times;
          </button>
          <h2>⌨️ Tangentbordsgenvägar</h2>
          <dl className="shortcuts-list">
            <div className="shortcut-row"><dt><kbd>A</kbd>–<kbd>Ö</kbd></dt><dd>Skriv bokstav</dd></div>
            <div className="shortcut-row"><dt><kbd>Mellanslag</kbd></dt><dd>Byt riktning</dd></div>
            <div className="shortcut-row"><dt><kbd>Tab</kbd></dt><dd>Nästa ledtråd</dd></div>
            <div className="shortcut-row"><dt><kbd>Shift</kbd>+<kbd>Tab</kbd></dt><dd>Föregående ledtråd</dd></div>
            <div className="shortcut-row"><dt><kbd>←</kbd> <kbd>→</kbd></dt><dd>Flytta vågrätt</dd></div>
            <div className="shortcut-row"><dt><kbd>↑</kbd> <kbd>↓</kbd></dt><dd>Flytta lodrätt</dd></div>
            <div className="shortcut-row"><dt><kbd>Backsteg</kbd></dt><dd>Radera &amp; flytta bakåt</dd></div>
            <div className="shortcut-row"><dt><kbd>Delete</kbd></dt><dd>Radera utan att flytta</dd></div>
            <div className="shortcut-row"><dt><kbd>?</kbd></dt><dd>Visa/dölj hjälpen</dd></div>
          </dl>
        </div>
      </div>
    </>
  );
}
