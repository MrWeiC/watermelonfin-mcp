# Metadata repair for owned imports

Approved in the Codex conversation on October 28, 2026. Scope: only metadata and images of media uploaded through this plugin by the currently authenticated user.

## Outcome

An authorized ordinary user can inspect, repair, and restore the metadata and artwork of a completed upload, refresh that item, and verify Jellyfin's resulting presentation. Existing Noah can be repaired without uploading its video again. Other media and other users' uploads remain outside this capability.

## Approach

Extend the existing MCP and native plugin together. The plugin resolves `OwnerId + JobId` from its existing upload manifest, validates the completed video, and resolves the associated Jellyfin item by its canonical path. The caller cannot supply a server path or substitute an arbitrary item ID. Completed uploads remain eligible after their upload expiry time; expiry controls unfinished uploads only.

Metadata repair accepts a bounded explicit set of fields: title, original title, sort title, overview, verified production year/premiere date, studio, genres, and verified provider identifiers. Artwork is bounded JPEG/PNG data supplied by the MCP from an explicitly allowed local file, with primary and backdrop roles. Remote artwork discovery/download remains a client-side research activity; no arbitrary server-side URL fetch is exposed.

Persist validated local metadata (`movie.nfo`) and artwork beside the owned video, preserve the prior metadata/artwork in a revision journal, and refresh only the exact indexed item. Where needed, use the Jellyfin adapter to apply explicit fields and locks so a folder's upload ID or an unrelated online match cannot replace the intended title. Return actual indexed metadata and image state separately from requested state; do not report queued refresh as verified success.

## Boundaries

- Keep the ordinary-user, non-deletion-enabled, explicit plugin allowlist checks on every request.
- Recheck owner, target authorization, manifest integrity, canonical destination, completion, and symlink constraints for reads, writes, refreshes, and restore.
- Never modify, move, overwrite, delete, or re-upload the video; retain the current unique publication layout.
- Only fixed metadata/artwork filenames and private per-job revision files may be written; never accept arbitrary filenames, filesystem paths, or URLs from the server API.
- Reject unknown fields, unsafe image content, excessive payloads, invalid years/provider IDs, concurrent stale revisions, and cross-job revision references.
- Capture state before changing it; a restore applies a new journaled revision. Missing pre-existing files and pre-existing Jellyfin fields must be represented accurately.
- Do not change shared libraries, trigger a full-library scan, or grant the MCP administrator access.
- Preserve v0.1.2 installation/configuration until a tested replacement is ready. Deployment requires updating the native plugin and a Jellyfin restart; do not claim live repair before it is verified.

## Alternatives considered

1. Administrator access to generic Jellyfin edit APIs: broad authority, contrary to the selected ownership scope.
2. Metadata only during new uploads: prevents future missing metadata but cannot repair the completed Noah import.
3. Scoped repair in the existing plugin (selected): reuses durable ownership manifests, supports existing imports, and keeps authority bounded.

## Validation

Tests must cover completed legacy v0.1.2 imports, foreign owners, revoked targets/users, malformed manifests, unfinished uploads, symlink substitution, bounded and authentic artwork, XML escaping, immutable video bytes, previous-state capture, restore, crash/concurrent writes, exact-item refresh, and actual Jellyfin SDK serialization. Final live checks must read back Noah's clean title, description, studio, genres, and primary image and verify that video streaming still works.

## Work order

First implement inspection and repair of completed owned imports with restore. Future-upload metadata submission is a follow-on that reuses the repair pipeline; it is not required to fix the existing Noah.
