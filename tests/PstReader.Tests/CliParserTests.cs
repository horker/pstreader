using PstReader.Cli;

namespace PstReader.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void UsesCalendarWhenCommandIsOmitted()
    {
        var result = CliParser.Parse(["archive.bin", "--format", "csv"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReaderCommand.Calendar, result.Options!.Command);
        Assert.Equal(OutputFormat.Csv, result.Options.Format);
        Assert.Equal("archive.bin", result.Options.InputPath);
    }

    [Fact]
    public void ParsesExplicitOffsetAndUnqualifiedLocalTimeAsUtc()
    {
        var result = CliParser.Parse([
            "calendar",
            "archive.pst",
            "--from",
            "2026-01-01T09:00:00+09:00",
            "--to",
            "2026-02-01",
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), result.Options!.FromUtc);
        var expectedLocalMidnightUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Unspecified),
                TimeZoneInfo.Local),
            TimeSpan.Zero);
        Assert.Equal(expectedLocalMidnightUtc, result.Options.ToUtc);
        Assert.Equal(TimeZoneInfo.Local.Id, result.Options.OutputTimeZone.Id);
    }

    [Fact]
    public void RejectsReversedPeriod()
    {
        var result = CliParser.Parse([
            "calendar", "archive.pst", "--from", "2026-02-01", "--to", "2026-01-01",
        ]);

        Assert.False(result.IsSuccess);
        Assert.Contains("--from", result.Error);
    }

    [Fact]
    public void RejectsPeriodForFoldersCommand()
    {
        var result = CliParser.Parse(["folders", "archive.pst", "--from", "2026-01-01"]);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ResolvesUtcOutputTimeZone()
    {
        var result = CliParser.Parse([
            "calendar", "archive.pst", "--output-time-zone", "utc",
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeZoneInfo.Utc.Id, result.Options!.OutputTimeZone.Id);
    }

    [Fact]
    public void ResolvesArbitrarySystemOutputTimeZone()
    {
        var systemTimeZone = TimeZoneInfo.GetSystemTimeZones()
            .First(zone => zone.Id != TimeZoneInfo.Local.Id && zone.Id != TimeZoneInfo.Utc.Id);

        var result = CliParser.Parse([
            "calendar", "archive.pst", "--time-zone", systemTimeZone.Id,
        ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(systemTimeZone.Id, result.Options!.OutputTimeZone.Id);
    }

    [Fact]
    public void RejectsUnknownOutputTimeZone()
    {
        var result = CliParser.Parse([
            "calendar", "archive.pst", "--output-time-zone", "Not/A-Time-Zone",
        ]);

        Assert.False(result.IsSuccess);
        Assert.Contains("タイムゾーン", result.Error);
    }
}
