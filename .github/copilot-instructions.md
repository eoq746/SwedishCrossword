# Copilot Instructions

## Project Guidelines
- User has a strict Azure budget constraint: approximately $50/month subscription credit and currently cannot increase spend; prioritize low-cost security hardening and avoid architectures that add significant ongoing cost.
- User prefers not to use Azure SQL auto-pause because it harms user experience; favor always-on database options for this app.
- Defer Azure VNet/private endpoint implementation due to budget constraints; prioritize non-costly security hardening first.
- User selected React as the frontend framework shell for migration.

## React Development Guidelines
- User prefers React puzzle code to separate grid logic from rendering.
- Use reducer-based navigation for state management.
- Favor data-driven cell models for better performance and maintainability.
- Split grid, clues, and leaderboard into separate components rather than a monolithic page component.
- User prefers app-wide CSS theming to use a common shared source of truth in token files, with only minor component-specific overrides kept in scoped CSS files.
- Implement the cookie consent UI as a floating banner overlaying the page rather than inline text in the document flow.
- For the puzzle page, the user prefers the clues panel as a single column split vertically into separate horizontal and vertical clue sections, with each section scrolling internally.
- When editing or removing a clue, updates must be applied to the clue’s original source JSON file (dsso, kelly, lexin, synonyms, or custom), not always custom-words.json.

## Security and Scanning
- User already runs CodeQL scanning in GitHub and does not need additional setup guidance for CodeQL.