// SPDX-License-Identifier: GPL-3.0-only
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WatermelonFin.Plugin.Configuration;
using WatermelonFin.Plugin.Core;

namespace WatermelonFin.Plugin;

[ApiController]
[Authorize]
[Route("WatermelonFin/Upload")]
public sealed class UploadController(IAuthorizationContext authorization, UploadStore store, IApplicationPaths paths, Func<PluginConfiguration> configuration) : ControllerBase
{
    private static readonly SemaphoreSlim ChunkReaders=new(4,4);
    private PluginConfiguration Config => configuration();
    private UploadTarget[] Targets()
    {
        var targets=Config.Targets??[];
        if(targets.Length is <1 or >20 || targets.Select(t=>t.Id).Distinct(StringComparer.Ordinal).Count()!=targets.Length) throw new UploadException("Invalid upload target configuration",503);
        foreach(var t in targets)
        {
            ImportPlan.Target(t.Id);
            if(string.IsNullOrWhiteSpace(t.Name)||t.Name.Length>100||t.Kind is not ("movies" or "tv" or "other"))throw new UploadException("Invalid upload target configuration",503);
        }
        return targets;
    }
    private async Task<Guid> RequireUser()
    {
        var info=await authorization.GetAuthorizationInfo(HttpContext);
        var user=info.User;
        bool Has(PermissionKind k)=>user?.Permissions.Any(p=>p.Kind==k&&p.Value)==true;
        var canDelete=Has(PermissionKind.EnableContentDeletion)||user?.Preferences.Any(p=>p.Kind==PreferenceKind.EnableContentDeletionFromFolders&&!string.IsNullOrWhiteSpace(p.Value))==true;
        UploadAccess.Require(new(Config.Enabled,info.IsAuthenticated&&user!=null&&info.UserId!=Guid.Empty&&!Has(PermissionKind.IsDisabled),info.IsApiKey,Has(PermissionKind.IsAdministrator),canDelete,(Config.AllowedUserIds??[]).Contains(info.UserId)));
        return info.UserId;
    }
    private async Task<Guid> RequireJob(Guid id)
    {
        var user=await RequireUser();var job=await store.Status(user,id);
        if(!Targets().Any(t=>t.Id==job.TargetId))throw new UploadException("Upload target is no longer authorized",403);
        return user;
    }
    private async Task<ActionResult> Safe(Func<Task<object>> work)
    {
        try{return Ok(await work());}
        catch(UploadException e){return StatusCode(e.StatusCode,new { error=e.Message });}
        catch(IOException){return Conflict(new { error="Upload storage conflict; existing files were not overwritten" });}
        catch(UnauthorizedAccessException){return StatusCode(503,new { error="Upload storage permissions do not allow this operation" });}
    }
    [HttpGet("Capabilities")]
    public Task<ActionResult> Capabilities()=>Safe(async()=>{await RequireUser();return new {protocol="watermelonfin-upload.v1",enabled=true,delete=false,overwrite=false,maxChunkBytes=UploadStore.MaximumChunkBytes,maxFileBytes=UploadStore.MaximumFileBytes};});
    [HttpGet("Targets")]
    public Task<ActionResult> GetTargets()=>Safe(async()=>{await RequireUser();return Targets().Select(t=>new {id=t.Id,name=t.Name,kind=t.Kind}).ToArray();});
    [HttpGet("AdminInfo")]
    [Authorize(Policy=Policies.RequiresElevation)]
    public Task<ActionResult> AdminInfo()=>Safe(()=>Task.FromResult<object>(new { root=Path.Combine(paths.ProgramDataPath,"watermelonfin-imports"), targets=Targets().Select(t=>new {id=t.Id,name=t.Name,kind=t.Kind,librarySourcePath=store.PrepareTarget(t.Id)}) }));
    [HttpPost("Jobs")]
    [RequestSizeLimit(8192)]
    public Task<ActionResult> Create([FromBody] UploadRequest request)=>Safe(async()=>{
        var owner=await RequireUser();var target=Targets().SingleOrDefault(t=>t.Id==request.TargetId)??throw new UploadException("Upload target not authorized",403);
        return await store.Create(owner,request,target.Kind);
    });
    [HttpGet("Jobs/{id:guid}")]
    public Task<ActionResult> Status(Guid id)=>Safe(async()=>await store.Status(await RequireJob(id),id));
    [HttpPatch("Jobs/{id:guid}")]
    [RequestSizeLimit(UploadStore.MaximumChunkBytes)]
    public Task<ActionResult> Append(Guid id,[FromQuery] long offset)=>Safe(async()=>{
        var owner=await RequireJob(id);
        if(Request.ContentLength is <1 or >UploadStore.MaximumChunkBytes)throw new UploadException("Invalid chunk length");
        if(!await ChunkReaders.WaitAsync(0))throw new UploadException("Upload concurrency limit reached; retry",429);
        try {
        using var memory=new MemoryStream();var buffer=new byte[65536];
        while(true){var count=await Request.Body.ReadAsync(buffer,HttpContext.RequestAborted);if(count==0)break;if(memory.Length+count>UploadStore.MaximumChunkBytes)throw new UploadException("Chunk limit exceeded");await memory.WriteAsync(buffer.AsMemory(0,count),HttpContext.RequestAborted);}
        if(Request.ContentLength.HasValue&&memory.Length!=Request.ContentLength)throw new UploadException("Incomplete upload chunk");
        return await store.Append(owner,id,offset,memory.ToArray());
        } finally { ChunkReaders.Release(); }
    });
    [HttpPost("Jobs/{id:guid}/Finalize")]
    public Task<ActionResult> Finalize(Guid id)=>Safe(async()=>await store.Finalize(await RequireJob(id),id));
}
