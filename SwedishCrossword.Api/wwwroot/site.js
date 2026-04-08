/*
 * LEADERBOARD CONFIGURATION
 * =========================
 * Uses the API backend for leaderboard storage.
 * Falls back to localStorage when the API is unreachable.
 */

const LEADERBOARD_PROXY_URL = '/api';

const LEADERBOARD_ENABLED = true;

/*
 * ANTI-CHEAT CONFIGURATION
 * ========================
 * These settings help prevent cheating on the leaderboard.
 */
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

// Re-entrancy guard for handleFocus to prevent focus loops
let lastFocusedCell = null;
let lastFocusTime = 0;
const FOCUS_DEBOUNCE_MS = 50;

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

// Analyze input pattern for suspicious activity
function analyzeInputPattern() {
    if (!ANTI_CHEAT.enabled) return { valid: true, reasons: [] };
    
    const reasons = [];
    const cellCount = countCells();
    
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

// Count fillable cells
function countCells() {
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

// Fetch leaderboard from Cloudflare Worker proxy
async function fetchRemoteLeaderboard() {
    if (!LEADERBOARD_ENABLED) return null;
    
    try {
        const response = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard`);
        
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

// Save leaderboard via Cloudflare Worker proxy
async function saveRemoteLeaderboard(leaderboard, newEntry) {
    if (!LEADERBOARD_ENABLED) return false;

    try {
        const getResponse = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard`);

        let allScores = {};
        if (getResponse.ok) {
            const data = await getResponse.json();
            allScores = data.scores || {};
        }

        const leaderboardKey = `${currentPuzzleDate}-${puzzleHash}`;

        // Merge with existing remote entries to avoid overwriting scores
        // submitted by other users since we last fetched
        const existing = (allScores[leaderboardKey] || []).filter(validateScoreEntry);
        const merged = [...leaderboard];
        existing.forEach(remote => {
            const isDuplicate = merged.some(e =>
                e.name === remote.name && e.time === remote.time && e.timestamp === remote.timestamp
            );
            if (!isDuplicate) merged.push(remote);
        });

        merged.sort((a, b) => {
            if (a.flagged && !b.flagged) return 1;
            if (!a.flagged && b.flagged) return -1;
            return a.time - b.time;
        });

        const cleanEntries = merged.filter(e => !e.flagged);
        const flaggedEntries = merged.filter(e => e.flagged);
        allScores[leaderboardKey] = cleanEntries.length >= 10
            ? cleanEntries.slice(0, 10)
            : [...cleanEntries, ...flaggedEntries].slice(0, 10);

        const cutoffDate = new Date();
        cutoffDate.setDate(cutoffDate.getDate() - 7);
        const cutoffStr = cutoffDate.toISOString().split('T')[0];

        for (const key of Object.keys(allScores)) {
            const dateMatch = key.match(/^(\d{4}-\d{2}-\d{2})/);
            if (dateMatch && dateMatch[1] < cutoffStr) {
                delete allScores[key];
            }
        }

        const putResponse = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ scores: allScores })
        });

        if (!putResponse.ok) {
            console.warn('Failed to save remote leaderboard:', putResponse.status);
            return false;
        }

        // Archive the new entry to historical leaderboard
        if (newEntry) {
            fetch(`${LEADERBOARD_PROXY_URL}/leaderboard/history`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ date: currentPuzzleDate, entry: newEntry })
            }).catch(e => console.warn('Failed to archive history entry:', e));
        }

        return true;
    } catch (e) {
        console.error('Error saving remote leaderboard:', e);
        return false;
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

// Add score to leaderboard with anti-cheat validation
async function addToLeaderboard(username, timeSeconds) {
    HAS_VIEWED_SOLUTION = checkIfViewedSolution();
    const validation = analyzeInputPattern();
    
    if (!validation.valid) {
        console.warn('Anti-cheat validation failed:', validation.reasons);
        suspiciousActivity = validation.reasons;
    }

    let leaderboard = await loadLeaderboard();

    const entry = createScoreEntry(username, timeSeconds);

    if (!validation.valid) {
        entry.flagged = true;
        entry.reasons = validation.reasons;
    }

    leaderboard.push(entry);

    leaderboard.sort((a, b) => {
        if (a.flagged && !b.flagged) return 1;
        if (!a.flagged && b.flagged) return -1;
        return a.time - b.time;
    });

    const cleanEntries = leaderboard.filter(e => !e.flagged);
    const flaggedEntries = leaderboard.filter(e => e.flagged);

    if (cleanEntries.length >= 10) {
        leaderboard = cleanEntries.slice(0, 10);
    } else {
        leaderboard = [...cleanEntries, ...flaggedEntries].slice(0, 10);
    }

    saveLocalLeaderboard(leaderboard);
    remoteLeaderboardCache = leaderboard;

    if (LEADERBOARD_ENABLED && validation.valid) {
        saveRemoteLeaderboard(leaderboard, entry).then(success => {
            if (!success) console.warn('Remote save failed, score saved locally only');
        });
    }

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
        dateEl.textContent = `Korsord: ${currentPuzzleDate}${modeText}`;
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
        return `
            <li class="leaderboard-item ${rankClass} ${isCurrentUser ? 'current-user' : ''}" ${isFlagged ? 'style="opacity: 0.6;"' : ''}>
                <span class="leaderboard-rank">${rankDisplay}</span>
                <span class="leaderboard-name">${escapeHtml(entry.name)}${isFlagged ? `<span class="flag-icon" title="${escapeHtml(flagTooltip)}">⚠</span>` : ''}</span>
                <span class="leaderboard-time">${formatTime(entry.time)}</span>
            </li>
        `;
    }).join('');
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

async function showUsernameModal() {
    if (hasSubmittedScore) return;
    
    HAS_VIEWED_SOLUTION = checkIfViewedSolution();
    const validation = analyzeInputPattern();

    document.getElementById('modal-time').textContent = formatTime(seconds);
    document.getElementById('username-modal').classList.add('active');
    
    const modalContent = document.querySelector('.modal');
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

    const savedName = localStorage.getItem('crossword-username') || '';
    document.getElementById('username-input').value = savedName;
    document.getElementById('username-input').focus();
    document.getElementById('username-input').select();
}

function closeModal() {
    document.getElementById('username-modal').classList.remove('active');
}

async function submitScore() {
    const input = document.getElementById('username-input');
    let username = input.value.trim();

    if (!username) username = 'Anonym';

    localStorage.setItem('crossword-username', username);

    await addToLeaderboard(username, seconds);
    hasSubmittedScore = true;

    closeModal();
    await renderLeaderboard();
}

// Fetch historical leaderboard from Cloudflare Worker
async function fetchLeaderboardHistory(days = 30) {
    if (!LEADERBOARD_ENABLED) return {};

    try {
        const response = await fetch(`${LEADERBOARD_PROXY_URL}/leaderboard/history?days=${days}`);
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

// Render historical leaderboard
async function renderLeaderboardHistory() {
    const container = document.getElementById('history-list');
    if (!container) return;

    container.innerHTML = '<li class="leaderboard-empty">Laddar historik...</li>';

    const history = await fetchLeaderboardHistory();
    const dates = Object.keys(history).sort().reverse();

    if (dates.length === 0) {
        container.innerHTML = '<li class="leaderboard-empty">Ingen historik tillgänglig ännu.</li>';
        return;
    }

    const medals = ['🥇', '🥈', '🥉'];

    container.innerHTML = dates.map(date => {
        const entries = history[date];

        // Group entries by puzzleHash to detect multiple puzzles on the same date
        const puzzleGroups = new Map();
        entries.forEach(entry => {
            const key = entry.puzzleHash || '_default';
            if (!puzzleGroups.has(key)) puzzleGroups.set(key, []);
            puzzleGroups.get(key).push(entry);
        });

        const hasMultiplePuzzles = puzzleGroups.size > 1;

        let groupsHtml = '';
        let puzzleIndex = 0;
        for (const [, groupEntries] of puzzleGroups) {
            puzzleIndex++;
            const puzzleLabel = hasMultiplePuzzles
                ? `<span class="history-puzzle-label">Pussel ${puzzleIndex}</span>`
                : '';
            const rows = groupEntries.map((entry, index) => {
                const rankDisplay = index < 3 ? medals[index] : `${index + 1}.`;
                const rankClass = index < 3 ? `rank-${index + 1}` : '';
                return `
                    <li class="leaderboard-item history-item ${rankClass}">
                        <span class="leaderboard-rank">${rankDisplay}</span>
                        <span class="leaderboard-name">${escapeHtml(entry.name)}</span>
                        <span class="leaderboard-time">${formatTime(entry.time)}</span>
                    </li>`;
            }).join('');
            groupsHtml += `${puzzleLabel}<ul class="history-entries">${rows}</ul>`;
        }

        return `
            <li class="history-date-group">
                <h4 class="history-date-heading">${date}</h4>
                ${groupsHtml}
            </li>`;
    }).join('');
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
});

async function loadPuzzle() {
    try {
        const response = await fetch('/api/puzzle/today');
        if (response.ok) {
            puzzleData = await response.json();
            console.log('Loaded puzzle from API');
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
    puzzleHash = generatePuzzleHash();
    inputEvents = [];
    usedShowSolution = false;
    suspiciousActivity = [];
    devToolsOpenedDuringSession = false;
    
    devToolsDetector.startMonitoring();
    
    if (puzzleData.createdAt) {
        currentPuzzleDate = puzzleData.createdAt.split(' ')[0];
    } else {
        currentPuzzleDate = new Date().toISOString().split('T')[0];
    }
    
    if (puzzleData.wordCount) {
        document.getElementById('puzzle-info').style.display = 'inline-block';
        document.getElementById('info-size').textContent = `${puzzleData.width}x${puzzleData.height}`;
        document.getElementById('info-words').textContent = `${puzzleData.wordCount} ord`;
        document.getElementById('info-fill').textContent = `${puzzleData.fillPercentage}%`;
    }
    
    if (puzzleData.createdAt) {
        document.getElementById('generation-date').textContent = `Genererat: ${puzzleData.createdAt}`;
    } else {
        const today = new Date().toLocaleDateString('sv-SE');
        document.getElementById('generation-date').textContent = today;
    }
    
    renderGrid();
    renderClues();
    buildCellClueMap();
    loadProgress();
    await renderLeaderboard();

    // Auto-load history data on desktop (no manual toggle needed)
    if (!window.matchMedia('(max-width:1100px)').matches) {
        renderLeaderboardHistory();
    }

    syncCluesHeight();
    startTimer();
    updateStats();
    updateClueFilledStatus();

    window.addEventListener('resize', syncCluesHeight);
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

// Call syncCluesHeight on resize also
window.addEventListener('resize', syncCluesHeight);

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
                input.dataset.answer = cellData.letter;
                
                // Mobile-friendly input attributes
                input.autocomplete = 'off';
                input.autocorrect = 'off';
                input.autocapitalize = 'characters';  // Force uppercase on each keystroke
                input.spellcheck = false;
                input.inputMode = 'text';  // Show standard text keyboard
                input.enterKeyHint = 'next';  // Show "Next" on mobile keyboard
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
        li.innerHTML = `<span class="clue-number">${clue.number}. </span>${clue.clue}`;
        li.dataset.number = clue.number;
        li.dataset.direction = 'across';
        li.dataset.clueIndex = idx;
        li.addEventListener('click', () => focusClue(clue.number, 'across'));
        acrossContainer.appendChild(li);
    });

    validDownClues.forEach((clue, idx) => {
        const li = document.createElement('li');
        li.className = 'clue-item';
        li.innerHTML = `<span class="clue-number">${clue.number}. </span>${clue.clue}`;
        li.dataset.number = clue.number;
        li.dataset.direction = 'down';
        li.dataset.clueIndex = idx;
        li.addEventListener('click', () => focusClue(clue.number, 'down'));
        downContainer.appendChild(li);
    });
}

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
}

// Toggle direction between across and down
function toggleDirection(row, col) {
    currentDirection = currentDirection === 'across' ? 'down' : 'across';
    highlightWord(row, col);
    highlightClue(row, col);
    updateDirectionButton();
    announce(currentDirection === 'across' ? 'Vågrätt' : 'Lodrätt');
}

// Update the direction toggle button's visual state
function updateDirectionButton() {
    const btn = document.getElementById('direction-toggle');
    if (!btn) return;
    
    const icon = btn.querySelector('.direction-icon');
    const text = btn.querySelector('.direction-text');
    
    if (currentDirection === 'down') {
        btn.classList.add('down');
        if (text) text.textContent = 'Lodrätt';
    } else {
        btn.classList.remove('down');
        if (text) text.textContent = 'Vågrätt';
    }
}

// Handler for the direction toggle button click
function toggleDirectionButton() {
    // Find the currently focused cell
    const activeInput = document.activeElement;
    if (activeInput && activeInput.tagName === 'INPUT') {
        const cell = activeInput.parentElement;
        if (cell && cell.classList.contains('cell')) {
            const row = parseInt(cell.dataset.row);
            const col = parseInt(cell.dataset.col);
            toggleDirection(row, col);
            return;
        }
    }
    
    // If no cell is focused, just toggle the direction state
    currentDirection = currentDirection === 'across' ? 'down' : 'across';
    updateDirectionButton();
    announce(currentDirection === 'across' ? 'Vågrätt' : 'Lodrätt');
}

// Make toggleDirectionButton available globally
window.toggleDirectionButton = toggleDirectionButton;

// Set up direction toggle button to prevent focus loss
document.addEventListener('DOMContentLoaded', () => {
    const directionBtn = document.getElementById('direction-toggle');
    if (directionBtn) {
        // Prevent the button from stealing focus on mousedown/touchstart
        directionBtn.addEventListener('mousedown', (e) => {
            e.preventDefault();
        });
        directionBtn.addEventListener('touchstart', (e) => {
            e.preventDefault();
        }, { passive: false });
        
        // Handle the actual toggle on click/touchend
        directionBtn.addEventListener('click', (e) => {
            e.preventDefault();
            toggleDirectionButton();
        });
        directionBtn.addEventListener('touchend', (e) => {
            e.preventDefault();
            toggleDirectionButton();
        }, { passive: false });
    }
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
}

function highlightWord(row, col) {
    document.querySelectorAll('.cell.word-highlight').forEach(c => c.classList.remove('word-highlight'));

    // Use cellClueMap to find the correct cells for this word (handles bent words)
    const key = `${row},${col}`;
    const entries = cellClueMap[key];
    if (entries && entries.length > 0) {
        const match = findBestEntry(entries, currentDirection, row, col);
        match.cells.forEach(c => {
            document.querySelector(`.cell[data-row="${c.row}"][data-col="${c.col}"]`)?.classList.add('word-highlight');
        });
        return;
    }

    // Fallback: straight-line walk (for cells not covered by any clue)
    if (currentDirection === 'across') {
        let startCol = col;
        while (startCol > 0 && puzzleData.cells[row]?.[startCol - 1] !== null) startCol--;
        for (let c = startCol; c < puzzleData.width && puzzleData.cells[row]?.[c] !== null; c++) {
            document.querySelector(`.cell[data-row="${row}"][data-col="${c}"]`)?.classList.add('word-highlight');
        }
    } else {
        let startRow = row;
        while (startRow > 0 && puzzleData.cells[startRow - 1]?.[col] !== null) startRow--;
        for (let r = startRow; r < puzzleData.height && puzzleData.cells[r]?.[col] !== null; r++) {
            document.querySelector(`.cell[data-row="${r}"][data-col="${col}"]`)?.classList.add('word-highlight');
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
    
    // Get the clue number from the start cell
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

function checkAnswers() {
    const inputs = document.querySelectorAll('.cell:not(.blocked) input');
    let correct = 0, total = inputs.length, filled = 0;
    inputs.forEach(input => {
        const cell = input.parentElement;
        cell.classList.remove('correct', 'incorrect', 'empty-warning');
        const value = input.value.toUpperCase();
        if (value) {
            filled++;
            if (value === input.dataset.answer) { correct++; cell.classList.add('correct'); }
            else { cell.classList.add('incorrect'); }
        } else { cell.classList.add('empty-warning'); }
    });
    if (filled === total && correct === total) {
        puzzleSolved = true; stopTimer();
        clearProgress();
        inputs.forEach(i => i.parentElement.classList.remove('empty-warning'));
        announce(`Grattis! Du löste korsordet på ${formatTime(seconds)}`);
        setTimeout(() => showUsernameModal(), 100);
    } else if (filled < total) {
        const message = `Du har ${total - filled} tomma rutor kvar. ${correct} av ${filled} ifyllda är korrekta.`;
        announce(message);
        alert(`Du har ${total - filled} tomma rutor kvar.\n\n${correct} av ${filled} ifyllda är korrekta.`);
    } else {
        const errorCount = filled - correct;
        announce(`${errorCount} bokstäver är felaktiga`);
        alert(`${errorCount} bokstäver är felaktiga. Försök igen!`);
    }
}

function clearGrid() {
    if (confirm('Vill du rensa alla svar?')) {
        document.querySelectorAll('.cell:not(.blocked) input').forEach(input => {
            input.value = '';
            input.parentElement.classList.remove('correct', 'incorrect', 'empty-warning');
        });
        inputEvents = [];
        updateStats();
        updateClueFilledStatus();
        clearProgress();
    }
}

function showSolution() {
    if (confirm('Vill du visa lösningen?')) {
        document.querySelectorAll('.cell:not(.blocked) input').forEach(input => {
            input.value = input.dataset.answer;
            input.parentElement.classList.remove('empty-warning', 'incorrect');
            input.parentElement.classList.add('correct');
        });
        puzzleSolved = true; stopTimer(); updateStats();
        updateClueFilledStatus();
        usedShowSolution = true;
        hasSubmittedScore = true;
        trackSolutionView();
        clearProgress();
    }
}

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

function isWordFilled(number, direction) {
    // ...existing code...
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

    // Safety margin: smaller in landscape to maximize grid
    const safety = isLandscape ? 0 : 6;

    // Effective outer-space for the grid element inside measureArea
    const maxOuterW = Math.max(40, areaWidth - safety);
    const maxOuterH = Math.max(40, areaHeight - safety - insideReserved);

    // Content area available for cells = outer minus grid chrome
    const contentAvailW = Math.max(20, maxOuterW - extraX);
    const contentAvailH = Math.max(20, maxOuterH - extraY);

    const gap = 1; // px between cells as used in CSS

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
    if (historyToggle) historyToggle.style.display = isSmall ? 'inline-block' : 'none';

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
    const nowShown = !document.body.classList.contains('show-leaderboard');
    document.body.classList.toggle('show-leaderboard');
    // hide clues when leaderboard shown
    if (document.body.classList.contains('show-leaderboard')) {
      document.body.classList.add('hide-clues');
      document.body.classList.remove('show-history');
    }
    leaderboardToggle.setAttribute('aria-expanded', String(document.body.classList.contains('show-leaderboard')));
  });

  if (historyToggle) historyToggle.addEventListener('click', () => {
    document.body.classList.toggle('show-history');
    if (document.body.classList.contains('show-history')) {
      document.body.classList.add('hide-clues');
      document.body.classList.remove('show-leaderboard');
      document.body.classList.remove('show-intro');
      renderLeaderboardHistory();
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
  const originalClose = window.closeModal;
  window.closeModal = function(){
    originalClose && originalClose();
    if (puzzleSolved && window.matchMedia('(max-width:1100px)').matches){
      document.body.classList.add('show-leaderboard');
      if (leaderboardToggle) leaderboardToggle.setAttribute('aria-expanded','true');
    }
  };
})();
