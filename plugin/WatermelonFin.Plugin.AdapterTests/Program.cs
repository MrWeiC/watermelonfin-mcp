// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WatermelonFin.Plugin;
using WatermelonFin.Plugin.Configuration;
using WatermelonFin.Plugin.Core;

var root=Path.Combine(OperatingSystem.IsMacOS()?"/private/tmp":Path.GetTempPath(),"wf-plugin-adapter-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var paths=DispatchProxy.Create<IApplicationPaths,PathsProxy>();((PathsProxy)(object)paths).Path=root;
var user=new User("restricted","auth","reset");user.Permissions.Add(new(PermissionKind.IsAdministrator,false));user.Permissions.Add(new(PermissionKind.EnableContentDeletion,false));
var auth=new FakeAuth { Info=new AuthorizationInfo { User=user,IsAuthenticated=true,IsApiKey=false } };
var config=new PluginConfiguration { Enabled=true,AllowedUserIds=[user.Id] };
var store=new UploadStore(Path.Combine(root,"uploads"));
var controller=new UploadController(auth,store,paths,()=>config){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
var failures=0;
void Check(bool ok){if(!ok)throw new Exception("Assertion failed");}
async Task Test(string name,Func<Task> run){try{await run();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
await Test("adapter honors disabled, revoked and privileged user state",async()=>{
 config.Enabled=false;Check((await controller.Capabilities() as ObjectResult)?.StatusCode==403);config.Enabled=true;
 config.AllowedUserIds=[];Check((await controller.Capabilities() as ObjectResult)?.StatusCode==403);config.AllowedUserIds=[user.Id];
 user.Permissions.Single(p=>p.Kind==PermissionKind.IsAdministrator).Value=true;Check((await controller.Capabilities() as ObjectResult)?.StatusCode==403);user.Permissions.Single(p=>p.Kind==PermissionKind.IsAdministrator).Value=false;
 auth.Info.IsApiKey=true;Check((await controller.Capabilities() as ObjectResult)?.StatusCode==403);auth.Info.IsApiKey=false;
 Check(await controller.Capabilities() is OkObjectResult);
});
await Test("real SDK controller accepts upload and enforces job ownership",async()=>{
 var data="abc"u8.ToArray();var request=new UploadRequest {TargetId="movies",FileName="Example (2024).mp4",Size=data.Length,Sha256=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()};
 var created=(JobView)((OkObjectResult)await controller.Create(request)).Value!;
 auth.Info.User=new User("other","auth","reset");config.AllowedUserIds=[auth.Info.User.Id];Check((await controller.Status(created.Id) as ObjectResult)?.StatusCode==403);
 auth.Info.User=user;config.AllowedUserIds=[user.Id];controller.Request.Body=new MemoryStream(data);controller.Request.ContentLength=data.Length;
 Check(await controller.Append(created.Id,0) is OkObjectResult);Check(((JobView)((OkObjectResult)await controller.Finalize(created.Id)).Value!).Status=="completed");
});
await Test("native controller advertises no destructive routes and requires auth",()=>{
 Check(typeof(UploadController).GetCustomAttribute<AuthorizeAttribute>()!=null);
 foreach(var method in typeof(UploadController).GetMethods())Check(method.GetCustomAttribute<HttpDeleteAttribute>()==null);
 Check(typeof(UploadController).GetMethod("AdminInfo")!.GetCustomAttribute<AuthorizeAttribute>()?.Policy=="RequiresElevation");return Task.CompletedTask;
});
await Test("extra upload fields fail closed",()=>{
 try{JsonSerializer.Deserialize<UploadRequest>("{\"TargetId\":\"movies\",\"Delete\":true}");throw new Exception("Unknown field accepted");}catch(JsonException){}return Task.CompletedTask;
});
await Test("job IDs retain canonical wire format under Jellyfin JSON settings",()=>{
 var id=Guid.NewGuid();var options=new JsonSerializerOptions();options.Converters.Add(new Jellyfin.Extensions.Json.Converters.JsonGuidConverter());
 var json=JsonSerializer.Serialize(new JobView(id,"uploading",1,0,DateTimeOffset.UtcNow),options);Check(json.Contains(id.ToString("D")));return Task.CompletedTask;
});
await Test("request casing matches MCP under Jellyfin JSON defaults",()=>{
 var request=JsonSerializer.Deserialize<UploadRequest>("{\"targetId\":\"movies\",\"fileName\":\"Example (2024).mp4\",\"size\":3,\"sha256\":\"abc\"}",Jellyfin.Extensions.Json.JsonDefaults.Options);
 Check(request?.TargetId=="movies" && request.Size==3);return Task.CompletedTask;
});
Console.WriteLine($"RESULT: {6-failures}/6 passed");failures+=await MetadataAdapterTests.Run();return failures==0?0:1;

public class PathsProxy:DispatchProxy {public string Path="";protected override object? Invoke(MethodInfo? method,object?[]? args)=>method?.ReturnType==typeof(string)?Path:null;}
public sealed class FakeAuth:IAuthorizationContext
{
 public AuthorizationInfo Info {get;set;}=new();
 public Task<AuthorizationInfo> GetAuthorizationInfo(HttpContext context)=>Task.FromResult(Info);
 public Task<AuthorizationInfo> GetAuthorizationInfo(HttpRequest context)=>Task.FromResult(Info);
}
