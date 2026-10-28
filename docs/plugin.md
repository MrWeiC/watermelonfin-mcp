# Native Jellyfin upload plugin

The companion plugin is built with .NET 10 against `Jellyfin.Controller` and `Jellyfin.Model` **12.4.0**. It uses Jellyfin's existing HTTP base path and user authentication; there is no separate server-side Node service, upload port, SSH requirement, or dependency on RemoteUpload.

**Preview status:** compiled and locally tested, including adapter tests using actual Jellyfin SDK types and JSON converters. Loading the plugin into a complete Jellyfin instance and verifying indexing/host permissions remain untested. Do not treat catalog availability as proof of production compatibility.

## Install through the Jellyfin catalog

The official repository is maintained by [@MrWeiC](https://github.com/MrWeiC/watermelonfin-mcp). The catalog address is:

```text
https://raw.githubusercontent.com/MrWeiC/watermelonfin-mcp/main/catalog/manifest.json
```

The catalog may be empty until a tested pre-release package has been published. Do not replace the catalog URL with a ChatGPT download page, an unverified fork, or a ZIP URL.

1. As a Jellyfin administrator, open **Dashboard → Plugins → Manage Repositories**, add the catalog URL, then install **WatermelonFin Upload** from the catalog.
2. Restart Jellyfin at a suitable time to load the plugin. The MCP does not restart the server automatically.
3. Open the plugin's configuration page. Uploads start **disabled**. Add the intended restricted Jellyfin user IDs to the allowlist. The plugin accepts neither administrator accounts/API keys nor users with media-deletion permissions.
4. Configure import targets. A target has only `Id`, `Name`, and `Kind`; clients cannot supply a server path. Supported kinds are `movies`, `tv`, and `other`.
5. The configuration page lists the plugin's `Imported/<target-id>` library-source paths. Add the appropriate paths to the intended Jellyfin libraries, preserving every existing source path. **Never index the plugin root or `Staging`.** Reading the administrator information prepares the target directories.
6. Enable uploads only after reviewing the account, targets, paths, and available storage.
7. Configure the local MCP's restricted-user login and `WATERMELONFIN_UPLOAD_ROOTS`; call `inspect_upload_plugin`, `list_upload_targets`, then `upload_media` with a small selected sample.
8. Verify the bytes, filename/layout, Jellyfin indexing, metadata behavior, and authorization revocation before broader use.

This requires one-time administrator access to Jellyfin, not system-level remote access. An ordinary user cannot install a server plugin merely by knowing the server's IP address.

## Import behavior

All files remain inside `<Jellyfin Program Data>/watermelonfin-imports`:

- `Staging/<job-id>` holds incomplete data and persistent job metadata.
- `Imported/<target-id>/<job-id>/...` holds verified, completed imports.

Each job is owned by the authenticated user. Every request rechecks the plugin allowlist; other users cannot query, resume, or publish that job. Revoking authorization blocks later requests. Clients never receive a general filesystem browser or path-writing API.

A movie target requires an unambiguous name such as `Title (2024).mp4`. A TV target requires a name such as `Title S01E03.mkv`. An `other` target preserves a validated filename. The MCP's optional `fileName` argument can clarify a new import's name without renaming the selected local source.

The new job namespace prevents same-name overwrites. A completed file is immutable through the API; there is no delete, overwrite, existing-file move, cleanup, full-library scan, remote shell, arbitrary URL download, or existing-library reorganization endpoint. Only the job's own staged bytes are moved at publication.

UUID grouping is deliberate in this preview: imports from separate jobs do not merge a series into a shared directory. Verify how the actual library type interprets this layout. **Upload completion is not confirmation of Jellyfin indexing.** Indexing depends on configured monitoring/schedules and must be checked independently.

Limits: 50 GiB per file; 4 MiB per chunk; 100 active uploads; 24-hour expiry; at most four buffered chunk readers. Completed/expired records no longer consume active-job count, but files are retained. There is no destructive automatic cleanup. Operators must manage storage separately.

## Security boundary

This native plugin shares Jellyfin's operating-system identity. Its API controls are not an independent filesystem sandbox. A compromised Jellyfin process, local administrator, or hostile process with the same filesystem identity may retain broader rights. The plugin root must not have untrusted writers. Windows ACLs, NAS filesystems, container volumes, host permissions, full server loading, and production indexing remain unverified. See [SECURITY.md](../SECURITY.md).

## Develop and package locally

Requirements: .NET 10 SDK, Node.js 22+, and Python 3.

```sh
npm ci --ignore-scripts
./scripts/validate-plugin.sh
```

Use `DOTNET_BINARY=/absolute/path/to/dotnet` if the SDK is not on PATH. Validation runs locally; no GitHub Actions workflow is used.

The v0.1.3 build outputs `artifacts/WatermelonFin.Upload-0.1.3.0.zip`, containing the plugin assembly and license/notices. A source archive is not an installable plugin package. The packager derives the version from the native project; it does not publish the ZIP or change the checked-in catalog.

To generate a catalog entry, supply the actual server-accessible HTTPS URL of the corresponding published ZIP:

```sh
python3 scripts/package-plugin.py --source-url ACTUAL_HTTPS_RELEASE_ASSET_URL --image-url ACTUAL_HTTPS_LOGO_URL
```

The packaging script computes the catalog checksum from the ZIP and also emits SHA-256 metadata. Do not publish a catalog entry with an invented URL. Build, package, and upload all release assets before publishing an immutable release; do not replace assets in an existing version.

## API

All routes are beneath the existing Jellyfin base path and require Jellyfin authentication. Daily routes also require the plugin allowlist/restricted-user checks; `AdminInfo` requires Jellyfin's administrator policy.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `WatermelonFin/Upload/Capabilities` | Protocol and safety/size limits |
| GET | `WatermelonFin/Upload/Targets` | Configured import targets |
| POST | `WatermelonFin/Upload/Jobs` | Create an owner-bound upload |
| GET | `WatermelonFin/Upload/Jobs/{id}` | Progress and resume metadata |
| PATCH | `WatermelonFin/Upload/Jobs/{id}?offset=N` | Bounded binary chunk |
| POST | `WatermelonFin/Upload/Jobs/{id}/Finalize` | Verify and publish the job's new file |
| GET | `WatermelonFin/Upload/AdminInfo` | Administrator-only path guidance |

The plugin provides a receiving API and a configuration page. A graphical drag-and-drop uploader and Apple client upload/pairing UI are not part of this preview.

### 0.1.2 compatibility fixes

MCP uses the standard `Authorization: MediaBrowser ...` header for login, reads and native uploads. New uploads use a readable top-level folder such as `Lesson One [unique-id]/Lesson One.mp4` instead of a UUID-only folder. Movies and TV retain their planned filenames and season paths inside a readable unique folder. The identifier remains visible where Jellyfin derives titles from folders; exact title rendering depends on library metadata settings. Existing jobs retain the old layout and are never moved or renamed.

### 0.1.3 owned-import metadata repair

The new MCP and native plugin can repair and restore only the authenticated user's completed video imports. Unique storage folders remain unchanged; explicit NFO/indexed titles avoid displaying job IDs as titles. See [the full workflow](owned-import-metadata.md) for replacement-field semantics, bounded local artwork, revision history, restore, and readback verification. Upgrading both components and restarting Jellyfin is required; the published v0.1.2 package lacks these routes.

| Method | Route under `WatermelonFin/Upload/Jobs/{job-id}` | Purpose |
| --- | --- | --- |
| GET | `Metadata` | Inspect owned import metadata, revision history, images, and indexed state |
| POST | `Metadata` | Replace supported metadata fields using an expected revision |
| POST | `Artwork/{primary-or-backdrop}?expectedRevision=...` | Stream a bounded local JPEG/PNG |
| POST | `Metadata/Restore` | Restore a job-private historical revision as a new revision |
| POST | `Metadata/Refresh` | Reapply and refresh/read back only the exact owned item |

Writes preserve video bytes and publication paths. There is no general metadata editor for other media, no media deletion/overwrite, and no arbitrary filesystem or remote-artwork-fetch API.

### Plugin branding

The package includes the existing WatermelonFin mark as `logo.png` and a Jellyfin `meta.json` declaring `imagePath: logo.png`. External plugin images use that local manifest rather than the server-bundled-only embedded-image interface. The packager also emits `artifacts/watermelonfin-logo.png` for hosting with a release. Catalog generation requires explicit, real HTTPS URLs for both ZIP and logo; no unpublished logo URL is guessed.

The MCP advertises the same bundled mark in `serverInfo.icons` using an inline PNG, so it requires no remote fetch. Whether a particular host renders protocol icons is a host UI capability.
