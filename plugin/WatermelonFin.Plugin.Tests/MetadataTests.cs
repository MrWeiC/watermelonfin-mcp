using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using WatermelonFin.Plugin.Core;

internal static class MetadataTests
{
 public static async Task<int> Run()
 {
  int failures=0;
  async Task Test(string name,Func<Task> run){try{await run();Console.WriteLine("PASS metadata: "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL metadata: "+name+": "+e.Message);}}
  static void Check(bool x){if(!x)throw new Exception("Assertion failed");}
  static async Task Deny(Func<Task> f){try{await f();}catch(Exception e)when(e is UploadException or IOException){return;}throw new Exception("Expected refusal");}
  async Task<(UploadStore uploads,ImportMetadataStore metadata,Guid owner,Guid job,string video)> Fixture(bool completed=true)
  {
   var root=Path.Combine(OperatingSystem.IsMacOS()?"/private/tmp":Path.GetTempPath(),"wf-metadata-"+Guid.NewGuid().ToString("N"));var u=new UploadStore(root);var owner=Guid.NewGuid();var bytes="immutable video"u8.ToArray();
   var job=await u.Create(owner,new UploadRequest{TargetId="home-school",FileName="Noah.mp4",Size=bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()},"other");
   await u.Append(owner,job.Id,0,bytes);if(completed)await u.Finalize(owner,job.Id);
   var video=completed?(await u.ResolveCompleted(owner,job.Id)).VideoPath:"";
   return(u,new ImportMetadataStore(u),owner,job.Id,video);
  }
  MetadataDocument Document()=>new(){Title="NOAH",Overview="Sight & Sound's live production <stage>",Studios=["Sight & Sound Theatres"],Genres=["Faith","Musical","Family"]};
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGOo/w8AAgABf//RyUkAAAAASUVORK5CYII=");
  await Test("foreign owner and incomplete imports cannot inspect or edit",async()=>{
   var f=await Fixture();await Deny(()=>f.metadata.Inspect(Guid.NewGuid(),f.job));await Deny(()=>f.metadata.Apply(Guid.NewGuid(),f.job,"initial",Document()));
   var pending=await Fixture(false);await Deny(()=>pending.metadata.Apply(pending.owner,pending.job,"initial",Document()));
  });
  await Test("legacy completed import remains repairable after upload expiry",async()=>{
   var f=await Fixture();var manifest=Path.Combine(f.uploads.Root,"Staging",f.job.ToString("N"),"job.json");var json=JsonNode.Parse(await File.ReadAllTextAsync(manifest))!;json["expiresAt"]=DateTimeOffset.UtcNow.AddDays(-3);json.AsObject().Remove("layoutVersion");
   var legacy=Path.Combine(f.uploads.Root,"Imported","home-school",f.job.ToString("N"),"Noah.mp4");Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);File.Move(f.video,legacy);await File.WriteAllTextAsync(manifest,json.ToJsonString());
   var state=await f.metadata.Apply(f.owner,f.job,"initial",Document());Check(state.Metadata!.Document.Title=="NOAH");
  });
  await Test("writes escaped local metadata without changing video bytes",async()=>{
   var f=await Fixture();var before=await File.ReadAllBytesAsync(f.video);var state=await f.metadata.Apply(f.owner,f.job,"initial",Document());
   var nfo=XDocument.Load(Path.Combine(Path.GetDirectoryName(f.video)!,"movie.nfo"));Check(nfo.Root!.Element("title")!.Value=="NOAH");Check(nfo.Root.Element("plot")!.Value==Document().Overview);Check(nfo.Root.Element("lockdata")!.Value=="true");Check((await File.ReadAllBytesAsync(f.video)).SequenceEqual(before));Check(state.Revision!="initial");
  });
  await Test("unknown fields oversized values and invalid identifiers fail closed",async()=>{
   var f=await Fixture();await Deny(()=>f.metadata.Apply(f.owner,f.job,"initial",new(){Title=new string('a',201)}));await Deny(()=>f.metadata.Apply(f.owner,f.job,"initial",new(){Title="Noah",ProviderIds=new(){["Imdb"]="https://evil"}}));
   try{JsonSerializer.Deserialize<MetadataDocument>("{\"title\":\"Noah\",\"serverPath\":\"/outside\"}",new JsonSerializerOptions(JsonSerializerDefaults.Web));throw new Exception("Unknown field accepted");}catch(JsonException){}
   Check((await f.metadata.Inspect(f.owner,f.job)).Revision=="initial");
  });
  await Test("artwork validates bounded content and fixed roles",async()=>{
   var f=await Fixture();await Deny(()=>f.metadata.SetArtwork(f.owner,f.job,"initial","../video",png));await Deny(()=>f.metadata.SetArtwork(f.owner,f.job,"initial","primary","not an image"u8.ToArray()));await Deny(()=>f.metadata.SetArtwork(f.owner,f.job,"initial","primary",new byte[8*1024*1024+1]));
   var corrupt=(byte[])png.Clone();corrupt[45]^=1;await Deny(()=>f.metadata.SetArtwork(f.owner,f.job,"initial","primary",corrupt));
   var state=await f.metadata.SetArtwork(f.owner,f.job,"initial","primary",png);Check(state.Files["poster.png"]!.SequenceEqual(png));Check(File.Exists(Path.Combine(Path.GetDirectoryName(f.video)!,"poster.png")));
  });
  await Test("restore retains original sidecars and originally absent artwork",async()=>{
   var f=await Fixture();var dir=Path.GetDirectoryName(f.video)!;await File.WriteAllTextAsync(Path.Combine(dir,"movie.nfo"),"<movie><title>Original</title></movie>");
   var first=await f.metadata.Apply(f.owner,f.job,"initial",Document());var image=await f.metadata.SetArtwork(f.owner,f.job,first.Revision,"primary",png);var restored=await f.metadata.Restore(f.owner,f.job,image.Revision,"initial");
   Check(await File.ReadAllTextAsync(Path.Combine(dir,"movie.nfo"))=="<movie><title>Original</title></movie>");Check(!File.Exists(Path.Combine(dir,"poster.png")));Check(restored.Revision!=image.Revision);Check(restored.Revision!="initial");
  });
  await Test("stale concurrent edits and foreign revision IDs cannot overwrite",async()=>{
   var f=await Fixture();var first=await f.metadata.Apply(f.owner,f.job,"initial",Document());await Deny(()=>f.metadata.Apply(f.owner,f.job,"initial",new(){Title="stale"}));var other=await Fixture();var otherState=await other.metadata.Apply(other.owner,other.job,"initial",new(){Title="other"});await Deny(()=>f.metadata.Restore(f.owner,f.job,first.Revision,otherState.Revision));
   var changes=await Task.WhenAll(Enumerable.Range(0,2).Select(async n=>{try{await f.metadata.Apply(f.owner,f.job,first.Revision,new(){Title="revision "+n});return true;}catch(UploadException){return false;}}));Check(changes.Count(x=>x)==1);
  });
  await Test("symlink sidecars and substituted ancestors are rejected",async()=>{
   var f=await Fixture();var outside=Path.Combine(f.uploads.Root,"outside.nfo");await File.WriteAllTextAsync(outside,"original");File.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(f.video)!,"movie.nfo"),outside);await Deny(()=>f.metadata.Apply(f.owner,f.job,"initial",Document()));Check(await File.ReadAllTextAsync(outside)=="original");
   var g=await Fixture();var parent=Path.GetDirectoryName(g.video)!;var moved=parent+"-moved";Directory.Move(parent,moved);Directory.CreateSymbolicLink(parent,moved);await Deny(()=>g.metadata.Inspect(g.owner,g.job));
  });
  await Test("pending publication recovers from authoritative revision after restart",async()=>{
   var f=await Fixture();var state=await f.metadata.Apply(f.owner,f.job,"initial",Document());var current=Path.Combine(f.uploads.Root,"Staging",f.job.ToString("N"),"metadata-current.json");var json=JsonNode.Parse(await File.ReadAllTextAsync(current))!;json["pending"]=true;await File.WriteAllTextAsync(current,json.ToJsonString());await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.video)!,"movie.nfo"),"partial");
   var recovered=await new ImportMetadataStore(f.uploads).Inspect(f.owner,f.job);Check(!recovered.Pending);Check(recovered.Revision==state.Revision);Check(XDocument.Load(Path.Combine(Path.GetDirectoryName(f.video)!,"movie.nfo")).Root!.Element("title")!.Value=="NOAH");
  });
  Console.WriteLine($"METADATA RESULT: {9-failures}/9 passed");return failures;
 }
}
