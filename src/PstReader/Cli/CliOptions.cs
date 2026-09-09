namespace PstReader.Cli;

internal enum ReaderCommand
{
    Info,
    Folders,
    Calendar,
    Tasks,
    Mail,
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
    bool IncludeBody,
    string? DateField = null,
    bool FlaggedOnly = false);

internal sealed record CliParseResult(CliOptions? Options, string? Error, bool ShowHelp)
{
    public bool IsSuccess => Options is not null;
}
