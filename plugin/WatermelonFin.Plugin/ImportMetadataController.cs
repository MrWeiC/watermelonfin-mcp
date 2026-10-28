// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WatermelonFin.Plugin.Configuration;
using WatermelonFin.Plugin.Core;

namespace WatermelonFin.Plugin;

public sealed record ImportMetadataView(
    [property:JsonPropertyName("jobId"),JsonConverter(typeof(CanonicalGuidConverter))] Guid JobId,
    [property:JsonPropertyName("revision")] string Revision,
    [property:JsonPropertyName("revisions")] string[] Revisions,
    [property:JsonPropertyName("persistedMetadata")] MetadataDocument? PersistedMetadata,
    [property:JsonPropertyName("indexedMetadata")] MetadataDocument? IndexedMetadata,
    [property:JsonPropertyName("artwork")] Dictionary<string,bool> Artwork,
    [property:JsonPropertyName("verified")] bool Verified,
    [property:JsonPropertyName("refreshStatus")] string RefreshStatus);

[ApiController]
[Authorize]
[Route("WatermelonFin/Upload/Jobs/{id:guid}")]
public sealed class ImportMetadataController(IAuthorizationContext authorization,UploadStore uploads,ImportMetadataStore metadata,JellyfinImportMetadata jellyfin,Func<PluginConfiguration> configuration):ControllerBase
{
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.Ordinal);
    private async Task<ActionResult> WithJob(Guid id,Func<Guid,CompletedImport,Task<ActionResult>> action)
    {
        var gate=Gates.GetOrAdd(uploads.Root+":"+id,new SemaphoreSlim(1));await gate.WaitAsync();
        try
        {
            var config=configuration();var owner=await UploadAuthorization.RequireUser(authorization,HttpContext,config);var import=await uploads.ResolveCompleted(owner,id);
            if(!(config.Targets??[]).Any(t=>t.Id==import.TargetId))throw new UploadException("Upload target is no longer authorized",403);
            return await action(owner,import);
        }
        catch(UploadException e){return StatusCode(e.StatusCode,new{error=e.Message});}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){return StatusCode(409,new{error="Import metadata storage is unavailable; inspect the import before retrying"});}
        finally{gate.Release();}
    }
    private async Task<ActionResult> View(Guid owner,CompletedImport import,ImportMetadataState state,string status)
    {
        IndexedImport? indexed=null;try{indexed=jellyfin.Read(import);}catch(UploadException){if(status!="pending")throw;}
        var verified=status!="pending"&&indexed!=null&&jellyfin.Matches(import,state,indexed);
        return Ok(new ImportMetadataView(import.Id,state.Revision,await metadata.History(owner,import.Id),state.Metadata?.Document,indexed?.Snapshot.Document,new(){["primary"]=indexed?.Snapshot.ArtworkPaths?.GetValueOrDefault("primary")!=null,["backdrop"]=indexed?.Snapshot.ArtworkPaths?.GetValueOrDefault("backdrop")!=null},verified,status=="completed"&&!verified?"mismatch":status));
    }
    private async Task<ActionResult> Publish(Guid owner,CompletedImport import,ImportMetadataState state)
    {
        try{await jellyfin.Synchronize(import,state);await jellyfin.Refresh(import);return await View(owner,import,state,"completed");}
        catch(Exception e)when(e is UploadException or IOException or InvalidOperationException or UnauthorizedAccessException){return await View(owner,import,state,"pending");}
    }
    [HttpGet("Metadata")]
    public Task<ActionResult> Inspect(Guid id)=>WithJob(id,async(owner,import)=>await View(owner,import,await metadata.Inspect(owner,id,jellyfin.Read(import).Snapshot),"not-requested"));
    [HttpPost("Metadata")]
    [RequestSizeLimit(65536)]
    public Task<ActionResult> Update(Guid id,[FromBody]MetadataUpdateRequest request)=>WithJob(id,async(owner,import)=>{
        if(request?.Metadata==null)throw new UploadException("Missing metadata document");var baseline=jellyfin.Read(import).Snapshot;
        return await Publish(owner,import,await metadata.Apply(owner,id,request.ExpectedRevision,request.Metadata,baseline));
    });
    [HttpPost("Artwork/{role}")]
    [RequestSizeLimit(MetadataArtwork.MaximumBytes)]
    public Task<ActionResult> SetArtwork(Guid id,string role,[FromQuery]string expectedRevision)=>WithJob(id,async(owner,import)=>{
        if(role is not ("primary" or "backdrop") || Request.ContentLength is <1 or >MetadataArtwork.MaximumBytes)throw new UploadException("Invalid artwork role or size");
        var baseline=jellyfin.Read(import).Snapshot;using var stream=new MemoryStream();var buffer=new byte[65536];
        while(true){var n=await Request.Body.ReadAsync(buffer,HttpContext.RequestAborted);if(n==0)break;if(stream.Length+n>MetadataArtwork.MaximumBytes)throw new UploadException("Artwork exceeds limit");await stream.WriteAsync(buffer.AsMemory(0,n));}
        if(stream.Length<1 || (Request.ContentLength.HasValue&&stream.Length!=Request.ContentLength))throw new UploadException("Incomplete artwork body");
        var bytes=stream.ToArray();jellyfin.ValidateArtwork(import,bytes);
        return await Publish(owner,import,await metadata.SetArtwork(owner,id,expectedRevision,role,bytes,baseline));
    });
    [HttpPost("Metadata/Restore")]
    [RequestSizeLimit(1024)]
    public Task<ActionResult> Restore(Guid id,[FromBody]MetadataRestoreRequest request)=>WithJob(id,async(owner,import)=>await Publish(owner,import,await metadata.Restore(owner,id,request.ExpectedRevision,request.Revision,jellyfin.Read(import).Snapshot)));
    [HttpPost("Metadata/Refresh")]
    [RequestSizeLimit(1024)]
    public Task<ActionResult> Refresh(Guid id,[FromBody]MetadataRefreshRequest request)=>WithJob(id,async(owner,import)=>{
        var state=await metadata.Inspect(owner,id,jellyfin.Read(import).Snapshot);if(state.Revision!=request.ExpectedRevision)throw new UploadException("Metadata revision conflict; inspect the import again",409);
        return await Publish(owner,import,state);
    });
}
