// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WatermelonFin.Plugin.Core;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MetadataDocument
{
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public int? ProductionYear { get; set; }
    public string? PremiereDate { get; set; }
    public string[] Studios { get; set; } = [];
    public string[] Genres { get; set; } = [];
    public Dictionary<string,string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Validate()
    {
        static void Text(string? value, int limit, bool required = false)
        {
            if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > limit) throw new UploadException("Invalid metadata text");
            try { if (value != null) XmlConvert.VerifyXmlChars(value); } catch (XmlException) { throw new UploadException("Invalid metadata characters"); }
        }
        Text(Title,200,true); Text(OriginalTitle,200); Text(SortTitle,200); Text(Overview,10000);
        if (ProductionYear is < 1800 or > 2100) throw new UploadException("Invalid production year");
        if (PremiereDate != null && (!DateOnly.TryParseExact(PremiereDate,"yyyy-MM-dd",out var date) || date.Year is < 1800 or > 2100 || (ProductionYear.HasValue && date.Year != ProductionYear))) throw new UploadException("Invalid premiere date");
        if (Studios == null || Genres == null || Studios.Length > 20 || Genres.Length > 20 || ProviderIds == null || ProviderIds.Count > 3) throw new UploadException("Invalid metadata collection");
        foreach (var studio in Studios) Text(studio,120,true);
        foreach (var genre in Genres) Text(genre,80,true);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key,value) in ProviderIds)
        {
            if (!keys.Add(key) || value == null || !Regex.IsMatch(value,key.ToLowerInvariant() switch { "imdb" => "^tt[0-9]{4,12}$", "tmdb" or "tvdb" => "^[1-9][0-9]{0,11}$", _ => "(?!)" })) throw new UploadException("Invalid metadata provider identifier");
        }
    }
    public byte[] Nfo()
    {
        Validate(); var movie=new XElement("movie",new XElement("title",Title),new XElement("lockdata","true"));
        void Add(string name,object? value) { if (value != null) movie.Add(new XElement(name,value)); }
        Add("originaltitle",OriginalTitle); Add("sorttitle",SortTitle); Add("plot",Overview); Add("year",ProductionYear); Add("premiered",PremiereDate);
        foreach(var studio in Studios) Add("studio",studio);
        foreach(var genre in Genres) Add("genre",genre);
        foreach(var (provider,id) in ProviderIds) movie.Add(new XElement("uniqueid",new XAttribute("type",provider.ToLowerInvariant()),id));
        return System.Text.Encoding.UTF8.GetBytes(new XDocument(new XDeclaration("1.0","utf-8",null),movie).ToString());
    }
}

public sealed record MetadataSnapshot(MetadataDocument Document, bool IsLocked, string[] LockedFields);
public sealed record ImportMetadataState(string Revision, MetadataSnapshot? Metadata, Dictionary<string,byte[]?> Files, bool Pending = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MetadataUpdateRequest
{
    public string ExpectedRevision { get; set; } = "";
    public MetadataDocument Metadata { get; set; } = new();
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MetadataRestoreRequest
{
    public string ExpectedRevision { get; set; } = "";
    public string Revision { get; set; } = "";
}
