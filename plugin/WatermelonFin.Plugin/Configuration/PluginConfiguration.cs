// SPDX-License-Identifier: GPL-3.0-only
using MediaBrowser.Model.Plugins;

namespace WatermelonFin.Plugin.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; }
    public Guid[] AllowedUserIds { get; set; } = [];
    public UploadTarget[] Targets { get; set; } =
    [
        new() { Id = "movies", Name = "Movies", Kind = "movies" },
        new() { Id = "shows", Name = "Shows", Kind = "tv" },
        new() { Id = "home-school", Name = "Home School", Kind = "other" }
    ];
}
public sealed class UploadTarget
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "other";
}
