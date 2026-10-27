// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WatermelonFin.Plugin.Core;

// Jellyfin normally emits GUIDs without hyphens. This protocol deliberately
// specifies D format so MCP job IDs remain portable and schema-valid.
public sealed class CanonicalGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>Guid.Parse(reader.GetString()!);
    public override void Write(Utf8JsonWriter writer,Guid value,JsonSerializerOptions options)=>writer.WriteStringValue(value.ToString("D"));
}
