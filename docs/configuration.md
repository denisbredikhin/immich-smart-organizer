# Configuration

The canonical configuration file is `immich-smart-organizer.yaml`. Create a safe example with:

```text
immich-smart-organizer init
```

`init` writes into the current working directory, or to the path supplied by `--output`. It never overwrites an existing file and does not create missing parent directories.

## Configuration discovery

The application selects one configuration file in this order:

1. `--config PATH` on `config validate` or `scan`.
2. The `IMMICH_SMART_ORGANIZER_CONFIG` environment variable.
3. `immich-smart-organizer.yaml` in the current working directory.

Relative config paths are resolved from the current working directory. No home-directory or parent-directory search is performed. If no file exists at the selected path, the CLI reports the path and suggests running `immich-smart-organizer init`.

Relative `state.path` values are resolved from the directory containing the selected configuration file, not from the current directory or tool installation location. The parent directory must already exist and be writable. Validation checks existing database files for write access; when the target does not exist, it uses a temporary delete-on-close file to check the parent directory.

## Secret handling

Set `immich.apiKeyEnv` to the **name** of an environment variable that contains an Immich API key. The key must not be put into YAML, command-line arguments, SQLite state, or logs. `config validate` checks that the named variable is set and non-empty, but does not print its value.

The example uses `IMMICH_API_KEY`:

```powershell
$env:IMMICH_API_KEY = "<your Immich API key>"
immich-smart-organizer config validate
```

Use the minimum Immich permissions required for the operations you enable. See [API compatibility](api-compatibility.md) for the v3.3.5 permission findings and unverified behaviors.

## Schema and defaults

Unknown properties are errors rather than silently ignored values. The root must include `immich` and at least one category. The safe example also includes the other sections explicitly.

| Property | Type | Default | Validation / meaning |
|---|---|---|---|
| `immich.url` | absolute URL | required | Must use HTTP or HTTPS and must not contain embedded user information. |
| `immich.apiKeyEnv` | environment variable name | required | Must be a valid variable name whose value is set and non-empty. |
| `scanner.mode` | `incremental` or `full` | `incremental` | `full` means candidate re-evaluation, not guaranteed exhaustive library enumeration. |
| `scanner.batchSize` | integer | `100` | `1`–`500`. |
| `scanner.concurrency` | integer | `2` | `1`–`8`. |
| `scanner.includeArchived` | boolean | `true` | Requests archived assets to be considered when supported by the scan implementation. |
| `scanner.includeExternalLibraries` | boolean | `true` | External-library coverage is not yet verified; see API compatibility notes. |
| `scanner.includeVideos` | boolean | `false` | Whether videos may be included in discovery. |
| `scanner.lookbackDays` | integer | `7` | `1`–`3650`; it is not a safe standalone incremental cursor. |
| `processing.dryRun` | boolean | `true` | Plans are not completed actions. Dry-run must send no mutation requests. |
| `processing.maxAssetsPerRun` | integer | `1000` | `1`–`100000`. |
| `processing.maxArchiveActionsPerRun` | integer | `25` | `0`–`1000`; `0` disables archive actions. |
| `processing.maxErrorsPerRun` | integer | `20` | `1`–`1000`. |
| `processing.ambiguousAssetPolicy` | `review` or `skip` | `review` | Ambiguous results must not be assigned arbitrarily. |
| `state.path` | file path | `./state.db` | Relative paths are based on the configuration file's directory. |
| `categories` | non-empty list | required | Names must be unique, case-insensitively. Each category needs a search query and a match condition. |
| `categories[].enabled` | boolean | `true` | Disabled categories are ignored by classification. |
| `categories[].search.contextual` | list of strings | empty | Contextual/CLIP search queries; these are not calibrated confidence values. |
| `categories[].search.ocr` | list of strings | empty | OCR search queries; exact matching semantics are determined by Immich. |
| `categories[].match.all` | list of conditions | empty | All listed conditions must match. |
| `categories[].match.any` | list of conditions | empty | At least one listed condition must match. |
| `categories[].match.not` | condition | unset | The category condition is satisfied when this condition does not match. |
| `categories[].match.exclude` | list of conditions | empty | Any matching exclusion blocks the category; exclusions do not qualify a category by themselves. |
| `categories[].actions.album` | string | unset | Optional album display name. |
| `categories[].actions.tag` | string | unset | Optional tag name. A slash is not interpreted as a filesystem path. |
| `categories[].actions.archive` | boolean | `false` | Archiving remains opt-in and must use conservative rules and action limits. |
| `logging.level` | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` | `Information` | Reserved for CLI logging behavior. |
| `logging.format` | `text` | `text` | JSON logging is not currently supported. |

Each condition must contain exactly one supported operator:

```yaml
match:
  all:
    - contextualAny: ["receipt", "restaurant bill"]
    - ocrAny: ["kassabon", "receipt"]
  exclude:
    - ocrAny: ["advertisement"]
```

Supported operators are `contextualAny`, `ocrAny`, and `ocrAll`. `ocrAll` requires every configured term to match, subject to Immich's actual OCR search semantics. Do not assume case folding, substring matching, regular expressions, or exact phrase semantics. No rule condition produces a numeric confidence score.

When both `all` and `any` are present, both groups must be satisfied. `not`, when present, must not match. No condition in `exclude` may match. A match section must include at least one positive condition (`all`, `any`, or `not`); exclusions alone are not a classification rule.

## Commands and exit codes

```text
immich-smart-organizer init [--output PATH]
immich-smart-organizer config validate [--config PATH]
immich-smart-organizer scan [--config PATH] [--dry-run] [--full] [--limit N] [--category NAME] [--verbose]
```

The current `scan` implementation checks the server version and performs configured read-only OCR and contextual candidate searches. It reports discovered candidates and partial-coverage reasons; classification and library actions are not implemented yet, so no scan invocation changes library assets. `--full` means full re-evaluation of candidates Immich search can discover, not exhaustive library enumeration. Incremental state is not applied in this phase.

| Code | Meaning |
|---:|---|
| `0` | Command completed successfully. |
| `1` | General or I/O failure, including cancellation. |
| `2` | Invalid arguments or configuration. |
| `3` | Immich connectivity or authentication/permission failure. |
| `4` | Unsupported Immich version, endpoint, or response contract. |
| `5` | Partial discovery; some searches or result windows were incomplete. |
