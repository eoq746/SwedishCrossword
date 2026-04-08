# Cloudflare Worker — Leaderboard API

This worker provides the leaderboard API for the crossword frontend, storing all data in Cloudflare KV. No personal data (such as IP addresses) is stored.

## Endpoints

| Method | Path                    | Description                                   |
|--------|-------------------------|-----------------------------------------------|
| GET    | `/leaderboard`          | Fetch current leaderboard scores              |
| PUT    | `/leaderboard`          | Update leaderboard scores (validated schema)  |
| POST   | `/leaderboard/history`  | Archive a score entry for historical tracking (stores `puzzleHash` for per-puzzle grouping) |
| GET    | `/leaderboard/history`  | Fetch historical leaderboard (query: `?days=30`) |

## Security Measures

- **CORS**: Strict origin equality check against an allowlist (no prefix matching)
- **Rate limiting**: IP-based sliding window (30 requests / 60 seconds) backed by KV — IPs are used only transiently for rate-limit keys with auto-expiry and are never persisted
- **Input validation**: All user-supplied values (`puzzleHash`, `name`, `date`, `time`) are validated for type, length, and format before use
- **Payload size limit**: Request bodies exceeding 50 KB are rejected (HTTP 413)
- **Privacy**: No IP addresses or personal identifiers are stored in KV. Solution-view tracking is handled entirely client-side via localStorage.
- **Error sanitisation**: Internal error details are never exposed to clients
- **Leaderboard schema enforcement**: `PUT /leaderboard` requires a `{ scores: { ... } }` structure with per-key entry limits

## Required KV Namespace

| Binding Name   | Description                                              |
|----------------|----------------------------------------------------------|
| `CROSSWORD_KV` | Stores current leaderboard, rate-limit windows (auto-expiring), and historical leaderboard entries (90-day TTL, grouped per puzzle hash) |

Create via: `wrangler kv namespace create CROSSWORD_KV`

## Configuration

The `wrangler.toml` file contains the worker name, KV namespace bindings, and observability settings.

## Deployment

```bash
npx wrangler deploy worker.js
```

## Local Development

Run:

```bash
npx wrangler dev worker.js
```
