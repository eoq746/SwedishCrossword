import { useEffect } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { usePageTitle } from '../hooks/usePageTitle';
import '../styles/static-pages.css';

declare global {
  interface Window {
    loadPuzzle?: () => void;
    revealLetter?: () => void;
    revealWord?: () => void;
    toggleShortcutsHelp?: () => void;
    submitScore?: () => void;
    __cspNonce__?: string;
  }
}

const SIZES = [
  { key: '10x10', label: '🟢 Liten', sub: '10×10' },
  { key: '15x15', label: '🟡 Mellan', sub: '15×15' },
  { key: '17x17', label: '🔴 Stor', sub: '17×17' },
];

// Static HTML structure that site.js expects to find in the DOM.
// React renders this once via dangerouslySetInnerHTML so it never touches
// the children again — site.js manages that subtree directly.
const PUZZLE_HTML = `
<div id="announcements" class="sr-only" aria-live="polite" aria-atomic="true"></div>

<div class="puzzle-info" id="puzzle-info" style="display:none;">
  <span id="info-size"></span>
  <span id="info-words"></span>
  <span id="info-fill"></span>
  <span id="info-difficulty" hidden></span>
</div>
<div class="generation-date" id="generation-date"></div>

<section aria-label="Spelinstruktioner" class="intro-section" id="intro-section">
  <h2>Så här spelar du</h2>
  <div class="info-cards">
    <div class="info-card">
      <div class="info-card-icon">🎮</div>
      <h3>Kontroller</h3>
      <ul>
        <li><kbd>Klicka</kbd> på en ruta för att aktivera</li>
        <li><kbd>Mellanslag</kbd> byter riktning</li>
        <li><kbd>Piltangenter</kbd> navigerar</li>
        <li><kbd>Tab</kbd> nästa ledtråd, <kbd>Shift+Tab</kbd> föregående</li>
        <li><kbd>Backspace</kbd> raderar och flyttar</li>
      </ul>
    </div>
    <div class="info-card">
      <div class="info-card-icon">🏆</div>
      <h3>Topplista</h3>
      <p>Lös korsordet och registrera din tid! Topplistan visar de 10 snabbaste tiderna och nollställs vid midnatt.</p>
    </div>
  </div>
</section>

<div id="loading" class="loading">
  <div class="loading-spinner"></div>
  <p>Laddar korsord...</p>
</div>

<div class="top-controls">
  <button id="clues-toggle" class="clues-toggle" aria-expanded="true" aria-controls="across-clues down-clues">Visa ledtrådar</button>
  <button id="leaderboard-toggle" class="clues-toggle" aria-expanded="false" aria-controls="leaderboard-list">Visa Topplista</button>
  <button id="history-toggle" class="clues-toggle" aria-expanded="false" aria-controls="history-section">Visa Historik</button>
  <button id="intro-toggle" class="clues-toggle" aria-expanded="false" aria-controls="intro-section">Visa info</button>
  <button id="help-toggle" class="clues-toggle" aria-label="Tangentbordsgenvägar" title="Tangentbordsgenvägar (?)">⌨️</button>
</div>

<div class="main-layout site-container" id="main-layout" role="main" style="display:none;">
  <div class="grid-section">
    <div class="grid-header">
      <h2>Dagens Korsord</h2>
      <div class="timer" id="timer">00:00</div>
    </div>
    <div class="grid-inner">
      <div class="controls">
        <button class="btn btn-primary" onclick="checkAnswers()">Kontrollera</button>
        <button class="btn btn-secondary" onclick="clearGrid()">Rensa</button>
        <button class="btn btn-hint" id="hint-letter-btn" title="Avslöja bokstav">💡 Bokstav</button>
        <button class="btn btn-hint" id="hint-word-btn" title="Avslöja ord">💡 Ord</button>
        <button class="btn btn-success" onclick="showSolution()">Visa lösning</button>
        <button class="btn btn-share" id="share-btn" onclick="shareResult()" style="display:none;">📤 Dela resultat</button>
        <div class="stats" id="stats"></div>
      </div>
      <div class="grid-area">
        <div class="crossword-grid" id="crossword-grid" role="grid" aria-label="Korsord"></div>
      </div>
    </div>
  </div>

  <div class="clues-section" id="clues-section">
    <h2>Ledtrådar</h2>
    <div class="clues-columns">
      <div class="clue-column">
        <div class="clue-direction">
          <h3 id="across-clues-heading">Vågrätt</h3>
          <ul class="clue-list" id="across-clues" role="list" aria-labelledby="across-clues-heading"></ul>
        </div>
      </div>
      <div class="clue-column">
        <div class="clue-direction">
          <h3 id="down-clues-heading">Lodrätt</h3>
          <ul class="clue-list" id="down-clues" role="list" aria-labelledby="down-clues-heading"></ul>
        </div>
      </div>
    </div>
  </div>

  <div class="leaderboard-section" aria-label="Topplista">
    <h2>Topplista</h2>
    <ul class="leaderboard-list" id="leaderboard-list" role="list"></ul>
    <div class="leaderboard-date" id="leaderboard-date"></div>
    <div id="friends-leaderboard-section" style="display:none;margin-top:20px;">
      <h3 style="font-size:1.1rem;">👥 Vänners resultat</h3>
      <ul class="leaderboard-list" id="friends-leaderboard-list" role="list"></ul>
    </div>
  </div>

  <div class="leaderboard-section history-section" id="history-section">
    <h2>Historisk Topplista</h2>
    <ul class="leaderboard-list" id="history-list"></ul>
  </div>

  <div class="player-stats-section" id="player-stats-section">
    <h2>Din Statistik</h2>
    <div id="player-stats"></div>
    <div id="personal-stats" style="display:none;"></div>
  </div>
</div>

<!-- Modals -->
<div class="modal-overlay" id="message-modal" role="dialog" aria-modal="true" aria-labelledby="message-modal-title">
  <div class="modal">
    <h3 id="message-modal-title"></h3>
    <p id="message-modal-body"></p>
    <div class="modal-buttons" id="message-modal-buttons"></div>
  </div>
</div>

<div class="modal-overlay" id="username-modal" role="dialog" aria-modal="true" aria-label="Spara resultat">
  <div class="modal">
    <h3>Grattis!</h3>
    <p>Du löste korsordet!</p>
    <div class="modal-time" id="modal-time">00:00</div>
    <p>Ange ditt namn för topplistan:</p>
    <input type="text" id="username-input" placeholder="Ditt namn" maxlength="20"
           autocomplete="off" autocorrect="off" autocapitalize="words" spellcheck="false">
    <div id="modal-login-menu" style="display:none;margin-bottom:8px;"></div>
    <div class="modal-buttons">
      <button class="btn btn-primary" onclick="submitScore()">Spara</button>
      <button class="btn btn-secondary" onclick="closeModal()">Hoppa över</button>
    </div>
  </div>
</div>

<div class="shortcuts-overlay" id="shortcuts-overlay" role="dialog" aria-label="Tangentbordsgenvägar" aria-modal="true" style="display:none;">
  <div class="shortcuts-card">
    <button class="shortcuts-close" id="shortcuts-close" aria-label="Stäng">&times;</button>
    <h2>⌨️ Tangentbordsgenvägar</h2>
    <dl class="shortcuts-list">
      <div class="shortcut-row"><dt><kbd>A</kbd>–<kbd>Ö</kbd></dt><dd>Skriv bokstav</dd></div>
      <div class="shortcut-row"><dt><kbd>Mellanslag</kbd></dt><dd>Byt riktning (vågrätt ↔ lodrätt)</dd></div>
      <div class="shortcut-row"><dt><kbd>Tab</kbd></dt><dd>Nästa ledtråd</dd></div>
      <div class="shortcut-row"><dt><kbd>Shift</kbd>+<kbd>Tab</kbd></dt><dd>Föregående ledtråd</dd></div>
      <div class="shortcut-row"><dt><kbd>←</kbd> <kbd>→</kbd></dt><dd>Flytta vågrätt</dd></div>
      <div class="shortcut-row"><dt><kbd>↑</kbd> <kbd>↓</kbd></dt><dd>Flytta lodrätt</dd></div>
      <div class="shortcut-row"><dt><kbd>Backsteg</kbd></dt><dd>Radera &amp; flytta bakåt</dd></div>
      <div class="shortcut-row"><dt><kbd>Delete</kbd></dt><dd>Radera utan att flytta</dd></div>
      <div class="shortcut-row"><dt><kbd>?</kbd></dt><dd>Visa/dölj den här hjälpen</dd></div>
    </dl>
  </div>
</div>
`;

export default function PuzzlePage() {
  const [searchParams] = useSearchParams();
  const size = searchParams.get('size') ?? '17x17';
  const date = searchParams.get('date') ?? '';

  usePageTitle(date ? `Korsord ${date}` : 'Spela Korsord');

  useEffect(() => {
    const SCRIPT_ID = 'puzzle-site-js';

    // Remove any previously injected instance (size/date change via key remount should
    // already unmount, but guard anyway).
    document.getElementById(SCRIPT_ID)?.remove();

    // site.js is same-origin so 'self' in script-src allows it without a nonce.
    const script = document.createElement('script');
    script.id = SCRIPT_ID;
    script.src = '/site.js';
    script.defer = false;
    script.async = false;

    script.onload = () => {
      // DOMContentLoaded has already fired in the SPA context, so call the
      // entry point manually and wire up the handlers that site.js normally
      // registers inside its DOMContentLoaded listeners.
      window.loadPuzzle?.();

      // Hint buttons: site.js uses preventDefault on mousedown/touchstart
      // to keep grid focus, then invokes the action on click/touchend.
      wireButton('hint-letter-btn', () => window.revealLetter?.());
      wireButton('hint-word-btn', () => window.revealWord?.());

      // Keyboard shortcuts dialog close handlers.
      const shortcutsClose = document.getElementById('shortcuts-close');
      shortcutsClose?.addEventListener('click', () => window.toggleShortcutsHelp?.());

      const shortcutsOverlay = document.getElementById('shortcuts-overlay');
      shortcutsOverlay?.addEventListener('click', (e) => {
        if (e.target === e.currentTarget) window.toggleShortcutsHelp?.();
      });

      // Help toggle button (added to top-controls in our React HTML).
      const helpToggle = document.getElementById('help-toggle');
      helpToggle?.addEventListener('click', () => window.toggleShortcutsHelp?.());

      // Username modal Enter key.
      const usernameInput = document.getElementById('username-input') as HTMLInputElement | null;
      usernameInput?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); window.submitScore?.(); }
      });
    };

    document.body.appendChild(script);

    return () => {
      script.remove();
      // Clean up body-level CSS classes site.js adds for mobile panels.
      document.body.classList.remove('hide-clues', 'show-leaderboard', 'show-intro', 'show-history');
    };
    // Dependencies intentionally omitted — this effect must run only once per
    // mount. Size/date changes force a remount via the key prop in App.tsx.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <>
      {/* Size selector — React-managed, not part of the game DOM */}
      <div className="puzzle-size-selector">
        {SIZES.map(s => (
          <Link
            key={s.key}
            to={`/puzzle${date ? `?size=${s.key}&date=${date}` : `?size=${s.key}`}`}
            className={`size-tab${size === s.key ? ' active' : ''}`}
          >
            {s.label} <span className="size-sub">{s.sub}</span>
          </Link>
        ))}
      </div>

      {/* Game container — React sets innerHTML once and never reconciles children */}
      <div dangerouslySetInnerHTML={{ __html: PUZZLE_HTML }} />
    </>
  );
}

/** Wires a focus-preserving button the same way site.js does in its DOMContentLoaded block. */
function wireButton(id: string, action: () => void) {
  const btn = document.getElementById(id);
  if (!btn) return;
  btn.addEventListener('mousedown', (e) => e.preventDefault());
  btn.addEventListener('touchstart', (e) => e.preventDefault(), { passive: false });
  btn.addEventListener('click', (e) => { e.preventDefault(); action(); });
  btn.addEventListener('touchend', (e) => { e.preventDefault(); action(); }, { passive: false });
}
