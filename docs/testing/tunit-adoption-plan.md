# TUnit Adoption Plan

## Goals

- Use more of TUnit's strengths without flattening scenario-heavy tests into generic parameter grids.
- Improve test readability, discoverability, filtering, and execution performance.
- Keep workflow and integration tests narrative-first when the setup tells the story of the behavior.

## Repository TUnit Usage Rules

### Prefer explicit scenario tests for narrative workflows

Keep tests explicit when the behavior is best understood as a story with meaningful setup and state transitions.

Examples in this repository:
- `SwedishCrossword.Api.Tests/LeaderboardStoreTests.cs`
- `SwedishCrossword.Api.Tests/ApiIntegrationTests.cs`
- `SwedishCrossword.Tests/VinkelordIntertwiningTests.cs`
- larger workflow sections in `SwedishCrossword.Tests/VinkelordTests.cs`

These tests may still gain:
- categories
- better display names for selected parameter rows
- fixture and lifecycle improvements
- safer parallelization and isolation rules

They should not be aggressively collapsed into large data tables.

### Prefer `[Arguments]` for small primitive case sets

Use `[Arguments]` when all of the following are true:
- inputs are compile-time constants
- each row expresses the same single behavior
- the row count stays small enough to read inline
- the test remains clearer than separate methods

Good repository examples:
- `SwedishCrossword.Api.Tests/TransientSqlErrorClassifierTests.cs`
- `SwedishCrossword.Tests/GridCellTests.cs`

### Prefer method data when rows get noisy

Use `MethodDataSource` when:
- inline `[Arguments]` rows become long or numerous
- the same cases need reuse across tests
- row data needs a small helper model or metadata
- discovery can stay lightweight

Discovery rule:
- method data must stay cheap at discovery time
- do not query databases, enumerate large files, or perform expensive object graphs during discovery
- prefer returning IDs, small DTOs, or compact case definitions and load heavy data during test execution

### Use class data only for reusable structured cases

Use `ClassDataSource` when:
- multiple files share the same structured cases
- test rows need richer setup objects but should remain reusable
- the extra indirection improves maintenance more than it hurts readability

Do not introduce class data for tiny scalar cases.

### Use `TestDataRow` only when method data needs row-specific metadata

Use `TestDataRow` when a method-based data source needs per-row:
- `DisplayName`
- `Categories`
- `Skip`

Phase 1 decision for this repository:
- no current file justifies `ClassDataSource`
- current method-data usage is still simple enough for tuples and primitive rows
- introduce `TestDataRow` only when a method-based dataset needs row-specific metadata that cannot be expressed cleanly in the test method itself

### Use matrix tests sparingly

Use matrix coverage only for true orthogonal combinations in pure unit tests.

Do not use matrix coverage when:
- the cross-product is large
- the output is hard to diagnose
- the behavior is workflow-driven
- setup cost is significant

Repository rule:
- prefer targeted `[Arguments]` rows over broad matrix expansion unless the matrix is both small and meaningful

Phase 1 decision for this repository:
- no current file requires matrix coverage yet
- likely future candidate: small orthogonal option checks such as `PrintOptionsTests`
- until a case clearly benefits from the cross-product, continue using targeted `[Arguments]` rows for readability and discovery control

### Use lifecycle features to reduce per-test overhead

Use TUnit lifecycle hooks to share safe setup where it improves performance:
- `[Before(Class)]` / `[After(Class)]` for expensive resources that are safe to share
- `[Before(Test)]` / `[After(Test)]` for isolation-sensitive resources

Repository rule:
- preserve isolation for temp directories, time providers, and mutable store state
- move setup to class scope only when state leakage risk is low and performance gain is meaningful

### Treat performance as an adoption gate

A TUnit refactor is only acceptable when it improves at least one of:
- readability
- execution speed
- discovery cost
- filterability
- maintenance

If a conversion only makes the code denser, do not do it.

## Row Metadata and Filtering Conventions

### Display names

Use `DisplayName` on parameter rows when the raw parameter list is not immediately readable in test explorer.

Repository rule:
- prefer short, behavior-first names
- include the key distinguishing value such as a code, character, option, or boundary
- do not repeat the entire test method name in the display name

Examples:
- `Transient code 40613`
- `Swedish char å uppercases to Å`
- `Width 0 throws`

### Categories

Use categories to support local targeting and CI splitting.

Initial category vocabulary:
- `Unit` — fast, isolated pure/unit tests
- `Integration` — host, HTTP, filesystem, or multi-component tests
- `Store` — persistence and storage tests
- `Validation` — input validation and normalization slices
- `Smoke` — high-signal fast checks suitable for quick runs
- `Slow` — expensive tests that should be easy to exclude

Repository rule:
- add categories first to parameterized unit tests and integration suites with clear grouping
- do not over-tag every legacy test immediately

### Custom properties and filters

Use custom properties only after categories are established and a genuine filtering need appears.

Preferred filter strategy:
- local quick runs target `Smoke` and selected `Unit` categories
- CI can split `Unit`, `Integration`, and `Slow` buckets
- avoid introducing custom property taxonomies before category conventions settle

Example TUnit filter commands:
- unit-only: `dotnet test -- --treenode-filter "/*/*/*/*[Category=Unit]"`
- integration-only: `dotnet test -- --treenode-filter "/*/*/*/*[Category=Integration]"`
- smoke-only: `dotnet test -- --treenode-filter "/*/*/*/*[Category=Smoke]"`
- exclude slow: `dotnet test -- --treenode-filter "/*/*/*/*[Category!=Slow]"`
- unit without slow: `dotnet test -- --treenode-filter "/*/*/*/*[(Category=Unit)&(Category!=Slow)]"`

## File Classification

### SwedishCrossword.Tests

| File | Strategy | Notes |
| --- | --- | --- |
| `AccidentalWordTests.cs` | Parameterization-first | Contains many property and string-status checks that can be grouped with `[Arguments]` or method data. |
| `AlternativeCluesTests.cs` | Metadata-first | Mostly narrative and randomized behavior checks; keep explicit, add categories where useful. |
| `CrosswordGenerationOptionsTests.cs` | Parameterization-first | Strong candidate for preset and dimension case tables. |
| `CrosswordGridTests.cs` | Parameterization-first | Boundary and argument validation cases can be selectively consolidated. |
| `GenerationHelpersTests.cs` | Parameterization-first | Small pure-function tests are good candidates for `[Arguments]` and method data. |
| `GridCellTests.cs` | Metadata-first | Already uses `[Arguments]`; add display names and small targeted expansions. |
| `GridValidatorTests.cs` | Mixed | Simple placement validity checks can be parameterized; connected-grid scenarios should stay explicit. |
| `PrintServiceTests.cs` | Lifecycle-first | Repeated setup suggests helper/factory cleanup and selective data-driven checks, but file and document scenarios stay readable as explicit tests. |
| `SafeJsonEncoderTests.cs` | Parameterization-first | Repeated option and encoding checks can be grouped carefully. |
| `SwedishDictionaryTests.cs` | Mixed | Validation edge cases are good for `[Arguments]`; broader dictionary behavior should remain explicit. |
| `VinkelordIntertwiningTests.cs` | Leave-mostly-explicit | Highly scenario-driven and diagram-like; optimize metadata and filtering, not heavy parameterization. |
| `VinkelordTests.cs` | Mixed | Geometric and position cases can use case tables; multi-bend walkthroughs should remain explicit. |
| `WordTests.cs` | Parameterization-first | Constructor normalization and directional calculations have obvious low-risk parameterization targets. |

### SwedishCrossword.Api.Tests

| File | Strategy | Notes |
| --- | --- | --- |
| `ApiIntegrationTests.cs` | Lifecycle-first | Best gains are fixture cleanup simplification, categorization, and filtering rather than broad data conversion; keep per-test isolation unless a shared host can be proven safe. |
| `ApiTestFixture.cs` | Lifecycle-first | Shared setup and cleanup point for performance-aware TUnit improvements. |
| `LeaderboardStoreTests.cs` | Leave-mostly-explicit | Store and challenge workflows are narrative-first; only selective validation slices should become data-driven. |
| `SubmissionTokenServiceTests.cs` | Mixed | Token validation edge cases can use `[Arguments]` or method data; JSON shape tests should stay explicit. |
| `TransientSqlErrorClassifierTests.cs` | Metadata-first | Already a good `[Arguments]` example; first target for display names and categories. |

## Initial Implementation Order

1. `SwedishCrossword.Api.Tests/TransientSqlErrorClassifierTests.cs`
2. `SwedishCrossword.Tests/GridCellTests.cs`
3. `SwedishCrossword.Tests/CrosswordGenerationOptionsTests.cs`
4. `SwedishCrossword.Tests/CrosswordGridTests.cs`
5. `SwedishCrossword.Tests/GenerationHelpersTests.cs`
6. `SwedishCrossword.Api.Tests/SubmissionTokenServiceTests.cs`

## Deferred or Minimal-Change Areas

These files should stay mostly explicit during the first rollout:
- `SwedishCrossword.Api.Tests/LeaderboardStoreTests.cs`
- `SwedishCrossword.Api.Tests/ApiIntegrationTests.cs`
- `SwedishCrossword.Tests/VinkelordIntertwiningTests.cs`
- workflow-heavy sections of `SwedishCrossword.Tests/VinkelordTests.cs`

## Pilot Slice Implemented

The first implementation slice is now in place and acts as the reference pattern for the broader rollout.

Completed pilot changes:
- `SwedishCrossword.Api.Tests/TransientSqlErrorClassifierTests.cs`
  - retained `[Arguments]`
  - added row `DisplayName` values
  - added filtering categories
- `SwedishCrossword.Tests/GridCellTests.cs`
  - retained `[Arguments]`
  - added row `DisplayName` values
  - added filtering categories
- `SwedishCrossword.Tests/CrosswordGenerationOptionsTests.cs`
  - consolidated repetitive preset dimension tests into a selective `[Arguments]` test
- `SwedishCrossword.Tests/GenerationHelpersTests.cs`
  - consolidated repeated `CountVowels` cases into a selective `[Arguments]` test
- `SwedishCrossword.Tests/SwedishDictionaryTests.cs`
  - introduced a lightweight static `MethodDataSource` for invalid `AddWord` inputs
- `SwedishCrossword.Api.Tests/ApiIntegrationTests.cs`
  - simplified lifecycle management to rely on TUnit hooks rather than duplicate disposal paths
- class-level categories added to key `Unit`, `Integration`, and `Store` suites
- concrete TUnit filter commands added for local and CI usage

Pilot acceptance criteria:
- parameterized tests show readable explorer names
- discovery-time data stays lightweight
- scenario tests remain explicit where setup tells the story
- integration cleanup remains isolated and deterministic

## Next Rollout Backlog

Recommended next files after the pilot slice:
1. `SwedishCrossword.Tests/CrosswordGridTests.cs`
2. `SwedishCrossword.Api.Tests/SubmissionTokenServiceTests.cs`
3. `SwedishCrossword.Tests/SafeJsonEncoderTests.cs`
4. selected validation-only slices in `SwedishCrossword.Api.Tests/LeaderboardStoreTests.cs`

Stop conditions for future conversions:
- the data shape becomes harder to read than explicit tests
- discovery cost increases materially
- the scenario loses diagnostic value
- lifecycle changes introduce shared mutable state risk

## Performance Constraints from TUnit Guidance

- Keep discovery-time data generation lightweight.
- Avoid matrix explosions; favor targeted cases.
- Share expensive setup only when isolation is still guaranteed.
- Use categories and filters to split fast and slow suites.
- Prefer performance-aware lifecycle improvements before introducing clever abstractions.

## Incremental Rollout Guardrails

Before converting a test or test group, require all of the following to be true:
- the new shape is shorter or clearer than the current one
- the test remains easy to diagnose from explorer output alone
- discovery-time work does not become heavier
- the conversion does not hide meaningful setup or workflow detail

Prefer leaving the test explicit when:
- setup is the behavior narrative
- multiple entities or state transitions matter to the assertion
- fake time, temp directories, host wiring, or persistence state are central to understanding the case
- a parameter table would require comments to stay understandable

Preferred rollout order for future changes:
- first add categories and display names
- then consolidate repetitive primitive cases with `[Arguments]`
- then use `MethodDataSource` for reusable but lightweight datasets
- only then consider richer row types or matrix coverage

Definition of done for each future slice:
- affected tests pass
- build passes
- any new data source remains lightweight at discovery time
- the adoption doc still matches the implemented conventions
