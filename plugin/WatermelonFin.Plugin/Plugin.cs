// SPDX-License-Identifier: GPL-3.0-only
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using WatermelonFin.Plugin.Configuration;

namespace WatermelonFin.Plugin;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths,xmlSerializer) { Instance=this; }
    public static Plugin? Instance { get; private set; }
    public override string Name => "WatermelonFin Upload";
    public override string Description => "Additive uploads and owned-import metadata repair for WatermelonFin MCP. No media deletion or video overwrite API.";
    public override Guid Id => Guid.Parse("cbe3ae89-5d55-4e89-bb49-cb09af9b9bfe");
    public IEnumerable<PluginPageInfo> GetPages() => [new() { Name="watermelonfin-upload", DisplayName=Name, EmbeddedResourcePath="WatermelonFin.Plugin.Configuration.configPage.html" }];
}
