# WatermelonFin MCP

A local Model Context Protocol (MCP) server for discovering Jellyfin servers, querying media libraries, and importing **new** media through a companion Jellyfin plugin.

**Early preview — not yet verified end to end on a production Jellyfin server.** The plugin has been compiled against Jellyfin 12.4.0. Local validation currently covers 33 JavaScript/MCP tests and 16 C# core/adapter tests. A successful local test run does not establish plugin loading, indexing, or deployment compatibility on every host.

WatermelonFin MCP is an independent project maintained by [@MrWeiC](https://github.com/MrWeiC). It is not affiliated with or endorsed by Jellyfin or Tailscale. The WatermelonFin Apple clients are separate applications; this repository does not include automatic Apple client pairing or a drag-and-drop upload interface.

## Capabilities

- Discover Jellyfin on a user-specified host and a bounded set of ports.
- Authenticate with a restricted Jellyfin username/password or an existing user token; no prior browser login is required.
- List libraries and search media without modifying existing content.
- Inspect the local Tailscale CLI for candidate addresses and MagicDNS names.
- Expose an onboarding recipe as an MCP prompt/resource.
- Plan new imports and stream selected local files directly to the native upload plugin, with resumable chunks and SHA-256 verification.

There are **no delete, overwrite, existing-media reorganization, arbitrary HTTP, arbitrary filesystem, or general shell tools**. The upload plugin starts disabled and requires an explicit user allowlist. These are application controls, not an operating-system sandbox; read [SECURITY.md](SECURITY.md) before enabling uploads.

## Requirements

| Component | Requirement |
| --- | --- |
| Local MCP | Node.js 22+ and a stdio-capable MCP host |
| Native plugin | Jellyfin 12.4.0; other server versions are unverified |
| Plugin development | .NET 10 SDK |
| Remote access | A trusted route to the server, such as your configured Tailscale network |

The native plugin reuses Jellyfin's port and user authentication. It does not require SSH, a separate upload port, a server-side Node installation, or the third-party RemoteUpload plugin. First-time plugin installation still requires a Jellyfin administrator and a server restart.

## Install the local MCP

Clone this repository, then run:

```sh
npm ci --ignore-scripts
npm test
```

Register the local entry point with your MCP host, replacing the absolute path:

```sh
codex mcp add watermelonfin -- node /absolute/path/watermelonfin-mcp/src/cli.js
```

For Claude Code:

```sh
claude mcp add watermelonfin -- node /absolute/path/watermelonfin-mcp/src/cli.js
```

A generic stdio configuration is available in [examples/mcp.json](examples/mcp.json). This preview is installed from source; no npm package is published. Host-specific registration commands may vary by host version.

An agent can retrieve the `onboard_jellyfin` prompt with a host address, or read `watermelonfin://recipes/onboard`. An IP address identifies a network destination; it does not grant login or administrator permissions.

## Credentials

Use a dedicated **non-administrator Jellyfin user with all media-deletion permissions disabled**. Administrator accounts, administrator API keys, and deletion-enabled users are rejected for daily operations.

Set `WATERMELONFIN_PROFILES` to non-secret connection metadata, for example:

```json
[{"id":"home","url":"https://media.example-tail.ts.net","serverId":"actual-server-id","username":"media-user","passwordEnv":"WATERMELONFIN_JELLYFIN_PASSWORD"}]
```

Supply the password in the referenced environment variable through your MCP host's secret configuration. **Do not paste passwords or tokens into chat, tool arguments, issues, or committed configuration files.** Call `authenticate_server` with only the profile ID. Session tokens obtained during login stay in process memory. Restarting the MCP requires a new login.

For an existing restricted-user token, use `tokenEnv` instead of `username` and `passwordEnv`. Environment variables are not a complete secret vault: the host agent and trusted processes under the same OS account remain part of the trust boundary. This preview does not provide its own secure-input UI, OS keychain adapter, or browser Quick Connect session import.

HTTPS uses normal certificate verification. HTTP credentials require prior establishment of a trusted LAN/Tailscale route and a host-configured, exact-origin allowlist in `WATERMELONFIN_TRUSTED_HTTP_ORIGINS`. Tools cannot modify this list. A matching Jellyfin server ID or a MagicDNS name is a consistency signal, not proof of server authenticity.

## Native upload plugin

See [the plugin guide](docs/plugin.md) for installation, configuration, endpoint details, and deployment limits.

1. Install the published plugin through Jellyfin's plugin repository/catalog and restart Jellyfin.
2. Keep uploads disabled until the administrator selects the intended import targets and allowlists a restricted user.
3. Add only the plugin's appropriate `Imported/<target-id>` directories as Jellyfin library sources. Preserve existing source paths; never index `Staging`.
4. Set `WATERMELONFIN_UPLOAD_ROOTS` to narrowly scoped local directories you explicitly permit the agent to upload from. Use `:` on Unix or `;` on Windows. The Unix uploader rejects symlinks and unsafe shared-write directory configurations.
5. Call `inspect_upload_plugin`, `list_upload_targets`, then `upload_media` with a selected file and target ID.

Bytes stream directly to the authenticated plugin, not through the model context. Each job is owned by one user. Publication uses a new job namespace and never replaces an existing destination. Ambiguous film/episode names require clarification; the `other` target type preserves a validated filename. Existing files are not automatically reorganized.

An interrupted upload can be resumed by supplying its job ID and the same file/target. Completed files cannot be modified through the upload API. **Upload completion does not mean Jellyfin has indexed the file.** Indexing relies on the configured library monitoring/schedule and must be verified separately. Expired/staged files are retained; there is no automatic cleanup route.

## Tools

| Tool | Purpose |
| --- | --- |
| `discover_server` | Bounded discovery on one specified host; no credentials |
| `inspect_connection` | Public server information at a full URL/base path |
| `inspect_tailscale` | Fixed, read-only local Tailscale status command |
| `inspect_capabilities` | Local configuration and implemented/unavailable capabilities |
| `authenticate_server` | Login through host-injected credentials |
| `list_libraries`, `search_media` | Restricted-user library reads |
| `plan_import` | A proposal for a new filename; no file changes |
| `inspect_upload_plugin`, `list_upload_targets` | Native plugin availability and configured targets |
| `upload_media` | Selected-file native plugin upload/resume |
| `export_connection` | Non-secret connection metadata; does not configure Apple clients |
| `upload_file` | Optional standalone-importer mode, only exposed when separately configured |

The legacy standalone importer is an optional alternative for operators who already have system-management access; it is **not required** by native plugin users. Its separate deployment contract is in [docs/provisioning.md](docs/provisioning.md).

## Local validation and release status

```sh
npm test
./scripts/validate-plugin.sh
```

The second command requires .NET 10 and builds/tests/packages the plugin locally. There are no GitHub Actions workflows. Tests include real MCP stdio initialization, simulated HTTP boundaries, temporary-filesystem uploads, and adapter checks against the actual Jellyfin SDK/JSON converters. They do not yet include loading this plugin in a complete Jellyfin instance or validating a production host's ACLs and indexing.

Pre-release plugin builds are intended for evaluation with a current backup and a small test import. Do not interpret this preview as a production-readiness or absolute no-data-loss guarantee.

## Maintenance and repository ownership

This is an owner-maintained repository. Only **@MrWeiC** is authorized to update the official branches/tags, merge changes, or publish official releases. Public visibility permits reading and forking; an external pull request is a proposal, not permission to change the official repository. Forks and independently built packages are not official releases.

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution expectations and [SECURITY.md](SECURITY.md) for vulnerability reporting. Licensing is described in [LICENSE.md](LICENSE.md).
