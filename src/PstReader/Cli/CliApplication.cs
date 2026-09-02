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
            error.WriteLine($"エラー: {parsed.Error}");
            error.WriteLine("使用方法は pstreader --help で確認できます。");
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
                if (result.Warnings.Count > 0)
                {
                    error.WriteLine($"警告: {result.Warnings.Count} 件の項目またはフォルダーを読み飛ばしました。");
                    foreach (var warning in result.Warnings.Take(10))
                    {
                        error.WriteLine($"警告: {warning}");
                    }

                    if (result.Warnings.Count > 10)
                    {
                        error.WriteLine($"警告: 残り {result.Warnings.Count - 10} 件は省略しました。");
                    }
                }
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
            error.WriteLine($"エラー: 入力ファイルが見つかりません: {options.InputPath}");
            return 3;
        }
        catch (PffFormatException ex)
        {
            error.WriteLine($"エラー: {ex.Message}");
            return 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XstReader.XstException or NotSupportedException)
        {
            error.WriteLine($"エラー: PST/OST を読み取れませんでした: {ex.Message}");
            return 3;
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("pstreader - Outlook/Office 非依存の PST/OST 読み取り CLI");
        writer.WriteLine();
        writer.WriteLine("使用方法:");
        writer.WriteLine("  pstreader info <file> [--format json|csv] [--output <file>]");
        writer.WriteLine("  pstreader folders <file> [--format json|csv] [--output <file>]");
        writer.WriteLine("  pstreader calendar <file> [--from <date>] [--to <date>]");
        writer.WriteLine("                     [--format json|csv] [--output <file>]");
        writer.WriteLine("                     [--output-time-zone <local|utc|time-zone-id>]");
        writer.WriteLine("                     [--include-body]");
        writer.WriteLine();
        writer.WriteLine("コマンドを省略した場合は calendar として扱います。");
        writer.WriteLine("入力日時にタイムゾーンがない場合はローカルタイムとして解釈します。");
        writer.WriteLine("出力時刻は既定でローカルタイムです。--time-zone は短縮別名です。");
        writer.WriteLine("--include-body を指定すると、本文と本文形式を出力します。");
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
