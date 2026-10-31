# Contributing

Contributions and bug reports are welcome. Please open an issue to discuss substantial changes before starting implementation.

## Development setup

1. Install the .NET 10 SDK.
2. Clone the repository and use the SDK pinned by `global.json`.
3. Restore and validate the solution:

```powershell
dotnet restore Immich.SmartOrganizer.slnx
dotnet build Immich.SmartOrganizer.slnx --no-restore
dotnet test --solution Immich.SmartOrganizer.slnx --no-build
dotnet format Immich.SmartOrganizer.slnx --verify-no-changes --no-restore
```

The tests use MSTest with Microsoft.Testing.Platform. They should not require an Immich server or a real API key.

To check the sanitized sample configuration locally, set a placeholder value for the configured environment variable and validate the example:

```powershell
$env:IMMICH_API_KEY = "local-validation-placeholder"
dotnet run --project src\Immich.SmartOrganizer -- config validate --config examples\immich-smart-organizer.yaml
```

This validation checks that the environment variable is set; it does not contact Immich. Never commit a real key, private configuration, database, log, or personal media.

## Pull requests

- Keep changes focused and explain the user-visible behavior and safety impact.
- Add or update tests for behavior changes.
- Preserve the read-only guarantee: scans must not issue Immich mutation requests.
- Update relevant documentation and examples.
- Run the checks above and report any that could not be run.
- Do not publish packages or create release tags as part of an ordinary pull request.

Use the pull request template and include reproduction steps for bug fixes.

## Reporting issues

Use the issue templates for bugs and feature requests. Do not report security vulnerabilities in a public issue; follow [SECURITY.md](SECURITY.md).
