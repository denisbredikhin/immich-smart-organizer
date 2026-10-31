# Troubleshooting

## `immich-smart-organizer` is not found

Confirm the tool installed successfully and that the .NET global tools directory is on `PATH`:

- Windows: `%USERPROFILE%\.dotnet\tools`
- Linux/macOS: `$HOME/.dotnet/tools`

Open a new terminal after changing `PATH`. For a local tool manifest, run the command from the manifest directory with `dotnet tool run immich-smart-organizer -- ...`.

## A .NET runtime is missing

The package is a framework-dependent .NET tool. Install the .NET 10 runtime (or SDK) for the operating system and architecture that run the command. The package does not bundle a runtime.

## Configuration is not found or invalid

Run `immich-smart-organizer init` from the directory in which you want the config file, or pass `--config PATH`. Check that `IMMICH_SMART_ORGANIZER_CONFIG` is not selecting a different file. Relative config paths use the current working directory; relative `state.path` values use the config file's directory.

For YAML errors, check indentation, property names, required sections, and list syntax. Unknown properties are rejected. Run:

```shell
immich-smart-organizer config validate --config PATH
```

The configured API-key environment variable must exist and be non-empty during validation. Validation does not contact Immich or print the key.

## API-key variable is missing

Set the variable whose name is stored in `immich.apiKeyEnv` in the same process environment that runs the CLI. For example, in PowerShell:

```powershell
$env:IMMICH_API_KEY = "<your Immich API key>"
```

Do not put the key value in the YAML file, command arguments, or logs. See [deployment](deployment.md) for scheduled-process configuration.

## Immich is unreachable or access is denied

Check `immich.url`, DNS, firewall and TLS settings, then verify the Immich server is available. Use an API key with the permissions needed for the requested read-only search (`asset.read`). A `401` usually indicates a missing or invalid key; a `403` indicates insufficient permission. Do not solve permission errors by granting administrator access without a specific need.

The CLI validates the Immich version and API response shape. An unsupported version or incompatible response is reported rather than silently treated as a successful scan.

## Scan reports partial coverage

Exit code `5` indicates partial discovery, for example a bounded smart-search result window, a search failure, or a configured candidate limit. Review the reasons in the output and the limitations in [API compatibility](api-compatibility.md). `--full` re-evaluates discoverable candidates; it does not enumerate every asset in the library.

## No categories match

Check that each rule term is also configured in the corresponding category `search.ocr` or `search.contextual` list. Rule evidence is based on the exact queries that returned an asset; query results are not OCR-document proof or category confidence scores. See [classification](classification.md).

## Archive or album changes did not occur

That is expected: the current implementation only displays a dry-run action plan. It does not mutate albums, tags, or archive state, even if `processing.dryRun` is false. Do not interpret planned actions as completed operations.

## Use verbose output carefully

`scan --verbose` includes per-candidate explanations and asset IDs. Review the output before sharing it. Do not include API keys, private server details, or personal media information in public bug reports.
