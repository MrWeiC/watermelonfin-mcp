// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json.Serialization;

namespace WatermelonFin.Plugin.Core;

public sealed class UploadException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UploadRequest
{
    [JsonPropertyName("targetId")]
    public string TargetId { get; set; } = "";
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";
    [JsonPropertyName("size")]
    public long Size { get; set; }
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}

public sealed record JobView(
    [property: JsonPropertyName("id"),JsonConverter(typeof(CanonicalGuidConverter))] Guid Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("offset")] long Offset,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("relativePath")] string? RelativePath = null,
    [property: JsonPropertyName("fileName")] string? FileName = null,
    [property: JsonPropertyName("sha256")] string? Sha256 = null,
    [property: JsonPropertyName("targetId")] string? TargetId = null,
    [property: JsonPropertyName("indexedInJellyfin")] bool IndexedInJellyfin = false);

internal sealed record UploadManifest(Guid Id, Guid OwnerId, string TargetId, string Kind, string FileName, string RelativePath, long Size, string Sha256, DateTimeOffset ExpiresAt, int LayoutVersion = 0);

public sealed record AccessFacts(bool Enabled, bool AuthenticatedUser, bool ApiKey, bool Administrator, bool DeletionAllowed, bool Allowlisted);
public sealed record CompletedImport(Guid Id, Guid OwnerId, string TargetId, string VideoPath, string JournalDirectory);
public static class UploadAccess
{
    public static void Require(AccessFacts facts)
    {
        if (!facts.Enabled) throw new UploadException("Uploads are disabled", 403);
        if (!facts.AuthenticatedUser || facts.ApiKey || facts.Administrator || facts.DeletionAllowed || !facts.Allowlisted)
            throw new UploadException("A specifically authorized restricted Jellyfin user is required", 403);
    }
}
