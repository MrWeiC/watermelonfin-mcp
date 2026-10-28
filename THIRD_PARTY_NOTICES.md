# Third-party notices

- Jellyfin server and plugin interfaces: https://github.com/jellyfin/jellyfin — GPL-3.0-only; see the upstream license.
- Jellyfin plugin template: https://github.com/jellyfin/jellyfin-plugin-template — GNU GPL version 3; its plugin structure informed the native adapter.
- Official MCP TypeScript SDK: https://github.com/modelcontextprotocol/typescript-sdk — MIT.
- Zod: https://github.com/colinhacks/zod — MIT.
- SkiaSharp: https://github.com/mono/SkiaSharp — MIT; the metadata adapter uses the Jellyfin-compatible codec for bounded full artwork decoding. The plugin ZIP does not bundle replacement SkiaSharp/native runtime binaries.
- .NET runtime/SDK: https://github.com/dotnet/runtime — upstream .NET licensing and third-party notices apply.

Other transitive dependencies retain their package license notices. The plugin release ZIP contains the WatermelonFin plugin assembly and its license/notice files, not copies of Jellyfin server assemblies or the .NET runtime.
