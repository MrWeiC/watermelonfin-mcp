// SPDX-License-Identifier: GPL-3.0-only
namespace WatermelonFin.Plugin.Core;

internal sealed class SafePaths
{
    public string Root { get; }
    public SafePaths(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        // Inspect every existing ancestor before creating any new descendant.
        EnsureAbsoluteDirectory(Root);
        CheckPrivate(Root);
    }
    private static void Check(string path, bool directory)
    {
        var item = directory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        item.Refresh();
        if (!item.Exists || item.LinkTarget != null || (item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UploadException("Missing or symlinked upload path refused");
        if (directory != ((item.Attributes & FileAttributes.Directory) != 0)) throw new UploadException("Invalid upload path type");
    }
    private static void CheckPrivate(string path)
    {
        if(!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite|UnixFileMode.OtherWrite)) != 0)throw new UploadException("Shared-writable upload directories are not supported");
    }
    private static void EnsureAbsoluteDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent != null && parent != path) EnsureAbsoluteDirectory(parent);
        if (!System.IO.Directory.Exists(path))
        {
            if (File.Exists(path) || new DirectoryInfo(path).LinkTarget != null) throw new UploadException("Invalid upload directory");
            System.IO.Directory.CreateDirectory(path);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Check(path, true);
    }
    public string Directory(params string[] components)
    {
        Check(Root, true); CheckPrivate(Root); var current = Root;
        foreach (var part in components)
        {
            if (part is "." or ".." || string.IsNullOrEmpty(part) || part.IndexOfAny(['/', '\\']) >= 0) throw new UploadException("Invalid upload path component");
            current = Path.Combine(current, part);
            if (!System.IO.Directory.Exists(current))
            {
                if (File.Exists(current) || new DirectoryInfo(current).LinkTarget != null) throw new UploadException("Symlink upload directory refused");
                System.IO.Directory.CreateDirectory(current);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(current, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Check(current, true); CheckPrivate(current);
        }
        return current;
    }
    public string ExistingFile(string directory, string name)
    {
        Check(directory, true); var path = Path.Combine(directory, name); Check(path, false); return path;
    }
    public static bool IsFile(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new UploadException("Symlink upload file refused");
        if (!File.Exists(path)) return false;
        Check(path, false); return true;
    }
}
