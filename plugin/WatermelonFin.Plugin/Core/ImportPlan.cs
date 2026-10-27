// SPDX-License-Identifier: GPL-3.0-only
using System.Text.RegularExpressions;

namespace WatermelonFin.Plugin.Core;

public sealed record ImportPlan(string RelativePath)
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".m4v", ".mov", ".avi", ".mp3", ".m4a", ".flac", ".wav" };
    private static string Component(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ')
            || value.Any(c => char.IsControl(c) || "\\/<>:\"|?*".Contains(c))
            || Regex.IsMatch(value, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            throw new UploadException("Invalid media filename");
        return value;
    }
    public static string Target(string value)
    {
        if (!Regex.IsMatch(value, @"^[a-z0-9][a-z0-9-]{0,40}$")) throw new UploadException("Invalid upload target");
        return value;
    }
    public static ImportPlan Create(string fileName, string kind)
    {
        Component(fileName); var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!Extensions.Contains(ext)) throw new UploadException("Unsupported media extension");
        var stem = Path.GetFileNameWithoutExtension(fileName);
        string Clean(string s) => Component(Regex.Replace(s.Replace('.', ' ').Replace('_', ' ').Trim(), @"\s+", " "));
        if (kind == "tv")
        {
            var m = Regex.Match(stem, @"^(.+?)[ ._-]+S(\d{2})E(\d{2})(?:[ ._-].*)?$", RegexOptions.IgnoreCase);
            if (!m.Success) throw new UploadException("needs_clarification: use a series title and SxxExx episode number");
            var title = Clean(m.Groups[1].Value); var season = m.Groups[2].Value; var episode = m.Groups[3].Value;
            return new($"{title}/Season {season}/{title} - S{season}E{episode}{ext}");
        }
        if (kind == "movies")
        {
            var m = Regex.Match(stem, @"^(.+?)\s*\((19\d{2}|20\d{2})\)$");
            if (!m.Success) throw new UploadException("needs_clarification: use a title and year, such as Example (2024).mp4");
            var title = $"{Clean(m.Groups[1].Value)} ({m.Groups[2].Value})";
            return new($"{title}/{title}{ext}");
        }
        if (kind == "other") return new(fileName);
        throw new UploadException("Unsupported target media kind");
    }
}
