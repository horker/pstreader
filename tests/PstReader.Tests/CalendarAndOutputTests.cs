using System.Text.Json;
using PstReader.Cli;
using PstReader.Models;
using PstReader.Output;
using PstReader.Services;

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
            "ルート/予定表",
            "振替休日（元旦）",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            "日本",
            true,
            "IPM.Appointment");

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Json, TimeZoneInfo.Local);

        var json = writer.ToString();
        Assert.Contains("ルート/予定表", json);
        Assert.Contains("振替休日（元旦）", json);
        Assert.Contains("日本", json);
        Assert.DoesNotContain("\\u30EB", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u65E5", json, StringComparison.OrdinalIgnoreCase);
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
}
