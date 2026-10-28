# Owned Import Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Let a restricted user inspect, repair, and restore metadata/artwork of their own completed plugin imports, including the existing Noah, without touching video bytes or other media.

**Architecture:** Reuse upload manifests as the sole ownership authority. A bounded core metadata store maintains sidecars and revisions; a Jellyfin adapter resolves the exact imported item, applies explicit presentation state, and refreshes/verifies only that item. MCP tools address imports using profile and job IDs, and stream allowed local artwork directly.

**Tech Stack:** Node.js 22+, MCP SDK 1.32.2, Zod 4.6.7, C#/.NET 10, Jellyfin 12.4.0.

**Spec:** `docs/superpowers/specs/2026-10-28-owned-import-metadata-design.md`.

## Global Constraints

- Only metadata and images of completed imports owned by the currently authenticated ordinary, non-deletion-enabled allowlisted user.
- No video modification, re-upload, relocation, deletion, broad scans, arbitrary server paths, arbitrary item IDs, or server-side URL fetches.
- Reuse current v0.1.2 manifests/layout; completed imports remain repairable beyond their unfinished-upload expiry.
- Preserve existing installation, credentials, and all current user configuration; keep secrets out of repository and tool responses.
- Bounded strict fields and image payloads, per-job serialization, revision conflict checks, previous-state snapshots, and restore are required.

## Review Focus

1. An old completed job remains repairable while an expired unfinished job is rejected (Task 1).
2. A valid job ID paired with another owner, revoked target, or substituted path never reaches metadata mutation (Tasks 1–2).
3. Unknown XML-like fields and malformed/oversized image payloads cannot create files or request remote URLs (Tasks 1–3).
4. Concurrent changes, failed refreshes, and restore of originally absent artwork preserve accurate revision/state reporting (Tasks 1–2).
5. A successful write does not count as completion until indexed title/artwork are read back and streaming still works (Tasks 2–4).

## Task 1: Ownership-bound metadata persistence and restoration

**Files:** modify `Core/UploadStore.cs`, `Core/Models.cs`, `Core/SafePaths.cs` as necessary; create `Core/ImportMetadataStore.cs` and metadata model file under `plugin/WatermelonFin.Plugin`; extend `plugin/WatermelonFin.Plugin.Tests/Program.cs`.

**Interfaces:** `UploadStore.ResolveCompleted(Guid owner, Guid jobId)` returns a validated owned completed import resolved from the manifest. `ImportMetadataStore.Inspect/Apply/Restore` consume that import, strict metadata, expected revision, and bounded image content; return revision and persisted state. No client-controlled destination enters these interfaces.

- [x] Write failing core tests for foreign owners, completed legacy manifests after expiry, incomplete imports, substituted ancestors, XML escaping/field bounds, image validation, concurrent stale revisions, snapshot/restore of present and absent files, and unchanged video hash.
- [x] Run the core test project and confirm failures identify missing metadata behavior.
- [x] Implement canonical ownership resolution, strict models, fixed sidecar/image locations, atomic publication, private per-job revision journal, and restore as a new revision.
- [x] Run all core tests; confirm rejected requests leave no side effects and video hashes remain unchanged.
- [x] Commit the passing ownership/persistence change.

## Task 2: Scoped native routes and Jellyfin integration

**Files:** create `ImportMetadataController.cs` and `JellyfinImportMetadata.cs`; modify `ServiceRegistrator.cs`, `UploadController.cs` capability response; extend `WatermelonFin.Plugin.AdapterTests/Program.cs`.

**Interfaces:** fixed routes under `WatermelonFin/Upload/Jobs/{jobId}/Metadata`, `/Artwork/{role}`, `/Metadata/Restore`, and `/Metadata/Refresh`. A Jellyfin adapter resolves the item from the validated completed import's path, reads presentation state, applies allowed fields/images, and refreshes that item only. Capability advertisement includes protocol version and supported metadata/artwork limits.

- [x] Write failing adapter tests for authentication/owner/target revocation on every route, unknown fields, body limits, item/path mismatch, exact-item refresh, original presentation snapshots, and response distinction between requested/persisted/indexed state.
- [x] Build against real Jellyfin 12.4.0 assemblies; record the expected failing tests.
- [x] Implement the adapter using actual SDK methods and register it through DI. Keep arbitrary item IDs and remote URLs absent from request models.
- [x] Run core and adapter suites; verify restore, locks/field preservation, and safe errors without credentials or server internals.
- [x] Commit the passing plugin endpoints/adapter change.

## Task 3: MCP inspection, updates, artwork, restore, and refresh

**Files:** create `src/import-metadata.js`, `tests/import-metadata.test.js`; modify `src/jellyfin.js`, `src/server.js`, and protocol tests.

**Interfaces:** tools `inspect_import_metadata`, `update_import_metadata`, `set_import_artwork`, `restore_import_metadata`, `refresh_import_metadata` accept `profileId + jobId`. Updates take strict metadata and an expected revision; artwork takes role plus a local file constrained by `WATERMELONFIN_UPLOAD_ROOTS`. Responses expose resulting revision, indexed values, image state, and verification status.

- [x] Write failing stdio/HTTP-boundary tests for each tool, strict unknown-field rejection, old-plugin capability refusal, allowed-root/symlink enforcement, bounded authentic images, redirect refusal, and secret scrubbing.
- [x] Run the new Node tests and confirm expected feature-missing failures.
- [x] Implement only bounded native metadata routes, safe local artwork streaming, strict tool schemas, and clear pending/verified results.
- [x] Run `npm test` and full native adapter validation; confirm no regressions in existing upload/read tools.
- [x] Commit the passing MCP tools change.

## Task 4: Documentation, packaging, review, and live repair

**Files:** update `README.md`, `SECURITY.md`, `docs/plugin.md`, onboarding recipe, version/package metadata, and packaging tests as required. Keep existing released tags/assets untouched.

- [x] Document ownership scope, required plugin upgrade, new routes/tools, bounded artwork workflow, revision/restore semantics, and ordinary-user permissions.
- [x] Run `npm test`, `npm audit --omit=dev`, and `scripts/validate-plugin.sh` with .NET 10; inspect the packaged DLL/version/checksum.
- [x] Review the complete branch with emphasis on the five Review Focus conditions and fix any findings with regression tests.
- [x] Produce a reviewable branch/PR and compiled plugin package. Preserve the active v0.1.2 until deployment is ready.
- [ ] After the updated server plugin is deployed/restarted, reconnect the local MCP and confirm capabilities.
- [ ] Inspect Noah, apply verified Sight & Sound title/description/studio/genres and artwork using only its owned job ID, and read back the new state.
- [ ] Verify clean title, registered poster, Home School membership, and successful bounded video streaming; record any metadata fields still unverified rather than guessing them.

## Self-review

All approved scope boundaries map to Tasks 1–3; deployment/live verification maps to Task 4. No general administrator or filesystem tool is introduced. Future-upload metadata submission intentionally follows this first repair pipeline. Exact Jellyfin adapter SDK signatures will be checked against restored 12.4.0 assemblies before implementation; unsupported SDK behavior must fail visibly rather than broaden permissions.
