// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WatermelonFin.Plugin.Core;

public sealed class UploadStore
{
    public const long MaximumFileBytes = 50L * 1024 * 1024 * 1024;
    public const int MaximumChunkBytes = 4 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private readonly SafePaths _paths;
    private readonly SemaphoreSlim _gate;
    private readonly int _maxActiveJobs;
    private readonly Func<string,long> _availableSpace;
    public string Root => _paths.Root;
    public UploadStore(string root, int maxActiveJobs = 100, Func<string,long>? availableSpace = null)
    {
        _paths = new SafePaths(root); _gate = Locks.GetOrAdd(Root, _ => new SemaphoreSlim(1)); _maxActiveJobs = maxActiveJobs; _availableSpace=availableSpace??(path=>new DriveInfo(path).AvailableFreeSpace);
        _paths.Directory("Staging"); _paths.Directory("Imported");
    }
    public string PrepareTarget(string targetId) => _paths.Directory("Imported",ImportPlan.Target(targetId));
    private string JobDir(Guid id) => _paths.Directory("Staging", id.ToString("N"));
    private async Task<UploadManifest> Load(Guid owner, Guid id)
    {
        // Unknown IDs must not create filesystem entries.
        var dir = Path.Combine(Root, "Staging", id.ToString("N"));
        if (!Directory.Exists(dir)) throw new UploadException("Unknown upload job", 404);
        dir = JobDir(id); var path = _paths.ExistingFile(dir, "job.json");
        if (new FileInfo(path).Length > 16384) throw new UploadException("Invalid upload manifest");
        var m = JsonSerializer.Deserialize<UploadManifest>(await File.ReadAllTextAsync(path), Json) ?? throw new UploadException("Invalid upload manifest");
        if (m.OwnerId != owner) throw new UploadException("Upload job belongs to another user", 403);
        if (m.LayoutVersion is < 0 or > 1 || m.Id != id || m.Size < 1 || m.Size > MaximumFileBytes || !Regex.IsMatch(m.Sha256, "^[a-f0-9]{64}$")
            || ImportPlan.Target(m.TargetId) != m.TargetId || ImportPlan.Create(m.FileName,m.Kind).RelativePath != m.RelativePath)
            throw new UploadException("Invalid upload manifest");
        return m;
    }
    private string Destination(UploadManifest m, bool create)
    {
        var parts = m.RelativePath.Split('/');
        // Version 0 retains the published paths from older installations.
        var dirs = m.LayoutVersion == 0
            ? new[] { "Imported", m.TargetId, m.Id.ToString("N") }.Concat(parts.SkipLast(1)).ToArray()
            : new[] { "Imported", m.TargetId, $"{(parts.Length > 1 ? parts[0] : Path.GetFileNameWithoutExtension(m.FileName))} [{m.Id:N}]" }.Concat(parts.Skip(1).SkipLast(1)).ToArray();
        var parent = create ? _paths.Directory(dirs) : Path.Combine(new[] { Root }.Concat(dirs).ToArray());
        if (!create && Directory.Exists(parent)) _paths.Directory(dirs);
        return Path.Combine(parent, m.RelativePath.Split('/').Last());
    }
    private async Task<JobView> View(UploadManifest m)
    {
        var dest = Destination(m,false); var complete = SafePaths.IsFile(dest);
        if (complete)
        {
            if(SafePaths.IsFile(Path.Combine(JobDir(m.Id),"body")))throw new UploadException("Destination collision; staged upload was not published",409);
            if (new FileInfo(dest).Length != m.Size) throw new UploadException("Published upload size mismatch");
            return new(m.Id,"completed",m.Size,m.Size,m.ExpiresAt,Path.GetRelativePath(Root,dest).Replace('\\','/'),m.FileName,m.Sha256,m.TargetId);
        }
        var body = _paths.ExistingFile(JobDir(m.Id),"body");
        return await Task.FromResult(new JobView(m.Id,DateTimeOffset.UtcNow > m.ExpiresAt ? "expired" : "uploading",m.Size,new FileInfo(body).Length,m.ExpiresAt,null,m.FileName,m.Sha256,m.TargetId));
    }
    public async Task<JobView> Create(Guid owner, UploadRequest request, string kind)
    {
        await _gate.WaitAsync(); try
        {
            if (owner == Guid.Empty) throw new UploadException("Missing authenticated owner",403);
            var target=ImportPlan.Target(request.TargetId); var plan=ImportPlan.Create(request.FileName,kind);
            if(request.Size<1||request.Size>MaximumFileBytes||!Regex.IsMatch(request.Sha256,"^[a-f0-9]{64}$")) throw new UploadException("Invalid upload size or SHA256");
            var active=0;long reserved=0;
            foreach(var dir in System.IO.Directory.EnumerateDirectories(_paths.Directory("Staging")))
            {
                if(!Guid.TryParseExact(Path.GetFileName(dir),"N",out var id))throw new UploadException("Unexpected staging entry");
                var manifestPath=Path.Combine(dir,"job.json"); if(!File.Exists(manifestPath))continue; // Incomplete create after process crash: retain, never clean unrelated files.
                var info=new FileInfo(manifestPath);if(info.LinkTarget!=null||info.Length>16384)throw new UploadException("Invalid staging metadata");
                UploadManifest? m;try { m=JsonSerializer.Deserialize<UploadManifest>(await File.ReadAllTextAsync(manifestPath),Json); } catch(JsonException) { continue; }
                if(m==null)continue;
                m=await Load(m.OwnerId,id);var view=await View(m);
                if(view.Status=="uploading"){active++;reserved+=Math.Max(0,m.Size-view.Offset);}
            }
            if(active>=_maxActiveJobs)throw new UploadException("Active upload quota exceeded",409);
            if(_availableSpace(Root) < request.Size+reserved+64*1024*1024)throw new UploadException("Insufficient disk space",409);
            var job=new UploadManifest(Guid.NewGuid(),owner,target,kind,request.FileName,plan.RelativePath,request.Size,request.Sha256,DateTimeOffset.UtcNow.AddHours(24),1);
            var directory=JobDir(job.Id);
            await using(var body=new FileStream(Path.Combine(directory,"body"),FileMode.CreateNew,FileAccess.Write,FileShare.None)) { await body.FlushAsync(); }
            await using(var meta=new FileStream(Path.Combine(directory,"job.pending"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){await JsonSerializer.SerializeAsync(meta,job,Json);meta.Flush(flushToDisk:true);}
            File.Move(Path.Combine(directory,"job.pending"),Path.Combine(directory,"job.json"),overwrite:false);
            return await View(job);
        } finally { _gate.Release(); }
    }
    public async Task<JobView> Status(Guid owner, Guid id)
    {
        await _gate.WaitAsync(); try { return await View(await Load(owner,id)); } finally { _gate.Release(); }
    }
    public async Task<CompletedImport> ResolveCompleted(Guid owner, Guid id)
    {
        await _gate.WaitAsync(); try
        {
            var manifest = await Load(owner, id);
            if ((await View(manifest)).Status != "completed") throw new UploadException("Metadata requires a completed upload", 409);
            var destination = Destination(manifest, false);
            _paths.ExistingFile(Path.GetDirectoryName(destination)!, Path.GetFileName(destination));
            return new(id, owner, manifest.TargetId, destination, JobDir(id));
        } finally { _gate.Release(); }
    }
    public async Task<JobView> Append(Guid owner, Guid id, long offset, byte[] bytes)
    {
        await _gate.WaitAsync(); try
        {
            var m=await Load(owner,id);var view=await View(m);
            if(view.Status!="uploading")throw new UploadException("Upload is completed or expired",409);
            if(bytes.Length<1||bytes.Length>MaximumChunkBytes||offset<0||offset!=view.Offset||bytes.Length>m.Size-offset)throw new UploadException("Wrong offset or chunk size",409);
            var path=_paths.ExistingFile(JobDir(id),"body");
            await using(var file=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.None)){if(file.Length!=offset)throw new UploadException("Upload offset changed",409);file.Position=offset;await file.WriteAsync(bytes);await file.FlushAsync();}
            return await View(m);
        } finally { _gate.Release(); }
    }
    public async Task<JobView> Finalize(Guid owner, Guid id)
    {
        await _gate.WaitAsync(); try
        {
            var m=await Load(owner,id);var view=await View(m);
            if(view.Status=="completed")return view;
            if(view.Status!="uploading"||view.Offset!=m.Size)throw new UploadException("Upload incomplete or expired",409);
            var source=_paths.ExistingFile(JobDir(id),"body");
            await using(var file=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None))
            {
                var digest=await SHA256.HashDataAsync(file);
                if(!CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(m.Sha256)))throw new UploadException("Upload checksum mismatch",409);
            }
            var dest=Destination(m,true);
            if(SafePaths.IsFile(dest))throw new UploadException("Destination already exists",409);
            File.Move(source,dest,overwrite:false); // Only this job's staged file; never overwrites published media.
            return await View(m);
        } finally { _gate.Release(); }
    }
}
