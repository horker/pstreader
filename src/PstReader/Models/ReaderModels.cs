namespace PstReader.Models;

internal sealed record StoreInfo(
    string InputFile,
    string DetectedType,
    int Version,
    int ClientVersion,
    string Layout,
    int PageSize,
    long FileSize,
    string RootFolder,
    int FolderCount,
    long DeclaredMessageCount);

internal sealed record FolderInfo(
    string Path,
    string Name,
    int Depth,
    long DeclaredMessageCount,
    int ChildFolderCount);

