# Immich Smart Organizer

Immich Smart Organizer is an early-stage, cross-platform .NET 10 command-line tool intended to classify and organize assets in an existing Immich library using Immich's own OCR and contextual search.

The project is currently at its foundation stage. This release only provides `--help` and `--version`; it does not yet connect to Immich or modify any library assets.

## Safety and scope

- The planned scan defaults to dry-run; generated configuration will keep archival disabled.
- Original photo and video files will never be moved, rewritten, or deleted.
- The tool will use documented Immich APIs only; no direct database access, NAS mount, external AI service, or telemetry is planned.
- The project is unofficial and is not affiliated with Immich.

See [API compatibility](docs/api-compatibility.md) for the version-pinned research and known search limitations.

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
