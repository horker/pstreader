using PstReader.Format;
using PstReader.Output;
using PstReader.Services;

namespace PstReader.Cli;

internal static class CliApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = CliParser.Parse(args);
        if (parsed.ShowHelp)
        {
            WriteHelp(output);
            return 0;
        }

        if (!parsed.IsSuccess)
        {
            error.WriteLine($"Error: {parsed.Error}");
            error.WriteLine("Run pstreader --help for usage information.");
            return 2;
        }

        var options = parsed.Options!;
        try
        {
            var header = PffHeaderReader.Read(options.InputPath);
            var service = new XstStoreService(options.InputPath, header);
            using var writerScope = new WriterScope(
                OutputSerializer.CreateWriter(options.OutputPath, output),
                output);

            if (options.Command is ReaderCommand.Calendar)
            {
                var result = service.ReadCalendar(options.FromUtc, options.ToUtc, options.IncludeBody);
                OutputSerializer.WriteCalendar(
                    writerScope.Writer,
                    result.Items,
                    options.Format,
                    options.OutputTimeZone,
                    options.IncludeBody);
                WriteWarnings(error, result.Warnings);
            }
            else if (options.Command is ReaderCommand.Tasks)
            {
                var result = service.ReadTasks(options.FromUtc, options.ToUtc, options.DateField!, options.IncludeBody);
                OutputSerializer.WriteTasks(writerScope.Writer, result.Items, options.Format,
                    options.OutputTimeZone, options.IncludeBody);
                WriteWarnings(error, result.Warnings);
            }
            else if (options.Command is ReaderCommand.Mail)
            {
                var result = service.ReadMail(options.FromUtc, options.ToUtc, options.DateField!,
                    options.IncludeBody, options.FlaggedOnly);
                OutputSerializer.WriteMail(writerScope.Writer, result.Items, options.Format,
                    options.OutputTimeZone, options.IncludeBody);
                WriteWarnings(error, result.Warnings);
            }
            else
            {
                var catalog = service.ReadCatalog();
                if (options.Command is ReaderCommand.Info)
                {
                    OutputSerializer.WriteStore(writerScope.Writer, catalog.Store, options.Format);
                }
                else
                {
                    OutputSerializer.WriteFolders(writerScope.Writer, catalog.Folders, options.Format);
                }
            }

            writerScope.Writer.Flush();
            return 0;
        }
        catch (FileNotFoundException)
        {
            error.WriteLine($"Error: Input file not found: {options.InputPath}");
            return 3;
        }
        catch (PffFormatException ex)
        {
            error.WriteLine($"Error: {ex.Message}");
            return 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XstReader.XstException or NotSupportedException)
        {
            error.WriteLine($"Error: Failed to read PST/OST: {ex.Message}");
            return 3;
        }
    }

    private static void WriteWarnings(TextWriter error, IReadOnlyList<string> warnings)
    {
        if (warnings.Count > 0)
        {
            error.WriteLine($"Warning: Skipped {warnings.Count} items or folders.");
            foreach (var warning in warnings.Take(10))
            {
                error.WriteLine($"Warning: {warning}");
            }

            if (warnings.Count > 10)
            {
                error.WriteLine($"Warning: Omitted {warnings.Count - 10} additional warnings.");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("pstreader - PST/OST reader CLI without Outlook or Office dependencies");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  pstreader info <file> [--format json|csv] [--output <file>]");
        writer.WriteLine("  pstreader folders <file> [--format json|csv] [--output <file>]");
        writer.WriteLine("  pstreader calendar <file> [--from <date>] [--to <date>]");
        writer.WriteLine("                     [--format json|csv] [--output <file>]");
        writer.WriteLine("                     [--output-time-zone <local|utc|time-zone-id>]");
        writer.WriteLine("                     [--include-body]");
        writer.WriteLine();
        writer.WriteLine("  pstreader tasks <file> [--date-field start|due|completed]");
        writer.WriteLine("  pstreader mail <file> [--date-field received|sent] [--flagged-only]");
        writer.WriteLine("  Shared tasks/mail options: [--from <date>] [--to <date>] [--format json|csv]");
        writer.WriteLine("                   [--output <file>] [--output-time-zone <local|utc|time-zone-id>]");
        writer.WriteLine("                   [--include-body]");
        writer.WriteLine("Default date fields: tasks: due, mail: received. Missing dates have no fallback.");
        writer.WriteLine("For tasks/mail, the selected date must be at or after from and before to.");
        writer.WriteLine("--flagged-only retrieves only flagged mail that has not been completed.");
        writer.WriteLine();
        writer.WriteLine("The command defaults to calendar when omitted.");
        writer.WriteLine("Input dates without a time zone are interpreted as local time.");
        writer.WriteLine("Output times default to local time. --time-zone is an alias for --output-time-zone.");
        writer.WriteLine("--include-body adds the body and its format to the output.");
    }

    private sealed class WriterScope(TextWriter writer, TextWriter standardOutput) : IDisposable
    {
        public TextWriter Writer { get; } = writer;

        public void Dispose()
        {
            if (!ReferenceEquals(Writer, standardOutput))
            {
                Writer.Dispose();
            }
        }
    }
}
