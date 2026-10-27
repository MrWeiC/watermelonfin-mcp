// SPDX-License-Identifier: GPL-3.0-only
using System.Security.Cryptography;
using WatermelonFin.Plugin.Core;

var failures = 0;
async Task Test(string name, Func<Task> run) { try { await run(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
void Check(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
async Task Deny(Func<Task> run) { try { await run(); } catch (Exception e) when (e is UploadException or IOException) { return; } throw new Exception("Expected refusal"); }
string Temp() { var p = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "wf-plugin-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
var owner = Guid.NewGuid(); var other = Guid.NewGuid();
var bytes = "new video bytes"u8.ToArray();
UploadRequest Request(string name = "Example (2024).mp4") => new() { TargetId = "movies", FileName = name, Size = bytes.Length, Sha256 = Hash(bytes) };

await Test("reject traversal and ambiguous naming", async () => {
 foreach(var name in new[]{"../a.mp4", "a/b.mp4", "a\\b.mp4", "CON.mp4", "a.txt"}) await Deny(() => Task.FromResult(ImportPlan.Create(name, "movies")));
 await Deny(() => Task.FromResult(ImportPlan.Create("video.mp4", "movies")));
 Check(ImportPlan.Create("Example S01E03.mkv", "tv").RelativePath == "Example/Season 01/Example - S01E03.mkv");
});
await Test("append, publish and retry; existing file unchanged", async () => {
 var root = Temp(); var store = new UploadStore(root); var original = Path.Combine(root, "existing.mp4"); await File.WriteAllTextAsync(original, "old");
 var job = await store.Create(owner, Request(), "movies");
 await store.Append(owner, job.Id, 0, bytes[..4]); await store.Append(owner, job.Id, 4, bytes[4..]);
 var result = await store.Finalize(owner, job.Id); Check(result.Status == "completed"); Check(result.RelativePath == (await store.Finalize(owner, job.Id)).RelativePath);
 Check(await File.ReadAllTextAsync(original) == "old"); Check((await File.ReadAllBytesAsync(Path.Combine(root, result.RelativePath!))).SequenceEqual(bytes));
 var restarted = new UploadStore(root); Check((await restarted.Status(owner, job.Id)).Status == "completed");
 await Deny(() => store.Append(owner, job.Id, bytes.Length, bytes));
});
await Test("cross-user jobs and bad offsets denied", async () => {
 var store = new UploadStore(Temp()); var job = await store.Create(owner, Request(), "movies");
 await Deny(() => store.Status(other, job.Id)); await Deny(() => store.Append(other, job.Id, 0, bytes)); await Deny(() => store.Finalize(other, job.Id));
 await Deny(() => store.Append(owner, job.Id, 1, bytes)); await Deny(() => store.Finalize(owner, job.Id));
});
await Test("checksum mismatch never publishes", async () => {
 var root = Temp(); var store = new UploadStore(root); var job = await store.Create(owner, Request(), "movies");
 await store.Append(owner, job.Id, 0, new byte[bytes.Length]); await Deny(() => store.Finalize(owner, job.Id));
 Check(!Directory.EnumerateFiles(Path.Combine(root,"Imported"),"*.mp4",SearchOption.AllDirectories).Any());
});
await Test("new jobs never overwrite and completed jobs free active quota", async () => {
 var store = new UploadStore(Temp(), maxActiveJobs:1); var a = await store.Create(owner,Request(),"movies"); await store.Append(owner,a.Id,0,bytes); var ar = await store.Finalize(owner,a.Id);
 var b = await store.Create(owner,Request(),"movies"); await store.Append(owner,b.Id,0,bytes); var br = await store.Finalize(owner,b.Id); Check(ar.RelativePath != br.RelativePath);
});
await Test("symlink target rejected before outside directory creation", async () => {
 var root=Temp(); var store=new UploadStore(root); var job=await store.Create(owner,Request(),"movies");await store.Append(owner,job.Id,0,bytes);
 var outside=Temp();Directory.CreateSymbolicLink(Path.Combine(root,"Imported","movies"),outside);
 await Deny(()=>store.Finalize(owner,job.Id));Check(!Directory.EnumerateFileSystemEntries(outside).Any());
});
await Test("preexisting same-size destination is a conflict, never a successful upload", async () => {
 var root=Temp();var store=new UploadStore(root);var job=await store.Create(owner,Request(),"movies");await store.Append(owner,job.Id,0,bytes);
 var parent=Path.Combine(root,"Imported","movies",job.Id.ToString("N"),"Example (2024)");Directory.CreateDirectory(parent);var dest=Path.Combine(parent,"Example (2024).mp4");await File.WriteAllBytesAsync(dest,new byte[bytes.Length]);
 await Deny(()=>store.Finalize(owner,job.Id));Check((await File.ReadAllBytesAsync(dest)).All(b=>b==0));
});
await Test("access denied unless enabled, restricted and allowlisted", async () => {
 foreach(var input in new[]{new AccessFacts(false,true,false,false,false,true),new AccessFacts(true,false,false,false,false,true),new AccessFacts(true,true,true,false,false,true),new AccessFacts(true,true,false,true,false,true),new AccessFacts(true,true,false,false,true,true),new AccessFacts(true,true,false,false,false,false)})
 await Deny(()=>Task.Run(()=>UploadAccess.Require(input)));
 UploadAccess.Require(new AccessFacts(true,true,false,false,false,true));
});
await Test("crashed partial manifest does not block unrelated uploads", async () => {
 var root=Temp();var store=new UploadStore(root);var broken=Path.Combine(root,"Staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(broken);await File.WriteAllTextAsync(Path.Combine(broken,"job.json"),"{\"id\":");
 await store.Create(owner,Request(),"movies");Check(await File.ReadAllTextAsync(Path.Combine(broken,"job.json"))=="{\"id\":");
});
await Test("free space probe uses actual import directory, not filesystem root", async () => {
 var root=Temp();string? probed=null;var store=new UploadStore(root,availableSpace:path=>{probed=path;return 0;});
 await Deny(()=>store.Create(owner,Request(),"movies"));Check(probed==root);
 if(!OperatingSystem.IsWindows())Check(new DriveInfo(root).Name==root);
});
Console.WriteLine($"RESULT: {10-failures}/10 passed"); return failures == 0 ? 0 : 1;
