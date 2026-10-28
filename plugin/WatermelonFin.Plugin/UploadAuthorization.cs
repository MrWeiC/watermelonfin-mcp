// SPDX-License-Identifier: GPL-3.0-only
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using WatermelonFin.Plugin.Configuration;
using WatermelonFin.Plugin.Core;

namespace WatermelonFin.Plugin;

internal static class UploadAuthorization
{
    public static async Task<Guid> RequireUser(IAuthorizationContext authorization,HttpContext context,PluginConfiguration configuration)
    {
        var info=await authorization.GetAuthorizationInfo(context);var user=info.User;
        bool Has(PermissionKind k)=>user?.Permissions.Any(p=>p.Kind==k&&p.Value)==true;
        var deletion=Has(PermissionKind.EnableContentDeletion)||user?.Preferences.Any(p=>p.Kind==PreferenceKind.EnableContentDeletionFromFolders&&!string.IsNullOrWhiteSpace(p.Value))==true;
        UploadAccess.Require(new(configuration.Enabled,info.IsAuthenticated&&user!=null&&info.UserId!=Guid.Empty&&!Has(PermissionKind.IsDisabled),info.IsApiKey,Has(PermissionKind.IsAdministrator),deletion,(configuration.AllowedUserIds??[]).Contains(info.UserId)));
        return info.UserId;
    }
}
