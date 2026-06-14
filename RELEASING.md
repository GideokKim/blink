# Releasing Blink

Releases are **tag-driven**. Pushing a version tag runs the
[`Release` workflow](.github/workflows/release.yml) on a Windows runner, which tests the
engine, publishes the app + indexer worker, compiles the Inno Setup installer, and
publishes a GitHub Release with auto-generated notes and `Blink-Setup-<version>.exe`
attached. You don't build or upload anything by hand.

## Versioning

- [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH`.
- Tags are the version prefixed with `v` (e.g. `v0.1.0`). The workflow strips the `v` and
  injects the number into the installer via `iscc /DAppVersion=...`, so the tag is the
  single source of truth for the release name, the installer version, and the asset name.
- A **pre-release** is any version with a semver pre-release suffix — anything after a `-`
  (e.g. `v0.0.1-test`, `v1.2.0-rc1`, `v0.9.0-beta`). It is published as a GitHub
  *pre-release* and never shows up as the "Latest" release.

## Cut a stable release

```bash
git checkout main
git pull
git tag v0.1.0
git push origin v0.1.0
```

That's it. Watch progress under the repo's **Actions** tab; when the run finishes, the
release appears at `https://github.com/GideokKim/blink/releases/tag/v0.1.0` with the
installer attached.

## Cut a pre-release (dry run / RC)

Use a tag with a pre-release suffix:

```bash
git tag v0.1.0-rc1
git push origin v0.1.0-rc1
```

This exercises the **entire** pipeline (tests → publish → installer → release) and produces
a real, downloadable `Blink-Setup-…exe`, but the release is flagged pre-release so it
doesn't affect the "Latest" release. Ideal for validating a change to the app, the
installer, or the workflow itself.

## Run manually (no tag)

From the **Actions** tab → **Release** → **Run workflow**:

- **Version** — the number without the leading `v` (e.g. `1.2.3` or `0.0.1-test`).
- **Mark as pre-release** — checkbox to force a pre-release even without a `-` suffix.

A manual run creates the tag at the current `main` commit.

## What the workflow does

1. Checkout + install .NET 8.
2. Resolve the version and decide pre-release (suffix `-…` or the manual checkbox).
3. `dotnet test Blink.Core.Tests` — a failing test blocks the release.
4. Publish `Blink.App` (self-contained, single file).
5. Publish `Blink.Indexer.Worker` (self-contained, single file).
6. *(if signing is configured)* Sign the app + worker exes via SignPath — see
   [Code signing](#code-signing-signpath).
7. Install Inno Setup (`choco install innosetup`).
8. Compile `installer/blink.iss` with the tag version.
9. *(if signing is configured)* Sign the resulting `Blink-Setup-*.exe` via SignPath.
10. Create the GitHub Release, then compose hybrid notes (CHANGELOG highlights + commit-derived
    detail), publish `latest.json`, and attach `installer/Output/Blink-Setup-*.exe`. See
    [Release notes](#release-notes).

## Code signing (SignPath)

Signed releases remove the **"Unknown publisher" / 알 수 없는 게시자** warning on the
downloaded `Blink-Setup-*.exe` (Windows SmartScreen reputation still builds up separately
as downloads accumulate). Blink uses [SignPath Foundation](https://signpath.org), which
gives **free** code signing to OSS projects — this is why Blink is licensed under GPL-3.0.

The workflow already contains the signing steps; they **stay dormant until the SignPath
secret exists** (`env.SIGNING_ENABLED`), so nothing breaks before setup. One-time setup:

1. **Enable MFA** on both your GitHub account and (next step) SignPath.
2. **Apply** at <https://signpath.org> for the Foundation OSS program. Requirements are
   already met: public repo, OSI license (GPL-3.0), and a published release. The certificate
   is issued to **"SignPath Foundation"** — that becomes the displayed publisher.
3. In the SignPath portal, create the project and note the slugs. They must match the
   workflow (`.github/workflows/release.yml`):
   - `project-slug: blink`
   - `signing-policy-slug: release-signing` (a SignPath default policy slug)
   - Add the predefined **GitHub.com** trusted build system to the org and link it to the
     project, and install the **SignPath GitHub App** on the repo.
   - Two **artifact configurations**. Because `actions/upload-artifact` zips its contents,
     the root element is `<zip-file>` for both. Use a schema-aware editor; namespace is
     `http://signpath.io/artifact-configuration/v1`.

   Artifact configuration `binaries`:

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="Blink.App.exe"><authenticode-sign/></pe-file>
       <pe-file path="Blink.Indexer.Worker.exe"><authenticode-sign/></pe-file>
     </zip-file>
   </artifact-configuration>
   ```

   Artifact configuration `installer` (the Setup name carries the version, hence the glob):

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="Blink-Setup-*.exe"><authenticode-sign/></pe-file>
     </zip-file>
   </artifact-configuration>
   ```
4. Add the credentials to the repo (**Settings → Secrets and variables → Actions**):
   - Secret `SIGNPATH_API_TOKEN` — your SignPath user/CI API token.
   - Variable `SIGNPATH_ORGANIZATION_ID` — your SignPath organization GUID.
5. Cut a pre-release tag (e.g. `v0.1.0-rc1`) to verify the signed installer end-to-end
   before a stable release.

Once the secret is present the workflow signs the two exes **before** packaging and the
final installer **after**, with no other changes needed.

## Release notes

Notes are **hybrid** — composed by the release workflow from two sources, so they read well
for users without depending on pull requests (this repo merges locally and pushes to `main`):

1. **Highlights (hand-written, user-facing)** — the version's section in
   [`CHANGELOG.md`](CHANGELOG.md). Write these in user-benefit terms ("멈춤 없는 검색"),
   not implementation terms. **This is the only part the in-app "새로워진 점" card shows.**
2. **Detail (auto-generated)** — every commit since the previous tag, grouped by
   Conventional Commit type (`feat`→✨, `fix`→🐛, `perf`→⚡, `docs`→📝, rest→🔧). This
   captures direct-to-`main` commits too, and appears in a collapsible *전체 변경 내역* block
   on the **web** release page only (not in the app).

### Before tagging a release

Edit `CHANGELOG.md`: move the items under `## [Unreleased]` into a new
`## [x.y.z] - YYYY-MM-DD` section, then leave a fresh empty `## [Unreleased]` on top. The
workflow reads the `## [x.y.z]` section by version (falling back to `## [Unreleased]`, then to
the auto-generated detail, so the in-app card is never empty).

## Delete / re-do a release

```bash
# Delete the remote + local tag
git push origin :refs/tags/v0.0.1-test
git tag -d v0.0.1-test
```

Then delete the GitHub Release from the **Releases** page (or via the API). Re-pushing the
same tag will not re-run the workflow unless the tag is recreated.

## Build the installer locally instead

If you'd rather build on your own Windows machine without cutting a release, see
[`installer/README.md`](installer/README.md) (publish the binaries, then `iscc installer\blink.iss`).

## Troubleshooting

- **WPF publish fails** — `Blink.App` only builds on Windows; the workflow already runs on
  `windows-latest`, which includes the .NET Windows Desktop pack.
- **`ISCC.exe` not found** — the workflow calls
  `C:\Program Files (x86)\Inno Setup 6\ISCC.exe` (the choco default). If a future Inno
  Setup changes that path, update the *Build installer* step.
- **Missing `Korean.isl`** — ships with Inno Setup 6 under `Languages\`. If you remove the
  Korean language line in `blink.iss`, also drop it from `[Languages]`.
- **Release step can't upload** — the job has `permissions: contents: write`; don't remove it.
