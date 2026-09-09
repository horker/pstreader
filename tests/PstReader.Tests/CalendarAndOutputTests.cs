using System.Text.Json;
using PstReader.Cli;
using PstReader.Models;
using PstReader.Output;
using PstReader.Services;
using XstReader;

namespace PstReader.Tests;

public sealed class CalendarAndOutputTests
{
    [Theory]
    [InlineData("2026-01-01T09:00:00Z", "2026-01-01T10:00:00Z", "2026-01-01T09:30:00Z", "2026-01-01T11:00:00Z", true)]
    [InlineData("2026-01-01T09:00:00Z", "2026-01-01T10:00:00Z", "2026-01-01T10:00:00Z", "2026-01-01T11:00:00Z", false)]
    [InlineData("2026-01-01T09:00:00Z", "2026-01-01T10:00:00Z", "2026-01-01T08:00:00Z", "2026-01-01T09:00:00Z", false)]
    public void UsesHalfOpenOverlapSemantics(
        string start,
        string end,
        string from,
        string to,
        bool expected)
    {
        var actual = XstStoreService.Overlaps(
            DateTimeOffset.Parse(start),
            DateTimeOffset.Parse(end),
            DateTimeOffset.Parse(from),
            DateTimeOffset.Parse(to));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CsvWriterQuotesCommasAndDoubleQuotes()
    {
        using var writer = new StringWriter();

        OutputSerializer.WriteCsvRow(writer, "a,b", "say \"hi\"", true);

        Assert.Equal(
            "\"a,b\",\"say \"\"hi\"\"\",\"True\"" + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void JsonWriterKeepsUnicodeCharactersReadable()
    {
        using var writer = new StringWriter();
        var item = new CalendarItem(
            "Root/Calendar",
            "Observed holiday (New Year’s Day)",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            "Japan",
            true,
            "IPM.Appointment");

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Json, TimeZoneInfo.Local);

        var json = writer.ToString();
        Assert.Contains("Root/Calendar", json);
        Assert.Contains("Observed holiday (New Year’s Day)", json);
        Assert.Contains("Japan", json);
        Assert.DoesNotContain("\\u2019", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonWriterConvertsTimesToRequestedTimeZone()
    {
        using var writer = new StringWriter();
        var outputTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test UTC+09",
            TimeSpan.FromHours(9),
            "Test UTC+09",
            "Test UTC+09");
        var item = new CalendarItem(
            "Root/Calendar",
            "Appointment",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
            string.Empty,
            false,
            "IPM.Appointment");

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Json, outputTimeZone);

        using var document = JsonDocument.Parse(writer.ToString());
        var outputItem = document.RootElement[0];
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T09:00:00+09:00"), outputItem.GetProperty("start").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T10:00:00+09:00"), outputItem.GetProperty("end").GetDateTimeOffset());
    }

    [Fact]
    public void CsvWriterConvertsTimesToRequestedTimeZone()
    {
        using var writer = new StringWriter();
        var outputTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Test UTC-08",
            TimeSpan.FromHours(-8),
            "Test UTC-08",
            "Test UTC-08");
        var item = new CalendarItem(
            "Root/Calendar",
            "Appointment",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
            string.Empty,
            false,
            "IPM.Appointment");

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Csv, outputTimeZone);

        var csv = writer.ToString();
        Assert.Contains("\"2025-12-31T16:00:00.0000000-08:00\"", csv);
        Assert.Contains("\"2025-12-31T17:00:00.0000000-08:00\"", csv);
    }

    [Fact]
    public void JsonWriterOmitsBodyByDefault()
    {
        using var writer = new StringWriter();
        var item = CreateItem("Hidden body", "text");

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Json, TimeZoneInfo.Utc);

        using var document = JsonDocument.Parse(writer.ToString());
        var outputItem = document.RootElement[0];
        Assert.False(outputItem.TryGetProperty("body", out _));
        Assert.False(outputItem.TryGetProperty("bodyFormat", out _));
    }

    [Fact]
    public void JsonWriterIncludesReadableBodyWhenRequested()
    {
        using var writer = new StringWriter();
        var item = CreateItem("Calendar body: café meeting", "text");

        OutputSerializer.WriteCalendar(
            writer,
            [item],
            OutputFormat.Json,
            TimeZoneInfo.Utc,
            includeBody: true);

        var json = writer.ToString();
        using var document = JsonDocument.Parse(json);
        var outputItem = document.RootElement[0];
        Assert.Equal("Calendar body: café meeting", outputItem.GetProperty("body").GetString());
        Assert.Equal("text", outputItem.GetProperty("bodyFormat").GetString());
        Assert.Contains("Calendar body: café meeting", json);
        Assert.DoesNotContain("\\u00E9", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvWriterIncludesBodyColumnsWhenRequested()
    {
        using var writer = new StringWriter();
        var item = CreateItem("Line 1\r\n\"Quote\"", "text");

        OutputSerializer.WriteCalendar(
            writer,
            [item],
            OutputFormat.Csv,
            TimeZoneInfo.Utc,
            includeBody: true);

        var csv = writer.ToString();
        Assert.StartsWith(
            "\"folderPath\",\"subject\",\"start\",\"end\",\"location\",\"isAllDay\",\"messageClass\",\"body\",\"bodyFormat\"",
            csv);
        Assert.Contains("\"Line 1\r\n\"\"Quote\"\"\"", csv);
    }

    [Fact]
    public void ExtractsPlainAndHtmlBodiesWithTheirFormats()
    {
        var plain = XstStoreService.ExtractBody(new Message { Body = "Plain text body" });
        var html = XstStoreService.ExtractBody(new Message { BodyHtml = "<p>HTML body</p>" });

        Assert.Equal(("Plain text body", "text"), plain);
        Assert.Equal(("<p>HTML body</p>", "html"), html);
    }

    [Fact]
    public void HtmlBodyCanRemainReadableUnicode()
    {
        var message = new Message { Body = "Café" };

        Assert.Equal("Café", message.GetBodyAsHtmlString(escapeUnicode: false));
        Assert.Equal("Caf&#x00E9;", message.GetBodyAsHtmlString());
    }

    private static CalendarItem CreateItem(string? body, string? bodyFormat) => new(
        "Root/Calendar",
        "Appointment",
        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
        string.Empty,
        false,
        "IPM.Appointment",
        body,
        bodyFormat);
}
