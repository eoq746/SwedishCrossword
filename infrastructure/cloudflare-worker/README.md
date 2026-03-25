# Cloudflare Worker — Leaderboard Proxy

This worker acts as a secure proxy between the crossword frontend and JSONBin.io, keeping API keys server-side. It also tracks solution views per IP using Cloudflare KV.

## Endpoints

| Method | Path                    | Description                                   |
|--------|-------------------------|-----------------------------------------------|
| GET    | `/leaderboard`          | Fetch current leaderboard scores              |
| PUT    | `/leaderboard`          | Update leaderboard scores                     |
| POST   | `/leaderboard/history`  | Archive a score entry for historical tracking |
| GET    | `/leaderboard/history`  | Fetch historical leaderboard (query: `?days=30`) |
| POST   | `/viewed-solution`      | Record that an IP viewed a solution           |
| POST   | `/check-solution-viewed`| Check if an IP has viewed a solution          |

## Required Environment Variables

Configure these as **secrets** in the Cloudflare dashboard (Settings → Variables):

| Variable          | Description                        |
|-------------------|------------------------------------|
| `JSONBIN_API_KEY`  | JSONBin.io API access key          |
| `JSONBIN_BIN_ID`   | JSONBin.io bin ID for leaderboard  |

## Required KV Namespace

| Binding Name   | Description                                              |
|----------------|----------------------------------------------------------|
| `CROSSWORD_KV` | Stores solution-view records (7-day TTL) and historical leaderboard entries (90-day TTL) |

Create via: `wrangler kv namespace create CROSSWORD_KV`

## Deployment

```bash
npx wrangler deploy worker.js
```

## Local Development

Create a `.dev.vars` file (git-ignored) with your secrets:

```
JSONBIN_API_KEY=your-key-here
JSONBIN_BIN_ID=your-bin-id-here
```

Then run:

```bash
npx wrangler dev worker.js
```
