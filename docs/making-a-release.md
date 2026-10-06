# Making a release

Releases are built, signed and drafted by GitHub (`.github/workflows/release.yml`) when you push a version tag. You
write the notes and publish. Installed copies of Tyrant then offer the update (Help → Check for updates, or the daily
check).

## One-time setup

1. **Make the signing key** (in `studio/`): `npm run updater-key`. It asks for a password and writes:
   - the **private key** to `%USERPROFILE%\.tyrant\tyrant-updater.key` — back it up somewhere safe (a password manager,
     a USB stick). Never commit it;
   - the **public key** to `studio/src-tauri/updater-key.pub` — commit this file.
2. **Add two GitHub secrets:** repository → Settings → Secrets and variables → Actions → New repository secret:
   - `TAURI_SIGNING_PRIVATE_KEY`: the whole content of `tyrant-updater.key`;
   - `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`: the password you chose.
3. **A screenshot for the README** (the app with a mod open) in `docs/images/`, linked under the title.

Builds made before step 1 still run; their Help → Check for updates says updates aren't set up in that build.

## Each release

1. `cd studio` → `npm run set-version 0.2.0` (writes the version in package.json, tauri.conf.json, Cargo.toml,
   Directory.Build.props and the Blender add-on's blender_manifest.toml).
2. Commit: `git commit -am "release: 0.2.0"`.
3. Tag and push: `git tag v0.2.0` → `git push origin main v0.2.0`.
4. GitHub → Actions → **Release**: it checks the tag matches the version and the key is set up, runs the tests, builds
   the Setup, signs it, writes `latest.json` and attaches the framework zip and the Blender add-on zip
   (`Tyrant-Blender-Addon-<version>.zip`; `set-version` also writes the version into the add-on's manifest) to a
   **draft** release.
5. Open the draft (Releases), write what changed, **Publish**. Only published releases are offered to users.

The first time, do a dry run: GitHub → Actions → **Release** → **Run workflow**, and give the tag of the current version
(e.g. `v0.1.0`; it must match `tauri.conf.json`). Check the draft it makes (the Setup, its `.sig`, `latest.json` and the
framework zip), then delete the draft and, if GitHub created it, the tag. The update banner links to the release page
for what changed, so write the notes there.

## If the private key is lost

Installed copies can only accept updates signed with that key. Make a new key (delete the old files, run
`npm run updater-key`, update the two secrets), release, and tell users to download that one Setup by hand; after that,
updates work again.
