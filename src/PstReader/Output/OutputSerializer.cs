using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PstReader.Cli;
using PstReader.Models;

namespace PstReader.Output;

internal static class OutputSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void WriteStore(TextWriter writer, StoreInfo store, OutputFormat format)
    {
        if (format is OutputFormat.Json)
        {
            WriteJson(writer, store);
            return;
        }

        WriteCsvRow(writer,
            "inputFile", "detectedType", "version", "clientVersion", "layout",
            "pageSize", "fileSize", "rootFolder", "folderCount", "declaredMessageCount");
        WriteCsvRow(writer,
            store.InputFile,
            store.DetectedType,
            store.Version,
            store.ClientVersion,
            store.Layout,
            store.PageSize,
            store.FileSize,
            store.RootFolder,
            store.FolderCount,
            store.DeclaredMessageCount);
    }

    public static void WriteFolders(TextWriter writer, IReadOnlyList<FolderInfo> folders, OutputFormat format)
    {
        if (format is OutputFormat.Json)
        {
            WriteJson(writer, folders);
            return;
        }

        WriteCsvRow(writer, "path", "name", "depth", "declaredMessageCount", "childFolderCount");
        foreach (var folder in folders)
        {
            WriteCsvRow(
                writer,
                folder.Path,
                folder.Name,
                folder.Depth,
                folder.DeclaredMessageCount,
                folder.ChildFolderCount);
        }
    }

    public static void WriteCalendar(
        TextWriter writer,
        IReadOnlyList<CalendarItem> items,
        OutputFormat format,
        TimeZoneInfo outputTimeZone,
        bool includeBody = false)
    {
        var outputItems = items.Select(item => new CalendarOutputItem(
            item.FolderPath,
            item.Subject,
            TimeZoneInfo.ConvertTime(item.Start, outputTimeZone),
            TimeZoneInfo.ConvertTime(item.End, outputTimeZone),
            item.Location,
            item.IsAllDay,
            item.MessageClass)).ToArray();

        if (format is OutputFormat.Json)
        {
            if (includeBody)
            {
                WriteJson(writer, outputItems.Select((item, index) => new CalendarOutputItemWithBody(
                    item.FolderPath,
                    item.Subject,
                    item.Start,
                    item.End,
                    item.Location,
                    item.IsAllDay,
                    item.MessageClass,
                    items[index].Body,
                    items[index].BodyFormat)).ToArray());
            }
            else
            {
                WriteJson(writer, outputItems);
            }

            return;
        }

        if (includeBody)
        {
            WriteCsvRow(
                writer,
                "folderPath", "subject", "start", "end", "location", "isAllDay", "messageClass", "body", "bodyFormat");
        }
        else
        {
            WriteCsvRow(writer, "folderPath", "subject", "start", "end", "location", "isAllDay", "messageClass");
        }

        for (var index = 0; index < outputItems.Length; index++)
        {
            var item = outputItems[index];
            var values = new List<object?>
            {
                item.FolderPath,
                item.Subject,
                item.Start.ToString("O", CultureInfo.InvariantCulture),
                item.End.ToString("O", CultureInfo.InvariantCulture),
                item.Location,
                item.IsAllDay,
                item.MessageClass,
            };

            if (includeBody)
            {
                values.Add(items[index].Body);
                values.Add(items[index].BodyFormat);
            }

            WriteCsvRow(writer, values.ToArray());
        }
    }

    public static TextWriter CreateWriter(string? outputPath, TextWriter standardOutput)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return standardOutput;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new StreamWriter(fullPath, false, new UTF8Encoding(false));
    }

    internal static void WriteJson<T>(TextWriter writer, T value) =>
        writer.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    internal static void WriteCsvRow(TextWriter writer, params object?[] values) =>
        writer.WriteLine(string.Join(',', values.Select(EscapeCsv)));

    private static string EscapeCsv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private sealed record CalendarOutputItem(
        string FolderPath,
        string Subject,
        DateTimeOffset Start,
        DateTimeOffset End,
        string Location,
        bool IsAllDay,
        string MessageClass);

    private sealed record CalendarOutputItemWithBody(
        string FolderPath,
        string Subject,
        DateTimeOffset Start,
        DateTimeOffset End,
        string Location,
        bool IsAllDay,
        string MessageClass,
        string? Body,
        string? BodyFormat);
}
