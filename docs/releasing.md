# Release process

The release workflow verifies SemVer tags, restores and builds the solution, runs the MTP tests, checks formatting, validates the example configuration, and packs and smoke-tests the tool. A tag push **does not publish** to NuGet.org.

## Required repository setup

Before a release can publish:

1. Create a GitHub Actions environment named `nuget`.
2. Require approval from the repository owner (or designated release maintainers) for jobs deploying to that environment.
3. Add a narrowly scoped NuGet.org API key as the environment secret `NUGET_API_KEY`.
4. Confirm the API key is restricted to this package and expires according to the owner's credential policy.

If that protected environment and secret are not configured, do not attempt a package publication.

## Preparing and approving a release

1. Move the release entries in `CHANGELOG.md` from `Unreleased` to a heading for the version being tagged, and update the project metadata if applicable.
2. Create and push a SemVer tag such as `v0.1.0-alpha.1` or `v0.1.0`. The tag workflow verifies and packages the release but does not publish it.
3. Review the completed tag workflow and its package artifact.
4. In GitHub Actions, manually run the **Release** workflow using the approved tag as the selected ref and set `publish` to `true`.
5. Approve the protected `nuget` environment only after checking the tag, workflow run, package version, and release notes.

The manual dispatch input and environment approval are separate gates. The workflow publishes only for a manually dispatched, valid SemVer tag with `publish: true`; ordinary branch builds and tag pushes cannot publish. Do not trigger this workflow or create a release tag without explicit owner approval.

The workflow uses the package version from the tag (with the leading `v` removed), publishes to NuGet.org, and creates a GitHub Release with `CHANGELOG.md` and the `.nupkg` artifact. The tool remains framework-dependent and requires the .NET 10 runtime.
