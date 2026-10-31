# Changelog

Notable changes to this project are recorded here.

## [Unreleased]

- Add explicitly enabled album membership, hierarchical tags and conservative archival on Immich 3.3.5.
- Add SQLite action journaling, incremental update/fingerprint checks, exclusive run locking and interruption recovery.
- Block live mutations on partial discovery, ambiguous/protected assets and unsafe archival rules; bound archive attempts and errors.
- Disable automatic mutation retries; reconcile duplicate membership and archive visibility against server responses.
- Add CI for build, MTP tests, formatting, example validation, packaging, and local tool smoke checks.
- Add a manually approved NuGet release workflow; tag builds do not publish packages.
- Document installation, deployment, troubleshooting, security reporting, and contribution guidance.
- License the project under MIT.
