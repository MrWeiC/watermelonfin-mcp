# Security policy and boundaries

## Reporting a vulnerability

Use this repository's **Security → Report a vulnerability** private reporting flow when available. Do not post credentials, private server addresses, media inventories, exploit details, or personal data in a public issue. No response-time or production-support commitment is made for this early preview.

## Official repository and releases

The official owner is **@MrWeiC**. Public visitors can read, fork, and propose changes; they do not receive write or release access. Only the owner is listed as the official code owner. Repository rules restrict branch/tag changes to the owner, and GitHub Actions are disabled. Published release assets are immutable when GitHub confirms that setting is enabled. These settings must be verified in GitHub; a CODEOWNERS file alone does not enforce them.

An account credential, authorized app, or integration acting on the owner's behalf can still exercise the authority GitHub grants it. This repository cannot protect against a compromised owner account, leaked owner token, or an owner changing its protection settings. Forks and third-party packages are not official releases.

## Local MCP controls

- Tools are explicitly allowlisted, with strict argument validation. There are no media-deletion, video-overwrite, arbitrary HTTP, arbitrary filesystem, general shell, or plugin-installer tools. Owned-import metadata repair is a separate bounded capability that can replace/restore only fixed sidecars and indexed presentation.
- Jellyfin reads target fixed endpoints. Credential login uses only `POST /Users/AuthenticateByName`; native upload requests use only the plugin's bounded capability/target/job routes. Redirects are refused, response sizes are bounded, and normal TLS validation remains enabled.
- Credential transmission requires host-configured transport trust and server-ID consistency checks. Daily operations reject administrator and deletion-enabled users. Server IDs are not authentication.
- Passwords/tokens are host-injected; tools do not accept them as arguments. Login tokens stay in memory and are not intentionally returned or logged. No automatic failed-login retry loop is used. The host agent and same-account processes are trusted; environment variables are not an OS secret vault. There is no built-in secure-input or keychain UI.
- Upload roots must be narrowly scoped. The service cannot prove that a natural-language instruction selected a file intentionally; the host must enforce tool approval and file-access policy.
- Files, names, and media metadata are untrusted data, not instructions. They do not expand the tool allowlist.

## Native Jellyfin plugin

The plugin starts disabled. Each daily request requires a current Jellyfin user session, an explicit plugin allowlist entry, and a non-administrator/non-deletion-enabled account. API keys and disabled users are rejected. Jobs are bound to their owner; a different user cannot query, resume, or publish them. Revoking plugin authorization blocks later requests.

All destinations are inside the fixed plugin-owned `watermelonfin-imports` root. Unfinished files remain in `Staging`, outside indexed `Imported` paths. New files are published to new job namespaces only after size/hash verification, without overwriting an existing destination. Clients cannot submit destination paths. There are no media deletion, existing-library reorganization, full-scan, cleanup, remote-command, or arbitrary URL-download endpoints.

The plugin shares **Jellyfin's operating-system identity**. Its application checks are not an independent OS sandbox. A compromised Jellyfin process, administrator, or hostile process with the same filesystem identity may have broader rights than the plugin API. Keep the plugin root private and exclude untrusted writers. Symlink checks are not a defense against a same-identity adversary racing filesystem changes. Windows ACLs, NAS filesystems, container volumes, full server loading, and production indexing remain unverified.

Limits: 50 GiB per file, 4 MiB per chunk, 100 active jobs, 24-hour job expiry, and at most four buffered chunk readers. Completed/expired records do not consume active-job count, but files are retained. There is no automatic destructive cleanup; operators must manage storage separately.

## Owned-import metadata repair (v0.1.3)

Every metadata read, edit, image update, restore, and refresh rechecks the restricted user, plugin allowlist, manifest owner, current target, completion, canonical video path, and exact indexed video. Completed legacy imports remain repairable after upload expiry. No arbitrary item ID, server path, remote image URL, library-wide scan, or privilege escalation is accepted.

Supported metadata is strict and bounded; NFO XML is escaped and external entities are prohibited. Existing unrelated NFO nodes/provider IDs are preserved. JPEG/PNG content is size/dimension bounded, structurally checked, and decoded with Jellyfin before publishing new artwork. Prior sidecars, indexed fields/locks, and original artwork registration are journaled per job. Restore may remove only an owned metadata/artwork sidecar that was originally absent; video files are never removed or rewritten. Revision conflicts reject stale edits. Recoverable pending writes and incomplete SDK refreshes are reported without claiming verified success.

Only owned-import sidecars and private journal files are written directly. Jellyfin's SDK persists that item's database/image registration. The same-OS-identity trust boundary and symlink-race limitations still apply. See [the workflow](docs/owned-import-metadata.md).

## Optional standalone importer

Standalone mode is separate from the native plugin. It uses a dedicated unprivileged OS identity, a private ingestion root, a separately protected endpoint, and job-bound grants. It must not have write mounts/ACLs for existing libraries. One importer process per root is supported. Existing media must remain outside its writable namespace. See `docs/provisioning.md`; none of these OS boundaries can be inferred solely from a successful API response.

## Dependency maintenance

The v0.1.1 preview updates the MCP SDK/Zod and transitive dependencies to address the advisories reported by the initial lockfile audit. Production dependency auditing reported zero known vulnerabilities on October 27, 2026. The initial v0.1.0 source remains retained as an immutable, superseded preview; use the newer release. Dependency alerts are read-only notifications, not permission for automated repository changes.

## Preview limitations

Local tests do not establish production network, account, ACL, crash/power-loss, metadata matching, or indexing behavior. Upload completion is not proof of indexing. There is no absolute no-data-loss guarantee. Evaluate the preview with a small sample and a current backup before considering wider use.
