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

            if (options.Command is ReaderCommand.Calendar)
            {
                error.WriteLine("エラー: calendar コマンドはまだ実装されていません。");
                return 2;
            }

            var catalog = service.ReadCatalog();
            using var writerScope = new WriterScope(
                OutputSerializer.CreateWriter(options.OutputPath, output),
                output);

            if (options.Command is ReaderCommand.Info)
            {
                OutputSerializer.WriteStore(writerScope.Writer, catalog.Store, options.Format);
            }
            else
            {
                OutputSerializer.WriteFolders(writerScope.Writer, catalog.Folders, options.Format);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XstReader.XstException)
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
        writer.WriteLine();
        writer.WriteLine("コマンドを省略した場合は calendar として扱います。");
        writer.WriteLine("日時にタイムゾーンがない場合は UTC として解釈します。");
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

