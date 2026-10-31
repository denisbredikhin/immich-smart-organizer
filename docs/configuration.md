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

Unknown properties are errors rather than silently ignored values. YamlDotNet rejects missing required members and explicit null values while loading the file. The required root sections are `immich`, `state`, and `categories`; each category requires `name`, `search`, and `match`. Semantic validation then checks values, ranges, and cross-field rules. The optional `scanner`, `processing`, and `logging` sections use their defaults when omitted. The safe example includes all sections explicitly.

| Property | Type | Default | Validation / meaning |
|---|---|---|---|
| `immich` | section | required | Immich connection settings. |
| `immich.url` | absolute URL | required | Must use HTTP or HTTPS and must not contain embedded user information. |
| `immich.apiKeyEnv` | environment variable name | required | Must be a valid variable name whose value is set and non-empty. |
| `scanner` | section | defaults | Optional; omitted values use the defaults below. |
| `scanner.mode` | `incremental` or `full` | `incremental` | `full` means candidate re-evaluation, not guaranteed exhaustive library enumeration. |
| `scanner.batchSize` | integer | `100` | `1`–`500`. |
| `scanner.concurrency` | integer | `2` | `1`–`8`; current execution is serial (one active request), within this upper bound. |
| `scanner.includeArchived` | boolean | `true` | `false` restricts searches to timeline visibility. |
| `scanner.includeExternalLibraries` | boolean | `true` | `false` adds a null `libraryId` filter; completeness of external-library discovery is not live-verified. |
| `scanner.includeVideos` | boolean | `false` | Whether videos may be included in discovery. |
| `scanner.lookbackDays` | integer | `7` | `1`–`3650`; reserved, not used as a capture-date cutoff because historical imports would be missed. |
| `processing` | section | defaults | Optional; omitted values use the defaults below. |
| `processing.dryRun` | boolean | `true` | Plans are not completed actions. Dry-run must send no mutation requests. |
| `processing.maxAssetsPerRun` | integer | `1000` | `1`–`100000`. |
| `processing.maxArchiveActionsPerRun` | integer | `25` | `0`–`1000`; bounds archive requests, including failed attempts; deferred actions stay pending. |
| `processing.maxErrorsPerRun` | integer | `20` | `1`–`1000`; stops search/action requests at the threshold. Authentication/incompatible-action failures stop execution immediately. |
| `processing.ambiguousAssetPolicy` | `review` or `skip` | `review` | Ambiguous results must not be assigned arbitrarily. |
| `state` | section | required | State database settings. |
| `state.path` | file path | `./state.db` | Relative paths are based on the configuration file's directory. |
| `categories` | non-empty list | required | Names must be unique, case-insensitively. Each category needs a search query and a match condition. |
| `categories[].name` | string | required | Unique category name. |
| `categories[].enabled` | boolean | `true` | Disabled categories are ignored by classification. |
| `categories[].search` | section | required | Configured discovery queries for this category. |
| `categories[].search.contextual` | list of strings | empty | Contextual/CLIP search queries; these are not calibrated confidence values. |
| `categories[].search.ocr` | list of strings | empty | OCR search queries; exact matching semantics are determined by Immich. |
| `categories[].match` | section | required | Rule conditions for this category. |
| `categories[].match.all` | list of conditions | empty | All listed conditions must match. Condition terms must also be configured in the corresponding search list. |
| `categories[].match.any` | list of conditions | empty | At least one listed condition must match when the list is non-empty. |
| `categories[].match.not` | condition | unset | The category condition is satisfied when this condition does not match. |
| `categories[].match.exclude` | list of conditions | empty | Any matching exclusion blocks the category; exclusions do not qualify a category by themselves. |
| `categories[].actions.album` | string | unset | Optional album display name. |
| `categories[].actions.tag` | string | unset | Optional hierarchy path with non-empty segments; children are created with Immich's `parentId`, never as filesystem paths. |
| `categories[].actions.archive` | boolean | `false` | Live archival requires mandatory contextual and OCR conditions in `match.all` and is bounded by the run limit. |
| `logging` | section | defaults | Optional; omitted values use the defaults below. |
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

Supported operators are `contextualAny`, `ocrAny`, and `ocrAll`. Rules compare terms case-insensitively and exactly against the search queries that returned the candidate; all terms must also appear in the matching `search.contextual` or `search.ocr` list. `ocrAll` means every listed OCR query independently returned the asset; it does not mean those terms were present together in one OCR document. Do not assume that Immich's own OCR search performs substring matching, regular expressions, or exact phrase matching. No rule condition produces a numeric confidence score. See [classification](classification.md) for details.

When both `all` and `any` are present, both groups must be satisfied. `not`, when present, must not match. No condition in `exclude` may match. A match section must include at least one positive condition in `all` or `any`; `not` and `exclude` only restrict a positive rule.

## Commands and exit codes

```text
immich-smart-organizer init [--output PATH]
immich-smart-organizer config validate [--config PATH]
immich-smart-organizer scan [--config PATH] [--dry-run] [--full] [--limit N] [--category NAME] [--verbose]
```

`scan` checks the server version, performs configured OCR and contextual searches, and evaluates category rules. With `processing.dryRun: false` it can apply live actions; `--dry-run` always overrides this. Dry-run does not create a SQLite journal or record completed actions. `--category` restricts actions, not discovery/classification: all enabled categories are evaluated to preserve conflict safety. Partial discovery blocks every live mutation.

`--full` (or `scanner.mode: full`) forces re-evaluation of discoverable candidates, not exhaustive library enumeration.

| Code | Meaning |
|---:|---|
| `0` | Command completed successfully. |
| `1` | General, state database, lock or I/O failure, including cancellation. |
| `2` | Invalid arguments or configuration. |
| `3` | Immich connectivity or authentication/permission failure. |
| `4` | Unsupported Immich version, endpoint, or response contract. |
| `5` | Partial discovery, action failure, or archive actions deferred by the run limit. Previously confirmed actions may already have succeeded. |

## State and incremental recovery

Live runs acquire an exclusive `<state.path>.lock` before discovery and retain it until the run ends. The empty lock file remains after release; do not delete it while any process is running. Use one canonical database path on a local filesystem for all schedules targeting the same library. Network filesystems, alternate aliases or multiple independent state databases are not distributed locks.

SQLite schema version 1 stores asset IDs, current update markers, processing timestamps, classification names, a hash of relevant configuration plus query evidence, pending/completed action records and the last action failure. It stores no credentials, media bytes or raw OCR text. Schema initialization is transactional; unsupported schema versions fail explicitly. A database is bound to an Immich URL and authenticated user to prevent cross-library reuse.

Incremental runs always rediscover query candidates and read current asset metadata. Unchanged completed candidates are skipped. Rule/action/query or asset marker changes trigger re-evaluation; `--full` bypasses the skip. Confirmed steps of an interrupted run are reused when fingerprints and markers match. Membership APIs treat server-reported `duplicate` as already satisfied; archive visibility is checked on the server before any archive request and again after a successful update.

The journal is not authoritative server state. To reconcile manually edited album/tag membership that does not change an asset's update marker, use `--full`. Removing an action from YAML never removes old album/tag membership or unarchives assets. Limited archive actions remain pending and subsequent runs advance them, provided those assets remain discoverable. No capture-time cursor or lookback cutoff is used, avoiding omission of historical imports.

Protect the database and logs: they can contain asset IDs and configured category/action names. Do not delete state to repair an interrupted run; retry with the same database first. See [manual recovery](classification.md#live-execution-and-manual-recovery).
