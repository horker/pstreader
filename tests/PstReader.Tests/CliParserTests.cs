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
    public void ParsesPeriodAsUtc()
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
        Assert.Equal(DateTimeOffset.Parse("2026-02-01T00:00:00Z"), result.Options.ToUtc);
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
}

