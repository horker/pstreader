using System.Globalization;

namespace PstReader.Cli;

internal static class CliParser
{
    public static CliParseResult Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            return new CliParseResult(null, null, true);
        }

        var index = 0;
        var command = ParseCommand(args[0]);
        if (command is not null)
        {
            index++;
        }
        else
        {
            command = ReaderCommand.Calendar;
        }

        if (index >= args.Length || args[index].StartsWith('-'))
        {
            return Failure("入力ファイルを指定してください。");
        }

        var inputPath = args[index++];
        var format = OutputFormat.Json;
        string? outputPath = null;
        DateTimeOffset? fromUtc = null;
        DateTimeOffset? toUtc = null;
        var outputTimeZone = TimeZoneInfo.Local;
        var outputTimeZoneSpecified = false;

        while (index < args.Length)
        {
            var option = args[index++];
            switch (option)
            {
                case "-h":
                case "--help":
                    return new CliParseResult(null, null, true);
                case "-f":
                case "--format":
                    if (!TryTakeValue(args, ref index, option, out var formatValue, out var formatError))
                    {
                        return Failure(formatError!);
                    }

                    if (!Enum.TryParse<OutputFormat>(formatValue, true, out format))
                    {
                        return Failure("--format には json または csv を指定してください。");
                    }

                    break;
                case "-o":
                case "--output":
                    if (!TryTakeValue(args, ref index, option, out outputPath, out var outputError))
                    {
                        return Failure(outputError!);
                    }

                    break;
                case "--from":
                    if (!TryTakeDate(args, ref index, option, out fromUtc, out var fromError))
                    {
                        return Failure(fromError!);
                    }

                    break;
                case "--to":
                    if (!TryTakeDate(args, ref index, option, out toUtc, out var toError))
                    {
                        return Failure(toError!);
                    }

                    break;
                case "--time-zone":
                case "--output-time-zone":
                    if (!TryTakeValue(args, ref index, option, out var timeZoneValue, out var timeZoneValueError))
                    {
                        return Failure(timeZoneValueError!);
                    }

                    if (!TryResolveTimeZone(timeZoneValue!, out outputTimeZone, out var timeZoneError))
                    {
                        return Failure(timeZoneError!);
                    }

                    outputTimeZoneSpecified = true;
                    break;
                default:
                    return Failure($"不明なオプションです: {option}");
            }
        }

        if (command is not ReaderCommand.Calendar &&
            (fromUtc is not null || toUtc is not null || outputTimeZoneSpecified))
        {
            return Failure("--from、--to、--output-time-zone は calendar コマンドでのみ使用できます。");
        }

        if (fromUtc is not null && toUtc is not null && fromUtc >= toUtc)
        {
            return Failure("--from は --to より前である必要があります。");
        }

        return new CliParseResult(
            new CliOptions(command.Value, inputPath, format, outputPath, fromUtc, toUtc, outputTimeZone),
            null,
            false);
    }

    private static ReaderCommand? ParseCommand(string value) => value.ToLowerInvariant() switch
    {
        "info" => ReaderCommand.Info,
        "folders" => ReaderCommand.Folders,
        "calendar" => ReaderCommand.Calendar,
        _ => null,
    };

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string? value,
        out string? error)
    {
        if (index >= args.Length || args[index].StartsWith('-'))
        {
            value = null;
            error = $"{option} の値を指定してください。";
            return false;
        }

        value = args[index++];
        error = null;
        return true;
    }

    private static bool TryTakeDate(
        string[] args,
        ref int index,
        string option,
        out DateTimeOffset? value,
        out string? error)
    {
        if (!TryTakeValue(args, ref index, option, out var text, out error))
        {
            value = null;
            return false;
        }

        if (HasExplicitOffset(text!))
        {
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                value = null;
                error = $"{option} の日時を解釈できません: {text}";
                return false;
            }

            value = parsed.ToUniversalTime();
            error = null;
            return true;
        }

        if (!DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedLocal))
        {
            value = null;
            error = $"{option} の日時を解釈できません: {text}";
            return false;
        }

        parsedLocal = DateTime.SpecifyKind(parsedLocal, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(parsedLocal))
        {
            value = null;
            error = $"{option} はローカルタイムゾーンに存在しない日時です。UTC オフセットを指定してください: {text}";
            return false;
        }

        if (TimeZoneInfo.Local.IsAmbiguousTime(parsedLocal))
        {
            value = null;
            error = $"{option} はローカルタイムゾーンで曖昧な日時です。UTC オフセットを指定してください: {text}";
            return false;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(parsedLocal, TimeZoneInfo.Local);
        value = new DateTimeOffset(utc, TimeSpan.Zero);
        error = null;
        return true;
    }

    private static bool HasExplicitOffset(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var timeSeparator = Math.Max(trimmed.LastIndexOf('T'), trimmed.LastIndexOf(' '));
        if (timeSeparator < 0)
        {
            return false;
        }

        return trimmed.IndexOf('+', timeSeparator + 1) >= 0 ||
               trimmed.IndexOf('-', timeSeparator + 1) >= 0;
    }

    private static bool TryResolveTimeZone(
        string value,
        out TimeZoneInfo timeZone,
        out string? error)
    {
        if (value.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            timeZone = TimeZoneInfo.Local;
            error = null;
            return true;
        }

        if (value.Equals("utc", StringComparison.OrdinalIgnoreCase))
        {
            timeZone = TimeZoneInfo.Utc;
            error = null;
            return true;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(value);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Local;
            error = $"出力タイムゾーンが見つからないか、定義が不正です: {value}";
            return false;
        }
    }

    private static CliParseResult Failure(string error) => new(null, error, false);
}
