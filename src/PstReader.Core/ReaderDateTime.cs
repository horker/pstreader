using System.Globalization;
using PstReader.Models;

namespace PstReader;

/// <summary>Shared date parsing, range validation, and output time-zone conversion.</summary>
public static class ReaderDateTime
{
    public static bool TryParse(string text, string option, out DateTimeOffset? value, out string? error)
    {
        if (HasExplicitOffset(text!))
        {
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                value = null;
                error = $"Cannot parse the date for {option}: {text}";
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
            error = $"Cannot parse the date for {option}: {text}";
            return false;
        }

        parsedLocal = DateTime.SpecifyKind(parsedLocal, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(parsedLocal))
        {
            value = null;
            error = $"{option} is an invalid time in the local time zone. Specify a UTC offset: {text}";
            return false;
        }

        if (TimeZoneInfo.Local.IsAmbiguousTime(parsedLocal))
        {
            value = null;
            error = $"{option} is ambiguous in the local time zone. Specify a UTC offset: {text}";
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

    public static bool TryResolveTimeZone(
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
            error = $"Output time zone not found or its definition is invalid: {value}";
            return false;
        }
    }

    public static DateTimeOffset Parse(string text)
    {
        if (!TryParse(text, "date", out var value, out var error))
            throw new FormatException(error);
        return value!.Value;
    }

    public static TimeZoneInfo ResolveTimeZone(string value)
    {
        if (!TryResolveTimeZone(value, out var zone, out var error))
            throw new ArgumentException(error, nameof(value));
        return zone;
    }

    public static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from is not null && to is not null && from >= to)
            throw new ArgumentException("From must be earlier than To.");
    }

    public static CalendarItem InTimeZone(CalendarItem item, TimeZoneInfo zone) => item with
    {
        Start = TimeZoneInfo.ConvertTime(item.Start, zone),
        End = TimeZoneInfo.ConvertTime(item.End, zone),
    };

    public static TaskItem InTimeZone(TaskItem item, TimeZoneInfo zone) => item with
    {
        Start = ConvertDate(item.Start, zone),
        Due = ConvertDate(item.Due, zone),
        Completed = ConvertDate(item.Completed, zone),
    };

    public static MailItem InTimeZone(MailItem item, TimeZoneInfo zone) => item with
    {
        Received = ConvertDate(item.Received, zone),
        Sent = ConvertDate(item.Sent, zone),
    };

    public static DateTimeOffset? ConvertDate(DateTimeOffset? value, TimeZoneInfo zone) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, zone);
}
