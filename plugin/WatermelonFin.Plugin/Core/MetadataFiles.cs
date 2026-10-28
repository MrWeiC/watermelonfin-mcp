// SPDX-License-Identifier: GPL-3.0-only
namespace WatermelonFin.Plugin.Core;

public static class MetadataFiles
{
    public static string Name(CompletedImport import,string logicalName)
    {
        if(logicalName is not ("movie.nfo" or "poster.jpg" or "poster.png" or "fanart.jpg" or "fanart.png"))throw new UploadException("Unsupported metadata filename");
        if(import.Kind!="tv")return logicalName;
        var stem=Path.GetFileNameWithoutExtension(import.VideoPath);
        return logicalName switch
        {
            "movie.nfo"=>stem+".nfo",
            "poster.jpg"=>stem+"-thumb.jpg",
            "poster.png"=>stem+"-thumb.png",
            "fanart.jpg"=>stem+"-fanart.jpg",
            _=>stem+"-fanart.png"
        };
    }
}
