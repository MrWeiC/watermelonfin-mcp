# Repair metadata of your own completed video imports

The v0.1.3 MCP and native plugin add title, description, studio, genre, verified provider ID, and artwork repair without re-uploading, moving, or changing video bytes. Both components must be upgraded. A v0.1.2 server continues to support uploads but rejects these tools with an explicit upgrade requirement.

## Ownership and supported items

Every operation addresses `profileId + jobId`, not an arbitrary Jellyfin item ID or server path. The plugin resolves the original manifest, verifies its `OwnerId`, target allowlist, completed video, and canonical path, then resolves exactly that indexed video. Administrator, API-key, disabled, deletion-enabled, revoked, and foreign-owner sessions are refused.

This covers movies, `other` video targets such as Home School, and individual TV episode imports. It does not edit enclosing series/season metadata, unrelated library media, or audio/music metadata. Completed imports remain eligible beyond the unfinished upload's 24-hour expiry. Existing v0.1.2 manifests and paths remain supported.

## Tools

| Tool | Input after `profileId + jobId` | Result |
| --- | --- | --- |
| `inspect_import_metadata` | None | Revision/history, persisted/indexed fields, artwork registration, verification state |
| `update_import_metadata` | `expectedRevision`, `metadata` | Snapshot, supported-field replacement, exact-item refresh and readback |
| `set_import_artwork` | `expectedRevision`, `role`, absolute local `path` | JPEG/PNG stream, snapshot, registration and readback |
| `restore_import_metadata` | `expectedRevision`, previous `revision` | New revision restoring that job's prior metadata/artwork |
| `refresh_import_metadata` | `expectedRevision` | Reapply and refresh/read back the same indexed item |

Use each returned revision as the next `expectedRevision`. Stale revisions are rejected; inspect again rather than silently retrying a write. `initial` identifies the original state and appears in history after the first change. Revision IDs are private to a job.

## Supported metadata

`title` is required (1–200 characters). Optional fields are `originalTitle`, `sortTitle`, `overview`, `productionYear`, `premiereDate` (`YYYY-MM-DD`), `studios`, `genres`, and `providerIds` (`imdb`, `tmdb`, `tvdb`). A supplied premiere date and year must agree. Unknown fields are refused.

Updates replace this supported field set: omitted optional fields are cleared, and omitted lists/IDs become empty. Inspect first and retain the supported values you want to keep. Unrelated provider IDs and NFO data such as actors are preserved. Explicit repaired metadata is locked against unintended online rematching; original fields and locks are journaled for restoration.

Example desired metadata, without guessing a release year or matching a similarly named feature film:

```json
{
  "title": "NOAH",
  "originalTitle": "NOAH",
  "sortTitle": "NOAH",
  "overview": "A live stage production from Sight & Sound Theatres following Noah and his family through the biblical account of the ark and flood.",
  "studios": ["Sight & Sound Theatres"],
  "genres": ["Faith", "Musical", "Family"],
  "providerIds": {}
}
```

Verify description and artwork against the intended production. Runtime similarity alone does not establish the release year, cast, or provider ID.

## Artwork

Roles are `primary` and `backdrop`. Use an explicitly selected local JPEG/PNG inside `WATERMELONFIN_UPLOAD_ROOTS`. Limits: 8 MiB, 8192 pixels per side, and 40 million pixels. The plugin checks structure/checksums where applicable and invokes Jellyfin's image decoder before publication. Existing local symlink/shared-write restrictions apply.

Research/download artwork on the client, then supply its local path. Metadata tools accept no artwork URL and expose no arbitrary server-side fetch. Bytes stream directly; responses contain fields/registration flags, not image bytes or server paths.

Movie/other-video sidecars use `movie.nfo`, `poster.jpg`/`poster.png`, and `fanart.jpg`/`fanart.png`. Episode sidecars use the episode basename with `.nfo`, `-thumb`, and `-fanart`. The plugin controls these names. Video names, publication paths, and bytes remain unchanged.

## Revisions and recovery

Before a change, original fields, locks, artwork registration, and supported sidecar bytes are saved in the private upload job directory. The per-job maximum is 100 historical revisions; reaching it refuses another change rather than deleting history. No public cleanup endpoint is added.

A pending authoritative journal lets the next authorized inspection recover incomplete sidecar publication. Restoring applies a new revision and recreates original sidecar bytes, including originally absent files; it never deletes a video. Cached original artwork remains retained and is registered again. Manual removal/relocation of that original cached image can prevent safe restoration and must not be reported as success.

Responses distinguish `persistedMetadata` from `indexedMetadata`. `verified: true` requires matching fields, locks, and image registration after the exact item is refreshed. `refreshStatus: pending` means the change is journaled but synchronization did not finish; inspect or refresh using its current revision. `mismatch` means readback differs from the requested state. Successful upload or playback alone is not proof of correct catalog presentation.

## Upgrade and live verification

1. Build/test/package v0.1.3 and review its checksum. A local ZIP is not automatically a published release/catalog entry.
2. A Jellyfin administrator installs the compiled native plugin and restarts the server at a suitable time. Preserve existing configuration and library paths; daily MCP credentials remain ordinary.
3. Update/restart the MCP. `inspect_upload_plugin` must return `metadataProtocol: watermelonfin-metadata.v1`, `ownedImportMetadata: true`, and `metadataRestore: true`.
4. Inspect the owned completed job, apply verified metadata/artwork with revision preconditions, and read back clean title, description, studio/genres, and primary image.
5. Verify intended-library membership and bounded video streaming. Retain the revision needed for restoration.
