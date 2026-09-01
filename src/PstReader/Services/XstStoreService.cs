using PstReader.Format;
using PstReader.Models;
using XstReader;

namespace PstReader.Services;

internal sealed class XstStoreService(string inputPath, PffHeader header)
{
    public (StoreInfo Store, IReadOnlyList<FolderInfo> Folders) ReadCatalog()
    {
        var xst = new XstFile(inputPath);
        var root = xst.ReadFolderTree();
        var folders = EnumerateFolders(root)
            .Select(item => new FolderInfo(
                NormalizePath(item.Folder.Path),
                item.Folder.Name ?? string.Empty,
                item.Depth,
                item.Folder.ContentCount,
                item.Folder.Folders.Count))
            .ToArray();

        var store = new StoreInfo(
            Path.GetFullPath(inputPath),
            header.StoreKind.ToString().ToUpperInvariant(),
            header.Version,
            header.ClientVersion,
            header.Layout.ToString(),
            header.PageSize,
            header.FileSize,
            root.Name ?? "Root",
            folders.Length,
            folders.Sum(folder => folder.DeclaredMessageCount));

        return (store, folders);
    }

    internal static IEnumerable<(Folder Folder, int Depth)> EnumerateFolders(Folder root)
    {
        var stack = new Stack<(Folder Folder, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            for (var index = current.Folder.Folders.Count - 1; index >= 0; index--)
            {
                stack.Push((current.Folder.Folders[index], current.Depth + 1));
            }
        }
    }

    internal static string NormalizePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/');
}

