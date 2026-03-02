using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;

namespace FEBuilderGBA.CLI
{
    static class Program
    {
        static int Main(string[] args)
        {
            var argsDic = ParseArgs(args);

            // Set up base directory (where the exe lives)
            string baseDir = Path.GetDirectoryName(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
            CoreState.BaseDirectory = baseDir;
            CoreState.IsCommandLine = true;
            CoreState.ArgsDic = argsDic;
            CoreState.Services = new CliAppServices();

            if (argsDic.ContainsKey("--help") || argsDic.ContainsKey("-h") || args.Length == 0)
            {
                PrintHelp();
                return 0;
            }

            if (argsDic.ContainsKey("--version"))
            {
                PrintVersion();
                return 0;
            }

            if (argsDic.ContainsKey("--makeups"))
            {
                return RunMakeUps(argsDic);
            }

            // Other commands not yet implemented
            Console.Error.WriteLine("Command not yet supported in cross-platform CLI.");
            Console.Error.WriteLine("Supported commands: --version, --help, --makeups");
            Console.Error.WriteLine("Run with --help for usage information.");
            return 1;
        }

        static void PrintVersion()
        {
            var asm = typeof(U).Assembly;
            var name = asm.GetName().Name;
            var ver = asm.GetName().Version;
            var baseDate = new DateTime(2000, 1, 1);
            string version = baseDate.AddDays(ver.Build).AddSeconds(ver.Revision * 2).ToString("yyyyMMdd.HH");
            Console.WriteLine($"{name} Version:{version}");
            Console.WriteLine("Copyright: 2017-");
            Console.WriteLine("License: GPLv3");
        }

        static void PrintHelp()
        {
            Console.WriteLine("FEBuilderGBA CLI - Cross-platform ROM hacking tool");
            Console.WriteLine();
            Console.WriteLine("Usage: FEBuilderGBA.CLI [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --version          Show version information");
            Console.WriteLine("  --help, -h         Show this help message");
            Console.WriteLine("  --rom=<path>       Specify ROM file to load");
            Console.WriteLine("  --makeups=<path>   Create UPS patch (requires --rom and --fromrom)");
            Console.WriteLine();
            Console.WriteLine("More commands coming soon. Use the WinForms GUI for full functionality.");
        }

        static int RunMakeUps(Dictionary<string, string> argsDic)
        {
            string upsPath = argsDic["--makeups"];
            if (string.IsNullOrEmpty(upsPath))
            {
                Console.Error.WriteLine("Error: --makeups requires a path, e.g. --makeups=output.ups");
                return 1;
            }

            if (!argsDic.ContainsKey("--rom") || string.IsNullOrEmpty(argsDic["--rom"]))
            {
                Console.Error.WriteLine("Error: --makeups requires --rom=<modified_rom>");
                return 1;
            }

            if (!argsDic.ContainsKey("--fromrom") || string.IsNullOrEmpty(argsDic["--fromrom"]))
            {
                Console.Error.WriteLine("Error: --makeups requires --fromrom=<original_rom>");
                return 1;
            }

            string modifiedRomPath = argsDic["--rom"];
            string originalRomPath = argsDic["--fromrom"];

            if (!File.Exists(modifiedRomPath))
            {
                Console.Error.WriteLine($"Error: Modified ROM not found: {modifiedRomPath}");
                return 1;
            }
            if (!File.Exists(originalRomPath))
            {
                Console.Error.WriteLine($"Error: Original ROM not found: {originalRomPath}");
                return 1;
            }

            byte[] src = File.ReadAllBytes(originalRomPath);
            byte[] dst = File.ReadAllBytes(modifiedRomPath);

            UPSUtilCore.MakeUPS(src, dst, upsPath);
            Console.WriteLine($"UPS patch created: {upsPath}");
            Console.WriteLine($"  Source: {originalRomPath} ({src.Length} bytes)");
            Console.WriteLine($"  Target: {modifiedRomPath} ({dst.Length} bytes)");
            return 0;
        }

        /// <summary>
        /// Parse CLI arguments into a dictionary. Supports --key=value and --flag forms.
        /// </summary>
        internal static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dic = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--"))
                {
                    int eq = arg.IndexOf('=');
                    if (eq >= 0)
                    {
                        dic[arg.Substring(0, eq)] = arg.Substring(eq + 1);
                    }
                    else
                    {
                        dic[arg] = "";
                    }
                }
                else if (arg == "-h")
                {
                    dic["--help"] = "";
                }
            }
            return dic;
        }
    }
}
