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
}

