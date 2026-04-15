// Service Worker for Svenskt Korsord
// Caches the app shell for offline access and puzzle data for offline play.
// __BUILD_VERSION__ is replaced at Docker build time with the git commit SHA.

const CACHE_VERSION = '__BUILD_VERSION__';
const SHELL_CACHE = `shell-${CACHE_VERSION}`;
const PUZZLE_CACHE = `puzzle-${CACHE_VERSION}`;

// App shell: static assets that rarely change
const SHELL_ASSETS = [
    '/',
    '/index.html',
    '/puzzle.html',
    '/calendar.html',
    '/site.js',
    '/site.min.css',
    '/site.webmanifest',
    '/favicon-32x32.png',
    '/favicon-16x16.png',
    '/apple-touch-icon.png',
    '/android-chrome-192x192.png',
    '/android-chrome-512x512.png'
];

// Install: pre-cache the app shell
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(SHELL_CACHE).then((cache) => cache.addAll(SHELL_ASSETS))
    );
    self.skipWaiting();
});

// Activate: clean up old caches
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(
                keys.filter((k) => k !== SHELL_CACHE && k !== PUZZLE_CACHE)
                    .map((k) => caches.delete(k))
            )
        )
    );
    self.clients.claim();
});

// Fetch strategy:
// - API puzzle requests: network-first, cache response for offline play
// - App shell assets: cache-first, fall back to network
// - Everything else: network-first with cache fallback
self.addEventListener('fetch', (event) => {
    const url = new URL(event.request.url);

    // Only handle GET requests from our origin
    if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;

    // Puzzle API: network-first so we always get the latest, cache for offline
    if (url.pathname.startsWith('/api/puzzle/')) {
        event.respondWith(
            fetch(event.request)
                .then((response) => {
                    if (response.ok) {
                        const clone = response.clone();
                        caches.open(PUZZLE_CACHE).then((cache) => cache.put(event.request, clone));
                    }
                    return response;
                })
                .catch(() => caches.match(event.request))
        );
        return;
    }

    // App shell: cache-first
    if (SHELL_ASSETS.includes(url.pathname)) {
        event.respondWith(
            caches.match(event.request).then((cached) => {
                const networkFetch = fetch(event.request).then((response) => {
                    if (response.ok) {
                        const clone = response.clone();
                        caches.open(SHELL_CACHE).then((cache) => cache.put(event.request, clone));
                    }
                    return response;
                });
                return cached || networkFetch;
            })
        );
        return;
    }

    // Everything else: network-first with cache fallback
    event.respondWith(
        fetch(event.request)
            .then((response) => {
                if (response.ok) {
                    const clone = response.clone();
                    caches.open(SHELL_CACHE).then((cache) => cache.put(event.request, clone));
                }
                return response;
            })
            .catch(() => caches.match(event.request))
    );
});
