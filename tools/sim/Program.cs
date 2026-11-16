using System;
using System.IO;
using System.Text;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Sim;

/// <summary>
/// Entry point of the <c>sim</c> tool (WP10). Parsing and every computation are in
/// <see cref="SimCommandLine"/>; this program only reads the input files, writes the output
/// files and prints the report.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        SimCommandResult result = SimCommandLine.Run(args, path => File.ReadAllText(path, Encoding.UTF8));
        foreach (SimOutputFile file in result.Files)
        {
            File.WriteAllText(file.Path, file.Content, new UTF8Encoding(false));
        }

        Console.Out.Write(result.Output);
        Console.Error.Write(result.Error);
        return result.ExitCode;
    }
}
