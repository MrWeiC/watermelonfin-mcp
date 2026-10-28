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
    private static readonly SemaphoreSlim ArtworkReaders=new(2,2);
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
    private async Task<ImportMetadataState> Load(Guid owner,CompletedImport import)
    {
        // Replay an existing authoritative journal before reading potentially dangling artwork registrations.
        var state=await metadata.Inspect(owner,import.Id);
        return state.Metadata==null?await metadata.Inspect(owner,import.Id,jellyfin.Read(import).Snapshot):state;
    }
    private async Task<bool> Drift(Guid owner,CompletedImport import,ImportMetadataState state)
        =>state.Synchronization!="pending"&&state.Observed!=null&&(!jellyfin.SnapshotMatches(state.Observed,jellyfin.Read(import).Snapshot)||!await metadata.FilesMatch(owner,import.Id,state.Revision));
    private async Task<ImportMetadataState> Editable(Guid owner,CompletedImport import,string expected,bool allowPending=false)
    {
        var state=await Load(owner,import);
        if(state.Revision!=expected)throw new UploadException("Metadata revision conflict; inspect the import again",409);
        if(!allowPending&&state.Synchronization is "pending" or "mismatch")throw new UploadException("Finish or restore the pending metadata revision first",409);
        if(await Drift(owner,import,state))throw new UploadException("Metadata changed outside this revision; inspect to capture the current state before editing",409);
        return state;
    }
    private async Task<ActionResult> View(Guid owner,CompletedImport import,ImportMetadataState state)
    {
        IndexedImport? indexed=null;try{indexed=jellyfin.Read(import);}catch(UploadException){if(state.Synchronization!="pending")throw;}
        var verified=state.Synchronization=="verified"&&indexed!=null&&jellyfin.Matches(import,state,indexed)&&await metadata.FilesMatch(owner,import.Id,state.Revision);
        var status=state.Synchronization=="verified"?(verified?"completed":"mismatch"):state.Synchronization;
        return Ok(new ImportMetadataView(import.Id,state.Revision,await metadata.History(owner,import.Id),state.Metadata?.Document,indexed?.Snapshot.Document,new(){["primary"]=indexed?.Snapshot.ArtworkPaths?.GetValueOrDefault("primary")!=null,["backdrop"]=indexed?.Snapshot.ArtworkPaths?.GetValueOrDefault("backdrop")!=null},verified,status));
    }
    private async Task<ActionResult> Publish(Guid owner,CompletedImport import,ImportMetadataState state)
    {
        state=await metadata.MarkSynchronization(owner,import.Id,state.Revision,"pending",baseline:state.Metadata);
        try
        {
            await jellyfin.Synchronize(import,state);await jellyfin.Refresh(import);var indexed=jellyfin.Read(import);
            var verified=jellyfin.Matches(import,state,indexed)&&await metadata.FilesMatch(owner,import.Id,state.Revision);
            state=await metadata.MarkSynchronization(owner,import.Id,state.Revision,verified?"verified":"mismatch",indexed.Snapshot);
        }
        catch(Exception e)when(e is UploadException or IOException or InvalidOperationException or UnauthorizedAccessException){state=await metadata.Inspect(owner,import.Id);}
        return await View(owner,import,state);
    }
    [HttpGet("Metadata")]
    public Task<ActionResult> Inspect(Guid id)=>WithJob(id,async(owner,import)=>{
        var state=await Load(owner,import);
        if(await Drift(owner,import,state))state=await metadata.AdoptCurrent(owner,id,state.Revision,jellyfin.Read(import).Snapshot);
        return await View(owner,import,state);
    });
    [HttpPost("Metadata")]
    [RequestSizeLimit(65536)]
    public Task<ActionResult> Update(Guid id,[FromBody]MetadataUpdateRequest request)=>WithJob(id,async(owner,import)=>{
        if(request?.Metadata==null)throw new UploadException("Missing metadata document");var state=await Editable(owner,import,request.ExpectedRevision);
        return await Publish(owner,import,await metadata.Apply(owner,id,request.ExpectedRevision,request.Metadata,state.Metadata));
    });
    [HttpPost("Artwork/{role}")]
    [RequestSizeLimit(MetadataArtwork.MaximumBytes)]
    public Task<ActionResult> SetArtwork(Guid id,string role,[FromQuery]string expectedRevision)=>WithJob(id,async(owner,import)=>{
        if(role is not ("primary" or "backdrop") || Request.ContentLength is <1 or >MetadataArtwork.MaximumBytes)throw new UploadException("Invalid artwork role or size");
        if(!ArtworkReaders.Wait(0))throw new UploadException("Artwork processing concurrency limit reached; retry",429);
        try {
        var state=await Editable(owner,import,expectedRevision);using var stream=new MemoryStream();var buffer=new byte[65536];
        while(true){var n=await Request.Body.ReadAsync(buffer,HttpContext.RequestAborted);if(n==0)break;if(stream.Length+n>MetadataArtwork.MaximumBytes)throw new UploadException("Artwork exceeds limit");await stream.WriteAsync(buffer.AsMemory(0,n));}
        if(stream.Length<1 || (Request.ContentLength.HasValue&&stream.Length!=Request.ContentLength))throw new UploadException("Incomplete artwork body");
        var bytes=stream.ToArray();jellyfin.ValidateArtwork(import,bytes);
        return await Publish(owner,import,await metadata.SetArtwork(owner,id,expectedRevision,role,bytes,state.Metadata));
        } finally { ArtworkReaders.Release(); }
    });
    [HttpPost("Metadata/Restore")]
    [RequestSizeLimit(1024)]
    public Task<ActionResult> Restore(Guid id,[FromBody]MetadataRestoreRequest request)=>WithJob(id,async(owner,import)=>{
        var state=await Editable(owner,import,request.ExpectedRevision,true);
        return await Publish(owner,import,await metadata.Restore(owner,id,request.ExpectedRevision,request.Revision,state.Metadata));
    });
    [HttpPost("Metadata/Refresh")]
    [RequestSizeLimit(1024)]
    public Task<ActionResult> Refresh(Guid id,[FromBody]MetadataRefreshRequest request)=>WithJob(id,async(owner,import)=>{
        var state=await Editable(owner,import,request.ExpectedRevision,true);
        return await Publish(owner,import,state);
    });
}
