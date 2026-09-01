using System.Globalization;
using System.Text;
using System.Text.Json;
using PstReader.Cli;
using PstReader.Models;

namespace PstReader.Output;

internal static class OutputSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
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
}

