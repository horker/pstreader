namespace PstReader.Cli;

internal enum ReaderCommand
{
    Info,
    Folders,
    Calendar,
}

internal enum OutputFormat
{
    Json,
    Csv,
}

internal sealed record CliOptions(
    ReaderCommand Command,
    string InputPath,
    OutputFormat Format,
    string? OutputPath,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    TimeZoneInfo OutputTimeZone,
    bool IncludeBody);

internal sealed record CliParseResult(CliOptions? Options, string? Error, bool ShowHelp)
{
    public bool IsSuccess => Options is not null;
}
