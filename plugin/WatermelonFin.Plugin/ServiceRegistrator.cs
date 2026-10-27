// SPDX-License-Identifier: GPL-3.0-only
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using WatermelonFin.Plugin.Core;

namespace WatermelonFin.Plugin;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<Func<Configuration.PluginConfiguration>>(_ => () => Plugin.Instance?.Configuration ?? new());
        services.AddSingleton(provider => new UploadStore(Path.Combine(provider.GetRequiredService<IApplicationPaths>().ProgramDataPath,"watermelonfin-imports")));
    }
}
