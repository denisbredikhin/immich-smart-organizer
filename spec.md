# Immich Smart Organizer — Development Specification

**Version:** 1.1 (26 October 2026)  
**Status:** Implementation brief for GitHub Copilot Agent  
**Repository:** https://github.com/denisbredikhin/immich-smart-organizer  
**NuGet package:** `Immich.SmartOrganizer` (verify availability before publishing)  
**CLI:** `immich-smart-organizer`  
**Namespace:** `Immich.SmartOrganizer`  
**Language/runtime:** C#, .NET 10  
**License:** MIT  
**First target:** Immich 3.3.5  

> **Instructions to the coding agent:** Build a real, production-quality open-source project, not just a plan or generated scaffolding. Verify third-party APIs and toolchain behavior against primary documentation and the target version before implementation. Execute all feasible tests; never assert that an unexecuted check passed. Development must proceed in small, reviewable **stacked pull requests**, using the **GitHub CLI (`gh`)** for GitHub operations. Commit, push, and open a pull request for each stage. Do not merge PRs without explicit user approval.

## 1. Mission, scope and guiding principles

Build a cross-platform, configurable CLI that classifies and organizes assets in an existing Immich library using **Immich's own OCR and contextual/CLIP search**, without locally running another machine-learning model. A YAML rules engine maps discovered assets to categories and safely applies optional actions: album membership, tags and archiving. Receipts and invoices are the initial example; the engine and product name must remain general enough for tickets, screenshots, boarding passes, warranty documents, and further use cases.

Priorities, in order: (1) safety of personal photographs, (2) correct use of supported Immich APIs, (3) transparent and explainable classification, (4) straightforward maintenance and testing, (5) ease of deployment, and (6) restrained dependency and operational footprint. Never move, rewrite or delete original photos. Do not access Immich PostgreSQL directly or require NAS mounts. No telemetry, external AI service, daemon, web UI, background loop or built-in scheduler in v1.

The application must be an ordinary SDK-style `.csproj` console application packaged as a **NuGet .NET Tool**. Do **not** implement a file-based app. Docker is intentionally outside v1 scope: it adds operational complexity for a one-shot CLI that can run natively on Debian/Windows/macOS. Users schedule one-shot invocations through cron, systemd timers or Windows Task Scheduler.

## 2. Research first: verified Immich integration

The first development phase must inspect the **versioned Immich 3.3.5** OpenAPI schema/source as well as official documentation, including:

- https://api.immich.app/
- https://docs.immich.app/features/searching/
- https://github.com/immich-app/immich
- https://immich.app/blog/v3.2.0-release

Verify the endpoints, authentication headers, minimum permissions, request/response shapes, pagination, asset/album/tag mutations, archive flags, external-library behavior, and **Search API v2** compatibility. Determine which OCR capabilities are actually surfaced through the API: search by keyword, raw extracted text (if available), phrase matching, matched text, and any limits. **Do not invent access to OCR text or relevance scores.** Clearly distinguish OCR search from contextual search. Inspect whether the API provides reliable asset enumeration and timestamps for comprehensive/incremental operation; document any coverage gaps before claiming exhaustive scans.

On startup, check connectivity, fetch Immich version, validate necessary capabilities where feasible, and fail clearly on unsupported operations. Prioritize v3.3.5; do not invent an elaborate multi-version compatibility layer. Capture endpoint research and compatibility caveats in `docs/api-compatibility.md`. Mocked tests do not constitute live compatibility verification.

## 3. Technology and code organization

Use C#/.NET 10, nullable reference types, implicit usings, async I/O, cancellation tokens, `HttpClient`, `System.Text.Json`, maintained YAML parsing (e.g., SharpYaml), and small local SQLite state. CLI argument parsing may use an established **stable** .NET library or a small handwritten parser when simpler. Use lightweight built-in logging as suitable. Avoid EF Core, MediatR, AutoMapper, service hosting, externally hosted databases, message brokers, overengineered DI, and a proliferation of projects/interfaces. Introduce abstractions only where they improve testability or meaningfully separate pure classification logic from side effects.

Suggested structure (adapt pragmatically; do not create empty placeholders):

```text
immich-smart-organizer/
├── .github/
│   ├── workflows/ci.yml
│   ├── workflows/release.yml
│   ├── ISSUE_TEMPLATE/
│   └── pull_request_template.md
├── src/Immich.SmartOrganizer/
│   ├── Immich.SmartOrganizer.csproj
│   ├── Program.cs
│   ├── Cli/
│   ├── Configuration/
│   ├── Immich/
│   ├── Search/
│   ├── Classification/
│   ├── Actions/
│   └── State/
├── tests/Immich.SmartOrganizer.Tests/
│   └── Immich.SmartOrganizer.Tests.csproj
├── examples/
│   ├── immich-smart-organizer.yaml
│   ├── receipts.yaml
│   └── documents.yaml
├── docs/
│   ├── configuration.md
│   ├── classification.md
│   ├── deployment.md
│   ├── troubleshooting.md
│   └── api-compatibility.md
├── .editorconfig
├── .gitignore
├── global.json
├── Directory.Build.props
├── Immich.SmartOrganizer.slnx
├── README.md
├── LICENSE
├── CONTRIBUTING.md
├── SECURITY.md
├── CODE_OF_CONDUCT.md
└── CHANGELOG.md
```

Keep a **single production executable project plus one MSTest project**. Other projects require concrete justification. Build/restore/test/pack through standard `dotnet` commands. Configure CLI as a tool (`PackAsTool=true`, `ToolCommandName=immich-smart-organizer`, `PackageId=Immich.SmartOrganizer`, `TargetFramework=net10.0`). Support global and local tool installation. Prefer framework-dependent tool packages for initial release; self-contained/RID binaries are optional later.

## 4. Execution model and CLI

Each invocation must parse arguments, load and validate configuration, carry out one operation, print a summary, return a meaningful exit code, and exit. No internal timer, cron parser, `BackgroundService`, Quartz or always-on process. Required commands:

```bash
immich-smart-organizer --help
immich-smart-organizer --version
immich-smart-organizer init [--output PATH]
immich-smart-organizer config validate [--config PATH]
immich-smart-organizer scan [--config PATH] [--dry-run] [--full] [--limit N] [--category NAME] [--verbose]
```

Support Ctrl+C and cancellation in long-running operations, and avoid interactive prompts during automated runs. Suggested exit codes: `0` success, `1` general failure, `2` invalid arguments/configuration, `3` connectivity/authentication, `4` incompatible API, `5` partial scan. Document exact semantics. `--full` means full **re-evaluation of discoverable candidates** unless research proves truly exhaustive discovery; label this honestly.

## 5. Configuration discovery and initialization

Canonical filename: `immich-smart-organizer.yaml`. Resolve the configuration in this exact precedence:

1. Explicit `--config` file path.
2. `IMMICH_SMART_ORGANIZER_CONFIG` environment variable.
3. `./immich-smart-organizer.yaml` in the **current working directory**.

Relative configuration paths are relative to the current working directory. Relative **state database** paths are relative to the containing directory of the resolved configuration file, not the tool installation directory. No implicit search of home/parent directories. Missing file -> actionable diagnostic proposing `immich-smart-organizer init`. `init` writes a safe example in the current directory (or explicit `--output`), refusing to overwrite by default. Never put API secrets in YAML or SQLite; load the secret from the environment variable named by `immich.apiKeyEnv`.

Validate required fields, URLs, types, rule operands, category uniqueness, unsupported actions, unknown properties, missing env vars, numerical limits and path permissions. Unknown properties should not be silently discarded. Keep example configurations and documented schema in sync. Avoid a hidden magic config directory or unannounced files.

### Proposed YAML contract

This YAML is the *desired application contract* and must be implemented and refined consistently, not treated as an existing Immich schema:

```yaml
immich:
  url: "http://localhost:2283"
  apiKeyEnv: "IMMICH_API_KEY"

scanner:
  mode: incremental
  batchSize: 100
  concurrency: 2
  includeArchived: true
  includeExternalLibraries: true
  includeVideos: false
  lookbackDays: 7

processing:
  dryRun: true
  maxAssetsPerRun: 1000
  maxArchiveActionsPerRun: 25
  maxErrorsPerRun: 20
  ambiguousAssetPolicy: review

state:
  path: "./state.db"

categories:
  - name: receipts
    enabled: true
    search:
      contextual:
        - "receipt"
        - "restaurant bill"
        - "shopping receipt"
      ocr:
        - "kassabon"
        - "btw"
        - "totaal"
        - "receipt"
    match:
      all:
        - contextualAny: ["receipt", "restaurant bill", "shopping receipt"]
        - ocrAny: ["kassabon", "btw", "totaal", "receipt"]
    actions:
      album: "Receipts"
      tag: "Documents/Receipts"
      archive: false

  - name: invoices
    enabled: true
    search:
      contextual: ["invoice", "printed invoice"]
      ocr: ["factuur", "factuurnummer", "invoice", "invoice number"]
    match:
      any:
        - ocrAny: ["factuur", "invoice"]
    actions:
      album: "Invoices"
      archive: false

logging:
  level: Information
  format: text
```

For **actual automatic archiving**, provide a more conservative documented example requiring multiple independent signals and a dry-run review; the simple invoice example above is **not** archival-safe. Config `processing.dryRun` defaults to true and `archive` defaults to false. If supported operations differ from the example, update schema and docs explicitly rather than silently changing behavior.

## 6. Search and evidence collection

### OCR search

Use actual OCR search, **not** a semantic `Context` filter. Configurable multilingual terms, including Dutch `factuur`, `kassabon`, `btw`, `totaal`; English `receipt`, `invoice`, `total`, `VAT`; French `facture`, `reçu`, `TVA`; German `Rechnung`, `Quittung`, `MwSt`. These are examples only; any language supported by the user's Immich installation can be configured. For actual OCR text (only if officially available), support Unicode/case/whitespace normalization. Do not promise substring, multiple independent keywords, regex, phrase-level evidence, or raw OCR extraction if the target API cannot support them. Search-hit evidence must be represented faithfully and documented.

### Contextual/CLIP search

Search semantic descriptions such as `receipt`, `restaurant bill`, `printed invoice`, `boarding pass`, `screenshot`. CLIP queries produce **ranked candidates**, not definitive labels or calibrated probabilities. If the API does not expose a comparable relevance score, do not infer one from list position or fabricate a `confidence=0.85` metric.

### Combining results

Collect evidence by asset ID, deduplicate across queries and pages, retain the precise queries that matched, and distinguish **candidate discovery** from **classification evidence** and action planning. Handle pagination, empty pages, caps, duplicates, errors and truncated search sets. Search result ranking limitations must not be hidden. A `--full` request must not claim exhaustive coverage unless the API and implemented enumeration strategy actually guarantee it. Investigate delayed indexing and metadata updates when designing incremental scanning; never use capture date alone because newly imported historical photos have old capture dates.

## 7. Generic rule engine

Support straightforward deterministic YAML conditions:

- `all` (AND), `any` (OR), `not` (NOT)
- `ocrAny`, `ocrAll` where technically supportable
- `contextualAny`
- Explicit exclusions and optional supported metadata predicates

Define exactly how repeated queries, negative matches, multiple categories, missing data and conflicting categories work. Default conflicts to review/skip rather than arbitrarily choosing a category. Clearly explain classification decisions to the user (`--verbose` and summaries). The engine must be generic: no receipt/invoice-specific special cases in its core. Do **not** add fake numeric confidence or a custom executable rule language. A generic term like `total`, `totaal` or `BTW` alone must never qualify a photo for automatic archival under safe defaults.

## 8. Configurable actions

Use official Immich APIs only:

**Albums:** find by name in the relevant user's scope, create if missing, add matching assets without duplicate membership. Handle identical album names and unexpected ownership explicitly. `Documents / Receipts` is an album display name, not a filesystem hierarchy.

**Tags:** optional, supported only when the target API truly allows tag creation/application and the supplied key has adequate permissions. Hierarchical slashes must be interpreted according to actual Immich tag semantics, not guessed. Optional tag failures may be nonfatal when explicitly configured.

**Archive:** optional and **off by default**. On explicit enablement, archive only assets satisfying conservative rules and action limits. Preserve originals and album membership. Do not affect unrelated assets. A review album can collect ambiguous candidates **without archiving**. Users must be able to understand and manually undo mistakes in Immich. Bulk rollback is optional future work; never implement automated mass-unarchiving implicitly.

Execution should maintain per-action status and recover from partial failures (e.g. album succeeds, archive fails). Never mark a mutation successful before server confirmation. Reconcile server state when appropriate for idempotency.

## 9. Safety and privacy

This is an application operating on personal photo libraries; safety is non-negotiable:

- `dryRun=true` initially, with **zero HTTP mutation requests** during a dry run.
- Archival disabled in generated configuration until explicitly enabled by a user.
- Never call asset deletion endpoints, edit photo bytes or move files.
- Set limits on scanned assets, archive mutations, concurrency and errors per run.
- Use conservative classification and review routes for uncertain evidence.
- Do not send photos, OCR text or metadata to external analytics, AI providers or telemetry.
- Never log API keys; minimize filenames, text, OCR excerpts and other private identifiers in ordinary logs.
- Gracefully stop on request cancellation or error thresholds.
- Detect/prevent overlapping mutation runs on one state database.
- Document recovery and manual unarchive/removal procedures.
- Do not treat dry-run plans as completed operations.

The API key should have **least necessary Immich permissions** for each enabled action. Document permissions for OCR/context search, album creation/membership, tag operations and archive status; never assume admin rights are needed. Fail clearly if enabled actions lack permissions.

## 10. SQLite and incremental state

Use a local SQLite file (default `./state.db` relative to config directory). Store the minimum useful fields: asset ID, processing timestamps, asset update marker if available, classification/evidence summary, rules fingerprint, pending/completed action statuses, and last failure. **Do not** store entire image contents, credentials or full OCR documents. Use SQLite transactions and consistent schema migrations as needed; avoid holding transactions open across network calls. Ensure retries do not duplicate album memberships or repeat confirmed archive operations. Handle interrupted runs and rule changes; store a relevant configuration fingerprint to trigger re-evaluation when rules change. Prevent simultaneous mutating instances from racing using a reliable lock or an equivalent safe mechanism. Reconcile state against Immich when needed; local state is not an authoritative source of actual server mutation success.

## 11. API client reliability

Reuse `HttpClient`; add bounded timeouts, cancellation, clear error types, bounded exponential backoff for retryable failures and server rate-limit guidance where supplied. Do not retry authorization/configuration failures indefinitely. Support paginated search and bulk operations only as documented. Log useful reasons for unauthorized/forbidden/not found/network/server problems without exposing secrets. Validate the Immich version and relevant supported capabilities early. No direct database connections or direct filesystem scanning.

## 12. Logging, CLI reports and exit codes

Default to readable, noninteractive console output, with optional verbose/debug and JSON logging if easy to support. Report the Immich version, scan mode, dry-run status, candidates returned, assets evaluated, category matches, ambiguous cases, planned vs confirmed album/tag/archive actions, errors and duration. Example (illustrative numbers only):

```text
Immich Smart Organizer
Immich version: 3.3.5
Mode: full candidate re-evaluation
Dry-run: enabled
Candidates discovered: 420
Assets evaluated: 420
Receipts: 185
Invoices: 43
Needs review: 27
Album additions planned: 228
Archivals planned: 0
Mutations executed: 0
```

Explain evidence when requested, e.g. matching `contextualAny=receipt`, `ocrAny=btw`, and exact rule name. Do not claim that a textual OCR query match proves a complete receipt classification.

## 13. Tests: MSTest + Microsoft.Testing.Platform

Use the **latest stable compatible MSTest SDK** and **Microsoft.Testing.Platform (MTP)** with .NET 10. Do not use xUnit, NUnit or the VSTest runner. Pin dependency/MSBuild SDK versions consistently for reproducible CI. `global.json` should select MTP as the runner when using the supported .NET test tooling, for example:

```json
{
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

Use `MSTest.Sdk` in the tests project with a verified version and version declaration mechanism (such as the supported `global.json` `msbuild-sdks` configuration). Verify `dotnet test` actually executes under MTP. Unit tests should cover YAML and precedence, invalid options and paths, environment-secret resolution, rule logic and NOT/AND/OR, Unicode OCR evidence, multi-category conflicts, candidate aggregation and pagination, dry-run mutation absence, album/tag/archive plans and errors, state persistence, idempotency, rule fingerprint changes, interruption, concurrency and action limits. For HTTP tests use mocked `HttpMessageHandler` or a local test server and cover auth, pagination, 429/5xx, malformed results, timeouts and retries. Optional *opt-in* integration tests may target a real Immich server using isolated assets/albums only; CI must never require personal production access.

Mandatory safety tests: (1) **dry-run makes no mutation requests**, (2) generic OCR keyword alone never archives under safe rules, (3) duplicate runs do not duplicate membership, (4) partial failure resumes correctly, (5) action limits cannot be bypassed, (6) no deletion endpoint is used, and (7) unsupported API behavior cannot silently turn into mutations.

## 14. Package, CI and releases

The solution must support `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet pack`. Configure the production project as a packable `.NET Tool`; do not accidentally pack tests. Verify locally by installing the produced `.nupkg` into a temporary tool path, then running `immich-smart-organizer --help`, `--version` and safe config validation. Include `.editorconfig`, nullable analysis, appropriate build warnings, consistent formatting and Dependabot where helpful.

`.github/workflows/ci.yml`: run on pushes and pull requests; set up .NET 10; restore, build, run MTP tests, check formatting, validate YAML/examples, pack tool, locally install and smoke-test. Must not require a live Immich server or private API key.

`.github/workflows/release.yml`: respond to explicitly approved SemVer tags (e.g. `v0.1.0`); run verification; pack and publish to NuGet.org via OIDC/trusted publishing if available or a narrowly scoped secret; create GitHub Release with changelog and artifacts. Never publish to NuGet from ordinary branches, or before user approval. Include NuGet description, authors/license, repository URL `https://github.com/denisbredikhin/immich-smart-organizer`, tags, README and accurate package metadata. Prefer an initial `0.1.0` prerelease until real Immich testing is complete. Do not claim framework-dependent NuGet tools are self-contained binaries.

## 15. Documentation and community files

Prepare a well-written English `README.md`: purpose, use cases, architecture at a glance, safety defaults, prerequisites, how to install global/local tool, Immich API key with least privilege, quick start, YAML example, dry run, enabling archiving, full/incremental behavior and coverage caveats, cron/systemd timer/Task Scheduler samples, CLI commands, troubleshooting, tested Immich versions, privacy, contributing and license. State clearly that this is an **unofficial project not affiliated with Immich**.

Provide `docs/configuration.md` (every option, type, default and safety meaning), `docs/classification.md` (OCR vs CLIP, rule semantics, why confidence probabilities are inappropriate, false positives), `docs/deployment.md` (Linux/Windows/macOS, env vars, config/current-dir/state rules, external scheduler, update/uninstall), `docs/troubleshooting.md`, `docs/api-compatibility.md`. Add `CONTRIBUTING.md`, `SECURITY.md` with private reporting guidance, `CODE_OF_CONDUCT.md`, `CHANGELOG.md`, issue and PR templates, and MIT `LICENSE` with correct copyright holder confirmed by repository owner. `.gitignore` must exclude real configs with secrets, `.env`, `*.db`, local state/logs and personal media artifacts while allowing sanitized example YAML. Never commit private photo library data or server secrets.

## 16. GitHub collaboration: staged development with stacked PRs (mandatory)

**Repository:** https://github.com/denisbredikhin/immich-smart-organizer

**GitHub CLI `gh` is installed and ready to use.** Use it for GitHub operations: checking authentication/repo state, creating and viewing PRs, reporting checks, and managing reviews. Ordinary `git` commands handle local branches/commits and `git push` handles pushing; use `gh` for PR creation and GitHub API operations. Do not invent native `gh stack` commands: **GitHub CLI does not provide native stacked-PR management by default.** Implement stacking with git branches and `gh pr create --base ... --head ...`, or discover and explicitly document a pre-existing, trusted extension if available; do not require installing a new extension.

### Required stage workflow

Break work into small, logically coherent, independently reviewable stages. **After each stage**:

1. Implement the scoped changes and run relevant checks.
2. Stage and commit with a meaningful conventional commit message.
3. Push the stage branch to `origin`.
4. Create a **draft pull request via `gh pr create`**, targeting the immediately preceding stage branch (the first stage targets the repository default branch, usually `main`).
5. Include purpose, key decisions, validation performed, risk/limitations and a link to the preceding PR in the PR description.
6. Report branch, commit SHA, PR URL, base branch and validation result to the user.
7. Continue on a new branch **based on the previous stage branch**, not on `main`, to form a PR stack, unless the user has instructed you to stop for review.

Do **not** merge PRs, tag releases, publish to NuGet.org, force-push others' work, or change branch protection without explicit owner approval. Creating/pushing branches and opening **draft** PRs is explicitly requested and permitted. If auth, permissions, remote branches, or CI block an operation, report the exact issue; do not falsely claim the PR was created.

### Example branch stack

```text
main
 └─ feat/01-foundation           PR #1 -> main
     └─ feat/02-config-cli      PR #2 -> feat/01-foundation
         └─ feat/03-immich-api  PR #3 -> feat/02-config-cli
             └─ feat/04-rules  PR #4 -> feat/03-immich-api
                 └─ feat/05-actions-state  PR #5 -> feat/04-rules
                     └─ feat/06-ci-docs  PR #6 -> feat/05-actions-state
```

A stacked PR displays **only its incremental diff** against its preceding branch. Prefer small stacks and split further if a PR becomes too large. If an earlier PR changes, propagate its updates to descendants safely and keep review diffs clean. Use well-understood `git rebase`/`git merge` procedures; avoid dangerous history rewriting on shared branches. `--force-with-lease` is acceptable **only when rebasing an agent-owned PR branch and necessary**, after confirming no other contributors' work would be overwritten; otherwise prefer a non-destructive update. When earlier PRs merge, retarget/rebase descendant PRs as appropriate using `gh pr edit --base`, while preserving review history as much as possible. Follow the repository's merge strategy. Never automatically merge all stacked PRs; await review/approval.

### Suggested commands (illustrative; inspect actual repo state)

```bash
# Confirm repository and authentication
gh auth status
gh repo view denisbredikhin/immich-smart-organizer

git fetch origin
# Start stage 1 from actual default branch
git switch -c feat/01-foundation origin/main
# ... implement and test ...
git add .
git commit -m "chore: establish solution and tests"
git push -u origin feat/01-foundation
gh pr create --draft --repo denisbredikhin/immich-smart-organizer \
  --base main --head feat/01-foundation \
  --title "chore: establish solution and tests" \
  --body "Stage 1 of stacked PR series; checks and decisions detailed here."

# Next stage is based on the previous branch
git switch -c feat/02-config-cli feat/01-foundation
# ... implement and test ...
git add .
git commit -m "feat: implement configuration and CLI"
git push -u origin feat/02-config-cli
gh pr create --draft --repo denisbredikhin/immich-smart-organizer \
  --base feat/01-foundation --head feat/02-config-cli \
  --title "feat: implement configuration and CLI" \
  --body "Stage 2; stacked on Stage 1; includes validation evidence."
```

Never assume `main` exists or the working tree is clean: inspect `gh repo view --json defaultBranchRef`, `git status`, and existing branches before acting. If a repo already has starter files/commits, preserve them and build from its actual default branch.

### Proposed stack stages and acceptance gates

**Stage 1 — Research + foundation.** Verify Immich 3.3.5 API, project baseline, .NET 10 NuGet Tool project, MSTest SDK+MTP setup, `.slnx`, build and initial docs. Gate: solution builds, tests execute via MTP, version/help works, API findings recorded. Create/push/PR #1.

**Stage 2 — Configuration and CLI.** `init`, `config validate`, YAML contract, env secrets, path precedence, CLI parsing and schema validation. Gate: unit tests and sample config validation, no secret leakage. Create/push PR #2 based on Stage 1.

**Stage 3 — Immich API and search.** Version detection/auth, OCR and contextual search, pagination/candidate aggregation, mocked HTTP tests and search limitations documented. Gate: API client tests and safe offline smoke test. Create/push PR #3 based on Stage 2.

**Stage 4 — Rules engine and dry-run.** Generic `all/any/not`, OCR/context predicates, conflict/exclusion rules, explainability, safe dry-run action plans. Gate: comprehensive rules/safety tests with **no HTTP mutations** in dry run. Create/push PR #4 based on Stage 3.

**Stage 5 — Mutations + persistent state.** Albums, optional tags, archival protection, SQLite, incremental state, retries/idempotency, concurrency locking, limits and recovery. Gate: mocked API tests for partial failure, duplicate runs and no-delete safety; no real library changes. Create/push PR #5 based on Stage 4. Split into two stacked PRs if too large.

**Stage 6 — Polishing + open-source delivery.** Documentation, real examples, diagnostics, formatting, complete MTP test suite, CI, tool-pack/install smoke tests, NuGet release workflow configured but **not executed**, legal/community files. Gate: all available local checks pass and evidence is in PR. Create/push PR #6 based on Stage 5.

**Optional Stage 7 — Real Immich validation.** Only with explicit access, authorization and a test library/isolated assets. Verify the running 3.3.5 server and safe operations, starting in dry-run; do not assume permission to mutate production media. Add fixes in a separate stacked PR. Never report live testing as performed when it was not.

If an implementation stage is too big, divide it into further named stacked PRs rather than stuffing unrelated changes into one review. The user must be able to review each PR independently by examining its incremental diff.

## 17. Final acceptance criteria

- [ ] `.NET 10` `.csproj` CLI builds and runs cross-platform in principle, with Linux tested.
- [ ] Packable NuGet global/local tool uses package ID `Immich.SmartOrganizer`, command `immich-smart-organizer`.
- [ ] YAML config resolution is CLI > env > current directory, with relative state path based on config directory.
- [ ] `init`, `config validate`, `scan`, `scan --dry-run`, `scan --full`, `--help`, `--version` work.
- [ ] Immich API usage is grounded in actual v3.3.5 endpoints and limitations.
- [ ] OCR and contextual searches are distinguished and produce traceable candidate evidence.
- [ ] Search pagination/limits and non-exhaustive results are clearly handled/documented.
- [ ] Rule engine is generic, deterministic, explainable and safe for negative/conflicting matches.
- [ ] Albums, optional tags, archive mutations use supported Immich APIs and user permissions.
- [ ] Dry-run performs **no Immich mutations**; default examples disable archiving.
- [ ] No original file editing/deletion; no external transmission/telemetry.
- [ ] SQLite incremental state survives interruption and avoids repeated actions.
- [ ] Action limits, retries, locking and errors are tested.
- [ ] MSTest SDK + MTP tests pass with a pinned stable compatible version.
- [ ] CI checks build/test/format/config/tool packaging and local installation.
- [ ] Release workflow exists but never publishes before owner approval.
- [ ] README, detailed docs, LICENSE, SECURITY, CONTRIBUTING, CHANGELOG and templates are complete.
- [ ] No Docker requirement, no built-in scheduler, no unnecessary hosted services.
- [ ] Repository metadata points to https://github.com/denisbredikhin/immich-smart-organizer.
- [ ] **Each stage is committed, pushed, and submitted as a draft stacked PR using `gh`**, with stage-to-stage base branches and an auditable PR chain.
- [ ] The user receives each stage's branch, SHA, PR URL, base, and checks.
- [ ] No PR merges, release tags or NuGet publication without explicit approval.
- [ ] Claims of live Immich 3.3.5 testing are made only after actual authorized integration tests.

## 18. Final instructions for Copilot

Start by inspecting the actual remote repository, GitHub CLI authentication, default branch, existing files and worktree status. Research the target APIs and .NET/MSTest tooling; then implement **incrementally through the stacked PR workflow in Section 16**. Do not produce only a plan; create working code, run validations, commit, push and open a draft PR **after each stage**. Keep earlier PRs reviewable as the stack evolves. Report honest blockers, test results and API limitations. The finished output is a small, dependable, publicly maintainable open-source **Immich Smart Organizer** .NET Tool, with strong safety defaults and clear documentation.
