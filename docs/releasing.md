# Release process

## Automatic main prereleases

The CI workflow publishes the verified package to NuGet.org after a push to `main`, only after both the build/test/package/smoke checks and formatting pass. Pull requests, other branches, and tag pushes cannot use this publishing job. It downloads the package from the same workflow run instead of rebuilding it.

Versions are `0.1.0-ci.<run-number>.<run-attempt>`, including retries so rebuilt packages never reuse a version. When only failed jobs are re-run, publication uses the original verified artifact and its version. Update the `PACKAGE_VERSION` base in the CI workflow when starting a new release line. Main prereleases do not create GitHub Releases; tagged releases below remain manually approved.

### One-time trusted publishing setup

1. Create a GitHub Actions environment named `nuget-main`, with a deployment branch rule allowing only the branch `main`. Do not add required reviewers if publication should be automatic.
2. In your NuGet.org account, open **Trusted Publishing** and add a GitHub policy with:
   - Repository owner: `denisbredikhin`
   - Repository: `immich-smart-organizer`
   - Workflow file: `ci.yml` (filename only)
   - Environment: `nuget-main`
3. Use a NuGet.org account authorized to publish `Immich.SmartOrganizer`. Check any policy activation deadline shown by NuGet.org, especially before the first publication.
4. Set the GitHub environment variable `NUGET_USER` to that account's NuGet.org **profile name**, not its email. For example, run the following locally, replacing the placeholder:

   ```powershell
   gh variable set NUGET_USER --env nuget-main --repo denisbredikhin/immich-smart-organizer --body "<nuget-profile-name>"
   ```

The workflow requests a short-lived API key using `NuGet/login@v1` and job-scoped `id-token: write`. No persistent API key is required for main publishing. Missing configuration or a rejected trust policy fails the publish job explicitly. `gh` can configure GitHub environments and variables, but cannot create the NuGet.org trust policy.

After merging the workflow and completing this setup, the next successful `main` push publishes automatically. To recover a failed publication, re-run the failed jobs for that main workflow run. Already-published versions are skipped without overwriting them.

Install the latest prerelease with:

```shell
dotnet tool install --global Immich.SmartOrganizer --prerelease
```

See [NuGet trusted publishing documentation](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) for the NuGet-side setup.

## Tagged releases

The release workflow verifies SemVer tags, restores and builds the solution, runs the MTP tests, checks formatting, validates the example configuration, and packs and smoke-tests the tool. A tag push **does not publish** to NuGet.org.

### Required repository setup

Before a release can publish:

1. Create a GitHub Actions environment named `nuget`.
2. Require approval from the repository owner (or designated release maintainers) for jobs deploying to that environment.
3. Add a narrowly scoped NuGet.org API key as the environment secret `NUGET_API_KEY`.
4. Confirm the API key is restricted to this package and expires according to the owner's credential policy.

If that protected environment and secret are not configured, do not attempt a package publication.

### Preparing and approving a release

1. Move the release entries in `CHANGELOG.md` from `Unreleased` to a heading for the version being tagged, and update the project metadata if applicable.
2. Create and push a SemVer tag such as `v0.1.0-alpha.1` or `v0.1.0`. The tag workflow verifies and packages the release but does not publish it.
3. Review the completed tag workflow and its package artifact.
4. In GitHub Actions, manually run the **Release** workflow using the approved tag as the selected ref and set `publish` to `true`.
5. Approve the protected `nuget` environment only after checking the tag, workflow run, package version, and release notes.

The manual dispatch input and environment approval are separate gates. The Release workflow publishes only for a manually dispatched, valid SemVer tag with `publish: true`; automatic main prereleases use the separate CI job and environment described above. Do not trigger the Release workflow or create a release tag without explicit owner approval.

The workflow uses the package version from the tag (with the leading `v` removed), publishes to NuGet.org, and creates a GitHub Release with `CHANGELOG.md` and the `.nupkg` artifact. The tool remains framework-dependent and requires the .NET 10 runtime.
