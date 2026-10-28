# Owned-import metadata review and readiness

The independent read-only review covered `ad148b1..08821bd`, including plugin/MCP branding. It found four Important issues and no Critical or Minor findings. The author addressed all four in one regression-driven fix pass; no second review was represented as having occurred.

| Finding | Correction | Regression evidence |
| --- | --- | --- |
| Inspection forgot a failed SDK refresh | Journal synchronization status per revision; verify only after successful save, exact-item refresh, and matching readback | Failed refresh and failed persistence remain pending after a fresh controller/store is created |
| Dangling artwork blocked recovery | Recover an existing journal before reading indexed artwork; distinguish absent registered files from unsafe paths | Interrupted restore can clear an index pointing at its removed poster and complete refresh |
| Header dimensions were treated as decoded image validation | Require bounded successful full Skia pixel decoding; limit concurrent artwork processing to two | CRC-valid corrupt-IDAT PNG is refused before any revision/image publication; truncated JPEG is refused |
| Later external corrections were overwritten | Compare observed fields and actual sidecar bytes; refuse stale edits; inspection journals external state as a fresh baseline without changing media | Artwork-only edit preserves administrator title correction and new NFO actor after baseline adoption |

The JPEG regression uses an actually truncated scan. Appending a new end marker is a different input that permissive codecs may recover; validation requires successful full decoding under the server-compatible codec, not a guarantee of source-image semantics.

Validation after the fix pass: 49 Node/MCP/HTTP/packaging tests, 25 native core tests, and 23 SDK adapter tests passed (97 total). Native builds reported zero warnings/errors; npm production audit reported zero known vulnerabilities. The native package manifest was deserialized with actual Jellyfin JSON options; its logo reference and identity were correct. The packaged logo and prepared official cover both completed full pixel decoding.

## Decisions and practical limits

- Development uses a separate clean clone/branch so the installed v0.1.2 and unrelated Apple-client changes remain intact; the cost is an additional checkout.
- Unique video storage folders and item identities remain unchanged; clean presentation depends on explicit metadata rather than renaming published video paths.
- The first adapter covers owned indexed videos and individual episodes. Audio/music and series/season containers require a later ownership model.
- Validation refresh does not invoke another metadata-provider lookup after explicit field persistence; automatic online enrichment is intentionally not part of repair.
- Live Noah appearance, library membership, streaming, and deployed icon rendering remain pending administrator installation of the new plugin and a Jellyfin restart. Local build/test results are not a claim of live repair.
- Windows ACL, NAS/container behavior, and power-loss durability were not established on this macOS development host. Deployment-specific compatibility remains subject to validation.
- A hostile process sharing Jellyfin's OS identity remains outside the application ownership boundary; this plugin is not an OS sandbox.
- Original artwork in Jellyfin-managed cache locations is preserved as its original registration/path. If another process/cache cleanup removes it, restore must remain unverified/pending rather than inventing replacement artwork; a fresh selected image may be needed. Owned sidecar artwork itself is journaled as bytes.

No Minor findings were deferred. The execution ledger is retained while live deployment/repair is pending.
