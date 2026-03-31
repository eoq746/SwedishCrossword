// Cloudflare Worker for JSONBin Proxy + Solution View Tracking
const ALLOWED_ORIGINS = [
    'https://svensktkorsord.se',
    'https://www.svensktkorsord.se',
    'https://eoq746.github.io'
];

export default {
    async fetch(request, env) {
        // Handle CORS preflight
        if (request.method === 'OPTIONS') {
            return handleCORS(request);
        }

        // Validate origin
        const origin = request.headers.get('Origin') || '';
        if (!ALLOWED_ORIGINS.some(allowed => origin.startsWith(allowed) || origin === allowed)) {
            return new Response(JSON.stringify({ error: 'Forbidden: Invalid origin' }), {
                status: 403,
                headers: { 'Content-Type': 'application/json' }
            });
        }

        const url = new URL(request.url);
        const path = url.pathname;
        const clientIP = request.headers.get('CF-Connecting-IP');

        try {
            //
            // -------------------------------
            // NEW: Track when someone views the solution
            // -------------------------------
            //
            if (path === '/viewed-solution' && request.method === 'POST') {
                const { puzzleHash } = await request.json();

                await env.CROSSWORD_KV.put(
                    `solution-viewed:${puzzleHash}:${clientIP}`,
                    Date.now().toString(),
                    { expirationTtl: 86400 * 7 } // 7 days
                );

                return new Response(JSON.stringify({ ok: true }), {
                    headers: corsHeaders(origin)
                });
            }

            //
            // -------------------------------
            // NEW: Check if IP viewed solution
            // -------------------------------
            //
            if (path === '/check-solution-viewed' && request.method === 'POST') {
                const { puzzleHash } = await request.json();

                const viewed = await env.CROSSWORD_KV.get(
                    `solution-viewed:${puzzleHash}:${clientIP}`
                );

                return new Response(JSON.stringify({ viewed: !!viewed }), {
                    headers: corsHeaders(origin)
                });
            }

            //
            // -------------------------------
            // EXISTING LEADERBOARD ENDPOINTS
            // -------------------------------
            //

            // GET /leaderboard - Fetch leaderboard
            if (request.method === 'GET' && path === '/leaderboard') {
                const response = await fetch(
                    `https://api.jsonbin.io/v3/b/${env.JSONBIN_BIN_ID}/latest`,
                    {
                        headers: { 'X-Access-Key': env.JSONBIN_API_KEY }
                    }
                );

                const data = await response.json();
                return new Response(JSON.stringify(data.record || {}), {
                    headers: corsHeaders(origin)
                });
            }

            // PUT /leaderboard - Update leaderboard
            if (request.method === 'PUT' && path === '/leaderboard') {
                const body = await request.json();

                const response = await fetch(
                    `https://api.jsonbin.io/v3/b/${env.JSONBIN_BIN_ID}`,
                    {
                        method: 'PUT',
                        headers: {
                            'Content-Type': 'application/json',
                            'X-Access-Key': env.JSONBIN_API_KEY
                        },
                        body: JSON.stringify(body)
                    }
                );

                if (!response.ok) {
                    return new Response(JSON.stringify({ error: 'Failed to update' }), {
                        status: response.status,
                        headers: corsHeaders(origin)
                    });
                }

                return new Response(JSON.stringify({ success: true }), {
                    headers: corsHeaders(origin)
                });
            }

            //
            // -------------------------------
            // HISTORICAL LEADERBOARD ENDPOINTS
            // -------------------------------
            //

            // POST /leaderboard/history - Archive a score entry for a puzzle date
            if (path === '/leaderboard/history' && request.method === 'POST') {
                const { date, entry } = await request.json();

                if (!date || !entry || !entry.name || typeof entry.time !== 'number') {
                    return new Response(JSON.stringify({ error: 'Invalid payload' }), {
                        status: 400,
                        headers: corsHeaders(origin)
                    });
                }

                const kvKey = `leaderboard-history:${date}`;
                const existing = await env.CROSSWORD_KV.get(kvKey, 'json') || [];

                // Avoid duplicates based on name + time + timestamp
                const isDuplicate = existing.some(e =>
                    e.name === entry.name && e.time === entry.time && e.timestamp === entry.timestamp
                );

                if (!isDuplicate) {
                    const record = {
                        name: entry.name,
                        time: entry.time,
                        timestamp: entry.timestamp
                    };
                    if (entry.puzzleHash) record.puzzleHash = entry.puzzleHash;
                    existing.push(record);

                    // Keep top 10 per puzzle to avoid one puzzle evicting another's entries
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
                        expirationTtl: 86400 * 90 // 90 days
                    });
                }

                return new Response(JSON.stringify({ ok: true }), {
                    headers: corsHeaders(origin)
                });
            }

            // GET /leaderboard/history - Fetch historical leaderboard data
            if (path === '/leaderboard/history' && request.method === 'GET') {
                const days = Math.min(parseInt(url.searchParams.get('days') || '30', 10), 90);
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
            return new Response(JSON.stringify({ error: error.message }), {
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
    if (ALLOWED_ORIGINS.some(allowed => origin.startsWith(allowed))) {
        return new Response(null, { headers: corsHeaders(origin) });
    }
    return new Response('Forbidden', { status: 403 });
}