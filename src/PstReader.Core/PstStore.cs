using PstReader.Format;
using PstReader.Models;
using PstReader.Services;

namespace PstReader;

/// <summary>Reads a PST/OST without Outlook. Each operation opens and closes its own file streams.</summary>
public sealed class PstStore
{
    private readonly XstStoreService service;

    public PstStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Specify an input file.", nameof(path));
        InputPath = Path.GetFullPath(path);
        service = new XstStoreService(InputPath, PffHeaderReader.Read(InputPath));
    }

    public string InputPath { get; }

    public (StoreInfo Store, IReadOnlyList<FolderInfo> Folders) ReadCatalog() => service.ReadCatalog();
    public StoreInfo ReadStore() => ReadCatalog().Store;
    public IReadOnlyList<FolderInfo> ReadFolders() => ReadCatalog().Folders;

    /// <summary>Returns appointments overlapping [from, to). Dates in the result are UTC.</summary>
    public CalendarReadResult ReadCalendar(DateTimeOffset? from = null, DateTimeOffset? to = null,
        bool includeBody = false)
    {
        ReaderDateTime.ValidateRange(from, to);
        return service.ReadCalendar(from, to, includeBody);
    }

    /// <summary>Filters by start, due (default), or completed. Missing dates have no fallback.</summary>
    public ItemReadResult<TaskItem> ReadTasks(DateTimeOffset? from = null, DateTimeOffset? to = null,
        string dateField = "due", bool includeBody = false)
    {
        ReaderDateTime.ValidateRange(from, to);
        dateField = dateField?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(dateField));
        if (dateField is not ("start" or "due" or "completed"))
            throw new ArgumentException("Specify start, due, or completed.", nameof(dateField));
        return service.ReadTasks(from, to, dateField, includeBody);
    }

    /// <summary>Filters by received (default) or sent; flaggedOnly excludes completed flags.</summary>
    public ItemReadResult<MailItem> ReadMail(DateTimeOffset? from = null, DateTimeOffset? to = null,
        string dateField = "received", bool includeBody = false, bool flaggedOnly = false)
    {
        ReaderDateTime.ValidateRange(from, to);
        dateField = dateField?.ToLowerInvariant() ?? throw new ArgumentNullException(nameof(dateField));
        if (dateField is not ("received" or "sent"))
            throw new ArgumentException("Specify received or sent.", nameof(dateField));
        return service.ReadMail(from, to, dateField, includeBody, flaggedOnly);
    }
}
