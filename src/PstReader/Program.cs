using System.Text;
using PstReader.Cli;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
return CliApplication.Run(args, Console.Out, Console.Error);
