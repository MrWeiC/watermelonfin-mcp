// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WatermelonFin.Plugin.Core;

public sealed class ImportMetadataStore(UploadStore uploads)
{
    private static readonly string[] Names=["movie.nfo","poster.jpg","poster.png","fanart.jpg","fanart.png"];
    private static readonly ConcurrentDictionary<string,SemaphoreSlim> Gates=new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    private static void Revision(string revision) { if(revision!="initial" && (!Guid.TryParseExact(revision,"D",out var id) || id.ToString("D")!=revision))throw new UploadException("Invalid metadata revision"); }
    private async Task<T> WithImport<T>(Guid owner,Guid job,Func<CompletedImport,Task<T>> action)
    {
        var gate=Gates.GetOrAdd(uploads.Root+":"+job,new SemaphoreSlim(1));await gate.WaitAsync();
        try{return await action(await uploads.ResolveCompleted(owner,job));}finally{gate.Release();}
    }
    private static int Limit(string name)=>name=="movie.nfo"?65536:MetadataArtwork.MaximumBytes;
    private static async Task<byte[]?> ReadFixed(string directory,string name,int limit)
    {
        var path=Path.Combine(directory,name);if(!SafePaths.IsFile(path))return null;
        if(new FileInfo(path).Length>limit)throw new UploadException("Metadata file exceeds limit");
        var result=await File.ReadAllBytesAsync(path);if(result.Length>limit)throw new UploadException("Metadata file exceeds limit");return result;
    }
    private static async Task WriteFixed(string directory,string name,byte[]? data)
    {
        var path=Path.Combine(directory,name);SafePaths.IsFile(path);
        if(data==null){File.Delete(path);return;}
        var temporary=Path.Combine(directory,".watermelonfin-"+Guid.NewGuid().ToString("N")+".pending");
        try
        {
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temporary,UnixFileMode.UserRead|UnixFileMode.UserWrite);await file.WriteAsync(data);file.Flush(flushToDisk:true);}
            SafePaths.IsFile(path);File.Move(temporary,path,true);
        }finally{File.Delete(temporary);}
    }
    private static async Task SaveState(CompletedImport import,string name,ImportMetadataState state)
        =>await WriteFixed(import.JournalDirectory,name,JsonSerializer.SerializeToUtf8Bytes(state,Json));
    private static void ValidateState(ImportMetadataState state)
    {
        Revision(state.Revision);
        if(state.Files==null || state.Files.Count!=Names.Length || Names.Any(n=>!state.Files.ContainsKey(n)) || state.Files.Any(f=>f.Value?.Length>Limit(f.Key)))throw new UploadException("Invalid metadata revision content");
        if(state.Metadata!=null){state.Metadata.Document.Validate();if(state.Metadata.LockedFields==null || state.Metadata.LockedFields.Length>100 || state.Metadata.LockedFields.Any(s=>s.Length>100))throw new UploadException("Invalid metadata lock snapshot");
            if(state.Metadata.ArtworkPaths is { } paths && (paths.Count>2 || paths.Any(p=>p.Key is not ("primary" or "backdrop") || p.Value?.Length>4096)))throw new UploadException("Invalid artwork snapshot");}
    }
    private static async Task<ImportMetadataState> ReadState(CompletedImport import,string name)
    {
        var bytes=await ReadFixed(import.JournalDirectory,name,64*1024*1024)??throw new UploadException("Unknown metadata revision",404);
        ImportMetadataState state;try{state=JsonSerializer.Deserialize<ImportMetadataState>(bytes,Json)??throw new JsonException();}catch(JsonException){throw new UploadException("Invalid metadata journal");}
        ValidateState(state);return state;
    }
    private static async Task Materialize(CompletedImport import,ImportMetadataState state)
    {
        foreach(var name in Names)await WriteFixed(Path.GetDirectoryName(import.VideoPath)!,name,state.Files[name]);
    }
    private static async Task<ImportMetadataState> Current(CompletedImport import,MetadataSnapshot? baseline)
    {
        if(SafePaths.IsFile(Path.Combine(import.JournalDirectory,"metadata-current.json")))
        {
            var state=await ReadState(import,"metadata-current.json");
            if(state.Pending){await Materialize(import,state);state=state with{Pending=false};await SaveState(import,"metadata-current.json",state);}
            return state;
        }
        var files=new Dictionary<string,byte[]?>();foreach(var name in Names)files[name]=await ReadFixed(Path.GetDirectoryName(import.VideoPath)!,name,Limit(name));
        return new("initial",baseline,files);
    }
    private static void Expected(ImportMetadataState state,string expected)
    {Revision(expected);if(state.Revision!=expected)throw new UploadException("Metadata revision conflict; inspect the import again",409);}
    private static async Task<ImportMetadataState> Commit(CompletedImport import,ImportMetadataState before,ImportMetadataState after)
    {
        after=after with{Revision=Guid.NewGuid().ToString("D"),Pending=true};ValidateState(after);
        if(Directory.EnumerateFiles(import.JournalDirectory,"metadata-revision-*.json").Take(100).Count()>=100)throw new UploadException("Metadata revision quota reached",409);
        var backup="metadata-revision-"+before.Revision+".json";
        if(!SafePaths.IsFile(Path.Combine(import.JournalDirectory,backup)))await SaveState(import,backup,before);
        await SaveState(import,"metadata-current.json",after);
        await Materialize(import,after);
        after=after with{Pending=false};await SaveState(import,"metadata-current.json",after);return after;
    }
    public Task<ImportMetadataState> Inspect(Guid owner,Guid job,MetadataSnapshot? baseline=null)
        =>WithImport(owner,job,import=>Current(import,baseline));
    public Task<ImportMetadataState> Apply(Guid owner,Guid job,string expected,MetadataDocument document,MetadataSnapshot? baseline=null)
        =>WithImport(owner,job,async import=>{
            document.Validate();var before=await Current(import,baseline);Expected(before,expected);
            var files=new Dictionary<string,byte[]?>(before.Files){["movie.nfo"]=document.Nfo(before.Files["movie.nfo"])};
            return await Commit(import,before,new("",new(document,true,[],before.Metadata?.ArtworkPaths),files));
        });
    public Task<ImportMetadataState> SetArtwork(Guid owner,Guid job,string expected,string role,byte[] bytes,MetadataSnapshot? baseline=null)
        =>WithImport(owner,job,async import=>{
            var stem=role switch{"primary"=>"poster","backdrop"=>"fanart",_=>throw new UploadException("Invalid artwork role")};var extension=MetadataArtwork.Validate(bytes);
            var before=await Current(import,baseline);Expected(before,expected);var files=new Dictionary<string,byte[]?>(before.Files){[stem+".jpg"]=null,[stem+".png"]=null};files[stem+"."+extension]=bytes;
            return await Commit(import,before,new("",before.Metadata,files));
        });
    public Task<ImportMetadataState> Restore(Guid owner,Guid job,string expected,string revision,MetadataSnapshot? baseline=null)
        =>WithImport(owner,job,async import=>{
            Revision(revision);var before=await Current(import,baseline);Expected(before,expected);var saved=await ReadState(import,"metadata-revision-"+revision+".json");
            if(saved.Revision!=revision)throw new UploadException("Metadata revision mismatch");return await Commit(import,before,saved);
        });
    public Task<string[]> History(Guid owner,Guid job)=>WithImport(owner,job,import=>{
        var names=Directory.EnumerateFiles(import.JournalDirectory,"metadata-revision-*.json").Take(101).ToArray();if(names.Length>100)throw new UploadException("Invalid metadata history");
        var revisions=new List<string>();foreach(var name in names){SafePaths.IsFile(name);var id=Path.GetFileName(name)[18..^5];Revision(id);revisions.Add(id);}return Task.FromResult(revisions.ToArray());
    });
}
