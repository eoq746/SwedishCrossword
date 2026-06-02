# Copilot Instructions

## Project Guidelines
- Prioritize low-cost security hardening and avoid architectures that add significant ongoing cost due to a strict Azure budget (~$50/month subscription credit).
- Favor always-on database options for this app; avoid Azure SQL auto-pause because it harms user experience.
- Defer Azure VNet/private endpoint implementation due to budget constraints; prioritize non-costly security hardening first.
- Use React as the frontend framework shell for migration.

## React Development Guidelines
- Separate grid logic from rendering for React puzzle code.
- Use reducer-based navigation for state management.
- Favor data-driven cell models for better performance and maintainability.
- Split grid, clues, and leaderboard into separate components rather than a monolithic page component.
- Use app-wide CSS theming with a common shared source of truth in token files; keep only minor component-specific overrides in scoped CSS files.
- Implement the cookie consent UI as a floating banner overlaying the page rather than inline text in the document flow.
- For the puzzle page, present the clues panel as a single column split vertically into separate horizontal and vertical clue sections, with each section scrolling internally.
- Apply edits or removals to a clue to the clue’s original source JSON file (dsso, kelly, lexin, synonyms, or custom), not always to custom-words.json.
- In the clue report dialog, leave the suggested clue field empty by default.
- For the friends leaderboard on the puzzle page, show it only to authenticated users, use a tab switch with the global leaderboard to avoid clutter, and include only accepted friends. Include the current user as a reference entry alongside accepted friends. If an authenticated user has no accepted friends yet, still show the Friends leaderboard tab with an empty state instead of hiding it. If there are accepted friends but none have solved the current puzzle yet, provide a distinct empty state for that scenario. Remember the user's last selected tab between Global and Friends, likely via local storage. Use standard ranking by solve time for the Friends leaderboard and visually highlight the current user if present.
- For friends challenge UX, default the challenge date to today but allow changing it, show a 'Play challenge' action after accepting instead of auto-redirecting, surface active challenges on both the profile and puzzle pages, show pending and accepted challenges in the profile list, reflect that each day has three difficulties, and allow sending challenges to all or selected friends at submission time.
- For challenge result logic, rank solves by (word hints used, letter hints used, time), so any hinted solve is worse than a clean solve and word hints are worse than letter hints. Challenges should expire at the end of the challenged puzzle day in Swedish local time, and expired challenges should be marked expired with no winner.
- Cap the profile page 'Latest results' section at 6 entries instead of the current larger limit.

## Security and Scanning
- Do not provide additional CodeQL setup guidance; CodeQL scanning already runs in GitHub.

## Code Formatting
- Enforce local formatting in pre-commit hooks instead of a separate pre-push hook.
- Use CRLF line endings for all files under frontend/ to maintain repository formatting consistency, including package.json.

## Version Control
- Before committing work, create and use a feature branch rather than committing directly from the current branch.