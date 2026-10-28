# Changelog

## 0.1.3 — pre-release (2026-10-28)

- Add five MCP tools for revisioned metadata/artwork inspection, repair, restore, and refresh of a restricted user's own completed video imports.
- Resolve ownership from durable manifests; accept no arbitrary item IDs, server paths, administrator credentials, or remote artwork URLs.
- Preserve video bytes and publication paths; journal prior presentation and recover interrupted sidecar publication.
- Support movie/other-video NFOs and individual episode sidecars while preserving unrelated NFO fields/provider IDs.
- Register artwork through Jellyfin and distinguish persisted changes from verified indexed presentation.
- Advertise metadata capabilities; reject older native plugins before mutation.
- Derive package/catalog versions from the native project.
- Bundle the existing WatermelonFin logo for Jellyfin installed-plugin/catalog images and MCP protocol icons.
- Persist SDK synchronization status, replay journals before reading dangling artwork, validate full image pixels, and detect/capture external metadata drift before subsequent edits.

## 0.1.2

- Standard Jellyfin authorization header, readable unique import folders, and refreshed dependency lockfile.
