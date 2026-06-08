import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { fetchChallenges, fetchFriends, fetchFriendsLeaderboard } from '../../api/profile';
import type { FriendChallengeInfo, FriendsLeaderboardEntry } from '../../api/profile';
import type { AuthUser } from '../../hooks/useAuth';
import type {
  CellKey,
  CheckResult,
  ClueEntry,
  HistoryResponse,
  HistoryRow,
  HintActionResult,
  LeaderboardResponse,
  PuzzleData,
  PuzzleSize,
  RevealSolutionResult,
  ScoreEntry,
} from './types';
import { buildClueEntries } from './gridModel';
import { recordPuzzleSolve } from '../../hooks/usePlayerStats';
import { findBestEntry, findFirstFillableCell, navReducer } from './navigation';
import { usePuzzleGridInput } from './usePuzzleGridInput';
import {
  announce,
  formatTime,
  getProgressKey,
  getTodayIso,
  loadLocalLeaderboard,
  saveLocalLeaderboard,
} from './utils';

interface UsePuzzleGameOptions {
  size: PuzzleSize;
  dateParam: string;
  user: AuthUser | null;
}

function readLocalStorage(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeLocalStorage(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // ignore storage failures and continue the in-memory game session
  }
}

function removeLocalStorage(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // ignore storage failures and continue the in-memory game session
  }
}

const LEADERBOARD_TAB_KEY = 'crossword-leaderboard-tab';
const PENDING_SCORE_KEY = 'crossword-pending-score';

type LeaderboardTab = 'global' | 'friends';

type AutoCheckFeedback = {
  tone: 'info' | 'error';
  text: string;
};

export function usePuzzleGame({ size, dateParam, user }: UsePuzzleGameOptions) {
  const [loading, setLoading] = useState(true);
  const [puzzleUnavailable, setPuzzleUnavailable] = useState(false);
  const [puzzleNotFound, setPuzzleNotFound] = useState(false);
  const [puzzle, setPuzzle] = useState<PuzzleData | null>(null);
  const [seconds, setSeconds] = useState(0);
  const [puzzleSolved, setPuzzleSolved] = useState(false);
  const [hasSubmittedScore, setHasSubmittedScore] = useState(false);
  const [revealedSolution, setRevealedSolution] = useState(false);
  const [letterHintsUsed, setLetterHintsUsed] = useState(0);
  const [wordHintsUsed, setWordHintsUsed] = useState(0);
  const [values, setValues] = useState<Record<string, string>>({});
  const [incorrectCells, setIncorrectCells] = useState<Record<string, true>>({});
  const [emptyWarningCells, setEmptyWarningCells] = useState<Record<string, true>>({});
  const [hintRevealedCells, setHintRevealedCells] = useState<Record<string, true>>({});
  const [leaderboard, setLeaderboard] = useState<ScoreEntry[]>([]);
  const [friendsLeaderboard, setFriendsLeaderboard] = useState<FriendsLeaderboardEntry[]>([]);
  const [puzzleChallenges, setPuzzleChallenges] = useState<FriendChallengeInfo[]>([]);
  const [hasFriends, setHasFriends] = useState(false);
  const [activeLeaderboardTab, setActiveLeaderboardTab] = useState<LeaderboardTab>(() => {
    const saved = readLocalStorage(LEADERBOARD_TAB_KEY);
    return saved === 'friends' ? 'friends' : 'global';
  });
  const [history, setHistory] = useState<HistoryResponse>({});
  const [usernameModalOpen, setUsernameModalOpen] = useState(false);
  const [username, setUsername] = useState('');
  const [autoCheckFeedback, setAutoCheckFeedback] = useState<AutoCheckFeedback | null>(null);

  const autoCheckTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const runAutoCheckRef = useRef<(() => Promise<CheckResult>) | null>(null);

  const [nav, dispatchNav] = useReducer(navReducer, {
    active: null,
    direction: 'across',
  });

  const puzzleDate = useMemo(() => {
    if (puzzle?.puzzleDate) return puzzle.puzzleDate;
    if (dateParam) return dateParam;
    return getTodayIso();
  }, [dateParam, puzzle?.puzzleDate]);

  const puzzleHash = puzzle?.puzzleHash ?? '';
  const currentPuzzleSize = puzzle ? `${puzzle.width}x${puzzle.height}` : size;

  const fillableCells = useMemo(() => {
    if (!puzzle) return [] as Array<{ row: number; col: number }>;
    const result: Array<{ row: number; col: number }> = [];
    for (let row = 0; row < puzzle.height; row++) {
      for (let col = 0; col < puzzle.width; col++) {
        if (puzzle.cells[row]?.[col] !== null) result.push({ row, col });
      }
    }
    return result;
  }, [puzzle]);

  const clueEntries = useMemo(
    () => (puzzle ? buildClueEntries(puzzle) : { across: [], down: [], byCell: {} }),
    [puzzle],
  );

  const activeEntry = useMemo(() => {
    if (!nav.active) return null;
    const key: CellKey = `${nav.active.row},${nav.active.col}`;
    return findBestEntry(clueEntries.byCell[key], nav.direction, nav.active.row, nav.active.col);
  }, [clueEntries.byCell, nav.active, nav.direction]);

  const filledCount = useMemo(
    () => fillableCells.filter(cell => Boolean(values[`${cell.row},${cell.col}`])).length,
    [fillableCells, values],
  );

  const progressPercent = fillableCells.length > 0 ? Math.round((filledCount / fillableCells.length) * 100) : 0;

  const currentHintSummary =
    letterHintsUsed + wordHintsUsed > 0
      ? `💡 ${letterHintsUsed > 0 ? `${letterHintsUsed} bokstav` : ''}${
          letterHintsUsed > 0 && wordHintsUsed > 0 ? ', ' : ''
        }${wordHintsUsed > 0 ? `${wordHintsUsed} ord` : ''}`
      : '';

  const historyRows = useMemo<HistoryRow[]>(() => {
    const keys = Object.keys(history).sort((a, b) => b.localeCompare(a)).slice(0, 30);
    const rows: HistoryRow[] = [];
    for (const key of keys) {
      const filtered = history[key].filter(e => !e.puzzleSize || e.puzzleSize === size);
      if (filtered.length > 0) rows.push([key, filtered]);
    }
    return rows;
  }, [history, size]);

  const activePuzzleChallenges = useMemo(
    () => puzzleChallenges.filter(c => c.date === puzzleDate && c.puzzleSize === currentPuzzleSize),
    [currentPuzzleSize, puzzleChallenges, puzzleDate],
  );

  const saveProgress = useCallback(
    (nextValues: Record<string, string>, nextSeconds: number, nextLetterHints: number, nextWordHints: number) => {
      if (!puzzleHash || puzzleSolved) return;
      const data = {
        puzzleHash,
        seconds: nextSeconds,
        cells: nextValues,
        letterHintsUsed: nextLetterHints,
        wordHintsUsed: nextWordHints,
        timestamp: Date.now(),
      };
      writeLocalStorage(getProgressKey(puzzleHash), JSON.stringify(data));
    },
    [puzzleHash, puzzleSolved],
  );

  const clearProgress = useCallback(() => {
    if (!puzzleHash) return;
    removeLocalStorage(getProgressKey(puzzleHash));
  }, [puzzleHash]);

  const activateCell = useCallback(
    (row: number, col: number, direction?: ClueEntry['direction']) => {
      if (!puzzle) return;
      if (row < 0 || row >= puzzle.height || col < 0 || col >= puzzle.width) return;
      if (puzzle.cells[row]?.[col] === null) return;
      dispatchNav({ type: 'set-active', cell: { row, col }, direction });
    },
    [puzzle],
  );

  const finishSolvedPuzzle = useCallback((solveSeconds: number, lHints: number, wHints: number) => {
    setPuzzleSolved(true);
    clearProgress();
    setUsernameModalOpen(true);
    setUsername(user?.alias ?? user?.name ?? readLocalStorage('crossword-username') ?? '');

    // Record in local per-device stats
    recordPuzzleSolve(size, solveSeconds);

    // Announce to screen readers
    const hintParts: string[] = [];
    if (lHints > 0) hintParts.push(`${lHints} bokst${lHints > 1 ? 'äver' : 'av'}`);
    if (wHints > 0) hintParts.push(`${wHints} ord`);
    const hintMsg = hintParts.length > 0 ? ` med ${hintParts.join(', ')}` : '';
    announce(`Grattis! Du löste korsordet på ${formatTime(solveSeconds)}${hintMsg}`);
  }, [clearProgress, size, user?.alias, user?.name]);

  const fetchLeaderboard = useCallback(async () => {
    if (!puzzleHash || !puzzleDate) return;
    try {
      const res = await fetch('/api/leaderboard', { credentials: 'same-origin' });
      if (!res.ok) throw new Error('Leaderboard unavailable');
      const data = (await res.json()) as LeaderboardResponse;
      const key = `${puzzleDate}-${puzzleHash}`;
      const entries = [...(data.scores[key] ?? [])].sort((a, b) => a.time - b.time).slice(0, 10);
      setLeaderboard(entries);
      saveLocalLeaderboard(puzzleDate, puzzleHash, entries);
    } catch {
      setLeaderboard(loadLocalLeaderboard(puzzleDate, puzzleHash));
    }
  }, [puzzleDate, puzzleHash]);

  const fetchHistory = useCallback(async () => {
    try {
      const res = await fetch('/api/leaderboard/history?days=30', { credentials: 'same-origin' });
      if (!res.ok) return;
      const data = (await res.json()) as HistoryResponse;
      setHistory(data);
    } catch {
      setHistory({});
    }
  }, []);

  const fetchFriendsLeaderboardData = useCallback(async () => {
    if (!user || !puzzleDate) {
      setFriendsLeaderboard([]);
      setHasFriends(false);
      return;
    }

    try {
      const friends = await fetchFriends();
      setHasFriends(friends.length > 0);
      const entries = await fetchFriendsLeaderboard(puzzleDate, puzzleHash || undefined);
      setFriendsLeaderboard(entries);
    } catch {
      setFriendsLeaderboard([]);
      setHasFriends(false);
    }
  }, [puzzleDate, puzzleHash, user]);

  const fetchPuzzleChallenges = useCallback(async () => {
    if (!user) {
      setPuzzleChallenges([]);
      return;
    }

    try {
      const challenges = await fetchChallenges();
      setPuzzleChallenges(challenges);
    } catch {
      setPuzzleChallenges([]);
    }
  }, [user]);

  useEffect(() => {
    let cancelled = false;

    const loadPuzzle = async () => {
      setLoading(true);
      setPuzzleUnavailable(false);
      setPuzzleNotFound(false);

      const query = `size=${encodeURIComponent(size)}`;
      const url = dateParam ? `/api/puzzle/${dateParam}?${query}` : `/api/puzzle/today?${query}`;

      try {
        const res = await fetch(url, { credentials: 'same-origin' });
        if (cancelled) return;

        if (res.status === 503) {
          setPuzzleUnavailable(true);
          setPuzzle(null);
          setLoading(false);
          return;
        }

        if (res.status === 404 && dateParam) {
          setPuzzleNotFound(true);
          setPuzzle(null);
          setLoading(false);
          return;
        }

        if (!res.ok) {
          setPuzzleUnavailable(true);
          setPuzzle(null);
          setLoading(false);
          return;
        }

        const data = (await res.json()) as PuzzleData;
        setPuzzle(data);
        setSeconds(0);
        setPuzzleSolved(false);
        setHasSubmittedScore(false);
        setRevealedSolution(false);
        setLetterHintsUsed(0);
        setWordHintsUsed(0);
        setValues({});
        setIncorrectCells({});
        setEmptyWarningCells({});
        setHintRevealedCells({});
        setAutoCheckFeedback(null);
        setUsernameModalOpen(false);

        const first = findFirstFillableCell(data);
        if (first) dispatchNav({ type: 'set-active', cell: first, direction: 'across' });

        setLoading(false);
      } catch {
        if (cancelled) return;
        setPuzzleUnavailable(true);
        setPuzzle(null);
        setLoading(false);
      }
    };

    loadPuzzle();
    return () => {
      cancelled = true;
    };
  }, [dateParam, size]);

  useEffect(() => {
    if (!puzzleHash) return;
    try {
      const raw = readLocalStorage(getProgressKey(puzzleHash));
      if (!raw) return;
      const parsed = JSON.parse(raw) as {
        puzzleHash: string;
        seconds?: number;
        cells?: Record<string, string>;
        letterHintsUsed?: number;
        wordHintsUsed?: number;
      };
      if (parsed.puzzleHash !== puzzleHash) return;
      setValues(parsed.cells ?? {});
      setSeconds(parsed.seconds ?? 0);
      setLetterHintsUsed(parsed.letterHintsUsed ?? 0);
      setWordHintsUsed(parsed.wordHintsUsed ?? 0);
    } catch {
      // ignore invalid cache
    }
  }, [puzzleHash]);

  useEffect(() => {
    if (!puzzle || puzzleSolved) return;
    const id = window.setInterval(() => {
      setSeconds(prev => {
        const next = prev + 1;
        if (next % 5 === 0) saveProgress(values, next, letterHintsUsed, wordHintsUsed);
        return next;
      });
    }, 1000);
    return () => window.clearInterval(id);
  }, [letterHintsUsed, puzzle, puzzleSolved, saveProgress, values, wordHintsUsed]);

  useEffect(() => {
    if (!puzzleHash || !puzzleDate) return;
    fetchLeaderboard();
    fetchHistory();
    void fetchFriendsLeaderboardData();
    void fetchPuzzleChallenges();
  }, [fetchFriendsLeaderboardData, fetchHistory, fetchLeaderboard, fetchPuzzleChallenges, puzzleDate, puzzleHash]);

  useEffect(() => {
    if (!user && activeLeaderboardTab === 'friends')
      setActiveLeaderboardTab('global');
  }, [activeLeaderboardTab, user]);

  useEffect(() => {
    writeLocalStorage(LEADERBOARD_TAB_KEY, activeLeaderboardTab);
  }, [activeLeaderboardTab]);

  const clearAutoCheckFeedback = useCallback(() => {
    setAutoCheckFeedback(null);
  }, []);

  const { handleCellChange, handleCellKeyDown } = usePuzzleGridInput({
    puzzle,
    puzzleSolved,
    nav,
    clueEntries,
    activeEntry,
    seconds,
    letterHintsUsed,
    wordHintsUsed,
    activateCell,
    dispatchNav,
    saveProgress,
    setValues,
    setIncorrectCells,
    setEmptyWarningCells,
    clearAutoCheckFeedback,
  });

  const validateAnswers = useCallback(async (): Promise<CheckResult> => {
    if (!puzzle || puzzleSolved) return { status: 'incorrect', emptyCount: 0, incorrectCount: 0 };

    const cells: Record<string, string> = {};
    const empty: Record<string, true> = {};
    for (const c of fillableCells) {
      const key: CellKey = `${c.row},${c.col}`;
      const value = values[key]?.toUpperCase() ?? '';
      if (value) cells[key] = value;
      else empty[key] = true;
    }

    if (Object.keys(empty).length > 0) {
      setIncorrectCells({});
      setEmptyWarningCells(empty);
      return { status: 'incomplete', emptyCount: Object.keys(empty).length, incorrectCount: 0 };
    }

    if (!puzzle.submissionToken || !puzzleDate) {
      setIncorrectCells({});
      setEmptyWarningCells({});
      return { status: 'error', emptyCount: 0, incorrectCount: 0 };
    }

    try {
      const res = await fetch('/api/puzzle/check', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ token: puzzle.submissionToken, puzzleDate, size, cells }),
      });

      if (!res.ok) {
        setIncorrectCells({});
        setEmptyWarningCells({});
        return { status: 'error', emptyCount: 0, incorrectCount: 0 };
      }

      const data = (await res.json()) as { solved: boolean; results: Record<string, boolean> };
      const incorrect: Record<string, true> = {};
      for (const c of fillableCells) {
        const key: CellKey = `${c.row},${c.col}`;
        if (!data.results[key]) incorrect[key] = true;
      }

      setIncorrectCells(incorrect);
      setEmptyWarningCells({});

      if (data.solved) {
        finishSolvedPuzzle(seconds, letterHintsUsed, wordHintsUsed);
        return { status: 'solved', emptyCount: 0, incorrectCount: 0 };
      }

      return {
        status: 'incorrect',
        emptyCount: 0,
        incorrectCount: Object.keys(incorrect).length,
      };
    } catch {
      setIncorrectCells({});
      setEmptyWarningCells({});
      return { status: 'error', emptyCount: 0, incorrectCount: 0 };
    }
  }, [fillableCells, finishSolvedPuzzle, puzzle, puzzleDate, puzzleSolved, size, values]);

  const checkAnswers = useCallback(async (): Promise<CheckResult> => {
    clearAutoCheckFeedback();
    return await validateAnswers();
  }, [clearAutoCheckFeedback, validateAnswers]);

  const runAutoCheck = useCallback(async (): Promise<CheckResult> => {
    const result = await validateAnswers();

    if (result.status === 'incomplete') {
      setAutoCheckFeedback({
        tone: 'info',
        text: `${result.emptyCount} rutor saknas fortfarande.`,
      });
      return result;
    }

    if (result.status === 'incorrect') {
      setAutoCheckFeedback({
        tone: 'error',
        text: `${result.incorrectCount} bokstäver är felaktiga.`,
      });
      return result;
    }

    setAutoCheckFeedback(null);
    return result;
  }, [validateAnswers]);

  // Keep a stable ref to the auto-check runner so the timer always calls the latest version.
  runAutoCheckRef.current = runAutoCheck;

  // Auto-check: when every fillable cell is filled, trigger a passive validation after a short debounce.
  // Uses runAutoCheckRef so the callback is never stale even across re-renders.
  useEffect(() => {
    if (puzzleSolved || fillableCells.length === 0) return;
    const allFilled = fillableCells.every(c => Boolean(values[`${c.row},${c.col}`]));
    if (!allFilled) return;

    if (autoCheckTimerRef.current !== null) clearTimeout(autoCheckTimerRef.current);
    autoCheckTimerRef.current = setTimeout(() => {
      autoCheckTimerRef.current = null;
      void runAutoCheckRef.current?.();
    }, 300);

    return () => {
      if (autoCheckTimerRef.current !== null) clearTimeout(autoCheckTimerRef.current);
    };
  }, [values, fillableCells, puzzleSolved]);

  const clearGrid = useCallback(() => {
    setValues({});
    setIncorrectCells({});
    setEmptyWarningCells({});
    setHintRevealedCells({});
    setLetterHintsUsed(0);
    setWordHintsUsed(0);
    clearAutoCheckFeedback();
    clearProgress();
  }, [clearAutoCheckFeedback, clearProgress]);

  const revealSolution = useCallback(async (): Promise<RevealSolutionResult> => {
    if (!puzzle || puzzleSolved || !puzzle.submissionToken || !puzzleDate) return 'unavailable';

    const allCoords = fillableCells;
    let letters: Record<string, string> = {};

    try {
      const res = await fetch('/api/puzzle/hint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          token: puzzle.submissionToken,
          puzzleDate,
          size,
          cells: allCoords.map(c => [c.row, c.col]),
        }),
      });
      if (!res.ok) return 'unavailable';
      const data = (await res.json()) as { letters: Record<string, string> };
      letters = data.letters;
    } catch {
      return 'unavailable';
    }

    const nextValues: Record<string, string> = {};
    const nextHints: Record<string, true> = {};
    for (const c of allCoords) {
      const key: CellKey = `${c.row},${c.col}`;
      const letter = letters[key]?.toUpperCase() ?? '';
      if (!letter) return 'unavailable';
      nextValues[key] = letter;
      nextHints[key] = true;
    }

    setValues(nextValues);
    setHintRevealedCells(nextHints);
    setIncorrectCells({});
    setEmptyWarningCells({});
    clearAutoCheckFeedback();
    setPuzzleSolved(true);
    setHasSubmittedScore(true);
    setRevealedSolution(true);
    clearProgress();
    return 'ok';
  }, [clearProgress, fillableCells, puzzle, puzzleDate, puzzleSolved, size]);

  const revealLetter = useCallback(async (): Promise<HintActionResult> => {
    if (!puzzle || puzzleSolved || !nav.active) return 'no-active-cell';
    if (!puzzle.submissionToken || !puzzleDate) return 'unavailable';

    const { row, col } = nav.active;
    const key: CellKey = `${row},${col}`;
    let revealed = '';

    try {
      const res = await fetch('/api/puzzle/hint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ token: puzzle.submissionToken, puzzleDate, size, cells: [[row, col]] }),
      });
      if (!res.ok) return 'unavailable';
      const data = (await res.json()) as { letters: Record<string, string> };
      revealed = data.letters[key] ?? '';
    } catch {
      return 'unavailable';
    }

    if (!revealed) return 'unavailable';

    setValues(prev => {
      const next = { ...prev, [key]: revealed };
      saveProgress(next, seconds, letterHintsUsed + 1, wordHintsUsed);
      return next;
    });
    clearAutoCheckFeedback();
    setHintRevealedCells(prev => ({ ...prev, [key]: true }));
    setIncorrectCells(prev => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
    setEmptyWarningCells(prev => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
    setLetterHintsUsed(v => v + 1);
    announce(`Avslöjade: ${revealed}`);
    return 'ok';
  }, [letterHintsUsed, nav.active, puzzle, puzzleDate, puzzleSolved, saveProgress, seconds, size, wordHintsUsed]);

  const revealWord = useCallback(async (): Promise<HintActionResult> => {
    if (!puzzle || puzzleSolved || !nav.active || !activeEntry) return 'no-active-cell';
    if (!puzzle.submissionToken || !puzzleDate) return 'unavailable';

    const targets = activeEntry.cells;
    let letters: Record<string, string> = {};

    try {
      const res = await fetch('/api/puzzle/hint', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({
          token: puzzle.submissionToken,
          puzzleDate,
          size,
          cells: targets.map(c => [c.row, c.col]),
        }),
      });
      if (!res.ok) return 'unavailable';
      const data = (await res.json()) as { letters: Record<string, string> };
      letters = data.letters;
    } catch {
      return 'unavailable';
    }

    const nextHints = wordHintsUsed + 1;
    const resolvedLetters = targets.map(c => ({
      key: `${c.row},${c.col}` as CellKey,
      letter: letters[`${c.row},${c.col}`]?.toUpperCase() ?? '',
    }));

    if (resolvedLetters.some(entry => !entry.letter)) return 'unavailable';

    clearAutoCheckFeedback();
    setValues(prev => {
      const next = { ...prev };
      for (const entry of resolvedLetters) {
        next[entry.key] = entry.letter;
      }
      saveProgress(next, seconds, letterHintsUsed, nextHints);
      return next;
    });

    setHintRevealedCells(prev => {
      const next = { ...prev };
      for (const entry of resolvedLetters) next[entry.key] = true;
      return next;
    });
    setWordHintsUsed(nextHints);
    announce(`Avslöjade helt ord (${resolvedLetters.length} bokstäver)`);
    return 'ok';
  }, [activeEntry, letterHintsUsed, nav.active, puzzle, puzzleDate, puzzleSolved, saveProgress, seconds, size, wordHintsUsed]);

  const generateShareText = useCallback((revealedSolution: boolean): string => {
    const w = puzzle?.width ?? 0;
    const h = puzzle?.height ?? 0;
    const sizeKey = size;
    const date = puzzleDate;
    const puzzleUrl = `https://svensktkorsord.se/app/puzzle?date=${date}&size=${sizeKey}`;

    if (revealedSolution) {
      return `🇸🇪 Svenskt Korsord ${date}\n📐 ${w}×${h}\n\nTesta dagens korsord! 👇\n${puzzleUrl}`;
    }

    const time = formatTime(seconds);
    const lH = letterHintsUsed;
    const wH = wordHintsUsed;
    const hintParts: string[] = [];
    if (lH > 0) hintParts.push(`${lH} bokst${lH > 1 ? 'äver' : 'av'}`);
    if (wH > 0) hintParts.push(`${wH} ord`);

    // Build emoji grid
    let emojiGrid = '';
    if (puzzle) {
      for (let r = 0; r < puzzle.height; r++) {
        let row = '';
        for (let c = 0; c < puzzle.width; c++) {
          if (puzzle.cells[r]?.[c] === null) {
            row += '⬛';
          } else {
            const key: CellKey = `${r},${c}`;
            row += hintRevealedCells[key] ? '🟨' : '🟩';
          }
        }
        emojiGrid += row + '\n';
      }
    }

    let text = `🇸🇪 Svenskt Korsord ${date}\n📐 ${w}×${h}\n⏱️ ${time}\n`;
    text += hintParts.length > 0 ? `💡 ${hintParts.join(', ')}\n` : `🏅 Inga ledtrådar!\n`;
    text += `\n${emojiGrid}\nKan du slå min tid? 👇\n${puzzleUrl}`;
    return text;
  }, [hintRevealedCells, letterHintsUsed, puzzle, puzzleDate, seconds, size, wordHintsUsed]);

  /** Save a pending score to localStorage so it can be restored after login. */
  const savePendingScore = useCallback(() => {
    if (!puzzleHash) return;
    try {
      writeLocalStorage(PENDING_SCORE_KEY, JSON.stringify({
        puzzleHash,
        seconds,
        letterHintsUsed,
        wordHintsUsed,
        date: puzzleDate,
        timestamp: Date.now(),
      }));
    } catch (e) {
      console.warn('Failed to save pending score:', e);
    }
  }, [letterHintsUsed, puzzleDate, puzzleHash, seconds, wordHintsUsed]);

  /** Restore a pending score after the user logs in, if it matches the current puzzle. */
  const checkAndRestorePendingScore = useCallback(() => {
    if (!user || !puzzleHash || hasSubmittedScore) return;
    try {
      const raw = readLocalStorage(PENDING_SCORE_KEY);
      if (!raw) return;
      const pending = JSON.parse(raw) as {
        puzzleHash: string;
        seconds: number;
        letterHintsUsed?: number;
        wordHintsUsed?: number;
        timestamp: number;
      };
      const TEN_MIN = 10 * 60 * 1000;
      if (pending.puzzleHash !== puzzleHash || Date.now() - pending.timestamp >= TEN_MIN) {
        removeLocalStorage(PENDING_SCORE_KEY);
        return;
      }
      removeLocalStorage(PENDING_SCORE_KEY);
      setSeconds(pending.seconds);
      setLetterHintsUsed(pending.letterHintsUsed ?? 0);
      setWordHintsUsed(pending.wordHintsUsed ?? 0);
      // Record local stats for the restored solve (size comes from the hook's size prop)
      recordPuzzleSolve(size, pending.seconds);
      setPuzzleSolved(true);
      setUsernameModalOpen(true);
      setUsername(user.alias ?? user.name ?? readLocalStorage('crossword-username') ?? '');
    } catch (e) {
      console.warn('Failed to restore pending score:', e);
    }
  }, [hasSubmittedScore, puzzleHash, user]);

  // When the user logs in while a pending score exists, restore it.
  useEffect(() => {
    checkAndRestorePendingScore();
  }, [checkAndRestorePendingScore]);

  const submitScore = useCallback(async () => {
    if (!puzzle || !puzzleHash || !puzzleDate || hasSubmittedScore) return false;
    setHasSubmittedScore(true);

    const trimmed = username.trim();
    const name = (trimmed || user?.alias || user?.name || 'Anonym').slice(0, 20);
    writeLocalStorage('crossword-username', name);

    const payload = {
      token: puzzle.submissionToken,
      name,
      time: seconds,
      puzzleHash,
      date: puzzleDate,
      puzzleSize: `${puzzle.width}x${puzzle.height}`,
      hintsUsed: letterHintsUsed,
      wordHintsUsed,
    };

    let submitted = false;
    try {
      const res = await fetch('/api/scores', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify(payload),
      });
      if (res.ok) {
        submitted = true;
        const data = (await res.json()) as { leaderboard?: ScoreEntry[] };
        if (data.leaderboard) {
          const entries = [...data.leaderboard].sort((a, b) => a.time - b.time).slice(0, 10);
          setLeaderboard(entries);
          saveLocalLeaderboard(puzzleDate, puzzleHash, entries);
        }
      }
    } catch {
      submitted = false;
    }

    if (!submitted) {
      const localEntry: ScoreEntry = {
        name,
        time: seconds,
        timestamp: Date.now(),
        puzzleHash,
        hintsUsed: letterHintsUsed,
        wordHintsUsed,
        userId: user?.userId ?? null,
      };
      const local = [...loadLocalLeaderboard(puzzleDate, puzzleHash), localEntry]
        .sort((a, b) => a.time - b.time)
        .slice(0, 10);
      setLeaderboard(local);
      saveLocalLeaderboard(puzzleDate, puzzleHash, local);
    }

    if (submitted)
      void fetchFriendsLeaderboardData();

    setUsernameModalOpen(false);
    return true;
  }, [
    fetchFriendsLeaderboardData,
    hasSubmittedScore,
    letterHintsUsed,
    puzzle,
    puzzleDate,
    puzzleHash,
    seconds,
    user?.alias,
    user?.name,
    user?.userId,
    username,
    wordHintsUsed,
  ]);

  const isClueFilled = useCallback(
    (entry: ClueEntry) => entry.cells.every(cell => Boolean(values[`${cell.row},${cell.col}`])),
    [values],
  );

  return {
    loading,
    puzzleUnavailable,
    puzzleNotFound,
    puzzle,
    puzzleDate,
    seconds,
    puzzleSolved,
    hasSubmittedScore,
    revealedSolution,
    letterHintsUsed,
    wordHintsUsed,
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
    savePendingScore,
    nav,
    clueEntries,
    activeEntry,
    filledCount,
    totalFillableCount: fillableCells.length,
    progressPercent,
    currentHintSummary,
    autoCheckFeedback,
    clearAutoCheckFeedback,
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
    generateShareText,
  };
}

export function formatLeaderboardTime(seconds: number): string {
  return formatTime(seconds);
}
