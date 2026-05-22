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

## Security and Scanning
- Do not provide additional CodeQL setup guidance; CodeQL scanning already runs in GitHub.

## Code Formatting
- Enforce local formatting in pre-commit hooks instead of a separate pre-push hook.
- Use CRLF line endings for package.json to maintain repository formatting consistency.