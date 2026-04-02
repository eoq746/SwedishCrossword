# Cloudflare Worker — Leaderboard API

This worker provides the leaderboard API for the crossword frontend, storing all data in Cloudflare KV. It also tracks solution views per IP.

## Endpoints

| Method | Path                    | Description                                   |
|--------|-------------------------|-----------------------------------------------|
| GET    | `/leaderboard`          | Fetch current leaderboard scores              |
| PUT    | `/leaderboard`          | Update leaderboard scores                     |
| POST   | `/leaderboard/history`  | Archive a score entry for historical tracking (stores `puzzleHash` for per-puzzle grouping) |
| GET    | `/leaderboard/history`  | Fetch historical leaderboard (query: `?days=30`) |
| POST   | `/viewed-solution`      | Record that an IP viewed a solution           |
| POST   | `/check-solution-viewed`| Check if an IP has viewed a solution          |

## Required KV Namespace

| Binding Name   | Description                                              |
|----------------|----------------------------------------------------------|
| `CROSSWORD_KV` | Stores current leaderboard, solution-view records (7-day TTL), and historical leaderboard entries (90-day TTL, grouped per puzzle hash) |

Create via: `wrangler kv namespace create CROSSWORD_KV`

## Deployment

```bash
npx wrangler deploy worker.js
```

## Local Development

Run:

```bash
npx wrangler dev worker.js
```
