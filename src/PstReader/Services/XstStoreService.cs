using PstReader.Format;
using PstReader.Models;
using XstReader;

namespace PstReader.Services;

internal sealed class XstStoreService(string inputPath, PffHeader header)
{
    private const string AppointmentPropertySet = "00062002-0000-0000-c000-000000000046";
    private const string CommonPropertySet = "00062008-0000-0000-c000-000000000046";
    private const string MeetingPropertySet = "6ed8da90-450b-101b-98da-00aa003f1305";

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
            string.IsNullOrWhiteSpace(root.Name) ? "Root" : root.Name,
            folders.Length,
            folders.Sum(folder => folder.DeclaredMessageCount));

        return (store, folders);
    }

    public CalendarReadResult ReadCalendar(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool includeBody = false)
    {
        var xst = new XstFile(inputPath);
        var root = xst.ReadFolderTree();
        var items = new List<CalendarItem>();
        var warnings = new List<string>();

        foreach (var entry in EnumerateFolders(root))
        {
            List<Message> messages;
            try
            {
                messages = xst.ReadMessages(entry.Folder);
            }
            catch (Exception ex) when (IsRecoverableItemError(ex))
            {
                warnings.Add($"{NormalizePath(entry.Folder.Path)}: フォルダーを読み取れません: {ex.Message}");
                continue;
            }

            foreach (var message in messages)
            {
                try
                {
                    xst.ReadMessageDetails(message);
                    var calendarItem = CreateCalendarItem(entry.Folder, message, fromUtc, toUtc, includeBody);
                    if (calendarItem is not null)
                    {
                        items.Add(calendarItem);
                    }
                }
                catch (Exception ex) when (IsRecoverableItemError(ex))
                {
                    warnings.Add(
                        $"{NormalizePath(entry.Folder.Path)} / {message.Subject ?? "<件名なし>"}: " +
                        $"項目を読み取れません: {ex.Message}");
                }
            }
        }

        return new CalendarReadResult(
            items.OrderBy(item => item.Start).ThenBy(item => item.Subject, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static CalendarItem? CreateCalendarItem(
        Folder folder,
        Message message,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        bool includeBody)
    {
        var messageClass = GetStandardString(message.Properties, 0x001A) ?? string.Empty;
        var start = GetNamedDate(message.Properties, AppointmentPropertySet, 0x820D)
                    ?? GetNamedDate(message.Properties, CommonPropertySet, 0x8516)
                    ?? GetStandardDate(message.Properties, 0x0060);
        var end = GetNamedDate(message.Properties, AppointmentPropertySet, 0x820E)
                  ?? GetNamedDate(message.Properties, CommonPropertySet, 0x8517)
                  ?? GetStandardDate(message.Properties, 0x0061);

        var hasAppointmentProperties = start is not null || end is not null;
        if (!messageClass.StartsWith("IPM.Appointment", StringComparison.OrdinalIgnoreCase) &&
            !(string.IsNullOrEmpty(messageClass) && hasAppointmentProperties))
        {
            return null;
        }

        if (start is null || end is null || end < start)
        {
            throw new InvalidDataException("予定の開始または終了日時が不正です。");
        }

        var clipStart = GetNamedDate(message.Properties, AppointmentPropertySet, 0x8235) ?? start;
        var clipEnd = GetNamedDate(message.Properties, AppointmentPropertySet, 0x8236) ?? end;
        if (!Overlaps(clipStart.Value, clipEnd.Value, fromUtc, toUtc))
        {
            return null;
        }

        var location = GetNamedString(message.Properties, AppointmentPropertySet, 0x8208)
                       ?? GetNamedString(message.Properties, MeetingPropertySet, 0x0002)
                       ?? string.Empty;
        var isAllDay = GetNamedBoolean(message.Properties, AppointmentPropertySet, 0x8215) ?? false;
        var (body, bodyFormat) = includeBody ? ExtractBody(message) : (null, null);

        return new CalendarItem(
            NormalizePath(folder.Path),
            message.Subject ?? string.Empty,
            start.Value,
            end.Value,
            location,
            isAllDay,
            messageClass,
            body,
            bodyFormat);
    }

    internal static (string? Body, string? BodyFormat) ExtractBody(Message message)
    {
        if (message.IsBodyHtml)
        {
            var html = TryGetHtmlBody(message);
            if (html is not null)
            {
                return (html, "html");
            }
        }

        if (message.IsBodyRtf)
        {
            var rtf = TryGetRtfBody(message);
            if (rtf is not null)
            {
                return (rtf, "rtf");
            }
        }

        if (message.Body is not null)
        {
            return (message.Body, "text");
        }

        var fallbackHtml = TryGetHtmlBody(message);
        if (fallbackHtml is not null)
        {
            return (fallbackHtml, "html");
        }

        var fallbackRtf = TryGetRtfBody(message);
        return fallbackRtf is null ? (null, null) : (fallbackRtf, "rtf");
    }

    private static string? TryGetHtmlBody(Message message)
    {
        try
        {
            return message.GetBodyAsHtmlString(escapeUnicode: false);
        }
        catch (Exception ex) when (IsRecoverableItemError(ex))
        {
            return null;
        }
    }

    private static string? TryGetRtfBody(Message message)
    {
        if (message.RtfCompressed is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return message.GetBodyAsRtfString();
        }
        catch (Exception ex) when (IsRecoverableItemError(ex))
        {
            return null;
        }
    }

    internal static bool Overlaps(
        DateTimeOffset start,
        DateTimeOffset end,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc) =>
        (fromUtc is null || end > fromUtc) &&
        (toUtc is null || start < toUtc);

    private static string? GetStandardString(IEnumerable<Property> properties, ushort tag) =>
        FindStandard(properties, tag)?.Value as string;

    private static DateTimeOffset? GetStandardDate(IEnumerable<Property> properties, ushort tag) =>
        ToDateTimeOffset(FindStandard(properties, tag)?.Value);

    private static string? GetNamedString(
        IEnumerable<Property> properties,
        string propertySet,
        uint lid) =>
        FindNamed(properties, propertySet, lid)?.Value as string;

    private static DateTimeOffset? GetNamedDate(
        IEnumerable<Property> properties,
        string propertySet,
        uint lid) =>
        ToDateTimeOffset(FindNamed(properties, propertySet, lid)?.Value);

    private static bool? GetNamedBoolean(
        IEnumerable<Property> properties,
        string propertySet,
        uint lid)
    {
        var value = FindNamed(properties, propertySet, lid)?.Value;
        return value switch
        {
            bool boolean => boolean,
            byte number => number != 0,
            short number => number != 0,
            int number => number != 0,
            uint number => number != 0,
            _ => null,
        };
    }

    private static Property? FindStandard(IEnumerable<Property> properties, ushort tag) =>
        properties.FirstOrDefault(property => (ushort)property.Tag == tag);

    private static Property? FindNamed(
        IEnumerable<Property> properties,
        string propertySet,
        uint lid) =>
        properties.FirstOrDefault(property =>
            property.IsNamed &&
            property.Lid == lid &&
            string.Equals(property.Guid, propertySet, StringComparison.OrdinalIgnoreCase));

    private static DateTimeOffset? ToDateTimeOffset(object? value) => value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(
            dateTime.Kind is DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                : dateTime).ToUniversalTime(),
        _ => null,
    };

    private static bool IsRecoverableItemError(Exception ex) =>
        ex is XstException or IOException or InvalidCastException or FormatException or ArgumentException;

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
