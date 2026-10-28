# Immich Smart Organizer

Immich Smart Organizer is a cross-platform .NET 10 command-line tool intended to classify and organize assets in an existing Immich library using Immich's own OCR and contextual search.

The project is under active development as a stack of reviewable draft pull requests. The current stage provides `init` and `config validate`; `scan` options are recognized but scanning is not implemented yet.

## Safety and scope

- The planned scan defaults to dry-run; generated configuration will keep archival disabled.
- Original photo and video files will never be moved, rewritten, or deleted.
- The tool will use documented Immich APIs only; no direct database access, NAS mount, external AI service, or telemetry is planned.
- The project is unofficial and is not affiliated with Immich.

See [API compatibility](docs/api-compatibility.md) for the version-pinned research and known search limitations.

## Quick start

```powershell
immich-smart-organizer init
$env:IMMICH_API_KEY = "<your Immich API key>"
immich-smart-organizer config validate
```

`init` creates `immich-smart-organizer.yaml` in the current directory and refuses to overwrite an existing file. The generated example uses `IMMICH_API_KEY` as the environment variable name; the key itself is never written to YAML. See [configuration](docs/configuration.md) for config lookup, validation, defaults, and path rules.

The upcoming API and scan stages will implement `scan`. Until then, a syntactically valid `scan` command returns a non-zero status and explicitly reports that no Immich requests were sent.

## Development

The project targets .NET 10 and uses MSTest with Microsoft.Testing.Platform.

```powershell
dotnet restore Immich.SmartOrganizer.slnx
dotnet build Immich.SmartOrganizer.slnx --no-restore
dotnet test --solution Immich.SmartOrganizer.slnx --no-build
dotnet run --project src\Immich.SmartOrganizer -- --help
dotnet run --project src\Immich.SmartOrganizer -- --version
```

No live Immich server or API key is needed for these checks.

The configuration validation tests use an in-memory environment provider and temporary directories; no real Immich server is contacted.
