// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using WatermelonFin.Plugin.Core;

namespace WatermelonFin.Plugin;

public sealed record IndexedImport(Guid ItemId, MetadataSnapshot Snapshot);

public sealed class JellyfinImportMetadata(ILibraryManager library,IProviderManager providers,IFileSystem fileSystem,IItemPersistenceService persistence,IServerApplicationPaths paths,IImageProcessor images)
{
    private static readonly string[] ProviderNames=["imdb","tmdb","tvdb"];
    private BaseItem Item(CompletedImport import)
    {
        var item=library.FindByPath(import.VideoPath,false);
        if(item is not Video || item.Id==Guid.Empty || !string.Equals(Path.GetFullPath(item.Path),Path.GetFullPath(import.VideoPath),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new UploadException("Completed import is not indexed as the expected video item",409);
        return item;
    }
    private void ImagePath(CompletedImport import,string path)
    {
        var full=Path.GetFullPath(path);
        var roots=new[]{Path.GetDirectoryName(import.VideoPath)!,paths.InternalMetadataPath,paths.CachePath}.Where(p=>!string.IsNullOrWhiteSpace(p)).Select(p=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)));
        var root=roots.FirstOrDefault(r=>!Path.GetRelativePath(r,full).StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&Path.GetRelativePath(r,full)!=".."&&!Path.IsPathRooted(Path.GetRelativePath(r,full)));
        if(root==null)throw new UploadException("Artwork is outside this import and Jellyfin-managed artwork locations",409);
        var info=new FileInfo(full);if(!info.Exists || info.LinkTarget!=null || (info.Attributes&FileAttributes.ReparsePoint)!=0 || info.Length>MetadataArtwork.MaximumBytes)throw new UploadException("Existing artwork cannot be safely restored",409);
        var current=Path.GetDirectoryName(full)!;
        while(true){var dir=new DirectoryInfo(current);if(!dir.Exists || dir.LinkTarget!=null || (dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new UploadException("Symlinked artwork location refused",409);if(current==root)break;current=Path.GetDirectoryName(current)??throw new UploadException("Invalid artwork location");}
    }
    private static string? Optional(string? value)=>string.IsNullOrEmpty(value)?null:value;
    public IndexedImport Read(CompletedImport import)
    {
        var item=Item(import);var artwork=new Dictionary<string,string?>();
        foreach(var (role,type) in new[]{("primary",ImageType.Primary),("backdrop",ImageType.Backdrop)})
        {var image=item.GetImageInfo(type,0);artwork[role]=image?.Path;if(image!=null)ImagePath(import,image.Path);}
        var document=new MetadataDocument{Title=item.Name,OriginalTitle=Optional(item.OriginalTitle),SortTitle=Optional(item.ForcedSortName),Overview=Optional(item.Overview),ProductionYear=item.ProductionYear,PremiereDate=item.PremiereDate?.ToString("yyyy-MM-dd"),Studios=item.Studios,Genres=item.Genres,ProviderIds=item.ProviderIds.Where(p=>ProviderNames.Contains(p.Key.ToLowerInvariant())).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase)};
        document.Validate(256);return new(item.Id,new(document,item.IsLocked,item.LockedFields.Select(f=>f.ToString()).ToArray(),artwork));
    }
    public void ValidateArtwork(CompletedImport import,byte[] bytes)
    {
        var extension=MetadataArtwork.Validate(bytes);var name=Path.Combine(import.JournalDirectory,".artwork-validation-"+Guid.NewGuid().ToString("N")+"."+extension);
        try
        {
            using(var file=new FileStream(name,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);}
            var size=images.GetImageDimensions(name);if(size.Width<1 || size.Height<1 || size.Width>8192 || size.Height>8192 || (long)size.Width*size.Height>40_000_000)throw new UploadException("Artwork could not be decoded within the allowed dimensions");
        }catch(UploadException){throw;}catch(Exception e)when(e is IOException or ArgumentException or InvalidOperationException){throw new UploadException("Artwork could not be decoded");}
        finally{File.Delete(name);}
    }
    private static string? DesiredImage(CompletedImport import,ImportMetadataState state,string role)
    {
        var stem=role=="primary"?"poster":"fanart";
        foreach(var extension in new[]{"jpg","png"})if(state.Files[stem+"."+extension]!=null)return Path.Combine(Path.GetDirectoryName(import.VideoPath)!,MetadataFiles.Name(import,stem+"."+extension));
        return state.Metadata?.ArtworkPaths?.GetValueOrDefault(role);
    }
    public async Task Synchronize(CompletedImport import,ImportMetadataState state)
    {
        var item=Item(import);var snapshot=state.Metadata??throw new UploadException("Missing original indexed metadata snapshot",409);var doc=snapshot.Document;doc.Validate(256);
        item.Name=doc.Title;item.OriginalTitle=doc.OriginalTitle;item.ForcedSortName=doc.SortTitle;item.Overview=doc.Overview;item.ProductionYear=doc.ProductionYear;
        item.PremiereDate=doc.PremiereDate==null?null:DateTime.SpecifyKind(DateOnly.ParseExact(doc.PremiereDate,"yyyy-MM-dd").ToDateTime(TimeOnly.MinValue),DateTimeKind.Utc);
        item.Studios=doc.Studios;item.Genres=doc.Genres;item.IsLocked=snapshot.IsLocked;
        item.LockedFields=snapshot.LockedFields.Select(s=>Enum.TryParse<MetadataField>(s,out var field)?field:throw new UploadException("Unknown metadata lock field")).ToArray();
        foreach(var key in item.ProviderIds.Keys.Where(k=>ProviderNames.Contains(k.ToLowerInvariant())).ToArray())item.ProviderIds.Remove(key);
        foreach(var (key,value) in doc.ProviderIds)item.ProviderIds[key]=value;
        var imageInfos=item.ImageInfos.ToList();
        foreach(var (role,type) in new[]{("primary",ImageType.Primary),("backdrop",ImageType.Backdrop)})
        {
            var desired=DesiredImage(import,state,role);var index=imageInfos.FindIndex(i=>i.Type==type);
            if(desired==null){if(index>=0)imageInfos.RemoveAt(index);continue;}
            ImagePath(import,desired);var size=images.GetImageDimensions(desired);
            var image=new ItemImageInfo{Path=desired,Type=type,DateModified=File.GetLastWriteTimeUtc(desired),Width=size.Width,Height=size.Height};
            if(index>=0)imageInfos[index]=image;else imageInfos.Add(image);
        }
        item.ImageInfos=imageInfos.ToArray();
        await library.UpdateItemAsync(item,item.GetParent(),ItemUpdateType.MetadataEdit,CancellationToken.None);
        await persistence.SaveImagesAsync(item,CancellationToken.None);
    }
    public Task Refresh(CompletedImport import)
    {
        var item=Item(import);var options=new MetadataRefreshOptions(new DirectoryService(fileSystem)){MetadataRefreshMode=MetadataRefreshMode.ValidationOnly,ImageRefreshMode=MetadataRefreshMode.ValidationOnly,ReplaceAllMetadata=false,EnableRemoteContentProbe=false,IsAutomated=false};
        return providers.RefreshSingleItem(item,options,CancellationToken.None);
    }
    private static string Comparable(MetadataDocument d)=>JsonSerializer.Serialize(new{d.Title,d.OriginalTitle,d.SortTitle,d.Overview,d.ProductionYear,d.PremiereDate,d.Studios,d.Genres,ProviderIds=d.ProviderIds.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>new{Key=p.Key.ToLowerInvariant(),p.Value})});
    public bool Matches(CompletedImport import,ImportMetadataState state,IndexedImport indexed)
    {
        var expected=state.Metadata;if(expected==null || Comparable(expected.Document)!=Comparable(indexed.Snapshot.Document) || expected.IsLocked!=indexed.Snapshot.IsLocked || !expected.LockedFields.SequenceEqual(indexed.Snapshot.LockedFields))return false;
        return new[]{"primary","backdrop"}.All(role=>DesiredImage(import,state,role)==indexed.Snapshot.ArtworkPaths?.GetValueOrDefault(role));
    }
}
