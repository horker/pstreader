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

internal sealed record CalendarItem(
    string FolderPath,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Location,
    bool IsAllDay,
    string MessageClass,
    string? Body = null,
    string? BodyFormat = null);

internal sealed record CalendarReadResult(
    IReadOnlyList<CalendarItem> Items,
    IReadOnlyList<string> Warnings);

internal sealed record TaskItem(
    string FolderPath, string Subject,
    DateTimeOffset? Start, DateTimeOffset? Due, DateTimeOffset? Completed,
    int? Status, double? PercentComplete, bool? IsComplete, string MessageClass,
    string? Body = null, string? BodyFormat = null);

internal sealed record MailItem(
    string FolderPath, string Subject, string SenderName, string SenderAddress,
    string To, string Cc, string Bcc, DateTimeOffset? Received, DateTimeOffset? Sent,
    bool? IsRead, bool? HasAttachments, string FlagStatus, string MessageClass,
    string? Body = null, string? BodyFormat = null);

internal sealed record ItemReadResult<T>(
    IReadOnlyList<T> Items, IReadOnlyList<string> Warnings);
