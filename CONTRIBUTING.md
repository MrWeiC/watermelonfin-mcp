# Contributing

WatermelonFin MCP is maintained by @MrWeiC. External issues and pull requests are welcome as proposals, but contributors do not receive write, merge, tag, release, or administration permissions by submitting a contribution. Only the owner decides which changes become part of the official project.

Discuss major changes before implementing them. Preserve the no-delete/no-overwrite boundary; do not add generic shell, filesystem, URL-fetching, or administrator APIs. Do not include real server addresses, credentials, usernames, media inventories, screenshots, local paths, or private logs in reports or test fixtures.

Run `npm test` before submitting a change. Changes under `plugin/` also require .NET 10 and `./scripts/validate-plugin.sh`. Validation runs locally; this repository intentionally does not use GitHub Actions. Describe what changed, why, and which checks ran. Clearly state any live-server behavior that remains unverified.

The plugin's license is GPL-3.0-only; the MCP portion has no general modification/distribution license beyond the rights stated in LICENSE.md. Ask the owner before proposing reuse or licensing changes. A contribution must be yours to submit and must not introduce incompatible third-party code.
