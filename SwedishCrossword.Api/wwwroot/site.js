// ╔═══════════════════════════════════════════════════════════════════════╗
// ║  Svenskt Korsord — site.js                                           ║
// ║                                                                      ║
// ║  Search for §N to jump to a section.                                 ║
// ╠═══════════════════════════════════════════════════════════════════════╣
// ║  §1  Configuration & Constants                                       ║
// ║  §2  Global State                                                    ║
// ║  §3  Theme (Dark Mode)                                               ║
// ║  §4  Cell–Clue Mapping & Utilities                                   ║
// ║  §5  Anti-Cheat & DevTools Detection                                 ║
// ║  §6  Player Statistics                                               ║
// ║  §7  Progress Persistence                                            ║
// ║  §8  Leaderboard & Score Submission                                  ║
// ║  §9  Modals & Puzzle Loading                                         ║
// ║  §10 Grid Rendering                                                ║
// ║  §11 Input Handling & Navigation                                     ║
// ║  §12 Clue Navigation & Highlighting                                  ║
// ║  §13 Answer Checking, Hints & Sharing                                ║
// ║  §14 Timer, Stats & Layout                                           ║
// ║  §15 Keyboard Shortcuts & Mobile Panels                              ║
// ╚═══════════════════════════════════════════════════════════════════════╝

// ═══════════════════════════════════════════════════════════════════════
// §1  Configuration & Constants
// ═══════════════════════════════════════════════════════════════════════

/*
 * Leaderboard: uses the API backend for storage.
 * Falls back to localStorage when the API is unreachable.
 */

const LEADERBOARD_PROXY_URL = '/api';

const LEADERBOARD_ENABLED = true;

// ── Global fetch interceptor: graceful "DB unavailable" handling ──
// Backend returns HTTP 503 with body {"code":"db_unavailable", ...} when
// Azure SQL is temporarily unavailable (for example during transient faults,
// failover, or short service disruptions). Show a single dismissible banner so
// users know that puzzle play still works while leaderboard, stats, friends
// etc. are temporarily disabled. Done globally so every fetch call site
// benefits without code churn.
(function installDbUnavailableInterceptor() {
    if (window.__dbBannerInstalled) return;
    window.__dbBannerInstalled = true;
    const originalFetch = window.fetch.bind(window);

    function showBanner() {
        if (document.getElementById('db-unavailable-banner')) return;
        // Lazy CSS injection — avoids touching shared stylesheets.
        if (!document.getElementById('db-unavailable-style')) {
            const style = document.createElement('style');
            style.id = 'db-unavailable-style';
            style.textContent =
                '#db-unavailable-banner{position:fixed;top:0;left:0;right:0;z-index:9999;' +
                'background:#fff3cd;color:#664d03;border-bottom:1px solid #ffe69c;' +
                'padding:.6rem 1rem;font:14px/1.4 system-ui,sans-serif;' +
                'display:flex;gap:1rem;align-items:center;justify-content:center;' +
                'box-shadow:0 1px 4px rgba(0,0,0,.08)}' +
                '#db-unavailable-banner button{background:transparent;border:0;' +
                'color:#664d03;font-size:1.1rem;cursor:pointer;padding:0 .25rem;line-height:1}' +
                '@media (prefers-color-scheme: dark){' +
                '#db-unavailable-banner{background:#3a2f00;color:#ffe69c;border-bottom-color:#665200}' +
                '#db-unavailable-banner button{color:#ffe69c}}';
            document.head.appendChild(style);
        }
        const banner = document.createElement('div');
        banner.id = 'db-unavailable-banner';
        banner.setAttribute('role', 'status');
        banner.setAttribute('aria-live', 'polite');
        banner.innerHTML =
            '<span>⚠️ Resultatlistor och statistik är tillfälligt otillgängliga – pussel fungerar som vanligt.</span>' +
            '<button type="button" aria-label="Stäng">×</button>';
        banner.querySelector('button').addEventListener('click', () => banner.remove());
        (document.body || document.documentElement).appendChild(banner);
    }

    window.fetch = async function patchedFetch(...args) {
        const response = await originalFetch(...args);
        if (response.status === 503) {
            // Clone so callers can still consume the body.
            try {
                const peek = response.clone();
                const ct = peek.headers.get('content-type') || '';
                if (ct.includes('json')) {
                    const body = await peek.json();
                    if (body && body.code === 'db_unavailable') {
                        window.dbUnavailable = true;
                        showBanner();
                    }
                }
            } catch (_) { /* ignore body-parse errors — never break the fetch */ }
        }
        return response;
    };
})();

// Anti-cheat settings
const ANTI_CHEAT = {
    // Minimum seconds required to complete (based on ~2 letters per seconds for fast typers)
    minTimePerCell: 0.3,
    // Maximum time between inputs to count as "human" (ms) - detects paste/automation
    maxInputInterval: 50,
    // Minimum unique input events required (prevents single paste)
    minInputEvents: 5,
    // Enable/disable anti-cheat (set to false for testing)
    enabled: true
};

// Friendly labels for each puzzle size (used in stats, leaderboard, headings)
const SIZE_LABELS = {
    '10x10': 'Liten (10×10)',
    '15x15': 'Mellan (15×15)',
    '17x17': 'Stor (17×17)'
};
const SIZE_ICONS = { '10x10': '🟢', '15x15': '🟡', '17x17': '🔴' };
function getSizeLabel(sizeKey) { return SIZE_LABELS[sizeKey] || sizeKey; }
function getSizeIcon(sizeKey) { return SIZE_ICONS[sizeKey] || '⬜'; }

// Stats & leaderboard reset date — data before this date is discarded
const STATS_RESET_DATE = '2026-04-14';

// ── Auth state ──
let authUser = null;
async function fetchAuthUser() {
    try {
        const res = await fetch('/api/auth/me', { credentials: 'same-origin', signal: AbortSignal.timeout(10000) });
        if (res.ok) {
            const data = await res.json();
            if (data.authenticated) { authUser = data; return; }
        }
    } catch (e) { console.warn('Auth check failed:', e); }
    authUser = null;
}

function renderAuthButton() {
    document.querySelectorAll('.auth-btn-container').forEach(container => {
        container.innerHTML = '';
        if (authUser) {
            const displayName = escapeHtml(authUser.name || 'Inloggad');
            container.innerHTML =
                `<a href="/profile.html" class="auth-user-name" title="Min profil: ${displayName}">${displayName}</a>` +
                `<button class="auth-btn auth-btn-logout" onclick="doLogout()">Logga ut</button>`;
        } else {
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            container.innerHTML =
                `<button class="auth-btn" onclick="showLoginMenu(this)">Logga in</button>` +
                `<div class="auth-login-menu">` +
                `<a href="/api/auth/login/google?returnUrl=${returnUrl}">Logga in med Google</a>` +
                `<a href="/api/auth/login/microsoft?returnUrl=${returnUrl}">Logga in med Microsoft</a></div>`;
        }
    });
}

function showLoginMenu(btn) {
    const menu = btn.parentElement.querySelector('.auth-login-menu');
    if (!menu) return;
    menu.classList.toggle('auth-menu-open');
    if (menu.classList.contains('auth-menu-open')) {
        setTimeout(() => {
            document.addEventListener('click', function close(e) {
                if (!menu.contains(e.target) && e.target !== btn) {
                    menu.classList.remove('auth-menu-open');
                    document.removeEventListener('click', close);
                }
            });
        }, 0);
    }
}

async function doLogout() {
    try { await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }); } catch {}
    authUser = null;
    renderAuthButton();
}

// Default puzzle data (used if puzzle.json fails to load)
let puzzleData = {
    width: 11,
    height: 11,
    wordCount: 2,
    fillPercentage: 10.0,
    createdAt: null,
    cells: [
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [{num: 1, letter: 'K'}, {letter: 'A'}, {num: 2, letter: 'L'}, {letter: 'S'}, {letter: 'O'}, {letter: 'N'}, {letter: 'G'}, {letter: 'E'}, {letter: 'R'}, null, null],
        [null, null, {letter: 'Ö'}, null, null, null, null, null, null, null, null],
        [null, null, {letter: 'V'}, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
        [null, null, null, null, null, null, null, null, null, null, null],
    ],
    clues: {
        across: [{ number: 1, clue: "Underkläder för män", answer: "KALSONGER" }],
        down: [{ number: 2, clue: "Träd med gröna blad", answer: "LÖV" }]
    }
};

// ═══════════════════════════════════════════════════════════════════════
// §2  Global State
// ═══════════════════════════════════════════════════════════════════════

let timerInterval, seconds = 0, puzzleSolved = false, currentDirection = 'across';
let currentPuzzleDate = null;
let hasSubmittedScore = false;
let remoteLeaderboardCache = null;

// Anti-cheat tracking variables
let inputEvents = [];
let puzzleStartTime = null;
let puzzleHash = null;
let usedShowSolution = false;
let suspiciousActivity = [];
let HAS_VIEWED_SOLUTION = false; // Tracked via localStorage
let letterHintsUsed = 0;
let wordHintsUsed = 0;

const FOCUS_DEBOUNCE_MS = 50;
let _autoCheckTimer = null;

// Auto-check: trigger checkAnswers() when every cell is filled.
// Uses a short debounce so the player can correct a mistyped last letter.
function autoCheckIfComplete() {
    if (puzzleSolved) return;
    clearTimeout(_autoCheckTimer);
    const allInputs = document.querySelectorAll('.cell:not(.blocked) input');
    if (!Array.from(allInputs).every(i => i.value.trim() !== '')) return;
    _autoCheckTimer = setTimeout(() => checkAnswers(), 300);
}

// Format hint summary text: "2 bokstäver, 1 ord" / "3 bokstäver" / "1 ord"
function formatHintSummary(letters, words) {
    const parts = [];
    if (letters > 0) parts.push(`${letters} bokst${letters > 1 ? 'äver' : 'av'}`);
    if (words > 0) parts.push(`${words} ord`);
    return parts.join(', ');
}

// Format hint badge HTML for leaderboard entries
function formatHintBadge(letters, words) {
    const l = letters || 0;
    const w = words || 0;
    if (l === 0 && w === 0) return '';
    const tooltip = formatHintSummary(l, w);
    return `<span class="hint-badge" title="${tooltip}">💡</span>`;
}

// Difficulty key → CSS class
const DIFFICULTY_CLASSES = { hard: 'difficulty-hard', medium: 'difficulty-medium', easy: 'difficulty-easy' };
// Difficulty key → display label
const DIFFICULTY_LABELS = { hard: 'Svår', medium: 'Medel', easy: 'Lätt' };
function getDifficultyClass(key) { return DIFFICULTY_CLASSES[key] || ''; }
function getDifficultyLabel(key) { return DIFFICULTY_LABELS[key] || key || ''; }

// Re-entrancy guard for handleFocus to prevent focus loops
let lastFocusedCell = null;
let lastFocusTime = 0;

// ═══════════════════════════════════════════════════════════════════════
// §3  Theme (Dark Mode)
// ═══════════════════════════════════════════════════════════════════════

function initTheme() {
    const saved = localStorage.getItem('crossword-theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    const theme = saved || (prefersDark ? 'dark' : 'light');
    applyTheme(theme);
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', e => {
        if (!localStorage.getItem('crossword-theme')) applyTheme(e.matches ? 'dark' : 'light');
    });
}

function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    const btn = document.getElementById('theme-toggle');
    if (btn) btn.textContent = theme === 'dark' ? '☀️' : '🌙';
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') || 'light';
    const next = current === 'dark' ? 'light' : 'dark';
    localStorage.setItem('crossword-theme', next);
    applyTheme(next);
}

// Apply theme immediately (before DOMContentLoaded) to prevent flash
initTheme();

// ═══════════════════════════════════════════════════════════════════════
// §4  Cell–Clue Mapping & Utilities
// ═══════════════════════════════════════════════════════════════════════

// Lookup: maps "row,col" -> array of { number, direction, cells, clueIndex }
// Built from puzzleData.clues so that bent words are correctly associated
// with all their cells (not just straight-line neighbours).
let cellClueMap = {};

// Find the best clue entry for a cell given the desired direction.
// Prefers entries whose *local* direction at the cell (based on neighbouring
// cells in the word path) matches, so that a vinkelord's down-segment doesn't
// steal focus from a regular across word sharing the same cell.
function findBestEntry(entries, direction, row, col) {
    if (!entries || entries.length === 0) return null;
    // Prefer an entry whose local direction at this cell matches
    let match = entries.find(e => {
        const idx = e.cells.findIndex(c => c.row === row && c.col === col);
        if (idx < 0 || e.cells.length < 2) return false;
        const ref = idx < e.cells.length - 1 ? e.cells[idx + 1] : e.cells[idx - 1];
        const localDir = ref.row === row ? 'across' : 'down';
        return localDir === direction;
    });
    if (match) return match;
    // Fall back to nominal clue direction
    match = entries.find(e => e.direction === direction);
    return match || entries[0];
}

// Build the cell-to-clue lookup from puzzleData.clues
function buildCellClueMap() {
    cellClueMap = {};
    ['across', 'down'].forEach(dir => {
        (puzzleData.clues[dir] || []).filter(c => c.number > 0).forEach((clue, idx) => {
            let cells;
            if (clue.cells && clue.cells.length > 0) {
                cells = clue.cells.map(c => ({ row: c[0], col: c[1] }));
            } else {
                cells = getWordCellsFallback(clue.number, dir);
            }
            if (!cells) return;
            const entry = { number: clue.number, direction: dir, cells, clueIndex: idx };
            cells.forEach(c => {
                const key = `${c.row},${c.col}`;
                if (!cellClueMap[key]) cellClueMap[key] = [];
                cellClueMap[key].push(entry);
            });
        });
    });
}

// Straight-line version of getWordCells used only for building the lookup
// when clue.cells is missing.
function getWordCellsFallback(number, direction) {
    let startRow = -1, startCol = -1;
    outer: for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            if (puzzleData.cells[row]?.[col]?.num === number) { startRow = row; startCol = col; break outer; }
        }
    }
    if (startRow < 0) return null;
    const cells = [];
    if (direction === 'across') {
        for (let c = startCol; c < puzzleData.width; c++) {
            if (puzzleData.cells[startRow]?.[c] === null) break;
            cells.push({ row: startRow, col: c });
        }
    } else {
        for (let r = startRow; r < puzzleData.height; r++) {
            if (puzzleData.cells[r]?.[startCol] === null) break;
            cells.push({ row: r, col: startCol });
        }
    }
    return cells;
}

// Announce messages to screen readers via ARIA live region
function announce(message) {
    const el = document.getElementById('announcements');
    if (el) {
        el.textContent = message;
        // Clear after a delay to allow re-announcing the same message
        setTimeout(() => { el.textContent = ''; }, 1000);
    }
}

// Throttle utility for performance-sensitive event handlers
function throttle(func, limit) {
    let lastCall = 0;
    let timeout = null;
    return function(...args) {
        const now = Date.now();
        const remaining = limit - (now - lastCall);
        
        if (remaining <= 0) {
            if (timeout) {
                clearTimeout(timeout);
                timeout = null;
            }
            lastCall = now;
            func.apply(this, args);
        } else if (!timeout) {
            timeout = setTimeout(() => {
                lastCall = Date.now();
                timeout = null;
                func.apply(this, args);
            }, remaining);
        }
    };
}

// ═══════════════════════════════════════════════════════════════════════
// §5  Anti-Cheat & DevTools Detection
// ═══════════════════════════════════════════════════════════════════════

// DevTools detection flag (shared with ES module above)
// Check if the ES module already set the flag before this script ran
let devToolsOpenedDuringSession = window.devToolsOpenedDuringSession || false;

// Sync the global variable for the ES module to update
// Use a backing variable to preserve state
Object.defineProperty(window, 'devToolsOpenedDuringSession', {
    get() { return devToolsOpenedDuringSession; },
    set(value) { 
        devToolsOpenedDuringSession = value;
        if (value) {
            console.log('DevTools detection flag set to true');
        }
    }
});

// Fallback DevTools detection (in case ES module doesn't load)
// Uses size-based detection as backup
const devToolsDetector = {
    isOpen: false,
    
    check() {
        const widthThreshold = window.outerWidth - window.innerWidth > 160;
        const heightThreshold = window.outerHeight - window.innerHeight > 160;
        
        if (
            !(heightThreshold && widthThreshold) &&
            (widthThreshold || heightThreshold)
        ) {
            if (!this.isOpen) {
                this.isOpen = true;
                devToolsOpenedDuringSession = true;
            }
        } else {
            this.isOpen = false;
        }
    },
    
    startMonitoring() {
        // Check periodically as fallback
        setInterval(() => this.check(), 1000);
        window.addEventListener('resize', () => this.check());
        this.check();
    }
};

// Generate a simple hash of the puzzle for verification
function generatePuzzleHash() {
    let str = '';
    for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            const cell = puzzleData.cells[row]?.[col];
            str += cell ? cell.letter : '#';
        }
    }
    // Simple hash function
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
        const char = str.charCodeAt(i);
        hash = ((hash << 5) - hash) + char;
        hash = hash & hash;
    }
    return hash.toString(36);
}

// Track input events for anti-cheat analysis
function trackInput(row, col, value) {
    const now = Date.now();
    inputEvents.push({
        time: now,
        row,
        col,
        value,
        interval: inputEvents.length > 0 ? now - inputEvents[inputEvents.length - 1].time : 0
    });
}

// Track when user views the solution (localStorage)
function trackSolutionView() {
    if (!puzzleHash) return;
    try {
        localStorage.setItem(`solution-viewed-${puzzleHash}`, Date.now().toString());
    } catch (e) {
        console.warn('Failed to track solution view:', e);
    }
}

// Check if user has viewed the solution before (localStorage)
function checkIfViewedSolution() {
    if (!puzzleHash) return false;
    try {
        return !!localStorage.getItem(`solution-viewed-${puzzleHash}`);
    } catch (e) {
        return false;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §6  Player Statistics
// ═══════════════════════════════════════════════════════════════════════

const PLAYER_STATS_KEY = 'playerStats';
const LOCAL_STORAGE_RESET_KEY = 'dataResetDate';

// One-time localStorage purge: remove old leaderboard, progress, and solution-
// viewed entries that predate STATS_RESET_DATE.  Runs once per reset cycle.
function purgeStaleLocalStorage() {
    try {
        if (localStorage.getItem(LOCAL_STORAGE_RESET_KEY) === STATS_RESET_DATE) return;

        const keysToRemove = [];
        for (let i = 0; i < localStorage.length; i++) {
            const key = localStorage.key(i);
            // crossword-leaderboard-YYYY-MM-DD-{hash}
            const lbMatch = key.match(/^crossword-leaderboard-(\d{4}-\d{2}-\d{2})/);
            if (lbMatch && lbMatch[1] < STATS_RESET_DATE) { keysToRemove.push(key); continue; }
            // crossword-progress-{hash}  — no date in key, but clear all stale progress
            if (key.startsWith('crossword-progress-')) { keysToRemove.push(key); continue; }
            // solution-viewed-{hash}
            if (key.startsWith('solution-viewed-')) { keysToRemove.push(key); continue; }
        }
        keysToRemove.forEach(k => localStorage.removeItem(k));
        localStorage.setItem(LOCAL_STORAGE_RESET_KEY, STATS_RESET_DATE);

        if (keysToRemove.length > 0) {
            console.log(`Purged ${keysToRemove.length} stale localStorage entries (reset date: ${STATS_RESET_DATE})`);
        }
    } catch (e) {
        console.warn('Failed to purge stale localStorage:', e);
    }
}
purgeStaleLocalStorage();

function defaultSizeStats() {
    return { totalSolved: 0, currentStreak: 0, bestStreak: 0, bestTime: null, totalTime: 0, lastSolvedDate: null, solvedDates: [] };
}

function loadPlayerStats() {
    try {
        const raw = localStorage.getItem(PLAYER_STATS_KEY);
        if (raw) {
            const stats = JSON.parse(raw);

            // Reset stats if they predate the reset date
            if (!stats.resetDate || stats.resetDate < STATS_RESET_DATE) {
                const fresh = { sizes: {}, resetDate: STATS_RESET_DATE };
                savePlayerStats(fresh);
                return fresh;
            }

            // Migrate legacy flat format → per-size format
            if (!stats.sizes) {
                const legacy = {
                    totalSolved: stats.totalSolved || 0,
                    currentStreak: stats.currentStreak || 0,
                    bestStreak: stats.bestStreak || 0,
                    bestTime: stats.bestTime ?? null,
                    totalTime: stats.totalTime || 0,
                    lastSolvedDate: stats.lastSolvedDate || null,
                    solvedDates: stats.solvedDates || []
                };
                stats.sizes = { '17x17': legacy };
                delete stats.totalSolved; delete stats.currentStreak;
                delete stats.bestStreak; delete stats.bestTime;
                delete stats.totalTime; delete stats.lastSolvedDate;
                delete stats.solvedDates;
                savePlayerStats(stats);
            }
            return stats;
        }
    } catch (e) {
        console.warn('Failed to load player stats:', e);
    }
    return { sizes: {}, resetDate: STATS_RESET_DATE };
}

function getStatsForSize(stats, sizeKey) {
    if (!stats.sizes[sizeKey]) stats.sizes[sizeKey] = defaultSizeStats();
    return stats.sizes[sizeKey];
}

function savePlayerStats(stats) {
    try {
        localStorage.setItem(PLAYER_STATS_KEY, JSON.stringify(stats));
    } catch (e) {
        console.warn('Failed to save player stats:', e);
    }
}

function recordPuzzleSolve(solveTimeSeconds) {
    const stats = loadPlayerStats();
    const sizeKey = getPuzzleSize();
    const s = getStatsForSize(stats, sizeKey);
    const todayStr = new Date().toISOString().split('T')[0];

    // Avoid double-recording the same date for this size
    if (s.solvedDates.includes(todayStr)) return stats;

    s.totalSolved = (s.totalSolved || 0) + 1;
    s.totalTime = (s.totalTime || 0) + solveTimeSeconds;

    if (s.bestTime === null || solveTimeSeconds < s.bestTime) {
        s.bestTime = solveTimeSeconds;
    }

    // Streak logic: check if yesterday was solved for THIS size
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    const yesterdayStr = yesterday.toISOString().split('T')[0];

    if (s.lastSolvedDate === yesterdayStr) {
        s.currentStreak = (s.currentStreak || 0) + 1;
    } else if (s.lastSolvedDate === todayStr) {
        // Already counted today — keep streak as is
    } else {
        s.currentStreak = 1;
    }

    s.bestStreak = Math.max(s.bestStreak || 0, s.currentStreak);
    s.lastSolvedDate = todayStr;

    // Keep last 90 dates for history
    if (!s.solvedDates.includes(todayStr)) {
        s.solvedDates.push(todayStr);
    }
    if (s.solvedDates.length > 90) {
        s.solvedDates = s.solvedDates.slice(-90);
    }

    savePlayerStats(stats);
    return stats;
}

function renderPlayerStats() {
    const panel = document.getElementById('player-stats');
    if (!panel) return;

    const stats = loadPlayerStats();
    const todayStr = new Date().toISOString().split('T')[0];
    const currentSize = getPuzzleSize();

    // Collect all sizes that have any data, plus the current size
    const allSizes = new Set(Object.keys(stats.sizes));
    allSizes.add(currentSize);
    const orderedSizes = [...allSizes].sort();

    let html = '';
    for (const sizeKey of orderedSizes) {
        const s = getStatsForSize(stats, sizeKey);
        const isCurrent = sizeKey === currentSize;

        // Recompute current streak based on today's date in case it's stale
        if (s.lastSolvedDate && s.lastSolvedDate !== todayStr) {
            const yesterday = new Date();
            yesterday.setDate(yesterday.getDate() - 1);
            if (s.lastSolvedDate !== yesterday.toISOString().split('T')[0]) {
                s.currentStreak = 0;
            }
        }

        const avgTime = s.totalSolved > 0 ? Math.round(s.totalTime / s.totalSolved) : 0;

        const label = getSizeLabel(sizeKey);
        const icon = getSizeIcon(sizeKey);
        const badge = isCurrent ? ' <span class="stats-size-badge">spelar nu</span>' : '';

        html += `
        <div class="stats-size-block${isCurrent ? ' stats-size-current' : ''}" data-size="${sizeKey}">
            <h3 class="stats-size-heading">${icon} ${label}${badge}</h3>
            <div class="player-stats-grid">
                <div class="stat-item">
                    <span class="stat-value">${s.totalSolved}</span>
                    <span class="stat-label">Lösta</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value">${s.currentStreak}</span>
                    <span class="stat-label">Streak</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value">${s.bestStreak}</span>
                    <span class="stat-label">Bästa streak</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value">${s.bestTime !== null ? formatTime(s.bestTime) : '--:--'}</span>
                    <span class="stat-label">Bästa tid</span>
                </div>
                <div class="stat-item">
                    <span class="stat-value">${avgTime > 0 ? formatTime(avgTime) : '--:--'}</span>
                    <span class="stat-label">Snittid</span>
                </div>
            </div>
        </div>`;
    }

    panel.innerHTML = html;
}

// Render server-side personal stats for signed-in users (synced across devices)
async function renderPersonalStats() {
    const panel = document.getElementById('personal-stats');
    if (!panel) return;
    if (!authUser || !authUser.userId) {
        panel.style.display = 'none';
        return;
    }
    panel.style.display = '';
    panel.innerHTML = '<p class="stats-loading">Laddar dina sparade resultat...</p>';

    try {
        const res = await fetch('/api/auth/my-stats', { credentials: 'same-origin', signal: AbortSignal.timeout(10000) });
        if (!res.ok) { panel.style.display = 'none'; return; }
        const stats = await res.json();
        if (!stats || stats.totalSolved === 0) {
            panel.innerHTML = '<p class="stats-empty">Du har inga sparade resultat ännu. Lös ett korsord medan du är inloggad!</p>';
            return;
        }

        let html = `
        <h3 class="personal-stats-heading">📊 Dina resultat (alla enheter)</h3>
        <div class="player-stats-grid">
            <div class="stat-item">
                <span class="stat-value">${stats.totalSolved}</span>
                <span class="stat-label">Lösta</span>
            </div>
            <div class="stat-item">
                <span class="stat-value">${stats.currentStreak}</span>
                <span class="stat-label">Streak</span>
            </div>
            <div class="stat-item">
                <span class="stat-value">${stats.bestStreak}</span>
                <span class="stat-label">Bästa streak</span>
            </div>
            <div class="stat-item">
                <span class="stat-value">${stats.bestTime > 0 ? formatTime(stats.bestTime) : '--:--'}</span>
                <span class="stat-label">Bästa tid</span>
            </div>
            <div class="stat-item">
                <span class="stat-value">${stats.averageTime > 0 ? formatTime(stats.averageTime) : '--:--'}</span>
                <span class="stat-label">Snittid</span>
            </div>
        </div>`;

        if (stats.perSize && Object.keys(stats.perSize).length > 0) {
            html += '<h4 class="personal-stats-subheading">Statistik per storlek</h4><div class="player-stats-grid">';
            for (const [size, s] of Object.entries(stats.perSize).sort((a, b) => a[0].localeCompare(b[0]))) {
                const icon = getSizeIcon(size);
                const label = getSizeLabel(size);
                html += `<div class="stat-item"><span class="stat-value">${icon} ${formatTime(s.bestTime)}</span><span class="stat-label">${label} bästa</span></div>`;
                html += `<div class="stat-item"><span class="stat-value">${formatTime(s.averageTime)}</span><span class="stat-label">${label} snitt (${s.count}st)</span></div>`;
                html += `<div class="stat-item"><span class="stat-value">${s.currentStreak}</span><span class="stat-label">${label} streak</span></div>`;
                html += `<div class="stat-item"><span class="stat-value">${s.bestStreak}</span><span class="stat-label">${label} bästa streak</span></div>`;
            }
            html += '</div>';
        }

        if (stats.recentSolves && stats.recentSolves.length > 0) {
            html += '<h4 class="personal-stats-subheading">Senaste resultat</h4><ul class="personal-recent-list">';
            for (const s of stats.recentSolves.slice(0, 10)) {
                const sizeLabel = s.puzzleSize ? getSizeLabel(s.puzzleSize) : '';
                const hintInfo = (s.hintsUsed || s.wordHintsUsed) ? ` 💡${s.hintsUsed + s.wordHintsUsed}` : '';
                html += `<li>${s.date} — ${formatTime(s.time)}${sizeLabel ? ' — ' + sizeLabel : ''}${hintInfo}</li>`;
            }
            html += '</ul>';
        }

        if (stats.badges && stats.badges.length > 0) {
            html += '<h4 class="personal-stats-subheading">🏅 Prestationer</h4><ul class="personal-recent-list">';
            for (const badge of stats.badges) {
                const state = badge.unlocked ? 'upplåst' : 'låst';
                html += `<li>${badge.icon || '🏅'} ${badge.name} — ${state}</li>`;
            }
            html += '</ul>';
        }

        panel.innerHTML = html;
    } catch (e) {
        console.warn('Failed to load personal stats:', e);
        panel.style.display = 'none';
    }
}


    // Check 1: Minimum time threshold
    const minTime = cellCount * ANTI_CHEAT.minTimePerCell;
    if (seconds < minTime) {
        reasons.push(`För snabb: ${seconds}s < ${Math.round(minTime)}s minimum`);
    }

    // Check 2: Too few input events (suggests paste/automation)
    if (inputEvents.length < ANTI_CHEAT.minInputEvents) {
        reasons.push(`För få inmatningar: ${inputEvents.length} < ${ANTI_CHEAT.minInputEvents}`);
    }

    // Check 3: Suspiciously fast consecutive inputs
    const fastInputs = inputEvents.filter(e => e.interval > 0 && e.interval < ANTI_CHEAT.maxInputInterval);
    if (fastInputs.length > cellCount * 0.5) {
        reasons.push(`Misstänkt automatisering: ${fastInputs.length} snabba inmatningar`);
    }

    // Check 4: Used show solution
    if (usedShowSolution) {
        reasons.push('Använde "Visa lösning"');
    }

    // Check 5: All inputs at exactly the same interval (bot pattern)
    if (inputEvents.length > 10) {
        const intervals = inputEvents.slice(1).map(e => e.interval);
        const avgInterval = intervals.reduce((a, b) => a + b, 0) / intervals.length;
        const variance = intervals.reduce((sum, i) => sum + Math.pow(i - avgInterval, 2), 0) / intervals.length;
        if (variance < 100 && avgInterval < 200) {
            reasons.push('Misstänkt bot: konstant inmatningshastighet');
        }
    }

    // Check 6: DevTools was opened during the session
    const devToolsWasOpened = window.devToolsOpenedDuringSession || devToolsOpenedDuringSession;
    if (devToolsWasOpened) {
        reasons.push('DevTools öppnades under sessionen');
    }

    // Check 7: Previously viewed solution (checked via localStorage)
    if (HAS_VIEWED_SOLUTION) {
        reasons.push('Lösningen visades tidigare');
    }

    return {
        valid: reasons.length === 0,
        reasons
    };
}

// Count fillable cells (prefer server-provided cellCount)
function countCells() {
    if (puzzleData.cellCount) return puzzleData.cellCount;
    let count = 0;
    for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            if (puzzleData.cells[row]?.[col] !== null) count++;
        }
    }
    return count;
}

// Create a signed score entry
function createScoreEntry(username, timeSeconds) {
    const entry = {
        name: username,
        time: timeSeconds,
        timestamp: Date.now(),
        puzzleHash: puzzleHash,
        inputCount: inputEvents.length,
        // Simple signature to detect tampering
        sig: btoa(JSON.stringify({
            n: username.substring(0, 3),
            t: timeSeconds,
            h: puzzleHash,
            c: inputEvents.length
        })).substring(0, 16)
    };
    return entry;
}

// Validate a score entry
function validateScoreEntry(entry) {
    if (!entry.sig || !entry.puzzleHash) return true; // Legacy entries
    
    try {
        const expectedSig = btoa(JSON.stringify({
            n: entry.name.substring(0, 3),
            t: entry.time,
            h: entry.puzzleHash,
            c: entry.inputCount || 0
        })).substring(0, 16);
        return entry.sig === expectedSig;
    } catch {
        return false;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §7  Progress Persistence
// ═══════════════════════════════════════════════════════════════════════

// Leaderboard key for localStorage - includes puzzle hash for uniqueness
function getLeaderboardKey() {
    const hashSuffix = puzzleHash ? `-${puzzleHash}` : '';
    return `crossword-leaderboard-${currentPuzzleDate || 'default'}${hashSuffix}`;
}

// Progress caching key - unique per puzzle
function getProgressKey() {
    return `crossword-progress-${puzzleHash || 'default'}`;
}

// Save current cell values and timer to localStorage
function saveProgress() {
    if (puzzleSolved || !puzzleHash) return;
    try {
        const cells = {};
        document.querySelectorAll('.cell:not(.blocked) input').forEach(input => {
            const cell = input.parentElement;
            const key = `${cell.dataset.row},${cell.dataset.col}`;
            if (input.value) cells[key] = input.value;
        });
        const data = {
            puzzleHash,
            seconds,
            cells,
            letterHintsUsed,
            wordHintsUsed,
            timestamp: Date.now()
        };
        localStorage.setItem(getProgressKey(), JSON.stringify(data));
    } catch (e) {
        console.warn('Failed to save progress:', e);
    }
}

// Load saved progress from localStorage and restore cell values + timer
function loadProgress() {
    if (!puzzleHash) return false;
    try {
        const raw = localStorage.getItem(getProgressKey());
        if (!raw) return false;
        const data = JSON.parse(raw);
        if (data.puzzleHash !== puzzleHash) {
            localStorage.removeItem(getProgressKey());
            return false;
        }
        // Restore cell values
        if (data.cells) {
            for (const [key, value] of Object.entries(data.cells)) {
                const [row, col] = key.split(',');
                const input = document.querySelector(`.cell[data-row="${row}"][data-col="${col}"] input`);
                if (input) input.value = value;
            }
        }
        // Restore timer
        if (typeof data.seconds === 'number' && data.seconds > 0) {
            seconds = data.seconds;
            document.getElementById('timer').textContent = formatTime(seconds);
        }
        // Restore hints
        if (typeof data.letterHintsUsed === 'number') {
            letterHintsUsed = data.letterHintsUsed;
        }
        if (typeof data.wordHintsUsed === 'number') {
            wordHintsUsed = data.wordHintsUsed;
        }
        // Backward compat: old saves only had hintsUsed
        if (typeof data.hintsUsed === 'number' && !data.letterHintsUsed && !data.wordHintsUsed) {
            letterHintsUsed = data.hintsUsed;
        }
        return true;
    } catch (e) {
        console.warn('Failed to load progress:', e);
        return false;
    }
}

// Clear saved progress from localStorage
function clearProgress() {
    try {
        localStorage.removeItem(getProgressKey());
    } catch (e) {
        console.warn('Failed to clear progress:', e);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §8  Leaderboard & Score Submission
// ═══════════════════════════════════════════════════════════════════════

// Load leaderboard from localStorage (fallback/cache)
function loadLocalLeaderboard() {
    try {
        const data = localStorage.getItem(getLeaderboardKey());
        const leaderboard = data ? JSON.parse(data) : [];
        return leaderboard.filter(validateScoreEntry);
    } catch (e) {
        console.error('Error loading local leaderboard:', e);
        return [];
    }
}

// Save leaderboard to localStorage
function saveLocalLeaderboard(leaderboard) {
    try {
        localStorage.setItem(getLeaderboardKey(), JSON.stringify(leaderboard));
    } catch (e) {
        console.error('Error saving local leaderboard:', e);
    }
}

// Fetch leaderboard from API backend
async function fetchRemoteLeaderboard() {
    if (!LEADERBOARD_ENABLED) return null;
    
    try {
        const response = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard`, { signal: AbortSignal.timeout(10000) });

        if (!response.ok) {
            console.warn('Failed to fetch remote leaderboard:', response.status);
            return null;
        }
        
        const data = await response.json();
        const scores = data.scores || {};
        const leaderboardKey = `${currentPuzzleDate}-${puzzleHash}`;
        const leaderboard = scores[leaderboardKey] || [];
        return leaderboard.filter(validateScoreEntry);
    } catch (e) {
        console.error('Error fetching remote leaderboard:', e);
        return null;
    }
}

// Load leaderboard (remote first, then local fallback)
async function loadLeaderboard() {
    if (LEADERBOARD_ENABLED && !remoteLeaderboardCache) {
        const remote = await fetchRemoteLeaderboard();
        if (remote !== null) {
            remoteLeaderboardCache = remote;
            saveLocalLeaderboard(remote);
            return remote;
        }
    }
    
    if (remoteLeaderboardCache) return remoteLeaderboardCache;
    return loadLocalLeaderboard();
}

// Submit score via server-validated POST /api/scores
async function addToLeaderboard(username, timeSeconds) {
    HAS_VIEWED_SOLUTION = checkIfViewedSolution();
    const validation = analyzeInputPattern();

    if (!validation.valid) {
        console.warn('Anti-cheat validation failed:', validation.reasons);
        suspiciousActivity = validation.reasons;
    }

    // Try server-side submission first (requires a valid submission token)
    if (LEADERBOARD_ENABLED && puzzleData.submissionToken && validation.valid) {
        try {
            const response = await fetch(`${LEADERBOARD_PROXY_URL}/scores`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                signal: AbortSignal.timeout(10000),
                body: JSON.stringify({
                    token: puzzleData.submissionToken,
                    name: username,
                    time: timeSeconds,
                    puzzleHash: puzzleHash,
                    date: currentPuzzleDate,
                    puzzleSize: puzzleData.width && puzzleData.height ? `${puzzleData.width}x${puzzleData.height}` : null,
                    hintsUsed: letterHintsUsed,
                    wordHintsUsed: wordHintsUsed
                })
            });

            if (response.ok) {
                const data = await response.json();
                if (data.leaderboard) {
                    remoteLeaderboardCache = data.leaderboard;
                    saveLocalLeaderboard(data.leaderboard);
                    return data.leaderboard;
                }
            } else {
                console.warn('Score submission rejected:', response.status);
            }
        } catch (e) {
            console.error('Error submitting score:', e);
        }
    }

    // Fallback: save locally only (no token, failed validation, or server error)
    let leaderboard = await loadLeaderboard();
    const entry = createScoreEntry(username, timeSeconds);

    if (!validation.valid) {
        entry.flagged = true;
        entry.reasons = validation.reasons;
    }

    leaderboard.push(entry);
    leaderboard.sort((a, b) => a.time - b.time);
    leaderboard = leaderboard.slice(0, 10);

    saveLocalLeaderboard(leaderboard);
    remoteLeaderboardCache = leaderboard;
    return leaderboard;
}

async function renderLeaderboard() {
    const list = document.getElementById('leaderboard-list');
    const dateEl = document.getElementById('leaderboard-date');

    // Clear existing content to prevent duplicates
    list.innerHTML = '';

    if (LEADERBOARD_ENABLED && !remoteLeaderboardCache) {
        list.innerHTML = '<li class="leaderboard-empty">Laddar topplista...</li>';
    }

    const leaderboard = await loadLeaderboard();

    if (currentPuzzleDate) {
        const modeText = LEADERBOARD_ENABLED ? ' (delad)' : ' (lokal)';
        const sizeLabel = getSizeLabel(getPuzzleSize());
        dateEl.textContent = `Korsord: ${currentPuzzleDate} — ${sizeLabel}${modeText}`;
    } else dateEl.textContent = '';

    if (!leaderboard || leaderboard.length === 0) {
        list.innerHTML = '<li class="leaderboard-empty">Ingen har klarat korsordet än...</li>';
        return;
    }

    const medals = ['🥇', '🥈', '🥉'];
    list.innerHTML = leaderboard.map((entry, index) => {
        const isCurrentUser = entry.timestamp && (Date.now() - entry.timestamp < 5000);
        const isFlagged = entry.flagged;
        const flagTooltip = isFlagged && entry.reasons ? entry.reasons.join('\n') : '';
        const rankDisplay = index < 3 ? medals[index] : `${index + 1}.`;
        const rankClass = index < 3 ? `rank-${index + 1}` : '';
        const hintBadge = formatHintBadge(entry.hintsUsed, entry.wordHintsUsed);
        const verifiedBadge = entry.userId ? '<span class="verified-badge" title="Verifierat konto">✓</span>' : '<span class="guest-badge" title="Gäst">👤</span>';

        // Build descriptive tooltip for the entire row
        const hL = entry.hintsUsed || 0;
        const hW = entry.wordHintsUsed || 0;
        let rowTooltip = `${escapeHtml(entry.name)} — ${formatTime(entry.time)}`;
        if (entry.userId) rowTooltip += '\n✓ Verifierat konto';
        else rowTooltip += '\n👤 Gäst';
        if (hL > 0 || hW > 0) {
            rowTooltip += `\n💡 Ledtrådar: ${formatHintSummary(hL, hW)}`;
        } else {
            rowTooltip += '\n🏅 Inga ledtrådar';
        }

        return `
            <li class="leaderboard-item ${rankClass} ${isCurrentUser ? 'current-user' : ''}" title="${rowTooltip}" ${isFlagged ? 'style="opacity: 0.6;"' : ''}>
                <span class="leaderboard-rank">${rankDisplay}</span>
                <span class="leaderboard-name">${escapeHtml(entry.name)}${verifiedBadge}${hintBadge}${isFlagged ? `<span class="flag-icon" title="${escapeHtml(flagTooltip)}">⚠</span>` : ''}</span>
                <span class="leaderboard-time">${formatTime(entry.time)}</span>
            </li>
        `;
    }).join('');

    await renderFriendsLeaderboard();
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Friends leaderboard
async function renderFriendsLeaderboard() {
    const section = document.getElementById('friends-leaderboard-section');
    const list = document.getElementById('friends-leaderboard-list');
    if (!section || !list) return;

    if (!currentPuzzleDate) { section.style.display = 'none'; return; }

    try {
        const hashParam = puzzleHash ? `&puzzleHash=${encodeURIComponent(puzzleHash)}` : '';
        const res = await fetch(`${LEADERBOARD_PROXY_URL}/friends/leaderboard?date=${encodeURIComponent(currentPuzzleDate)}${hashParam}`, { credentials: 'same-origin', signal: AbortSignal.timeout(10000) });
        if (res.status === 401) return; // not logged in
        if (!res.ok) return;

        const entries = await res.json();
        if (!entries || entries.length === 0) { section.style.display = 'none'; return; }

        section.style.display = '';
        const medals = ['🥇', '🥈', '🥉'];
        list.innerHTML = entries.map((entry, index) => {
            const rankDisplay = index < 3 ? medals[index] : `${index + 1}.`;
            const rankClass = index < 3 ? `rank-${index + 1}` : '';
            const hintBadge = formatHintBadge(entry.hintsUsed, entry.wordHintsUsed);
            const friendVerifiedBadge = '<span class="verified-badge" title="Verifierat konto">✓</span>';
            return `
                <li class="leaderboard-item ${rankClass}">
                    <span class="leaderboard-rank">${rankDisplay}</span>
                    <span class="leaderboard-name">${escapeHtml(entry.name)}${friendVerifiedBadge}${hintBadge}</span>
                    <span class="leaderboard-time">${formatTime(entry.time)}</span>
                </li>
            `;
        }).join('');
    } catch (e) {
        console.warn('Friends leaderboard failed:', e);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §9  Modals & Puzzle Loading
// ═══════════════════════════════════════════════════════════════════════

function closeMessageModal() {
    const overlay = document.getElementById('message-modal');
    overlay.classList.remove('active');
    overlay.removeEventListener('click', handleMessageOverlayClick);
    document.removeEventListener('keydown', handleMessageEscape);
}

function handleMessageOverlayClick(e) { if (e.target === e.currentTarget) closeMessageModal(); }
function handleMessageEscape(e) { if (e.key === 'Escape') closeMessageModal(); }

function openMessageModal() {
    const overlay = document.getElementById('message-modal');
    overlay.classList.add('active');
    overlay.addEventListener('click', handleMessageOverlayClick);
    document.addEventListener('keydown', handleMessageEscape);
}

function showMessageModal(title, message) {
    document.getElementById('message-modal-title').textContent = title;
    document.getElementById('message-modal-body').textContent = message;
    const buttons = document.getElementById('message-modal-buttons');
    buttons.innerHTML = '';
    const ok = document.createElement('button');
    ok.className = 'btn btn-primary';
    ok.textContent = 'OK';
    ok.addEventListener('click', closeMessageModal);
    buttons.appendChild(ok);
    openMessageModal();
    ok.focus();
}

function showConfirmModal(title, message, onConfirm, confirmLabel = 'Ja', danger = false) {
    document.getElementById('message-modal-title').textContent = title;
    document.getElementById('message-modal-body').textContent = message;
    const buttons = document.getElementById('message-modal-buttons');
    buttons.innerHTML = '';
    const confirm = document.createElement('button');
    confirm.className = danger ? 'btn btn-danger' : 'btn btn-primary';
    confirm.textContent = confirmLabel;
    confirm.addEventListener('click', () => { closeMessageModal(); onConfirm(); });
    const cancel = document.createElement('button');
    cancel.className = 'btn btn-secondary';
    cancel.textContent = 'Avbryt';
    cancel.addEventListener('click', closeMessageModal);
    buttons.appendChild(confirm);
    buttons.appendChild(cancel);
    openMessageModal();
    confirm.focus();
}

async function showUsernameModal() {
    if (hasSubmittedScore) return;

    HAS_VIEWED_SOLUTION = checkIfViewedSolution();
    const validation = analyzeInputPattern();

    document.getElementById('modal-time').textContent = formatTime(seconds);

    // Show hint count in modal
    let hintEl = document.getElementById('modal-hints');
    const totalHints = letterHintsUsed + wordHintsUsed;
    if (totalHints > 0) {
        if (!hintEl) {
            hintEl = document.createElement('p');
            hintEl.id = 'modal-hints';
            hintEl.style.cssText = 'color: #d97706; font-size: 0.85rem; margin-bottom: 8px;';
            document.querySelector('.modal-time').insertAdjacentElement('afterend', hintEl);
        }
        hintEl.textContent = `💡 ${formatHintSummary(letterHintsUsed, wordHintsUsed)}`;
    } else if (hintEl) {
        hintEl.remove();
    }

    openUsernameModal();

    const modalContent = document.querySelector('#username-modal .modal');
    let warningEl = document.getElementById('cheat-warning');

    if (!validation.valid) {
        if (!warningEl) {
            warningEl = document.createElement('p');
            warningEl.id = 'cheat-warning';
            warningEl.style.cssText = 'color: #dc2626; font-size: 0.8rem; margin-top: -10px; margin-bottom: 10px;';
            modalContent.insertBefore(warningEl, document.getElementById('username-input'));
        }
        warningEl.textContent = 'Misstänkt aktivitet upptäckts. Ditt resultat kan markeras.';
    } else if (warningEl) {
        warningEl.remove();
    }

    const savedName = (authUser && authUser.alias) ? authUser.alias : (authUser && authUser.name) ? authUser.name : (localStorage.getItem('crossword-username') || '');
    const nameInput = document.getElementById('username-input');
    nameInput.value = savedName;
    nameInput.readOnly = false;
    nameInput.title = '';
    nameInput.style.opacity = '';

    // Show or hide the login prompt in the modal
    let loginPrompt = document.getElementById('modal-login-prompt');
    if (!authUser) {
        if (!loginPrompt) {
            loginPrompt = document.createElement('p');
            loginPrompt.id = 'modal-login-prompt';
            loginPrompt.style.cssText = 'font-size: 0.85rem; margin-bottom: 8px; text-align: center;';
            const buttonsEl = document.querySelector('#username-modal .modal-buttons');
            buttonsEl.parentElement.insertBefore(loginPrompt, buttonsEl);
        }
        loginPrompt.innerHTML = '🔒 <a href="#" id="modal-login-link" style="color: var(--accent, #2563eb);">Logga in</a> för att få en ✓ vid ditt namn.';
        document.getElementById('modal-login-link').addEventListener('click', function(e) {
            e.preventDefault();
            savePendingScore();
            // Show provider links inside the modal
            const menu = document.getElementById('modal-login-menu');
            if (menu) {
                const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
                menu.innerHTML =
                    '<a href="/api/auth/login/google?returnUrl=' + returnUrl + '" class="btn btn-primary" style="display:block;margin-bottom:6px;text-decoration:none;text-align:center;">Google</a>' +
                    '<a href="/api/auth/login/microsoft?returnUrl=' + returnUrl + '" class="btn btn-primary" style="display:block;text-decoration:none;text-align:center;">Microsoft</a>';
                menu.style.display = 'block';
            }
        });
    } else if (loginPrompt) {
        loginPrompt.remove();
    }
    // Hide login provider menu (reset from previous open)
    const loginMenu = document.getElementById('modal-login-menu');
    if (loginMenu) loginMenu.style.display = 'none';

    nameInput.focus();
    nameInput.select();
}

function savePendingScore() {
    try {
        localStorage.setItem('crossword-pending-score', JSON.stringify({
            puzzleHash: puzzleHash,
            seconds: seconds,
            letterHintsUsed: letterHintsUsed,
            wordHintsUsed: wordHintsUsed,
            date: currentPuzzleDate,
            returnUrl: window.location.href,
            timestamp: Date.now()
        }));
    } catch (e) {
        console.warn('Failed to save pending score:', e);
    }
}

function checkPendingScore() {
    try {
        const raw = localStorage.getItem('crossword-pending-score');
        if (!raw) return;
        const pending = JSON.parse(raw);
        // Only restore if it's for this puzzle, user is now signed in, and it's recent (< 10 min)
        if (pending.puzzleHash === puzzleHash && authUser && (Date.now() - pending.timestamp < 600000)) {
            localStorage.removeItem('crossword-pending-score');
            // Restore completion state
            seconds = pending.seconds;
            letterHintsUsed = pending.letterHintsUsed || 0;
            wordHintsUsed = pending.wordHintsUsed || 0;
            document.getElementById('timer').textContent = formatTime(seconds);
            puzzleSolved = true;
            stopTimer();
            setTimeout(() => showUsernameModal(), 300);
        } else if (Date.now() - pending.timestamp >= 600000) {
            localStorage.removeItem('crossword-pending-score');
        }
    } catch (e) {
        console.warn('Failed to check pending score:', e);
    }
}

function closeModal() {
    const modal = document.getElementById('username-modal');
    modal.classList.remove('active');
    document.removeEventListener('keydown', _usernameModalEscape);
    modal.removeEventListener('click', _usernameModalBackdrop);
}

function _usernameModalEscape(e) { if (e.key === 'Escape') closeModal(); }
function _usernameModalBackdrop(e) { if (e.target === e.currentTarget) closeModal(); }

function openUsernameModal() {
    const modal = document.getElementById('username-modal');
    modal.classList.add('active');
    document.addEventListener('keydown', _usernameModalEscape);
    modal.addEventListener('click', _usernameModalBackdrop);
}

async function submitScore() {
    if (hasSubmittedScore) return;
    const input = document.getElementById('username-input');
    let username = input.value.trim();

    if (!username && authUser && authUser.name) username = authUser.name;
    if (!username) username = 'Anonym';

    // If signed-in user typed a different name than their current alias, ask to update
    if (authUser && authUser.alias && username !== authUser.alias) {
        closeModal(); // Close username modal first so confirm modal is visible
        showConfirmModal(
            'Byt alias?',
            `Vill du ändra ditt alias till "${username}"? Det kommer användas på alla framtida topplistor.`,
            async () => { await finishScoreSubmission(username, true); },
            'Ja, byt alias'
        );
        // Also add a "keep old" option — submit with old alias without changing
        const buttons = document.getElementById('message-modal-buttons');
        const keepBtn = document.createElement('button');
        keepBtn.className = 'btn btn-secondary';
        keepBtn.textContent = 'Nej, behåll "' + authUser.alias + '"';
        keepBtn.addEventListener('click', async () => { closeMessageModal(); await finishScoreSubmission(authUser.alias, false); });
        buttons.appendChild(keepBtn);
        return;
    }

    // Signed-in user without an alias — offer to save as alias
    if (authUser && !authUser.alias && username !== 'Anonym') {
        closeModal(); // Close username modal first so confirm modal is visible
        showConfirmModal(
            'Spara alias?',
            `Vill du använda "${username}" som ditt alias? Det visas på topplistor med en ✓.`,
            async () => { await finishScoreSubmission(username, true); },
            'Ja, spara'
        );
        const buttons = document.getElementById('message-modal-buttons');
        const skipBtn = document.createElement('button');
        skipBtn.className = 'btn btn-secondary';
        skipBtn.textContent = 'Nej tack';
        skipBtn.addEventListener('click', async () => { closeMessageModal(); await finishScoreSubmission(username, false); });
        buttons.appendChild(skipBtn);
        return;
    }

    await finishScoreSubmission(username, false);
}

async function finishScoreSubmission(username, updateAlias) {
    if (hasSubmittedScore) return;
    hasSubmittedScore = true;

    if (updateAlias && authUser) {
        try {
            const res = await fetch(`${LEADERBOARD_PROXY_URL}/auth/alias`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                signal: AbortSignal.timeout(10000),
                body: JSON.stringify({ alias: username })
            });
            if (res.ok) {
                authUser.alias = username;
            } else {
                const data = await res.json().catch(() => null);
                const err = data && data.error ? data.error : 'Kunde inte byta alias.';
                showMessageModal('Alias', err);
                return;
            }
        } catch (e) {
            console.error('Failed to update alias:', e);
        }
    }

    localStorage.setItem('crossword-username', username);

    await addToLeaderboard(username, seconds);

    closeModal();
    await renderLeaderboard();
}

// Fetch historical leaderboard from API backend
async function fetchLeaderboardHistory(days = 30) {
    if (!LEADERBOARD_ENABLED) return {};

    try {
        const response = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard/history?days=${days}`, { signal: AbortSignal.timeout(10000) });
        if (!response.ok) {
            console.warn('Failed to fetch leaderboard history:', response.status);
            return {};
        }
        return await response.json();
    } catch (e) {
        console.error('Error fetching leaderboard history:', e);
        return {};
    }
}

// Render historical leaderboard (filtered by current puzzle size)
async function renderLeaderboardHistory() {
    const container = document.getElementById('history-list');
    if (!container) return;

    container.innerHTML = '<li class="leaderboard-empty">Laddar historik...</li>';

    const history = await fetchLeaderboardHistory();
    const dates = Object.keys(history).sort().reverse();
    const currentSize = getPuzzleSize();

    if (dates.length === 0) {
        container.innerHTML = '<li class="leaderboard-empty">Ingen historik tillgänglig ännu.</li>';
        return;
    }

    const medals = ['🥇', '🥈', '🥉'];
    let hasContent = false;

    container.innerHTML = dates.map(date => {
        const entries = history[date];

        // Group entries by puzzleHash to detect multiple puzzles on the same date
        const puzzleGroups = new Map();
        entries.forEach(entry => {
            const key = entry.puzzleHash || '_default';
            if (!puzzleGroups.has(key)) puzzleGroups.set(key, []);
            puzzleGroups.get(key).push(entry);
        });

        // Filter: only show groups matching the current puzzle size
        let filteredGroups = [];
        for (const [, groupEntries] of puzzleGroups) {
            const size = groupEntries[0]?.puzzleSize;
            if (size && size !== currentSize) continue;
            filteredGroups.push(groupEntries);
        }
        if (filteredGroups.length === 0) return '';
        hasContent = true;

        let groupsHtml = '';
        const showLabels = filteredGroups.length > 1;
        filteredGroups.forEach((groupEntries, idx) => {
            const size = groupEntries[0]?.puzzleSize;
            let puzzleLabel = '';
            if (showLabels) {
                const label = size ? getSizeLabel(size) : `Pussel ${idx + 1}`;
                puzzleLabel = `<span class="history-puzzle-label">${label}</span>`;
            } else if (size) {
                puzzleLabel = `<span class="history-puzzle-label">${getSizeLabel(size)}</span>`;
            }
            const rows = groupEntries.map((entry, index) => {
                const rankDisplay = index < 3 ? medals[index] : `${index + 1}.`;
                const rankClass = index < 3 ? `rank-${index + 1}` : '';
                const hintBadge = formatHintBadge(entry.hintsUsed, entry.wordHintsUsed);
                const historyVerifiedBadge = entry.userId ? '<span class="verified-badge" title="Verifierat konto">✓</span>' : '<span class="guest-badge" title="Gäst">👤</span>';
                const hL = entry.hintsUsed || 0;
                const hW = entry.wordHintsUsed || 0;
                let rowTooltip = `${escapeHtml(entry.name)} — ${formatTime(entry.time)}`;
                if (entry.userId) rowTooltip += '\n✓ Verifierat konto';
                else rowTooltip += '\n👤 Gäst';
                if (hL > 0 || hW > 0) {
                    rowTooltip += `\n💡 Ledtrådar: ${formatHintSummary(hL, hW)}`;
                } else {
                    rowTooltip += '\n🏅 Inga ledtrådar';
                }
                return `
                    <li class="leaderboard-item history-item ${rankClass}" title="${rowTooltip}">
                        <span class="leaderboard-rank">${rankDisplay}</span>
                        <span class="leaderboard-name">${escapeHtml(entry.name)}${historyVerifiedBadge}${hintBadge}</span>
                        <span class="leaderboard-time">${formatTime(entry.time)}</span>
                    </li>`;
            }).join('');
            groupsHtml += `${puzzleLabel}<ul class="history-entries">${rows}</ul>`;
        });

        return `
            <li class="history-date-group">
                <h4 class="history-date-heading">${date}</h4>
                ${groupsHtml}
            </li>`;
    }).join('');

    if (!hasContent) {
        container.innerHTML = '<li class="leaderboard-empty">Ingen historik för denna storlek ännu.</li>';
    }
}

// Handle Enter key in username input
document.addEventListener('DOMContentLoaded', () => {
    const usernameInput = document.getElementById('username-input');
    if (usernameInput) {
        usernameInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                submitScore();
            }
        });
    }
    const themeBtn = document.getElementById('theme-toggle');
    if (themeBtn) themeBtn.addEventListener('click', toggleTheme);
});

function showPuzzleUnavailable() {
    document.getElementById('loading').style.display = 'none';
    const layout = document.getElementById('main-layout');
    layout.style.display = '';
    layout.innerHTML = `
        <div class="unavailable-card">
            <div class="unavailable-icon">🔧</div>
            <h2>Korsordet genereras...</h2>
            <p>Dagens korsord håller på att skapas. Det brukar ta någon minut.</p>
            <p>Försök igen om en stund!</p>
            <button class="btn btn-primary" onclick="location.reload()">Försök igen</button>
            <a href="/calendar.html" class="btn btn-secondary" style="text-decoration:none;display:inline-block;margin-left:8px;">Spela äldre korsord</a>
        </div>`;
}

async function loadPuzzle() {
    // Fetch auth state in parallel with puzzle load (non-blocking)
    fetchAuthUser().then(() => { renderAuthButton(); checkPendingScore(); });
    try {
        const params = new URLSearchParams(window.location.search);
        const dateParam = params.get('date');
        const sizeParam = params.get('size') || '17x17';
        const sizeQuery = `size=${encodeURIComponent(sizeParam)}`;
        const url = dateParam ? `/api/puzzle/${dateParam}?${sizeQuery}` : `/api/puzzle/today?${sizeQuery}`;
        const response = await fetch(url, { signal: AbortSignal.timeout(15000) });
        if (response.ok) {
            puzzleData = await response.json();
            console.log('Loaded puzzle from API');
        } else if (response.status === 404 && dateParam) {
            console.log('No puzzle for ' + dateParam);
            document.getElementById('loading').style.display = 'none';
            document.getElementById('main-layout').style.display = '';
            const gridHeader = document.querySelector('.grid-header h2');
            if (gridHeader) gridHeader.textContent = 'Inget korsord tillgängligt för ' + dateParam;
            return;
        } else if (response.status === 503) {
            console.log('Puzzle not ready yet (503)');
            showPuzzleUnavailable();
            return;
        } else {
            console.log('API puzzle endpoint returned ' + response.status + ', using default puzzle');
        }
    } catch (e) { 
        console.log('Error loading puzzle from API, using default puzzle:', e);
    }

    try {
        await init();
    } catch (initError) {
        console.error('Error during init:', initError);
        document.getElementById('loading').style.display = 'none';
        // Clear inline display so CSS media queries control layout
        document.getElementById('main-layout').style.display = '';
    }
}

async function init() {
    document.getElementById('loading').style.display = 'none';
    // Let CSS determine the appropriate layout (do not force 'flex' which overrides media queries)
    document.getElementById('main-layout').style.display = '';
    
    puzzleStartTime = Date.now();
    puzzleHash = puzzleData.puzzleHash || generatePuzzleHash();
    inputEvents = [];
    usedShowSolution = false;
    letterHintsUsed = 0;
    wordHintsUsed = 0;
    suspiciousActivity = [];
    devToolsOpenedDuringSession = false;
    
    devToolsDetector.startMonitoring();
    
    // Use the server-provided puzzle date (the date the puzzle is *for*),
    // falling back to the URL date parameter or today's date.
    const params = new URLSearchParams(window.location.search);
    const dateParam = params.get('date');
    const todayStr = new Date().toISOString().split('T')[0];

    if (puzzleData.puzzleDate) {
        currentPuzzleDate = puzzleData.puzzleDate;
    } else if (dateParam) {
        currentPuzzleDate = dateParam;
    } else {
        currentPuzzleDate = todayStr;
    }

    // Update heading for historical puzzles and size
    const isHistorical = dateParam && dateParam !== todayStr;
    const gridHeader = document.querySelector('.grid-header h2');
    const sizeLabel = getSizeLabel(getPuzzleSize());
    if (gridHeader) {
        if (isHistorical) {
            gridHeader.textContent = `Korsord ${currentPuzzleDate} — ${sizeLabel}`;
        } else {
            gridHeader.textContent = `Dagens Korsord — ${sizeLabel}`;
        }
    }

    if (puzzleData.wordCount) {
        document.getElementById('puzzle-info').style.display = 'inline-block';
        document.getElementById('info-size').textContent = `${puzzleData.width}x${puzzleData.height}`;
        document.getElementById('info-words').textContent = `${puzzleData.wordCount} ord`;
        document.getElementById('info-fill').textContent = `${puzzleData.fillPercentage}%`;
        const diffEl = document.getElementById('info-difficulty');
        // TODO: re-enable when difficulty display is ready
        // if (diffEl && puzzleData.difficulty) {
        //     diffEl.textContent = getDifficultyLabel(puzzleData.difficulty);
        //     diffEl.className = getDifficultyClass(puzzleData.difficulty);
        // }
    }

    document.getElementById('generation-date').textContent = currentPuzzleDate;
    
    renderGrid();
    renderClues();
    buildCellClueMap();
    loadProgress();
    await renderLeaderboard();
    renderPlayerStats();
    renderPersonalStats();

    // Auto-load history data on desktop (no manual toggle needed)
    if (!window.matchMedia('(max-width:1100px)').matches) {
        renderLeaderboardHistory();
    }

    syncCluesHeight();
    startTimer();
    updateStats();
    updateClueFilledStatus();

    // Once layout is stable, scroll the grid into view
    setTimeout(() => {
        const gridSection = document.querySelector('.grid-section');
        if (!gridSection) return;
        const isMobile = window.matchMedia('(max-width:1099px)').matches;
        if (isMobile) {
            // On mobile the clues are below — align grid near top of viewport
            const rect = gridSection.getBoundingClientRect();
            const targetY = window.scrollY + rect.top - 8;
            window.scrollTo({ top: Math.max(0, targetY), behavior: 'smooth' });
        } else {
            // On desktop center the grid section vertically
            const rect = gridSection.getBoundingClientRect();
            const targetY = window.scrollY + rect.top + rect.height / 2 - window.innerHeight / 2;
            window.scrollTo({ top: Math.max(0, targetY), behavior: 'smooth' });
        }
    }, 300);

    window.addEventListener('resize', syncCluesHeight);

    // Keep the focused cell visible above the on-screen keyboard.
    // visualViewport.height shrinks when the keyboard opens; we scroll
    // so the active input stays within the visible area.
    if (window.visualViewport) {
        let lastVVHeight = window.visualViewport.height;
        window.visualViewport.addEventListener('resize', () => {
            const vv = window.visualViewport;
            const keyboardOpen = vv.height < lastVVHeight - 50;
            lastVVHeight = vv.height;
            if (!keyboardOpen) return;
            const active = document.activeElement;
            if (!active || !active.closest('.cell')) return;
            const rect = active.getBoundingClientRect();
            const visibleBottom = vv.offsetTop + vv.height;
            if (rect.bottom > visibleBottom - 20) {
                const scrollBy = rect.bottom - visibleBottom + 60;
                window.scrollBy({ top: scrollBy, behavior: 'smooth' });
            } else if (rect.top < vv.offsetTop + 10) {
                window.scrollBy({ top: rect.top - vv.offsetTop - 40, behavior: 'smooth' });
            }
        });
    }
}

function syncCluesHeight() {
    const gridSection = document.querySelector('.grid-section');
    const cluesSection = document.querySelector('.clues-section');
    const leaderboardSection = document.querySelector('.leaderboard-section');
    const historySection = document.getElementById('history-section');

    // Only adjust when using wide layout (desktop)
    const isWide = window.matchMedia('(min-width:1100px)').matches;
    if (!gridSection) return;

    if (isWide) {
        // On desktop, sync clues height to match grid section and split
        // leaderboard + history into two equal halves of that same height.
        const gridHeight = gridSection.offsetHeight;
        if (gridHeight > 0) {
            if (cluesSection) {
                cluesSection.style.height = gridHeight + 'px';
                cluesSection.style.maxHeight = gridHeight + 'px';
            }
            // Account for the CSS gap between the two halves
            const gap = parseFloat(getComputedStyle(gridSection.parentElement).gap) || 24;
            const halfHeight = Math.floor((gridHeight - gap) / 2);
            if (leaderboardSection) {
                leaderboardSection.style.height = halfHeight + 'px';
                leaderboardSection.style.maxHeight = halfHeight + 'px';
            }
            if (historySection) {
                historySection.style.height = halfHeight + 'px';
                historySection.style.maxHeight = halfHeight + 'px';
            }
        }
    } else {
        // On small screens remove enforced heights so sections flow naturally
        if (cluesSection) { 
            cluesSection.style.maxHeight = ''; 
            cluesSection.style.height = ''; 
        }
        if (leaderboardSection) { 
            leaderboardSection.style.maxHeight = ''; 
            leaderboardSection.style.height = ''; 
        }
        if (historySection) { 
            historySection.style.maxHeight = ''; 
            historySection.style.height = ''; 
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §10  Grid Rendering
// ═══════════════════════════════════════════════════════════════════════

function renderGrid() {
    const grid = document.getElementById('crossword-grid');
    grid.innerHTML = '';

    // Expose number of cols/rows to CSS so --cell-size can be computed
    grid.style.setProperty('--cols', puzzleData.width);
    grid.style.setProperty('--rows', puzzleData.height);

    // Ensure grid uses CSS Grid layout; remove any leftover row wrappers
    grid.classList.remove('using-rows');

    for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            const cellData = puzzleData.cells[row]?.[col];
            const cellDiv = document.createElement('div');
            cellDiv.className = 'cell';
            cellDiv.dataset.row = row;
            cellDiv.dataset.col = col;
            if (cellData === null) {
                cellDiv.classList.add('blocked');
            } else {
                if (cellData.num) {
                    const numSpan = document.createElement('span');
                    numSpan.className = 'number';
                    numSpan.textContent = cellData.num;
                    cellDiv.appendChild(numSpan);
                }
                if (cellData.bend) {
                    const arrowSpan = document.createElement('span');
                    arrowSpan.className = 'bend-arrow';
                    arrowSpan.textContent = cellData.bend === 'down' ? '↴' : '↳';
                    cellDiv.appendChild(arrowSpan);
                }
                const input = document.createElement('input');
                input.type = 'text';
                input.maxLength = 1;
                if (cellData.letter) input.dataset.answer = cellData.letter;
                
                // Mobile-friendly input attributes
                input.autocomplete = 'off';
                input.autocorrect = 'off';
                input.autocapitalize = 'characters';  // Force uppercase on each keystroke
                input.spellcheck = false;
                input.enterKeyHint = 'next';  // Show "Next" on mobile keyboard
                input.inputMode = 'text';
                input.setAttribute('aria-label', `Rad ${row + 1}, kolumn ${col + 1}`);
                
                input.addEventListener('input', handleInput);
                input.addEventListener('keydown', handleKeyDown);
                input.addEventListener('focus', () => handleFocus(row, col));
                cellDiv.appendChild(input);
            }
            grid.appendChild(cellDiv);
        }
    }

    // After rendering, ensure grid dimensions fit by forcing a reflow and updating clue heights
    requestAnimationFrame(() => {
        updateStats();
        updateClueFilledStatus();
        syncCluesHeight();
    });
}

function renderClues() {
    const acrossContainer = document.getElementById('across-clues');
    const downContainer = document.getElementById('down-clues');
    acrossContainer.innerHTML = '';
    downContainer.innerHTML = '';

    // Filter out clues with invalid numbers (0 or missing)
    const validAcrossClues = (puzzleData.clues.across || []).filter(clue => clue.number > 0);
    const validDownClues = (puzzleData.clues.down || []).filter(clue => clue.number > 0);

    validAcrossClues.forEach((clue, idx) => {
        const li = document.createElement('li');
        li.className = 'clue-item';
        li.innerHTML = `<span class="clue-number">${clue.number}. </span>${escapeHtml(clue.clue)}`;
        li.dataset.number = clue.number;
        li.dataset.direction = 'across';
        li.dataset.clueIndex = idx;
        li.addEventListener('click', () => focusClue(clue.number, 'across'));
        acrossContainer.appendChild(li);
    });

    validDownClues.forEach((clue, idx) => {
        const li = document.createElement('li');
        li.className = 'clue-item';
        li.innerHTML = `<span class="clue-number">${clue.number}. </span>${escapeHtml(clue.clue)}`;
        li.dataset.number = clue.number;
        li.dataset.direction = 'down';
        li.dataset.clueIndex = idx;
        li.addEventListener('click', () => focusClue(clue.number, 'down'));
        downContainer.appendChild(li);
    });
}

// ═══════════════════════════════════════════════════════════════════════
// §11  Input Handling & Navigation
// ═══════════════════════════════════════════════════════════════════════

function isValidSwedishLetter(value) {
    if (value === "") return true; // tomt är okej
    return /^[A-Za-zåäöÅÄÖ]$/.test(value);
}

function handleInput(e) {
    const cell = e.target.parentElement;
    const row = parseInt(cell.dataset.row);
    const col = parseInt(cell.dataset.col);
    
    let val = e.target.value;

    // On mobile, space might come through as input - use it to toggle direction
    if (val === ' ' || val === '  ') {
        e.target.value = e.target.dataset.previousValue || '';
        toggleDirection(row, col);
        e.preventDefault();
        return;
    }

    if (val.length > 1) val = val.charAt(0);

    if (!isValidSwedishLetter(val)) {
        e.target.value = "";
        return;
    }

    if (val) e.target.value = val.toUpperCase(); else e.target.value = "";

    e.target.parentElement.classList.remove('empty-warning');
    
    if (e.target.value) trackInput(row, col, e.target.value);

    if (e.target.value) moveInDirection(e.target);
    updateStats();
    updateClueFilledStatus();
    saveProgress();

    if (e.target.value) autoCheckIfComplete();
}

// Toggle direction between across and down
function toggleDirection(row, col) {
    currentDirection = currentDirection === 'across' ? 'down' : 'across';
    highlightWord(row, col);
    highlightClue(row, col);
    announce(currentDirection === 'across' ? 'Vågrätt' : 'Lodrätt');
}

// Set up hint buttons and other focus-preserving controls
document.addEventListener('DOMContentLoaded', () => {
    // Set up hint buttons to prevent focus loss from grid cell inputs
    function setupFocusPreservingButton(id, action) {
        const btn = document.getElementById(id);
        if (!btn) return;
        btn.addEventListener('mousedown', (e) => e.preventDefault());
        btn.addEventListener('touchstart', (e) => e.preventDefault(), { passive: false });
        btn.addEventListener('click', (e) => { e.preventDefault(); action(); });
        btn.addEventListener('touchend', (e) => { e.preventDefault(); action(); }, { passive: false });
    }
    setupFocusPreservingButton('hint-letter-btn', revealLetter);
    setupFocusPreservingButton('hint-word-btn', revealWord);
});

function handleKeyDown(e) {
    const cell = e.target.parentElement;
    const row = parseInt(cell.dataset.row);
    const col = parseInt(cell.dataset.col);

    const key = e.key;

    if (e.ctrlKey || e.metaKey) { e.preventDefault(); return; }

    // Tab / Shift+Tab: move to next/previous clue in the same direction
    if (key === 'Tab') {
        e.preventDefault();
        if (e.shiftKey) {
            moveToPreviousClue(row, col);
        } else {
            moveToNextClue(row, col);
        }
        return;
    }

    if (key === 'Backspace') {
        if (e.target.value) {
            e.target.value = '';
            updateStats();
            updateClueFilledStatus();
            saveProgress();
        }
        // Always move back on backspace (whether cell was empty or not)
        moveBackInDirection(e.target);
        e.preventDefault();
        return;
    }

    if (key === 'Delete') {
        // Delete only clears current cell, does not move
        if (e.target.value) {
            e.target.value = '';
            updateStats();
            updateClueFilledStatus();
            saveProgress();
        }
        e.preventDefault();
        return;
    }

    switch (key) {
        case 'ArrowRight': currentDirection = 'across'; moveTo(row, col + 1); e.preventDefault(); return;
        case 'ArrowLeft': currentDirection = 'across'; moveTo(row, col - 1); e.preventDefault(); return;
        case 'ArrowDown': currentDirection = 'down'; moveTo(row + 1, col); e.preventDefault(); return;
        case 'ArrowUp': currentDirection = 'down'; moveTo(row - 1, col); e.preventDefault(); return;
        case ' ':
        case 'Spacebar': // Older browsers
            toggleDirection(row, col);
            e.preventDefault();
            return;
    }

    if (/^[A-Za-zåäöÅÄÖ]$/.test(key)) { e.target.value = ''; return; }

    e.preventDefault();
}

function handleFocus(row, col) {
    // Debounce rapid focus events on the same cell to prevent loops
    const cellKey = `${row},${col}`;
    const now = Date.now();
    
    // If tapping the same cell again (within a reasonable time), toggle direction
    // This provides an alternative way to change direction on mobile
    if (cellKey === lastFocusedCell && (now - lastFocusTime) > FOCUS_DEBOUNCE_MS && (now - lastFocusTime) < 500) {
        toggleDirection(row, col);
        lastFocusTime = now;
        return;
    }
    
    if (cellKey === lastFocusedCell && (now - lastFocusTime) < FOCUS_DEBOUNCE_MS) {
        return;
    }
    lastFocusedCell = cellKey;
    lastFocusTime = now;

    const cell = document.querySelector(`.cell[data-row="${row}"][data-col="${col}"]`);
    if (cell) {
        const input = cell.querySelector('input');
        if (input) { 
            input.dataset.previousValue = input.value; 
            // Update aria-label with current clue context for screen readers
            const key2 = `${row},${col}`;
            const entries = typeof cellClueMap !== 'undefined' ? cellClueMap[key2] : null;
            let label = `Rad ${row + 1}, kolumn ${col + 1}`;
            if (entries && entries.length > 0) {
                const match = findBestEntry(entries, currentDirection, row, col);
                if (match && match.number > 0) {
                    const dir = match.direction === 'across' ? 'vågrätt' : 'lodrätt';
                    const clues = match.direction === 'across' ? puzzleData.clues.across : puzzleData.clues.down;
                    const clue = clues?.find(c => c.number === match.number);
                    label += `, ${match.number} ${dir}`;
                    if (clue) label += `: ${clue.clue}`;
                }
            }
            input.setAttribute('aria-label', label);
            // Use requestAnimationFrame instead of setTimeout to avoid focus race conditions
            requestAnimationFrame(() => {
                // Only select if this input is still the active element
                if (document.activeElement === input) {
                    input.select();
                }
            });
        }
    }
    highlightWord(row, col);
    highlightClue(row, col);

    // If the on-screen keyboard is open, ensure the focused cell is visible
    if (window.visualViewport) {
        requestAnimationFrame(() => {
            const vv = window.visualViewport;
            const cell = document.querySelector(`.cell[data-row="${row}"][data-col="${col}"]`);
            if (!cell) return;
            const rect = cell.getBoundingClientRect();
            const visibleBottom = vv.offsetTop + vv.height;
            if (rect.bottom > visibleBottom - 20) {
                window.scrollBy({ top: rect.bottom - visibleBottom + 60, behavior: 'smooth' });
            } else if (rect.top < vv.offsetTop + 10) {
                window.scrollBy({ top: rect.top - vv.offsetTop - 40, behavior: 'smooth' });
            }
        });
    }
}

function highlightWord(row, col) {
    document.querySelectorAll('.cell.word-highlight').forEach(c => c.classList.remove('word-highlight'));

    // Use cellClueMap to find the correct clue for this cell + direction
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    let clueNumber = 0;
    let clueDirection = currentDirection;
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        clueNumber = match.number;
        clueDirection = match.direction;
    }

    if (clueNumber <= 0) {
        // Fallback: straight-line walk to find start cell
        let startRow = row, startCol = col;
        if (currentDirection === 'across') {
            while (startCol > 0 && puzzleData.cells[row]?.[startCol - 1] !== null) startCol--;
        } else {
            while (startRow > 0 && puzzleData.cells[startRow - 1]?.[col] !== null) startRow--;
        }
        const startCellData = puzzleData.cells[startRow]?.[startCol];
        clueNumber = startCellData?.num || 0;
    }

    if (clueNumber > 0) {
        const clueItem = document.querySelector(`.clue-item[data-number="${clueNumber}"][data-direction="${clueDirection}"]`);
        if (clueItem) {
            clueItem.classList.add('active');
            const listContainer = clueItem.closest('.clue-list');
            if (listContainer) {
                const target = clueItem.offsetTop - listContainer.offsetTop - 8;
                listContainer.scrollTo({ top: target, behavior: 'smooth' });
            }
        }
    }
}

function moveTo(row, col) {
    if (row < 0 || row >= puzzleData.height || col < 0 || col >= puzzleData.width) return false;
    if (puzzleData.cells[row]?.[col] === null) return false;
    const cell = document.querySelector(`.cell[data-row="${row}"][data-col="${col}"]`);
    if (cell && !cell.classList.contains('blocked')) { cell.querySelector('input')?.focus({ preventScroll: true }); return true; }
    return false;
}

function moveInDirection(input) {
    const cell = input.parentElement;
    const row = parseInt(cell.dataset.row), col = parseInt(cell.dataset.col);

    // Use cellClueMap to find the next cell along the word path (handles bent words)
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        const cells = match.cells;
        const idx = cells.findIndex(c => c.row === row && c.col === col);
        if (idx >= 0 && idx < cells.length - 1) {
            const next = cells[idx + 1];
            // Update currentDirection to match the local direction at the
            // destination cell so that highlighting and further navigation
            // correctly follow bent words (vinkelord) through the bend.
            const destRef = (idx + 2 < cells.length) ? cells[idx + 2] : cells[idx];
            const destLocalDir = (destRef.row === next.row) ? 'across' : 'down';
            if (currentDirection !== destLocalDir) {
                currentDirection = destLocalDir;
                updateDirectionButton();
            }
            if (moveTo(next.row, next.col)) return;
        }
    }

    // Fallback: straight-line movement
    currentDirection === 'across' ? moveTo(row, col + 1) : moveTo(row + 1, col);
}

function moveBackInDirection(input) {
    const cell = input.parentElement;
    const row = parseInt(cell.dataset.row), col = parseInt(cell.dataset.col);

    // Use cellClueMap to find the previous cell along the word path (handles bent words)
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        const cells = match.cells;
        const idx = cells.findIndex(c => c.row === row && c.col === col);
        if (idx > 0) {
            const prev = cells[idx - 1];
            // Update currentDirection to match the local direction at the
            // destination cell so that highlighting and further navigation
            // correctly follow bent words (vinkelord) through the bend.
            const destIdx = idx - 1;
            const destRef = (destIdx + 1 < cells.length) ? cells[destIdx + 1] : cells[destIdx - 1];
            const destLocalDir = (destRef.row === prev.row) ? 'across' : 'down';
            if (currentDirection !== destLocalDir) {
                currentDirection = destLocalDir;
                updateDirectionButton();
            }
            if (moveTo(prev.row, prev.col)) return;
        }
    }

    // Fallback: straight-line movement
    currentDirection === 'across' ? moveTo(row, col - 1) : moveTo(row - 1, col);
}

// ═══════════════════════════════════════════════════════════════════════
// §12  Clue Navigation & Highlighting
// ═══════════════════════════════════════════════════════════════════════

function focusClue(number, direction) {
    currentDirection = direction;
    for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            if (puzzleData.cells[row]?.[col]?.num === number) { moveTo(row, col); return; }
        }
    }
}

// Find the index of the current clue in the filtered clues array for the
// active direction.  Uses cellClueMap (which stores clueIndex) so that
// duplicate clue numbers are resolved correctly.
function findCurrentClueIndex(row, col) {
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        if (match && match.direction === currentDirection) return match.clueIndex;
    }
    // Fallback: search by number (may be inaccurate with duplicate numbers)
    const clueNumber = findCurrentClueNumber(row, col);
    const clues = currentDirection === 'across'
        ? (puzzleData.clues.across || []).filter(c => c.number > 0)
        : (puzzleData.clues.down || []).filter(c => c.number > 0);
    return clues.findIndex(c => c.number === clueNumber);
}

// Focus the first cell of a clue using its cells array when available,
// so that clues sharing the same number are handled correctly.
function focusClueFirstCell(clue, direction) {
    currentDirection = direction;
    if (clue.cells && clue.cells.length > 0) {
        moveTo(clue.cells[0][0], clue.cells[0][1]);
        return;
    }
    focusClue(clue.number, direction);
}

// Get the starting cell [row, col] for a clue, using its cells array or
// falling back to scanning the grid for the numbered cell.
function getClueStartCell(clue, direction) {
    if (clue.cells && clue.cells.length > 0) return clue.cells[0];
    for (let r = 0; r < puzzleData.height; r++) {
        for (let c = 0; c < puzzleData.width; c++) {
            if (puzzleData.cells[r]?.[c]?.num === clue.number) return [r, c];
        }
    }
    return null;
}

// Move to the next clue in the current direction
function moveToNextClue(currentRow, currentCol) {
    const clues = currentDirection === 'across' 
        ? (puzzleData.clues.across || []).filter(c => c.number > 0)
        : (puzzleData.clues.down || []).filter(c => c.number > 0);

    if (clues.length === 0) return;

    // Find the current clue index directly (handles duplicate clue numbers)
    const currentIndex = findCurrentClueIndex(currentRow, currentCol);

    // Loop through subsequent clues, skipping any whose start cell is the
    // same as the current cell (prevents stuck Tab when multiple clues
    // share a starting cell, e.g. vinkelord with the same number).
    for (let i = 1; i <= clues.length; i++) {
        const candidateIndex = (currentIndex + i) % clues.length;
        const candidate = clues[candidateIndex];
        const start = getClueStartCell(candidate, currentDirection);
        if (start && start[0] === currentRow && start[1] === currentCol) continue;
        focusClueFirstCell(candidate, currentDirection);
        return;
    }

    // All clues share the same start cell — just advance index so the
    // highlighted clue in the sidebar still changes.
    const nextIndex = currentIndex >= 0 ? (currentIndex + 1) % clues.length : 0;
    focusClueFirstCell(clues[nextIndex], currentDirection);
}

// Move to the previous clue in the current direction
function moveToPreviousClue(currentRow, currentCol) {
    const clues = currentDirection === 'across' 
        ? (puzzleData.clues.across || []).filter(c => c.number > 0)
        : (puzzleData.clues.down || []).filter(c => c.number > 0);

    if (clues.length === 0) return;

    // Find the current clue index directly (handles duplicate clue numbers)
    const currentIndex = findCurrentClueIndex(currentRow, currentCol);

    // Loop backwards through clues, skipping any whose start cell is the
    // same as the current cell (prevents stuck Shift+Tab).
    for (let i = 1; i <= clues.length; i++) {
        const candidateIndex = (currentIndex - i + clues.length) % clues.length;
        const candidate = clues[candidateIndex];
        const start = getClueStartCell(candidate, currentDirection);
        if (start && start[0] === currentRow && start[1] === currentCol) continue;
        focusClueFirstCell(candidate, currentDirection);
        return;
    }

    // All clues share the same start cell — just go back one index.
    const prevIndex = currentIndex > 0 ? currentIndex - 1 : clues.length - 1;
    focusClueFirstCell(clues[prevIndex], currentDirection);
}

// Find the clue number for the word at the given position in the current direction
// Uses cellClueMap to correctly handle bent words where straight-line walking
// would produce ambiguous results.
function findCurrentClueNumber(row, col) {
    // Use cellClueMap first for accurate bent word support
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        return match.number;
    }

    // Fallback: original straight-line logic
    const cellData = puzzleData.cells[row]?.[col];
    
    // First, check if the current cell has a clue number
    if (cellData?.num) {
        // Check if this clue number exists in the clues for the current direction
        const clues = currentDirection === 'across' 
            ? (puzzleData.clues.across || [])
            : (puzzleData.clues.down || []);
        
        if (clues.some(c => c.number === cellData.num)) {
            return cellData.num;
        }
    }
    
    // Otherwise, walk backwards to find the start of the word
    let startRow = row, startCol = col;
    
    if (currentDirection === 'across') {
        while (startCol > 0 && puzzleData.cells[row]?.[startCol - 1] !== null) startCol--;
    } else {
        while (startRow > 0 && puzzleData.cells[startRow - 1]?.[col] !== null) startRow--;
    }
    const startCellData = puzzleData.cells[startRow]?.[startCol];
    return startCellData?.num || 0;
}

function highlightClue(row, col) {
    document.querySelectorAll('.clue-item').forEach(item => item.classList.remove('active'));

    // Use cellClueMap to find the correct clue for this cell + direction
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    let clueNumber = 0;
    let clueDirection = currentDirection;
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        clueNumber = match.number;
        clueDirection = match.direction;
    }

    if (clueNumber <= 0) {
        // Fallback: straight-line walk to find start cell
        let startRow = row, startCol = col;
        if (currentDirection === 'across') {
            while (startCol > 0 && puzzleData.cells[row]?.[startCol - 1] !== null) startCol--;
        } else {
            while (startRow > 0 && puzzleData.cells[startRow - 1]?.[col] !== null) startRow--;
        }
        const startCellData = puzzleData.cells[startRow]?.[startCol];
        clueNumber = startCellData?.num || 0;
    }

    if (clueNumber > 0) {
        const clueItem = document.querySelector(`.clue-item[data-number="${clueNumber}"][data-direction="${clueDirection}"]`);
        if (clueItem) {
            clueItem.classList.add('active');
            const listContainer = clueItem.closest('.clue-list');
            if (listContainer) {
                const target = clueItem.offsetTop - listContainer.offsetTop - 8;
                listContainer.scrollTo({ top: target, behavior: 'smooth' });
            }
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §13  Answer Checking, Hints & Sharing
// ═══════════════════════════════════════════════════════════════════════

// Helper: derive puzzle size variant for server check/hint requests
function getPuzzleSize() {
    const params = new URLSearchParams(window.location.search);
    return params.get('size') || '17x17';
}

// Helper: check answers locally using data-answer attributes (fallback for offline / old cached puzzles)
function checkAnswersLocal() {
    const inputs = document.querySelectorAll('.cell:not(.blocked) input');
    let correct = 0, total = inputs.length, filled = 0;
    inputs.forEach(input => {
        const cell = input.parentElement;
        cell.classList.remove('correct', 'incorrect', 'empty-warning');
        const value = input.value.toUpperCase();
        if (value) {
            filled++;
            if (value === input.dataset.answer) { correct++; }
            else { cell.classList.add('incorrect'); }
        } else { cell.classList.add('empty-warning'); }
    });
    return { correct, total, filled };
}

// Helper: handle a completed check result (correct/total/filled counts)
function handleCheckResult(correct, total, filled) {
    if (filled === total && correct === total) {
        puzzleSolved = true; stopTimer();
        clearProgress();
        document.querySelectorAll('.cell:not(.blocked) input').forEach(i => {
            i.parentElement.classList.remove('empty-warning');
            i.parentElement.classList.add('correct');
        });
        recordPuzzleSolve(seconds);
        renderPlayerStats();
        const hintMsg = (letterHintsUsed + wordHintsUsed) > 0 ? ` med ${formatHintSummary(letterHintsUsed, wordHintsUsed)}` : '';
        announce(`Grattis! Du löste korsordet på ${formatTime(seconds)}${hintMsg}`);
        setTimeout(() => showUsernameModal(), 100);
    } else if (filled < total) {
        const message = `Du har ${total - filled} tomma rutor kvar. ${correct} av ${filled} ifyllda är korrekta.`;
        announce(message);
        showMessageModal('Inte klart ännu', `Du har ${total - filled} tomma rutor kvar. ${correct} av ${filled} ifyllda är korrekta.`);
    } else {
        const errorCount = filled - correct;
        announce(`${errorCount} bokstäver är felaktiga`);
        showMessageModal('Felaktiga bokstäver', `${errorCount} bokstäver är felaktiga. Försök igen!`);
    }
}

async function checkAnswers() {
    if (puzzleSolved) return;
    const inputs = document.querySelectorAll('.cell:not(.blocked) input');
    const total = inputs.length;

    // Try server-side validation
    if (puzzleData.submissionToken && currentPuzzleDate) {
        const cells = {};
        let filled = 0;
        inputs.forEach(input => {
            const cell = input.parentElement;
            cell.classList.remove('correct', 'incorrect', 'empty-warning');
            const key = `${cell.dataset.row},${cell.dataset.col}`;
            const value = input.value.toUpperCase();
            if (value) { cells[key] = value; filled++; }
        });

        try {
            const response = await fetch(`${LEADERBOARD_PROXY_URL}/puzzle/check`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: AbortSignal.timeout(10000),
                body: JSON.stringify({
                    token: puzzleData.submissionToken,
                    puzzleDate: currentPuzzleDate,
                    cells,
                    size: getPuzzleSize()
                })
            });

            if (response.ok) {
                const data = await response.json();
                let correct = 0;
                inputs.forEach(input => {
                    const cell = input.parentElement;
                    const key = `${cell.dataset.row},${cell.dataset.col}`;
                    const value = input.value.toUpperCase();
                    if (!value) {
                        cell.classList.add('empty-warning');
                    } else if (data.results && data.results[key]) {
                        correct++;
                    } else if (value) {
                        cell.classList.add('incorrect');
                    }
                });
                handleCheckResult(correct, total, filled);
                return;
            }
        } catch (e) {
            console.warn('Server check failed, falling back to local:', e);
        }
    }

    // Fallback: check answers locally using data-answer attributes
    const { correct, total: t, filled } = checkAnswersLocal();
    handleCheckResult(correct, t, filled);
}

function clearGrid() {
    showConfirmModal('Rensa korsord', 'Vill du rensa alla svar?', () => {
        document.querySelectorAll('.cell:not(.blocked) input').forEach(input => {
            input.value = '';
            input.parentElement.classList.remove('correct', 'incorrect', 'empty-warning', 'hint-revealed');
        });
        inputEvents = [];
        letterHintsUsed = 0;
        wordHintsUsed = 0;
        updateStats();
        updateClueFilledStatus();
        clearProgress();
    }, 'Rensa', true);
}

function showSolution() {
    showConfirmModal('Visa lösning', 'Vill du visa lösningen? Du kommer inte kunna skicka in ditt resultat.', async () => {
        const inputs = document.querySelectorAll('.cell:not(.blocked) input');
        let revealed = false;

        // Try server-side: request all cell answers
        if (puzzleData.submissionToken && currentPuzzleDate) {
            const cellCoords = [];
            inputs.forEach(input => {
                const cell = input.parentElement;
                cellCoords.push([parseInt(cell.dataset.row), parseInt(cell.dataset.col)]);
            });

            try {
                const response = await fetch(`${LEADERBOARD_PROXY_URL}/puzzle/hint`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    signal: AbortSignal.timeout(10000),
                    body: JSON.stringify({
                        token: puzzleData.submissionToken,
                        puzzleDate: currentPuzzleDate,
                        cells: cellCoords,
                        size: getPuzzleSize()
                    })
                });

                if (response.ok) {
                    const data = await response.json();
                    if (data.letters) {
                        inputs.forEach(input => {
                            const cell = input.parentElement;
                            const key = `${cell.dataset.row},${cell.dataset.col}`;
                            if (data.letters[key]) {
                                input.value = data.letters[key];
                                cell.classList.remove('empty-warning', 'incorrect');
                                cell.classList.add('correct');
                            }
                        });
                        revealed = true;
                    }
                }
            } catch (e) {
                console.warn('Server solution failed, falling back to local:', e);
            }
        }

        // Fallback: local data-answer attributes
        if (!revealed) {
            inputs.forEach(input => {
                if (input.dataset.answer) {
                    input.value = input.dataset.answer;
                    input.parentElement.classList.remove('empty-warning', 'incorrect');
                    input.parentElement.classList.add('correct');
                    revealed = true;
                }
            });
        }

        if (!revealed) {
            showMessageModal('Kunde inte visa lösningen', 'Servern svarade inte. Försök igen om en stund.');
            return;
        }

        puzzleSolved = true; stopTimer(); updateStats();
        updateClueFilledStatus();
        usedShowSolution = true;
        hasSubmittedScore = true;
        trackSolutionView();
        clearProgress();
        const shareBtn = document.getElementById('share-btn');
        if (shareBtn) {
            shareBtn.textContent = '📤 Dela korsord';
            shareBtn.style.display = '';
        }
    }, 'Visa lösning', true);
}

async function revealLetter() {
    if (puzzleSolved) return;
    const activeInput = document.activeElement;
    if (!activeInput || activeInput.tagName !== 'INPUT') {
        announce('Välj en ruta först');
        return;
    }
    const cell = activeInput.parentElement;
    if (!cell || cell.classList.contains('blocked')) return;
    const row = parseInt(cell.dataset.row);
    const col = parseInt(cell.dataset.col);

    // Try server-side hint
    if (puzzleData.submissionToken && currentPuzzleDate) {
        try {
            const response = await fetch(`${LEADERBOARD_PROXY_URL}/puzzle/hint`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: AbortSignal.timeout(10000),
                body: JSON.stringify({
                    token: puzzleData.submissionToken,
                    puzzleDate: currentPuzzleDate,
                    cells: [[row, col]],
                    size: getPuzzleSize()
                })
            });

            if (response.ok) {
                const data = await response.json();
                const key = `${row},${col}`;
                const answer = data.letters && data.letters[key];
                if (answer) {
                    if (activeInput.value.toUpperCase() === answer) {
                        announce('Redan korrekt');
                        return;
                    }
                    activeInput.value = answer;
                    cell.classList.add('hint-revealed');
                    cell.classList.remove('incorrect', 'empty-warning');
                    letterHintsUsed++;
                    trackInput(row, col, answer);
                    updateStats();
                    updateClueFilledStatus();
                    saveProgress();
                    announce(`Avslöjade: ${answer}`);
                    autoCheckIfComplete();
                    return;
                }
            }
        } catch (e) {
            console.warn('Server hint failed, falling back to local:', e);
        }
    }

    // Fallback: local data-answer
    const answer = activeInput.dataset.answer;
    if (!answer) return;
    if (activeInput.value.toUpperCase() === answer) {
        announce('Redan korrekt');
        return;
    }
    activeInput.value = answer;
    cell.classList.add('hint-revealed');
    cell.classList.remove('incorrect', 'empty-warning');
    letterHintsUsed++;
    trackInput(row, col, answer);
    updateStats();
    updateClueFilledStatus();
    saveProgress();
    announce(`Avslöjade: ${answer}`);
    autoCheckIfComplete();
}

async function revealWord() {
    if (puzzleSolved) return;
    const activeInput = document.activeElement;
    if (!activeInput || activeInput.tagName !== 'INPUT') {
        announce('Välj en ruta först');
        return;
    }
    const cell = activeInput.parentElement;
    if (!cell || cell.classList.contains('blocked')) return;
    const row = parseInt(cell.dataset.row);
    const col = parseInt(cell.dataset.col);

    // Find the word cells using cellClueMap (handles bent words)
    let wordCells = null;
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        wordCells = match.cells;
    }
    if (!wordCells) {
        // Fallback: straight-line walk
        wordCells = getWordCellsFallback(0, currentDirection);
        if (!wordCells) wordCells = [{ row, col }];
    }

    // Try server-side hint for all word cells
    let serverLetters = null;
    if (puzzleData.submissionToken && currentPuzzleDate) {
        try {
            const cellCoords = wordCells.map(c => [c.row, c.col]);
            const response = await fetch(`${LEADERBOARD_PROXY_URL}/puzzle/hint`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                signal: AbortSignal.timeout(10000),
                body: JSON.stringify({
                    token: puzzleData.submissionToken,
                    puzzleDate: currentPuzzleDate,
                    cells: cellCoords,
                    size: getPuzzleSize()
                })
            });

            if (response.ok) {
                const data = await response.json();
                if (data.letters) serverLetters = data.letters;
            }
        } catch (e) {
            console.warn('Server hint failed, falling back to local:', e);
        }
    }

    let revealed = 0;
    wordCells.forEach(c => {
        const inp = document.querySelector(`.cell[data-row="${c.row}"][data-col="${c.col}"] input`);
        if (!inp) return;
        const k = `${c.row},${c.col}`;
        const answer = serverLetters ? serverLetters[k] : inp.dataset.answer;
        if (!answer || inp.value.toUpperCase() === answer) return;
        inp.value = answer;
        inp.parentElement.classList.add('hint-revealed');
        inp.parentElement.classList.remove('incorrect', 'empty-warning');
        trackInput(c.row, c.col, answer);
        revealed++;
    });
    if (revealed > 0) {
        wordHintsUsed++;
        updateStats();
        updateClueFilledStatus();
        saveProgress();
        announce(`Avslöjade helt ord (${revealed} bokstäver)`);
        autoCheckIfComplete();
    }
}

function generateShareText() {
    const size = `${puzzleData.width}×${puzzleData.height}`;
    const date = currentPuzzleDate || new Date().toISOString().split('T')[0];
    const difficulty = puzzleData.difficulty || '';
    const diffLabel = difficulty ? ` • ${difficulty}` : '';
    const sizeKey = getPuzzleSize();
    const puzzleUrl = `https://svensktkorsord.se/puzzle.html?date=${date}&size=${sizeKey}`;

    // If show solution was used, only share the puzzle link — no personal result
    if (usedShowSolution) {
        let text = `🇸🇪 Svenskt Korsord ${date}\n`;
        text += `📐 ${size}${diffLabel}\n\n`;
        text += `Testa dagens korsord! 👇\n`;
        text += puzzleUrl;
        return text;
    }

    const time = formatTime(seconds);
    const totalHints = letterHintsUsed + wordHintsUsed;

    // Build emoji grid: 🟩 correct, 🟨 hint-revealed, ⬛ blocked
    let emojiGrid = '';
    for (let r = 0; r < puzzleData.height; r++) {
        let row = '';
        for (let c = 0; c < puzzleData.width; c++) {
            if (puzzleData.cells[r]?.[c] === null) {
                row += '⬛';
            } else {
                const cellEl = document.querySelector(`.cell[data-row="${r}"][data-col="${c}"]`);
                if (cellEl && cellEl.classList.contains('hint-revealed')) {
                    row += '🟨';
                } else {
                    row += '🟩';
                }
            }
        }
        emojiGrid += row + '\n';
    }

    // Header with date, size, and optional difficulty
    let text = `🇸🇪 Svenskt Korsord ${date}\n`;
    text += `📐 ${size}${diffLabel}\n`;
    text += `⏱️ ${time}\n`;

    // Emphasize hints on their own line
    if (totalHints > 0) {
        text += `💡 ${formatHintSummary(letterHintsUsed, wordHintsUsed)}\n`;
    } else {
        text += `🏅 Inga ledtrådar!\n`;
    }

    text += `\n${emojiGrid}\n`;

    // Challenge call-to-action
    text += `Kan du slå min tid? 👇\n`;
    text += puzzleUrl;

    return text;
}

async function shareResult() {
    const text = generateShareText();

    // Always copy to clipboard first
    let copied = false;
    try {
        await navigator.clipboard.writeText(text);
        copied = true;
    } catch (_) { /* clipboard may be unavailable */ }

    // Then offer native share sheet if supported
    if (navigator.share) {
        try {
            await navigator.share({ text });
            // If the share sheet was shown, still confirm the clipboard copy
            if (copied) announce('Resultat kopierat till urklipp');
            return;
        } catch (e) {
            if (e.name === 'AbortError') {
                // User dismissed the share sheet — still show clipboard confirmation
                if (copied) {
                    const btn = document.getElementById('share-btn');
                    if (btn) {
                        const original = btn.textContent;
                        btn.textContent = 'Kopierat! ✓';
                        setTimeout(() => { btn.textContent = original; }, 2000);
                    }
                    announce('Resultat kopierat till urklipp');
                }
                return;
            }
        }
    }

    // No native share — show clipboard confirmation or fallback
    if (copied) {
        const btn = document.getElementById('share-btn');
        if (btn) {
            const original = btn.textContent;
            btn.textContent = 'Kopierat! ✓';
            setTimeout(() => { btn.textContent = original; }, 2000);
        }
        announce('Resultat kopierat till urklipp');
    } else {
        showMessageModal('Dela resultat', 'Kunde inte kopiera automatiskt. Prova att dela via webbläsarmenyn.');
    }
}

// ═══════════════════════════════════════════════════════════════════════
// §14  Timer, Stats & Layout
// ═══════════════════════════════════════════════════════════════════════

function startTimer() { timerInterval = setInterval(() => { if (!puzzleSolved) { seconds++; document.getElementById('timer').textContent = formatTime(seconds); if (seconds % 5 === 0) saveProgress(); } }, 1000); }
function stopTimer() { clearInterval(timerInterval); }
function formatTime(s) { return `${Math.floor(s/60).toString().padStart(2,'0')}:${(s%60).toString().padStart(2,'0')}`; }
function updateStats() {
    const inputs = document.querySelectorAll('.cell:not(.blocked) input');
    const filled = Array.from(inputs).filter(i => i.value).length;
    document.getElementById('stats').textContent = `${filled}/${inputs.length} rutor ifyllda (${Math.round(filled/inputs.length*100)}%)`;
}

function updateClueFilledStatus() {
    // Use each clue's own cells array for precise identification
    (puzzleData.clues.across || []).filter(clue => clue.number > 0).forEach((clue, idx) => {
        const isFilled = isWordFilledByClue(clue, 'across');
        const clueItem = document.querySelector(`.clue-item[data-direction="across"][data-clue-index="${idx}"]`)
            || document.querySelector(`.clue-item[data-number="${clue.number}"][data-direction="across"]`);
        if (clueItem) clueItem.classList.toggle('filled', isFilled);
    });

    (puzzleData.clues.down || []).filter(clue => clue.number > 0).forEach((clue, idx) => {
        const isFilled = isWordFilledByClue(clue, 'down');
        const clueItem = document.querySelector(`.clue-item[data-direction="down"][data-clue-index="${idx}"]`)
            || document.querySelector(`.clue-item[data-number="${clue.number}"][data-direction="down"]`);
        if (clueItem) clueItem.classList.toggle('filled', isFilled);
    });
}

function isWordFilledByClue(clue, direction) {
    let cells;
    if (clue.cells && clue.cells.length > 0) {
        cells = clue.cells.map(c => ({ row: c[0], col: c[1] }));
    } else {
        cells = getWordCells(clue.number, direction);
    }
    if (!cells || cells.length === 0) return false;
    return cells.every(cell => {
        const input = document.querySelector(`.cell[data-row="${cell.row}"][data-col="${cell.col}"] input`);
        return input && input.value.trim() !== '';
    });
}

function getWordCells(number, direction) {
    // Try to find the clue with its cells array (handles bent words correctly)
    const clues = direction === 'across'
        ? (puzzleData.clues.across || [])
        : (puzzleData.clues.down || []);
    const clue = clues.find(c => c.number === number);
    if (clue && clue.cells && clue.cells.length > 0) {
        return clue.cells.map(c => ({ row: c[0], col: c[1] }));
    }

    // Fallback: straight-line walk
    let startRow = -1, startCol = -1;
    outer: for (let row = 0; row < puzzleData.height; row++) {
        for (let col = 0; col < puzzleData.width; col++) {
            if (puzzleData.cells[row]?.[col]?.num === number) { startRow = row; startCol = col; break outer; }
        }
    }
    if (startRow < 0) return null;
    const cells = [];
    if (direction === 'across') {
        for (let c = startCol; c < puzzleData.width; c++) {
            if (puzzleData.cells[startRow]?.[c] === null) break;
            cells.push({ row: startRow, col: c });
        }
    } else {
        for (let r = startRow; r < puzzleData.height; r++) {
            if (puzzleData.cells[r]?.[startCol] === null) break;
            cells.push({ row: r, col: startCol });
        }
    }
    return cells;
}

// Compute and set a pixel-precise --cell-size based on container and its children
function computeCellSize() {
    const grid = document.getElementById('crossword-grid');
    const gridSection = document.querySelector('.grid-section');
    const mainLayout = document.getElementById('main-layout');
    const gridArea = document.querySelector('.grid-inner .grid-area') || gridSection;
    if (!grid || !gridSection || !puzzleData) return;

    const cols = Math.max(1, puzzleData.width || parseInt(getComputedStyle(grid).getPropertyValue('--cols')) || 11);
    const rows = Math.max(1, puzzleData.height || parseInt(getComputedStyle(grid).getPropertyValue('--rows')) || 11);

    // Determine layout mode
    const isLandscape = window.matchMedia('(max-width:1100px) and (orientation: landscape)').matches;
    const isDesktop = window.matchMedia('(min-width:1100px)').matches;

    // On desktop, compute cell size from both available width and height
    // to prevent cells from overflowing the grid section border.
    if (isDesktop) {
        const gap = 1;
        // Measure available width from the grid area container
        const containerWidth = gridArea.clientWidth;
        if (containerWidth <= 0) {
            grid.style.removeProperty('--cell-size');
            grid.style.width = 'auto';
            grid.style.height = 'auto';
            return;
        }

        // Account for grid's own border and padding
        const gridStyle = window.getComputedStyle(grid);
        const borderX = (parseFloat(gridStyle.borderLeftWidth) || 0) + (parseFloat(gridStyle.borderRightWidth) || 0);
        const padX = (parseFloat(gridStyle.paddingLeft) || 0) + (parseFloat(gridStyle.paddingRight) || 0);
        const extraX = borderX + padX;

        const contentWidth = containerWidth - extraX;
        const cellByWidth = (contentWidth - (cols - 1) * gap) / cols;

        // Height constraint — match CSS breakpoint reserved heights and max cell sizes
        const vw = window.innerWidth;
        let reservedHeight, maxCell;
        if (vw >= 2560) { reservedHeight = 200; maxCell = 88; }
        else if (vw >= 1920) { reservedHeight = 220; maxCell = 80; }
        else if (vw >= 1400) { reservedHeight = 240; maxCell = 72; }
        else { reservedHeight = 280; maxCell = 64; }

        const minCell = 36;
        const cellByHeight = (window.innerHeight - reservedHeight) / rows;

        // Floor to avoid sub-pixel rounding causing overflow
        let chosen = Math.floor(Math.min(cellByWidth, cellByHeight, maxCell));
        chosen = Math.max(minCell, chosen);

        if (isFinite(chosen) && chosen >= minCell) {
            grid.style.setProperty('--cell-size', chosen + 'px');
        } else {
            grid.style.removeProperty('--cell-size');
        }
        grid.style.width = 'auto';
        grid.style.height = 'auto';
        return;
    }

    // For mobile/landscape, compute precise cell size
    const measureArea = isLandscape ? gridArea : gridSection;

    // Compute measured sizes from measureArea (the area the grid should fill)
    let areaWidth = Math.max(40, measureArea.clientWidth);
    let areaHeight = Math.max(40, measureArea.clientHeight);

    // Account for any elements inside the measureArea (e.g., .stats, .grid-header) that consume vertical space
    let insideReserved = 0;
    const statsEl = document.querySelector('.stats');
    const gridHeader = document.querySelector('.grid-header');

    if (statsEl && measureArea.contains(statsEl)) {
        insideReserved += statsEl.offsetHeight;
    }
    if (gridHeader && gridSection.contains(gridHeader)) {
        insideReserved += gridHeader.offsetHeight + 12; // 12 for margin
    }

    // Read grid element's extra chrome (border + padding) so we can translate between outer and content sizes
    const gridStyle = window.getComputedStyle(grid);
    const borderX = (parseFloat(gridStyle.borderLeftWidth) || 0) + (parseFloat(gridStyle.borderRightWidth) || 0);
    const borderY = (parseFloat(gridStyle.borderTopWidth) || 0) + (parseFloat(gridStyle.borderBottomWidth) || 0);
    const padX = (parseFloat(gridStyle.paddingLeft) || 0) + (parseFloat(gridStyle.paddingRight) || 0);
    const padY = (parseFloat(gridStyle.paddingTop) || 0) + (parseFloat(gridStyle.paddingBottom) || 0);
    const extraX = borderX + padX;
    const extraY = borderY + padY;

    const gap = 1; // px between cells as used in CSS

    // Mobile portrait: size cells purely by available width — the page scrolls naturally
    if (!isLandscape) {
        const safety = 6;
        const contentAvailW = Math.max(20, areaWidth - safety - extraX);
        const cellByWidth = (contentAvailW - (cols - 1) * gap) / cols;

        const minCell = 12;
        const maxCell = 64;
        let chosen = Math.min(cellByWidth, maxCell);
        if (!isFinite(chosen) || chosen < minCell) chosen = minCell;
        chosen = Math.max(minCell, chosen);

        grid.style.setProperty('--cell-size', chosen + 'px');
        grid.style.width = 'auto';
        grid.style.height = 'auto';
        return;
    }

    // Landscape: fit both width and height so the grid fits without scrolling
    const maxOuterW = Math.max(40, areaWidth);
    const maxOuterH = Math.max(40, areaHeight - insideReserved);

    // Content area available for cells = outer minus grid chrome
    const contentAvailW = Math.max(20, maxOuterW - extraX);
    const contentAvailH = Math.max(20, maxOuterH - extraY);

    // Candidate cell sizes as floats to allow fine-grained fit
    const cellByWidthFloat = (contentAvailW - (cols - 1) * gap) / cols;
    const cellByHeightFloat = (contentAvailH - (rows - 1) * gap) / rows;

    // sensible clamps for mobile
    const minCell = 12;
    const maxCell = 64;

    // Choose the largest fractional cell that fits both dimensions
    let chosen = Math.min(cellByWidthFloat, cellByHeightFloat, maxCell);
    if (!isFinite(chosen) || chosen < minCell) chosen = minCell;
    chosen = Math.max(minCell, Math.min(chosen, maxCell));

    // Apply the computed size
    grid.style.setProperty('--cell-size', chosen + 'px');

    // Compute desired outer sizes (content + chrome)
    const totalContentW = chosen * cols + (cols - 1) * gap;
    const totalContentH = chosen * rows + (rows - 1) * gap;
    const desiredOuterW = totalContentW + extraX;
    const desiredOuterH = totalContentH + extraY;

    // Clamp to available outer space
    const finalW = Math.min(desiredOuterW, maxOuterW);
    const finalH = Math.min(desiredOuterH, maxOuterH);

    // Set explicit outer size so the grid visually fills measureArea as much as possible
    grid.style.boxSizing = 'border-box';
    grid.style.width = finalW + 'px';
    grid.style.height = finalH + 'px';
}

// Move timer between header and controls depending on layout (landscape => controls)
function updateTimerPosition() {
    const timer = document.getElementById('timer');
    const controls = document.querySelector('.grid-inner .controls');
    const header = document.querySelector('.grid-header');
    if (!timer || !controls || !header) return;

    const isLandscape = window.matchMedia('(max-width:1100px) and (orientation: landscape)').matches;
    if (isLandscape) {
        if (!controls.contains(timer)) {
            // move timer into controls at the top
            controls.insertBefore(timer, controls.firstChild);
            timer.classList.add('timer-in-controls');
        }
    } else {
        if (!header.contains(timer)) {
            // move timer back to header (append at end)
            header.appendChild(timer);
            timer.classList.remove('timer-in-controls');
        }
    }
}

// Ensure computeCellSize runs on resize and after rendering (with throttling for performance)
(function(){
    const onResizeHandler = () => {
        computeCellSize();
        syncCluesHeight();
        updateTimerPosition();
    };
    
    // Throttle resize events to max 10 times per second for better performance
    const throttledResize = throttle(onResizeHandler, 100);
    
    window.addEventListener('resize', throttledResize);
    window.addEventListener('orientationchange', onResizeHandler); // Don't throttle orientation changes
})();

// Ensure initial compute once DOM is stable
setTimeout(() => { computeCellSize(); updateTimerPosition(); }, 120);

// ═══════════════════════════════════════════════════════════════════════
// §15  Keyboard Shortcuts & Mobile Panels
// ═══════════════════════════════════════════════════════════════════════

// ── Keyboard shortcuts help dialog ──
function toggleShortcutsHelp() {
    const overlay = document.getElementById('shortcuts-overlay');
    if (!overlay) return;
    const visible = overlay.style.display !== 'none';
    overlay.style.display = visible ? 'none' : 'flex';
    if (!visible) overlay.querySelector('.shortcuts-close')?.focus();
}
function trapFocusInShortcuts(e) {
    const overlay = document.getElementById('shortcuts-overlay');
    if (!overlay || overlay.style.display === 'none') return;
    if (e.key !== 'Tab') return;
    const focusable = overlay.querySelectorAll('button, [href], [tabindex]:not([tabindex="-1"])');
    if (focusable.length === 0) return;
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (e.shiftKey) { if (document.activeElement === first) { e.preventDefault(); last.focus(); } }
    else { if (document.activeElement === last) { e.preventDefault(); first.focus(); } }
}
(function() {
    document.addEventListener('keydown', (e) => {
        trapFocusInShortcuts(e);
        // "?" opens/closes help (only when not typing in an input or modal username field)
        if (e.key === '?' && !e.target.closest('input')) {
            e.preventDefault();
            toggleShortcutsHelp();
            return;
        }
        // Escape closes the shortcuts dialog if open
        if (e.key === 'Escape') {
            const overlay = document.getElementById('shortcuts-overlay');
            if (overlay && overlay.style.display !== 'none') {
                overlay.style.display = 'none';
                e.stopPropagation();
            }
        }
    });
    document.addEventListener('DOMContentLoaded', () => {
        const helpBtn = document.getElementById('help-toggle');
        if (helpBtn) helpBtn.addEventListener('click', toggleShortcutsHelp);
        const closeBtn = document.getElementById('shortcuts-close');
        if (closeBtn) closeBtn.addEventListener('click', toggleShortcutsHelp);
        const overlay = document.getElementById('shortcuts-overlay');
        if (overlay) overlay.addEventListener('click', (e) => {
            if (e.target === overlay) toggleShortcutsHelp();
        });
    });
})();

document.addEventListener('DOMContentLoaded', loadPuzzle);

// Toggle handling for mobile panels
(function(){
  const cluesToggle = document.getElementById('clues-toggle');
  const leaderboardToggle = document.getElementById('leaderboard-toggle');
  const introToggle = document.getElementById('intro-toggle');
  const historyToggle = document.getElementById('history-toggle');

  const updateVisibility = () => {
    const isSmall = window.matchMedia('(max-width:1100px)').matches;
    if (!isSmall) {
      document.body.classList.remove('show-leaderboard');
      document.body.classList.remove('show-intro');
      document.body.classList.remove('show-history');
      document.body.classList.remove('hide-clues');
    }
    if (cluesToggle) cluesToggle.style.display = isSmall ? 'inline-block' : 'none';
    if (leaderboardToggle) leaderboardToggle.style.display = isSmall ? 'inline-block' : 'none';
    if (introToggle) introToggle.style.display = isSmall ? 'inline-block' : 'none';
    if (historyToggle) historyToggle.style.display = 'none';

    // Default: clues visible on mobile unless hide-clues is set
    if (isSmall && !document.body.classList.contains('hide-clues')) {
      cluesToggle && cluesToggle.setAttribute('aria-expanded', 'true');
    }
    // Reflect intro toggle state
    if (introToggle) {
      introToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-intro')));
    }
    if (leaderboardToggle) {
      leaderboardToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-leaderboard')));
    }
    if (historyToggle) {
      historyToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-history')));
    }
  };

  if (cluesToggle) cluesToggle.addEventListener('click', () => {
    document.body.classList.toggle('hide-clues');
    const cluesVisible = !document.body.classList.contains('hide-clues');
    // When clues become visible, close leaderboard, history and intro to prioritize clues
    if (cluesVisible) {
      document.body.classList.remove('show-leaderboard');
      document.body.classList.remove('show-intro');
      document.body.classList.remove('show-history');
    } else {
      // When clues are hidden, also ensure intro, history and leaderboard are closed
      document.body.classList.remove('show-leaderboard');
      document.body.classList.remove('show-intro');
      document.body.classList.remove('show-history');
    }
    cluesToggle.setAttribute('aria-expanded', String(cluesVisible));
  });

  if (leaderboardToggle) leaderboardToggle.addEventListener('click', () => {
    document.body.classList.toggle('show-leaderboard');
    if (document.body.classList.contains('show-leaderboard')) {
      document.body.classList.add('hide-clues');
      document.body.classList.remove('show-intro');
      renderLeaderboardHistory();
    } else {
      // Closing leaderboard: restore default view
      document.body.classList.remove('hide-clues');
    }
    leaderboardToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-leaderboard')));
  });

  if (historyToggle) historyToggle.addEventListener('click', () => {
    document.body.classList.toggle('show-history');
    if (document.body.classList.contains('show-history')) {
      document.body.classList.add('hide-clues');
      document.body.classList.remove('show-intro');
      renderLeaderboardHistory();
    } else {
      // Closing history: restore default view
      document.body.classList.remove('hide-clues');
    }
    historyToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-history')));
  });

  if (introToggle) introToggle.addEventListener('click', () => {
    // Toggle the class that CSS checks to show the intro on mobile
    document.body.classList.toggle('show-intro');
    // when intro is shown, hide clues, leaderboard and history to prioritize intro
    if (document.body.classList.contains('show-intro')) {
      document.body.classList.add('hide-clues');
      document.body.classList.remove('show-leaderboard');
      document.body.classList.remove('show-history');
    }
    introToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-intro')));
  });

  window.addEventListener('resize', updateVisibility);
  document.addEventListener('DOMContentLoaded', updateVisibility);

  // After puzzle completion and modal close, show leaderboard on small screens
  // and reveal the share button in the controls area.
  const originalClose = window.closeModal;
  window.closeModal = function(){
    originalClose && originalClose();
    if (puzzleSolved) {
      const shareBtn = document.getElementById('share-btn');
      if (shareBtn) shareBtn.style.display = '';
      if (window.matchMedia('(max-width:1100px)').matches) {
        document.body.classList.add('show-leaderboard');
        document.body.classList.add('hide-clues');
        if (leaderboardToggle) leaderboardToggle.setAttribute('aria-expanded','true');
        renderLeaderboardHistory();
      }
    }
  };
})();

// Detect clue-list overflow and add fade indicator
(function(){
  function checkClueOverflow(){
    document.querySelectorAll('.clue-direction').forEach(dir => {
      const list = dir.querySelector('.clue-list');
      if(list) dir.classList.toggle('has-overflow', list.scrollHeight > list.clientHeight + 4);
    });
  }
  window.addEventListener('resize', checkClueOverflow);
  const obs = new MutationObserver(checkClueOverflow);
  document.addEventListener('DOMContentLoaded', () => {
    const cl = document.getElementById('across-clues');
    if(cl) obs.observe(cl, {childList:true});
    const dl = document.getElementById('down-clues');
    if(dl) obs.observe(dl, {childList:true});
    checkClueOverflow();
  });
})();
