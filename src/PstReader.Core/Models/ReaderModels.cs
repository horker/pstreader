namespace PstReader.Models;

public sealed record StoreInfo(
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

public sealed record FolderInfo(
    string Path,
    string Name,
    int Depth,
    long DeclaredMessageCount,
    int ChildFolderCount);

public sealed record CalendarItem(
    string FolderPath,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    bool IsAllDay,
    string MessageClass,
    string? Body = null,
    string? BodyFormat = null);

public sealed record CalendarReadResult(
    IReadOnlyList<CalendarItem> Items,
    IReadOnlyList<string> Warnings);

public sealed record TaskItem(
    string FolderPath, string Subject,
    DateTimeOffset? Start, DateTimeOffset? Due, DateTimeOffset? Completed,
    int? Status, double? PercentComplete, bool? IsComplete, string MessageClass,
    string? Body = null, string? BodyFormat = null);

public sealed record MailItem(
    string FolderPath, string Subject, string SenderName, string SenderAddress,
    string To, string Cc, string Bcc, DateTimeOffset? Received, DateTimeOffset? Sent,
    bool? IsRead, bool? HasAttachments, string FlagStatus, string MessageClass,
    string? Body = null, string? BodyFormat = null);

public sealed record ItemReadResult<T>(
    IReadOnlyList<T> Items, IReadOnlyList<string> Warnings);
