# Security policy

## Supported versions

Security fixes are intended for the latest source on `main` and the latest published release. Older releases may not receive fixes.

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's **Report a vulnerability** flow for this repository (Security → Advisories → Report a vulnerability). If private vulnerability reporting is unavailable, contact the repository owner privately through GitHub. Do not open a public issue or include exploit details in a public discussion.

Include the affected version or commit, impact, and concise reproduction details. Do not send API keys, private Immich URLs, personal media, or other credentials. Reports are reviewed as time permits; no response-time guarantee is made.

## Security boundaries

The tool reads an Immich API key from the environment variable named in configuration. Keep the key out of YAML, command arguments, logs, and source control. Dry-run is enabled by default; live mutations require explicit configuration. The tool does not transmit data to third-party AI services or collect telemetry. Search and the local SQLite journal contain asset identifiers; protect state files and review verbose logs before sharing them.
