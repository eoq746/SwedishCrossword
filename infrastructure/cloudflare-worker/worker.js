// Cloudflare Worker for Leaderboard
const ALLOWED_ORIGINS = [
    'https://svensktkorsord.se',
    'https://www.svensktkorsord.se',
    'https://eoq746.github.io'
];

// Rate limiting: max requests per IP within the window
const RATE_LIMIT = {
    maxRequests: 30,
    windowSeconds: 60
};

// Input constraints
const MAX_PUZZLE_HASH_LENGTH = 20;
const MAX_NAME_LENGTH = 30;
const MAX_BODY_SIZE = 50 * 1024; // 50 KB
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

// Validate that puzzleHash is a short, safe alphanumeric string
function isValidPuzzleHash(hash) {
    if (typeof hash !== 'string') return false;
    if (hash.length === 0 || hash.length > MAX_PUZZLE_HASH_LENGTH) return false;
    return /^[a-zA-Z0-9_-]+$/.test(hash);
}

// Sanitise a username: trim, truncate, strip control characters
function sanitiseName(name) {
    if (typeof name !== 'string') return '';
    // eslint-disable-next-line no-control-regex
    return name.replace(/[\x00-\x1F\x7F]/g, '').trim().substring(0, MAX_NAME_LENGTH);
}

// Simple sliding-window rate limiter backed by KV
async function checkRateLimit(env, ip) {
    const key = `ratelimit:${ip}`;
    const now = Math.floor(Date.now() / 1000);
    const record = await env.CROSSWORD_KV.get(key, 'json');

    let hits = 1;
    if (record && (now - record.start) < RATE_LIMIT.windowSeconds) {
        hits = record.hits + 1;
    }

    await env.CROSSWORD_KV.put(key, JSON.stringify({ start: record && (now - record.start) < RATE_LIMIT.windowSeconds ? record.start : now, hits }), {
        expirationTtl: RATE_LIMIT.windowSeconds
    });

    return hits > RATE_LIMIT.maxRequests;
}

export default {
    async fetch(request, env) {
        // Handle CORS preflight
        if (request.method === 'OPTIONS') {
            return handleCORS(request);
        }

        // Validate origin — strict equality only (no startsWith)
        const origin = request.headers.get('Origin') || '';
        if (!ALLOWED_ORIGINS.includes(origin)) {
            return new Response(JSON.stringify({ error: 'Forbidden' }), {
                status: 403,
                headers: { 'Content-Type': 'application/json' }
            });
        }

        const url = new URL(request.url);
        const path = url.pathname;
        const clientIP = request.headers.get('CF-Connecting-IP') || 'unknown';

        // Rate limiting
        try {
            if (await checkRateLimit(env, clientIP)) {
                return new Response(JSON.stringify({ error: 'Too many requests' }), {
                    status: 429,
                    headers: { ...corsHeaders(origin), 'Retry-After': String(RATE_LIMIT.windowSeconds) }
                });
            }
        } catch (_) {
            // If rate-limit check itself fails, allow the request through
        }

        try {
            // Reject oversized request bodies early
            const contentLength = parseInt(request.headers.get('Content-Length') || '0', 10);
            if (contentLength > MAX_BODY_SIZE) {
                return new Response(JSON.stringify({ error: 'Payload too large' }), {
                    status: 413,
                    headers: corsHeaders(origin)
                });
            }

            //
            // --- LEADERBOARD ENDPOINTS ---
            //

            // GET /leaderboard
            if (request.method === 'GET' && path === '/leaderboard') {
                const data = await env.CROSSWORD_KV.get('leaderboard:current', 'json') || {};
                return new Response(JSON.stringify(data), {
                    headers: corsHeaders(origin)
                });
            }

            // PUT /leaderboard — validate structure before writing
            if (request.method === 'PUT' && path === '/leaderboard') {
                const body = await request.json();

                // Body must be an object with a 'scores' property
                if (!body || typeof body !== 'object' || Array.isArray(body) || !body.scores || typeof body.scores !== 'object') {
                    return new Response(JSON.stringify({ error: 'Invalid payload: expected { scores: { ... } }' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                // Limit the total number of date-keys and entries per key
                const dateKeys = Object.keys(body.scores);
                if (dateKeys.length > 30) {
                    return new Response(JSON.stringify({ error: 'Too many leaderboard date keys' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                // Validate and sanitise each entry
                for (const key of dateKeys) {
                    const entries = body.scores[key];
                    if (!Array.isArray(entries)) {
                        return new Response(JSON.stringify({ error: `Invalid entries for key ${key}` }), {
                            status: 400,
                            headers: corsHeaders(origin)
                        });
                    }
                    if (entries.length > 20) {
                        body.scores[key] = entries.slice(0, 20);
                    }
                    for (const entry of body.scores[key]) {
                        if (typeof entry.name === 'string') {
                            entry.name = sanitiseName(entry.name);
                        }
                        if (typeof entry.time !== 'number' || entry.time < 0 || entry.time > 86400) {
                            entry.time = 0;
                        }
                    }
                }

                const serialised = JSON.stringify(body);
                if (serialised.length > MAX_BODY_SIZE) {
                    return new Response(JSON.stringify({ error: 'Payload too large after validation' }), {
                        status: 413,
                        headers: corsHeaders(origin)
                    });
                }

                await env.CROSSWORD_KV.put('leaderboard:current', serialised);

                return new Response(JSON.stringify({ success: true }), {
                    headers: corsHeaders(origin)
                });
            }

            //
            // --- HISTORICAL LEADERBOARD ENDPOINTS ---
            //

            // POST /leaderboard/history
            if (path === '/leaderboard/history' && request.method === 'POST') {
                const { date, entry } = await request.json();

                if (!date || !DATE_PATTERN.test(date)) {
                    return new Response(JSON.stringify({ error: 'Invalid date format' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                if (!entry || typeof entry.time !== 'number' || entry.time < 0 || entry.time > 86400) {
                    return new Response(JSON.stringify({ error: 'Invalid entry' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                const name = sanitiseName(entry.name);
                if (!name) {
                    return new Response(JSON.stringify({ error: 'Invalid name' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                if (entry.puzzleHash && !isValidPuzzleHash(entry.puzzleHash)) {
                    return new Response(JSON.stringify({ error: 'Invalid puzzleHash' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                const kvKey = `leaderboard-history:${date}`;
                const existing = await env.CROSSWORD_KV.get(kvKey, 'json') || [];

                const isDuplicate = existing.some(e =>
                    e.name === name && e.time === entry.time && e.timestamp === entry.timestamp
                );

                if (!isDuplicate) {
                    const record = {
                        name,
                        time: entry.time,
                        timestamp: entry.timestamp
                    };
                    if (entry.puzzleHash) record.puzzleHash = entry.puzzleHash;
                    existing.push(record);

                    // Keep top 10 per puzzle
                    const groups = {};
                    existing.forEach(e => {
                        const g = e.puzzleHash || '_default';
                        if (!groups[g]) groups[g] = [];
                        groups[g].push(e);
                    });
                    let trimmed = [];
                    for (const g of Object.values(groups)) {
                        g.sort((a, b) => a.time - b.time);
                        trimmed = trimmed.concat(g.slice(0, 10));
                    }

                    await env.CROSSWORD_KV.put(kvKey, JSON.stringify(trimmed), {
                        expirationTtl: 86400 * 90
                    });
                }

                return new Response(JSON.stringify({ ok: true }), {
                    headers: corsHeaders(origin)
                });
            }

            // GET /leaderboard/history
            if (path === '/leaderboard/history' && request.method === 'GET') {
                const rawDays = parseInt(url.searchParams.get('days') || '30', 10);
                const days = Math.min(Number.isFinite(rawDays) && rawDays > 0 ? rawDays : 30, 90);
                const history = {};

                const now = new Date();
                const fetches = [];
                for (let i = 0; i < days; i++) {
                    const d = new Date(now);
                    d.setDate(d.getDate() - i);
                    const dateStr = d.toISOString().split('T')[0];
                    fetches.push(
                        env.CROSSWORD_KV.get(`leaderboard-history:${dateStr}`, 'json')
                            .then(data => { if (data) history[dateStr] = data; })
                    );
                }

                await Promise.all(fetches);

                return new Response(JSON.stringify(history), {
                    headers: corsHeaders(origin)
                });
            }

            // Default 404
            return new Response(JSON.stringify({ error: 'Not found' }), {
                status: 404,
                headers: corsHeaders(origin)
            });

        } catch (error) {
            // Never expose internal error details to the client
            console.error('Worker error:', error);
            return new Response(JSON.stringify({ error: 'Internal server error' }), {
                status: 500,
                headers: corsHeaders(origin)
            });
        }
    }
};

function corsHeaders(origin) {
    return {
        'Content-Type': 'application/json',
        'Access-Control-Allow-Origin': origin,
        'Access-Control-Allow-Methods': 'GET, PUT, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type'
    };
}

function handleCORS(request) {
    const origin = request.headers.get('Origin') || '';
    if (ALLOWED_ORIGINS.includes(origin)) {
        return new Response(null, { headers: corsHeaders(origin) });
    }
    return new Response('Forbidden', { status: 403 });
}