using PstReader.Models;
using XstReader;

namespace PstReader.Services;

internal sealed partial class XstStoreService
{
    private const string TaskPropertySet = "00062003-0000-0000-c000-000000000046";

    public ItemReadResult<TaskItem> ReadTasks(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        string dateField = "due", bool includeBody = false)
    {
        var result = ReadItems((folder, message) => CreateTaskItem(folder, message, includeBody));
        return new(FilterAndSort(result.Items, item => TaskDate(item, dateField),
            item => item.Subject, fromUtc, toUtc), result.Warnings);
    }

    public ItemReadResult<MailItem> ReadMail(DateTimeOffset? fromUtc, DateTimeOffset? toUtc,
        string dateField = "received", bool includeBody = false, bool flaggedOnly = false)
    {
        var result = ReadItems((folder, message) => CreateMailItem(folder, message, includeBody));
        return new(FilterMail(result.Items, fromUtc, toUtc, dateField, flaggedOnly), result.Warnings);
    }

    internal static IReadOnlyList<MailItem> FilterMail(IEnumerable<MailItem> items,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string dateField, bool flaggedOnly) =>
        FilterAndSort(items.Where(item => !flaggedOnly || item.FlagStatus == "flagged"),
            item => MailDate(item, dateField), item => item.Subject, fromUtc, toUtc);

    internal static DateTimeOffset? TaskDate(TaskItem item, string field) => field switch
    {
        "start" => item.Start,
        "due" => item.Due,
        "completed" => item.Completed,
        _ => throw new ArgumentException("Invalid task date field.", nameof(field)),
    };

    internal static DateTimeOffset? MailDate(MailItem item, string field) => field switch
    {
        "received" => item.Received,
        "sent" => item.Sent,
        _ => throw new ArgumentException("Invalid mail date field.", nameof(field)),
    };

    internal static IReadOnlyList<T> FilterAndSort<T>(IEnumerable<T> items,
        Func<T, DateTimeOffset?> date, Func<T, string> subject,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc) =>
        items.Where(item => InPeriod(date(item), fromUtc, toUtc))
            .OrderBy(item => date(item) is null).ThenBy(date)
            .ThenBy(subject, StringComparer.OrdinalIgnoreCase).ToArray();

    internal static bool InPeriod(DateTimeOffset? date, DateTimeOffset? fromUtc, DateTimeOffset? toUtc) =>
        (fromUtc is null && toUtc is null) ||
        (date is not null && (fromUtc is null || date >= fromUtc) && (toUtc is null || date < toUtc));

    private static bool IsClass(string value, string baseClass) =>
        value.Equals(baseClass, StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith(baseClass + ".", StringComparison.OrdinalIgnoreCase);

    internal static TaskItem? CreateTaskItem(Folder folder, Message message, bool includeBody = false)
    {
        var properties = message.Properties;
        var messageClass = GetStandardString(properties, 0x001A) ?? string.Empty;
        if (!IsClass(messageClass, "IPM.Task"))
            return null;
        var progressValue = FindNamed(properties, TaskPropertySet, 0x8102)?.Value;
        double? progress = progressValue switch
        {
            double value when double.IsFinite(value) => value * 100,
            float value when float.IsFinite(value) => value * 100d,
            _ => null,
        };
        var (body, bodyFormat) = includeBody ? ExtractBody(message) : (null, null);
        return new TaskItem(NormalizePath(folder.Path), message.Subject ?? string.Empty,
            GetNamedDate(properties, TaskPropertySet, 0x8104),
            GetNamedDate(properties, TaskPropertySet, 0x8105),
            GetNamedDate(properties, TaskPropertySet, 0x810F),
            ToInteger(FindNamed(properties, TaskPropertySet, 0x8101)?.Value),
            progress, GetNamedBoolean(properties, TaskPropertySet, 0x811C),
            messageClass, body, bodyFormat);
    }

    internal static MailItem? CreateMailItem(Folder folder, Message message, bool includeBody = false)
    {
        var properties = message.Properties;
        var messageClass = GetStandardString(properties, 0x001A) ?? string.Empty;
        if (!IsClass(messageClass, "IPM.Note"))
            return null;
        var flags = ToInteger(FindStandard(properties, 0x0E07)?.Value);
        var (body, bodyFormat) = includeBody ? ExtractBody(message) : (null, null);
        return new MailItem(NormalizePath(folder.Path), message.Subject ?? string.Empty,
            GetStandardString(properties, 0x0C1A) ?? string.Empty,
            FirstNonEmpty(GetStandardString(properties, 0x5D01), GetStandardString(properties, 0x0C1F)),
            RecipientList(message, RecipientType.To, 0x0E04),
            RecipientList(message, RecipientType.Cc, 0x0E03),
            RecipientList(message, RecipientType.Bcc, 0x0E02),
            GetStandardDate(properties, 0x0E06), GetStandardDate(properties, 0x0039),
            flags is null ? null : (flags & 1) != 0,
            flags is null ? null : (flags & 0x10) != 0,
            FlagStatus(FindStandard(properties, 0x1090)?.Value), messageClass, body, bodyFormat);
    }

    internal static string FlagStatus(object? value) => value is null ? "none" : ToInteger(value) switch
    {
        0 => "none",
        1 => "complete",
        2 => "flagged",
        _ => "unknown",
    };

    private static int? ToInteger(object? value) => value switch
    {
        byte number => number,
        short number => number,
        int number => number,
        uint number when number <= int.MaxValue => (int)number,
        _ => null,
    };

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string RecipientList(Message message, RecipientType type, ushort displayTag)
    {
        var recipients = message.Recipients.Where(recipient => recipient.RecipientType == type)
            .Select(recipient =>
            {
                var name = recipient.DisplayName ?? string.Empty;
                var address = FirstNonEmpty(GetStandardString(recipient.Properties, 0x39FE), recipient.EmailAddress);
                return string.IsNullOrEmpty(name) ? address :
                    string.IsNullOrEmpty(address) || name.Equals(address, StringComparison.OrdinalIgnoreCase)
                        ? name : $"{name} <{address}>";
            }).Where(value => !string.IsNullOrEmpty(value)).ToArray();
        return recipients.Length > 0 ? string.Join("; ", recipients) :
            GetStandardString(message.Properties, displayTag) ?? string.Empty;
    }
}
