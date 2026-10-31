# Immich Smart Organizer

[![CI](https://github.com/denisbredikhin/immich-smart-organizer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/denisbredikhin/immich-smart-organizer/actions/workflows/ci.yml)

Immich Smart Organizer is an unofficial, cross-platform .NET 10 command-line tool for finding and classifying assets in an Immich library using Immich's OCR and contextual search.

The tool initializes and validates configuration, discovers candidates, evaluates category rules, and plans or applies album, tag, and optional archive actions. **Dry-run is enabled by default.** Live runs persist action progress in local SQLite for incremental processing and interruption recovery.

## Architecture at a glance

```text
CLI and configuration
        -> Immich OCR / contextual search
        -> deterministic category rules and evidence
        -> dry-run report OR confirmed API actions + local SQLite state
```

The CLI is composed with Microsoft.Extensions.DependencyInjection. Search and classification use Immich query evidence; live execution uses bounded HTTP requests and an exclusively locked SQLite action journal.

## Safety and scope

- Dry-run sends no album, tag, or archive mutation requests and never writes completed scan state.
- The generated configuration enables dry-run and disables archiving.
- Use the [example YAML](examples/immich-smart-organizer.yaml) as a starting point. Live actions require `processing.dryRun: false`; archival additionally requires `actions.archive: true` and mandatory OCR plus contextual conditions in `match.all`.
- Partial discovery blocks all live mutations. Ambiguous matches never receive category actions, even with `--category`.
- Original photo and video files are never moved, rewritten, or deleted.
- The tool uses Immich's documented API. It does not access the database or media filesystem, transmit data to an external AI service, or collect telemetry.
- Search results are bounded candidates, not an exhaustive inventory of the library.

The project is not affiliated with or endorsed by Immich.

See [API compatibility](docs/api-compatibility.md) for the version-pinned research and known search limitations.

## Requirements and installation

The tool requires the .NET 10 runtime. Install the .NET 10 SDK if you plan to build from source.

NuGet publication has not yet been performed. After a release is published, install the global tool with:

```shell
dotnet tool install --global Immich.SmartOrganizer
```

To pin a specific published version, add `--version <version>`. For a repository-local tool, run `dotnet new tool-manifest` if needed, then:

```shell
dotnet tool install Immich.SmartOrganizer
```

Until the first package release, build and run the tool from source using the [development instructions](#development).

## Quick start

```powershell
immich-smart-organizer init
$env:IMMICH_API_KEY = "<your Immich API key>"
immich-smart-organizer config validate
immich-smart-organizer scan --dry-run
```

`init` creates `immich-smart-organizer.yaml` in the current directory and refuses to overwrite an existing file. The generated configuration refers to the `IMMICH_API_KEY` environment variable; the key itself is never written to YAML. See [configuration](docs/configuration.md) for config lookup, validation, defaults, and path rules.

`scan` checks the Immich server version, performs configured OCR and contextual searches, and evaluates category rules against the queries that returned each candidate. It deduplicates results by asset ID and reports when configured limits or Immich's bounded search results make coverage partial. Inspect `scan --dry-run --verbose` before setting `processing.dryRun: false`. Keep archival disabled until you have reviewed conservative rules on an isolated library. See [classification and execution](docs/classification.md) for rule semantics, safety gates, and manual recovery.

`--full` re-evaluates candidates that Immich's search APIs can discover; it does not guarantee that every library asset can be enumerated. Scan exit codes are documented in [configuration](docs/configuration.md). For deployment, scheduling, updates, and troubleshooting, see [deployment](docs/deployment.md), [troubleshooting](docs/troubleshooting.md), and [release instructions](docs/releasing.md).

## Commands

```text
immich-smart-organizer init [--output PATH]
immich-smart-organizer config validate [--config PATH]
immich-smart-organizer scan [--config PATH] [--dry-run] [--full] [--limit N] [--category NAME] [--verbose]
immich-smart-organizer --help
immich-smart-organizer --version
```

Incremental live scans rediscover candidates, compare current query evidence, rules fingerprints and asset update markers, and skip unchanged completed actions. `--full` forces re-evaluation; neither mode is exhaustive library enumeration. See [state and recovery](docs/configuration.md#state-and-incremental-recovery).

## Compatibility, privacy, and reporting

The Immich API contract was reviewed against version 3.3.5. That contract has **not** been verified against a live Immich server. Live execution is restricted to that exact stable version; read-only search accepts stable 3.2.0+ within major version 3. Discovery requires `asset.read`; live runs additionally need `user.read` and the [permissions](docs/api-compatibility.md#authentication-and-permissions) for enabled actions. No administrator permission is assumed. Review verbose logs before sharing them: they can contain asset IDs.

## Development

The project targets .NET 10 and uses MSTest with Microsoft.Testing.Platform. The SDK version is pinned in `global.json`.

```powershell
dotnet restore Immich.SmartOrganizer.slnx
dotnet build Immich.SmartOrganizer.slnx --no-restore
dotnet test --solution Immich.SmartOrganizer.slnx --no-build
dotnet format Immich.SmartOrganizer.slnx --verify-no-changes --no-restore
$env:IMMICH_API_KEY = "local-validation-placeholder"
dotnet run --project src\Immich.SmartOrganizer -- config validate --config examples\immich-smart-organizer.yaml
dotnet run --project src\Immich.SmartOrganizer -- --help
dotnet run --project src\Immich.SmartOrganizer -- --version
```

The placeholder key is only needed because validation checks that the configured environment variable is present; it is not sent to Immich. No live Immich server or real API key is needed for these checks. CI also packs the tool, installs it into an isolated tool path, and smoke-tests the installed commands.

See [CONTRIBUTING](CONTRIBUTING.md) for the full contribution workflow. Security issues should be reported privately as described in [SECURITY](SECURITY.md).

## License

This project is licensed under the [MIT License](LICENSE).
