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

        OutputSerializer.WriteCalendar(writer, [item], OutputFormat.Json);

        var json = writer.ToString();
        Assert.Contains("ルート/予定表", json);
        Assert.Contains("振替休日（元旦）", json);
        Assert.Contains("日本", json);
        Assert.DoesNotContain("\\u30EB", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u65E5", json, StringComparison.OrdinalIgnoreCase);
    }
}
