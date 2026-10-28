using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WatermelonFin.Plugin;
using WatermelonFin.Plugin.Configuration;
using WatermelonFin.Plugin.Core;

internal static class MetadataAdapterTests
{
 public static async Task<int> Run()
 {
  var failures=0;
  void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
  async Task Test(string name,Func<Task> action){try{await action();Console.WriteLine("PASS metadata adapter: "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL metadata adapter: "+name+": "+e.Message);}}
  static JsonElement Data(ActionResult r)=>JsonSerializer.SerializeToElement(((OkObjectResult)r).Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
  static MetadataDocument Doc()=>new(){Title="NOAH",Overview="Sight & Sound stage production",Studios=["Sight & Sound Theatres"],Genres=["Faith","Musical","Family"]};
  async Task<Fixture> Make()
  {
   var root=Path.Combine(OperatingSystem.IsMacOS()?"/private/tmp":Path.GetTempPath(),"wf-metadata-sdk-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var paths=DispatchProxy.Create<IServerApplicationPaths,PathsProxy>();((PathsProxy)(object)paths).Path=root;
   var user=new Jellyfin.Database.Implementations.Entities.User("restricted","auth","reset");var auth=new FakeAuth{Info=new(){User=user,IsAuthenticated=true}};var config=new PluginConfiguration{Enabled=true,AllowedUserIds=[user.Id]};
   var store=new UploadStore(Path.Combine(root,"uploads"));var bytes="immutable movie"u8.ToArray();var job=await store.Create(user.Id,new(){TargetId="home-school",FileName="Noah.mp4",Size=bytes.Length,Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()},"other");await store.Append(user.Id,job.Id,0,bytes);await store.Finalize(user.Id,job.Id);var completed=await store.ResolveCompleted(user.Id,job.Id);
   var movie=new Movie{Id=Guid.NewGuid(),Path=completed.VideoPath,Name="Old title",Overview="Old overview",IsLocked=false,LockedFields=[MetadataField.Name],Genres=["Old genre"],Studios=["Old studio"],ProviderIds=new(){["Imdb"]="tt1234567",["Custom"]="leave-me"}};
   var f=new Fixture{Root=root,Store=store,Job=job.Id,Movie=movie,Auth=auth,Config=config,Owner=user.Id};
   var library=DispatchProxy.Create<ILibraryManager,MetadataProxy>();((MetadataProxy)(object)library).Call=(method,args)=>method.Name switch{
    "FindByPath"=>f.Mismatch?new Movie{Id=Guid.NewGuid(),Path="/outside/foreign.mp4",Name="foreign"}:movie,
    "UpdateItemAsync"=>f.PersistenceFails?throw new IOException("simulated persistence failure"):Task.CompletedTask,
    _=>throw new Exception("Unexpected library operation: "+method.Name)};
   var providers=DispatchProxy.Create<IProviderManager,MetadataProxy>();((MetadataProxy)(object)providers).Call=(method,args)=>{
    if(method.Name!="RefreshSingleItem")throw new Exception("Unexpected provider operation: "+method.Name);
    Check(ReferenceEquals(args![0],movie));f.Refreshed.Add(movie.Id);if(f.RefreshFails)throw new IOException("simulated SDK failure");if(f.Clobber)movie.Name="Wrong online match";if(f.ClobberFromNfo&&((MetadataRefreshOptions)args[1]!).MetadataRefreshMode!=MetadataRefreshMode.None)movie.Name=System.Xml.Linq.XDocument.Load(Path.Combine(Path.GetDirectoryName(movie.Path)!,"movie.nfo")).Root!.Element("title")!.Value;
    return Task.FromResult(ItemUpdateType.None);
   };
   var persistence=DispatchProxy.Create<IItemPersistenceService,MetadataProxy>();((MetadataProxy)(object)persistence).Call=(method,args)=>method.Name=="SaveImagesAsync"?Task.CompletedTask:throw new Exception("Unexpected persistence operation");
   var filesystem=DispatchProxy.Create<IFileSystem,MetadataProxy>();((MetadataProxy)(object)filesystem).Call=(method,args)=>throw new Exception("Unexpected filesystem operation: "+method.Name);
   var images=DispatchProxy.Create<IImageProcessor,MetadataProxy>();((MetadataProxy)(object)images).Call=(method,args)=>method.Name=="GetImageDimensions"?new ImageDimensions(1,1):throw new Exception("Unexpected image operation");
   var adapter=new JellyfinImportMetadata(library,providers,filesystem,persistence,paths,images);
   f.Restart=()=>new ImportMetadataController(auth,store,new ImportMetadataStore(new UploadStore(store.Root)),adapter,()=>config){ControllerContext=new(){HttpContext=new DefaultHttpContext()}};f.Controller=f.Restart();
   return f;
  }
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGOo/w8AAgABf//RyUkAAAAASUVORK5CYII=");
  await Test("all routes reject foreign owners revoked targets and users",async()=>{
   var f=await Make();var c=f.Controller;f.Config.AllowedUserIds=[];Check((await c.Inspect(f.Job) as ObjectResult)?.StatusCode==403);f.Config.AllowedUserIds=[f.Owner];
   f.Config.Targets=[];Check((await c.Inspect(f.Job) as ObjectResult)?.StatusCode==403);f.Config.Targets=[new(){Id="home-school",Name="Home School",Kind="other"}];
   var other=new Jellyfin.Database.Implementations.Entities.User("other","auth","reset");f.Auth.Info.User=other;f.Config.AllowedUserIds=[other.Id];
   foreach(var r in new[]{await c.Inspect(f.Job),await c.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}),await c.SetArtwork(f.Job,"primary","initial"),await c.Restore(f.Job,new(){ExpectedRevision="initial",Revision="initial"}),await c.Refresh(f.Job,new(){ExpectedRevision="initial"})})Check((r as ObjectResult)?.StatusCode==403);
   Check(f.Refreshed.Count==0);Check(!File.Exists(Path.Combine(Path.GetDirectoryName(f.Movie.Path)!,"movie.nfo")));
  });
  await Test("updates real item fields and locks while preserving video and unrelated provider IDs",async()=>{
   var f=await Make();var before=await File.ReadAllBytesAsync(f.Movie.Path);var r=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));
   Check(r.GetProperty("verified").GetBoolean());Check(f.Movie.Name=="NOAH" && f.Movie.IsLocked);Check(f.Movie.Overview==Doc().Overview && f.Movie.Studios.Single()=="Sight & Sound Theatres");Check(f.Movie.ProviderIds["Custom"]=="leave-me");Check(!f.Movie.ProviderIds.ContainsKey("Imdb"));Check((await File.ReadAllBytesAsync(f.Movie.Path)).SequenceEqual(before));Check(f.Refreshed.SequenceEqual(new[]{f.Movie.Id}));
   Check(!r.ToString().Contains(f.Root));
  });
  await Test("restore recovers original indexed fields locks and cached image registration",async()=>{
   var f=await Make();var old=Path.Combine(f.Root,"old-poster.png");await File.WriteAllBytesAsync(old,png);f.Movie.ImageInfos=[new(){Path=old,Type=ImageType.Primary,Width=1,Height=1}];
   var r=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));var rev=r.GetProperty("revision").GetString()!;
   f.Controller.Request.Body=new MemoryStream(png);f.Controller.Request.ContentLength=png.Length;
   var image=Data(await f.Controller.SetArtwork(f.Job,"primary",rev));
   var restored=Data(await f.Controller.Restore(f.Job,new(){ExpectedRevision=image.GetProperty("revision").GetString()!,Revision="initial"}));
   Check(restored.GetProperty("verified").GetBoolean());Check(f.Movie.Name=="Old title"&&!f.Movie.IsLocked);Check(f.Movie.LockedFields.SequenceEqual(new[]{MetadataField.Name}));Check(f.Movie.GetImageInfo(ImageType.Primary,0).Path==old);Check(File.Exists(old));Check(!File.Exists(Path.Combine(Path.GetDirectoryName(f.Movie.Path)!,"movie.nfo")));
  });
  await Test("artwork body limits and fixed roles precede image registration",async()=>{
   var f=await Make();f.Controller.Request.ContentLength=8*1024*1024+1;Check((await f.Controller.SetArtwork(f.Job,"primary","initial") as ObjectResult)?.StatusCode==400);
   f.Controller.Request.ContentLength=png.Length;f.Controller.Request.Body=new MemoryStream(png);Check((await f.Controller.SetArtwork(f.Job,"unknown","initial") as ObjectResult)?.StatusCode==400);
   f.Controller.Request.Body=new MemoryStream(png);var r=Data(await f.Controller.SetArtwork(f.Job,"primary","initial"));Check(r.GetProperty("verified").GetBoolean());Check(f.Movie.GetImageInfo(ImageType.Primary,0).Path.EndsWith("poster.png"));Check(f.Movie.GetImageInfo(ImageType.Primary,0).Width==1);
  });
  await Test("path mismatches never edit an arbitrary item",async()=>{
   var f=await Make();f.Mismatch=true;Check((await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}) as ObjectResult)?.StatusCode==409);Check(f.Movie.Name=="Old title");Check(f.Refreshed.Count==0);
  });
  await Test("a refresh that changes requested metadata is reported unverified",async()=>{
   var f=await Make();f.Clobber=true;var r=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));Check(!r.GetProperty("verified").GetBoolean());Check(r.GetProperty("indexedMetadata").GetProperty("title").GetString()=="Wrong online match");Check(f.Refreshed.Count==1);
  });
  await Test("refresh failure retains committed revision and explicit pending result",async()=>{
   var f=await Make();f.RefreshFails=true;var r=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));Check(!r.GetProperty("verified").GetBoolean());Check(r.GetProperty("refreshStatus").GetString()=="pending");var rev=r.GetProperty("revision").GetString()!;Check(rev!="initial");
   f.RefreshFails=false;var resumed=Data(await f.Controller.Refresh(f.Job,new(){ExpectedRevision=rev}));Check(resumed.GetProperty("verified").GetBoolean());
  });
  await Test("unknown metadata fields cannot smuggle paths or URLs",()=>{
   foreach(var text in new[]{"{\"expectedRevision\":\"initial\",\"path\":\"/foreign\"}","{\"expectedRevision\":\"initial\",\"metadata\":{\"title\":\"Noah\",\"posterUrl\":\"http://evil\"}}"})
   {try{JsonSerializer.Deserialize<MetadataUpdateRequest>(text,Jellyfin.Extensions.Json.JsonDefaults.Options);throw new Exception("Unknown field accepted");}catch(JsonException){}}
   return Task.CompletedTask;
  });
  await Test("actual Jellyfin JSON defaults accept and emit MCP metadata casing",()=>{
   var request=JsonSerializer.Deserialize<MetadataUpdateRequest>("{\"expectedRevision\":\"initial\",\"metadata\":{\"title\":\"NOAH\",\"studios\":[\"Sight & Sound Theatres\"],\"genres\":[\"Faith\"]}}",Jellyfin.Extensions.Json.JsonDefaults.Options);
   Check(request?.ExpectedRevision=="initial"&&request.Metadata.Title=="NOAH"&&request.Metadata.Studios.Single()=="Sight & Sound Theatres");
   using var json=JsonDocument.Parse(JsonSerializer.Serialize(Doc(),Jellyfin.Extensions.Json.JsonDefaults.Options));Check(json.RootElement.GetProperty("title").GetString()=="NOAH");return Task.CompletedTask;
  });
  await Test("a long folder-derived original title can be repaired and restored",async()=>{
   var f=await Make();var original=new string('N',180)+" [6d2a193854f54c6bbc8ec6c13c82cb08]";f.Movie.Name=original;var changed=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));Check(f.Movie.Name=="NOAH");
   var restored=Data(await f.Controller.Restore(f.Job,new(){ExpectedRevision=changed.GetProperty("revision").GetString()!,Revision="initial"}));Check(f.Movie.Name==original);Check(restored.GetProperty("verified").GetBoolean());
  });
  await Test("incorrect existing metadata can be repaired without invalidating its restoration snapshot",async()=>{
   var f=await Make();f.Movie.ProductionYear=1500;f.Movie.PremiereDate=new DateTime(2025,1,1,0,0,0,DateTimeKind.Utc);f.Movie.ProviderIds["Imdb"]="incorrect old identifier";f.Movie.Overview=new string('x',12000);f.Movie.Genres=Enumerable.Range(0,25).Select(i=>"old genre "+i).ToArray();
   var result=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));Check(result.GetProperty("verified").GetBoolean());var restored=Data(await f.Controller.Restore(f.Job,new(){ExpectedRevision=result.GetProperty("revision").GetString()!,Revision="initial"}));
   Check(restored.GetProperty("verified").GetBoolean());Check(f.Movie.ProductionYear==1500&&f.Movie.PremiereDate?.Year==2025);Check(f.Movie.ProviderIds["Imdb"]=="incorrect old identifier"&&f.Movie.Overview.Length==12000&&f.Movie.Genres.Length==25);
  });
  await Test("failed refresh stays pending on inspection and across controller restart",async()=>{
   var f=await Make();f.RefreshFails=true;var changed=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));var inspected=Data(await f.Controller.Inspect(f.Job));Check(!inspected.GetProperty("verified").GetBoolean());Check(inspected.GetProperty("refreshStatus").GetString()=="pending");
   var bytes=await File.ReadAllTextAsync(Path.Combine(f.Store.Root,"Staging",f.Job.ToString("N"),"metadata-current.json"));Check(bytes.Contains("pending"));f.Controller=f.Restart();var restarted=Data(await f.Controller.Inspect(f.Job));Check(!restarted.GetProperty("verified").GetBoolean()&&restarted.GetProperty("refreshStatus").GetString()=="pending");
   f.RefreshFails=false;var done=Data(await f.Controller.Refresh(f.Job,new(){ExpectedRevision=changed.GetProperty("revision").GetString()!}));Check(done.GetProperty("verified").GetBoolean());
  });
  await Test("journal replay clears a dangling image registration after interrupted restore",async()=>{
   var f=await Make();f.Controller.Request.Body=new MemoryStream(png);f.Controller.Request.ContentLength=png.Length;var image=Data(await f.Controller.SetArtwork(f.Job,"primary","initial"));var revision=image.GetProperty("revision").GetString()!;
   await new ImportMetadataStore(f.Store).Restore(f.Owner,f.Job,revision,"initial");
   var inspected=Data(await f.Controller.Inspect(f.Job));Check(!inspected.GetProperty("verified").GetBoolean());var refreshed=Data(await f.Controller.Refresh(f.Job,new(){ExpectedRevision=inspected.GetProperty("revision").GetString()!}));Check(refreshed.GetProperty("verified").GetBoolean());Check(f.Movie.GetImageInfo(ImageType.Primary,0)==null);
  });
  await Test("corrupt pixel streams cannot publish artwork even when image dimensions parse",async()=>{
   var f=await Make();var corrupt=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAACElEQVR4nAMAAAAAAUgGidIAAAAASUVORK5CYII=");f.Controller.Request.Body=new MemoryStream(corrupt);f.Controller.Request.ContentLength=corrupt.Length;Check((await f.Controller.SetArtwork(f.Job,"primary","initial") as ObjectResult)?.StatusCode==400);Check(f.Movie.GetImageInfo(ImageType.Primary,0)==null);Check((await new ImportMetadataStore(f.Store).Inspect(f.Owner,f.Job)).Revision=="initial");
  });
  await Test("artwork-only edits reject external drift and inspection safely adopts it",async()=>{
   var f=await Make();var first=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));var revision=first.GetProperty("revision").GetString()!;f.Movie.Name="Administrator correction";var nfo=Path.Combine(Path.GetDirectoryName(f.Movie.Path)!,"movie.nfo");var modified=(await File.ReadAllTextAsync(nfo)).Replace("</movie>","<actor><name>New actor</name></actor></movie>");await File.WriteAllTextAsync(nfo,modified);
   f.Controller.Request.Body=new MemoryStream(png);f.Controller.Request.ContentLength=png.Length;Check((await f.Controller.SetArtwork(f.Job,"primary",revision) as ObjectResult)?.StatusCode==409);Check(f.Movie.Name=="Administrator correction"&&await File.ReadAllTextAsync(nfo)==modified);
   f.ClobberFromNfo=true;var adopted=Data(await f.Controller.Inspect(f.Job));Check(adopted.GetProperty("revision").GetString()!=revision);Check(adopted.GetProperty("refreshStatus").GetString()=="external-changes");
   f.Controller.Request.Body=new MemoryStream(png);var completed=Data(await f.Controller.SetArtwork(f.Job,"primary",adopted.GetProperty("revision").GetString()!));Check(completed.GetProperty("verified").GetBoolean());Check(f.Movie.Name=="Administrator correction"&&(await File.ReadAllTextAsync(nfo)).Contains("New actor"));
  });
  await Test("truncated JPEG scan cannot publish even with a readable frame header",async()=>{
   using var bitmap=new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(64,64));for(var y=0;y<64;y++)for(var x=0;x<64;x++)bitmap.SetPixel(x,y,new SkiaSharp.SKColor((byte)(x*7+y*11),(byte)(x*17+y*3),(byte)(x*13+y*19)));using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,90);var bytes=encoded.ToArray();var scan=-1;for(var i=0;i<bytes.Length-1;i++)if(bytes[i]==255&&bytes[i+1]==218){scan=i;break;}Check(scan>0);var length=(bytes[scan+2]<<8)|bytes[scan+3];var truncated=bytes[..(scan+2+length+1)];
   var f=await Make();f.Controller.Request.Body=new MemoryStream(truncated);f.Controller.Request.ContentLength=truncated.Length;Check((await f.Controller.SetArtwork(f.Job,"primary","initial") as ObjectResult)?.StatusCode==400);Check((await new ImportMetadataStore(f.Store).Inspect(f.Owner,f.Job)).Revision=="initial");
  });
  await Test("failed persistence is not verified from mutated in-memory fields",async()=>{
   var f=await Make();f.PersistenceFails=true;var changed=Data(await f.Controller.Update(f.Job,new(){ExpectedRevision="initial",Metadata=Doc()}));Check(f.Movie.Name=="NOAH"&&f.Refreshed.Count==0);Check(!changed.GetProperty("verified").GetBoolean());f.Controller=f.Restart();var inspected=Data(await f.Controller.Inspect(f.Job));Check(!inspected.GetProperty("verified").GetBoolean()&&inspected.GetProperty("refreshStatus").GetString()=="pending");
   f.PersistenceFails=false;var done=Data(await f.Controller.Refresh(f.Job,new(){ExpectedRevision=changed.GetProperty("revision").GetString()!}));Check(done.GetProperty("verified").GetBoolean());
  });
  Console.WriteLine($"METADATA ADAPTER RESULT: {17-failures}/17 passed");return failures;
 }
 private sealed class Fixture
 {
  public string Root="";public UploadStore Store=null!;public Guid Job;public Guid Owner;public Movie Movie=null!;public FakeAuth Auth=null!;public PluginConfiguration Config=null!;public ImportMetadataController Controller=null!;public List<Guid> Refreshed=[];public bool Mismatch;public bool Clobber;public bool RefreshFails;public bool PersistenceFails;public bool ClobberFromNfo;public Func<ImportMetadataController> Restart=null!;
 }
}
public class MetadataProxy:DispatchProxy
{
 public Func<MethodInfo,object?[]?,object?> Call=null!;
 protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>Call(targetMethod!,args);
}
